// Policy layer over an IBoard (Gigabyte Control Center or LibreHardwareMonitor): validation/safety, original-curve
// backup, persistence and a watchdog that re-applies the user's curves if something else (sleep/resume, GCC, BIOS)
// overwrites them. Saved state is keyed by each header's stable key (FanCurve.Key), never by its position: a header
// list that changes order (a LibreHardwareMonitor or BIOS update) must not put one fan's curve on another.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

sealed class FanManager
{
    // Curves keep the point count the board reports (8 on the B760M GAMING X AX); used when a readback is unusable.
    const int DefaultPointCount = 8, MinPointCount = 3, MaxPointCount = 16;
    // State file format: 2 = keyed by FanCurve.Key. Version 1 (no "version" field) was keyed by position.
    const int FormatVersion = 2;
    // Flat part of the fixed-mode curve for 8 points; other counts spread their points over the same 0..70 C range.
    static readonly double[] FixedModeTemps8 = { 0, 20, 30, 40, 50, 60, 70 };

    readonly IBoard _bridge;
    readonly AppConfig _cfg;
    readonly string _backupPath, _desiredPath, _turboPath;
    readonly object _sync = new object();
    // The state files as read, until the board's headers are known: a version 1 file needs them to be migrated.
    readonly Dictionary<string, object> _rawBackup, _rawDesired, _rawTurbo;
    // The board's headers by position, i.e. what the API's indexes mean this session. Null until the board answered.
    string[] _keys, _titles;
    // Turbo = every header at 100% (GPU untouched). Remembers each fan's previous request (null = unmanaged)
    // so turning it off puts things back exactly as they were. Persisted so it survives a restart.
    bool _turbo;
    Dictionary<string, object> _turboPrev = new Dictionary<string, object>();
    Dictionary<string, FanCurve> _backup = new Dictionary<string, FanCurve>();
    // What the user asked for, plus the hardware readback signature we expect to keep seeing. An entry whose key no
    // header has any more is an orphan: kept, shown in the panel, never applied.
    Dictionary<string, Dictionary<string, object>> _desired = new Dictionary<string, Dictionary<string, object>>();
    Timer _watchdog;

    public FanManager(IBoard bridge, AppConfig cfg, string dataDir)
    {
        _bridge = bridge;
        _cfg = cfg;
        _backupPath = Path.Combine(dataDir, "backup-curves.json");
        _desiredPath = Path.Combine(dataDir, "desired-curves.json");
        _turboPath = Path.Combine(dataDir, "turbo.json");
        _rawBackup = DataFile.Read(_backupPath);
        _rawDesired = DataFile.Read(_desiredPath);
        _rawTurbo = DataFile.Read(_turboPath);
        _turbo = _rawTurbo != null && Json.Get(_rawTurbo, "active") is bool active && active;
    }

    public bool TurboActive { get { lock (_sync) return _turbo; } }

    // The first call that needs the board reads its headers, then the state files (migrating version 1 ones), and
    // saves the original curve of every header seen for the first time. Throws while the board does not answer;
    // the next call tries again.
    void EnsureLoaded()
    {
        lock (_sync)
        {
            if (_keys != null) return;
            var curves = _bridge.GetCurves();
            _titles = curves.Select(c => c.Title ?? "").ToArray();
            _keys = curves.Select(KeyOf).ToArray();

            bool migrated = false;
            _desired = ByKey(_rawDesired, v => v as Dictionary<string, object>, e => TitleOf(e), ref migrated);
            _backup = ByKey(_rawBackup, v => v is Dictionary<string, object> d ? FanCurve.FromDictionary(d) : null, c => c.Title, ref migrated);
            var prev = _rawTurbo == null ? null : Json.Get(_rawTurbo, "prev") as Dictionary<string, object>;
            bool turboV1 = _rawTurbo != null && Json.GetInt(_rawTurbo, "version", 1) < FormatVersion;
            if (prev != null)
                foreach (var kv in prev)
                {
                    int index;
                    if (!turboV1) _turboPrev[kv.Key] = kv.Value;
                    else if (int.TryParse(kv.Key, out index) && index >= 0 && index < _keys.Length) _turboPrev[_keys[index]] = kv.Value;
                }
            if (migrated || turboV1)
            {
                // Builds older than the keys cannot read version 2: the version 1 files stay next to it as .v1.
                foreach (var path in new[] { _desiredPath, _backupPath, _turboPath })
                    if (File.Exists(path) && !File.Exists(path + ".v1")) File.Copy(path, path + ".v1");
                SaveDesired();
                SaveBackup();
                SaveTurbo();
                Log.Info("Arquivos de curvas convertidos para o formato com chave por fan (os antigos ficaram como .v1).");
            }
            foreach (var kv in _desired)
                if (Array.IndexOf(_keys, kv.Key) < 0)
                    Log.Warn("A configuração salva para '" + TitleOf(kv.Value) + "' não corresponde a nenhum fan desta placa e não foi aplicada.");
            CaptureBackups(curves);
        }
    }

    // Version 2 files: {"version": 2, "fans": {"<key>": entry}}. Version 1 files were {"<index>": entry}: an entry is
    // kept for the header now at that index only when the titles match; otherwise it gets a key no header has, which
    // makes it an orphan (never applied). One damaged entry costs only its own fan.
    Dictionary<string, T> ByKey<T>(Dictionary<string, object> raw, Func<object, T> parse, Func<T, string> titleOf, ref bool migrated) where T : class
    {
        var result = new Dictionary<string, T>();
        if (raw == null) return result;
        bool v2 = Json.GetInt(raw, "version", 1) >= FormatVersion;
        var entries = v2 ? Json.Get(raw, "fans") as Dictionary<string, object> : raw;
        if (entries == null) return result;
        foreach (var kv in entries)
        {
            T value;
            try { value = parse(kv.Value); }
            catch (FormatException) { value = null; }
            if (value == null) continue;
            if (v2) { result[kv.Key] = value; continue; }
            int index;
            if (!int.TryParse(kv.Key, out index)) continue;
            migrated = true;
            string title = titleOf(value) ?? "";
            bool same = index >= 0 && index < _keys.Length && (title.Length == 0 || title == _titles[index]);
            result[same ? _keys[index] : "v1:" + index + ":" + title] = value;
        }
        return result;
    }

    // The first time a header is seen its curve is saved, so Restore can always go back to it.
    void CaptureBackups(List<FanCurve> curves)
    {
        var missing = new List<string>();
        bool added = false;
        foreach (var c in curves)
        {
            string key = KeyOf(c);
            if (_backup.ContainsKey(key)) continue;
            if (!_desired.ContainsKey(key)) _backup[key] = c;
            // Without a backup (lost or damaged file) a fan FanPilot already manages is no longer at its original.
            // On software-curve boards that original is simply the BIOS; in a chip it is gone, and saving FanPilot's
            // own curve as "original" would make Restore a silent no-op forever.
            else if (!_bridge.HardwareCurves) _backup[key] = new FanCurve { Index = c.Index, Key = key, Title = c.Title, Mode = FanCurve.BiosMode, TempInput = 0, DeltaT = 1 };
            else { missing.Add(c.Title); continue; }
            added = true;
        }
        if (added)
        {
            SaveBackup();
            Log.Info("Saved original fan curves to " + _backupPath);
        }
        if (missing.Count > 0)
            AppNotices.Add("Sem a curva original de " + string.Join(", ", missing) + ": o backup se perdeu enquanto o FanPilot controlava " +
                           (missing.Count > 1 ? "esses fans" : "esse fan") + ". \"Restaurar original\" fica indisponível para " + (missing.Count > 1 ? "eles." : "ele."));
    }

    static string KeyOf(FanCurve c) { return c.Key ?? "index:" + c.Index; }

    static string TitleOf(Dictionary<string, object> desired)
    {
        var curve = Json.Get(desired, "curve") as Dictionary<string, object>;
        return Json.GetString(desired, "title", null) ?? Json.GetString(curve, "title", "");
    }

    string KeyAt(int index)
    {
        EnsureLoaded();
        if (index < 0 || index >= _keys.Length) throw new ArgumentException("Fan inexistente.");
        return _keys[index];
    }

    public void SetTurbo(bool on)
    {
        if (on == TurboActive) return;
        EnsureLoaded();
        int n = _keys.Length;
        if (on && n == 0) throw new ArgumentException("Esta placa-mãe não oferece controle de fans pelo FanPilot.");
        if (on)
        {
            lock (_sync)
            {
                _turboPrev = new Dictionary<string, object>();
                foreach (var key in _keys)
                {
                    Dictionary<string, object> d;
                    _turboPrev[key] = _desired.TryGetValue(key, out d) ? Json.Get(d, "request") : null;
                }
                _turbo = true;
                SaveTurbo();
            }
            for (int i = 0; i < n; i++)
            {
                var req = new Dictionary<string, object> { { "mode", "fixed" }, { "percent", 100 } };
                try { ApplyCore(i, req); } catch (Exception ex) { Log.Warn("Turbo fan " + i + ": " + ex.Message); }
            }
            Log.Info("Turbo ON");
            return;
        }
        Dictionary<string, object> previous;
        lock (_sync) { previous = new Dictionary<string, object>(_turboPrev); _turbo = false; }
        for (int i = 0; i < n; i++)
        {
            object prev;
            previous.TryGetValue(_keys[i], out prev);
            try
            {
                var req = prev as Dictionary<string, object>;
                if (req != null) ApplyCore(i, req); else RestoreCore(i);
            }
            catch (Exception ex) { Log.Warn("Turbo off, fan " + i + ": " + ex.Message); }
        }
        lock (_sync) { _turboPrev.Clear(); SaveTurbo(); }
        Log.Info("Turbo OFF");
    }

    static void EnsureNotTurbo(bool turbo)
    {
        if (turbo) throw new ArgumentException("Desligue o Turbo para ajustar um fan individualmente.");
    }

    public void Start()
    {
        try { EnsureLoaded(); }
        catch (Exception ex) { Log.Warn("Fans: a placa ainda não respondeu (" + ex.Message + "); nova tentativa no próximo acesso."); }
        // Software-curve boards start with every header on the BIOS: put the user's curves back right away instead
        // of letting the watchdog find them "changed externally" a few seconds later.
        if (!_bridge.HardwareCurves) ReapplyDesired();
        _watchdog = new Timer(_ => Watchdog(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
    }

    public Dictionary<string, object> Describe()
    {
        EnsureLoaded();
        var curves = _bridge.GetCurves();
        var fans = new List<object>();
        var orphans = new List<object>();
        lock (_sync)
        {
            foreach (var c in curves)
            {
                string key = KeyOf(c);
                var d = c.ToDictionary();
                d["key"] = key;
                d["pointCount"] = PointCountFor(key, c);
                Dictionary<string, object> desired;
                d["managed"] = _desired.TryGetValue(key, out desired);
                d["request"] = desired == null ? null : Json.Get(desired, "request");
                d["hasBackup"] = _backup.ContainsKey(key);
                fans.Add(d);
            }
            foreach (var kv in _desired)
                if (Array.IndexOf(_keys, kv.Key) < 0)
                    orphans.Add(new Dictionary<string, object> { { "key", kv.Key }, { "title", TitleOf(kv.Value) } });
        }
        return new Dictionary<string, object> {
            { "supported", _bridge.FanControlSupported },
            { "backend", _bridge.BackendName },
            // false: FanPilot runs the curves; the panel explains that closing it hands the fans to the BIOS.
            { "hardwareCurves", _bridge.HardwareCurves },
            { "notice", _bridge.Notice },
            { "board", MachineData.Board },
            { "fans", fans },
            // Saved for a header this board no longer has: the panel offers to discard them.
            { "orphans", orphans },
            { "tempInputs", _bridge.TempInputTitles },
            { "turbo", TurboActive },
            { "limits", new Dictionary<string, object> {
                { "minDutyPercent", _cfg.MinDutyPercent }, { "fixedModeSafetyTemp", _cfg.FixedModeSafetyTemp },
                { "maxFullSpeedTemp", _cfg.MaxFullSpeedTemp } } }
        };
    }

    // request: { mode: "fixed", percent, tempInput? } | { mode: "curve", style: "SmartFanMode"|"StairStepMode", points: [[t,%]...], tempInput }
    public FanCurve Apply(int index, Dictionary<string, object> request)
    {
        EnsureNotTurbo(TurboActive);
        return ApplyCore(index, request);
    }

    FanCurve ApplyCore(int index, Dictionary<string, object> request)
    {
        string key = KeyAt(index);
        var current = _bridge.GetCurve(index);
        var curve = BuildCurve(key, request, current);
        var result = _bridge.SetCurve(curve);
        lock (_sync)
        {
            _desired[key] = new Dictionary<string, object> {
                { "request", request }, { "curve", curve.ToDictionary() }, { "signature", result.Signature() }, { "title", result.Title }
            };
            SaveDesired();
        }
        Log.Info("Fan " + index + " (" + result.Title + ") <- " + Json.Serialize(request));
        return result;
    }

    // The original curve's layout is what GCC/the board use; our own writes keep that count, so readbacks agree.
    int PointCountFor(string key, FanCurve current)
    {
        FanCurve original;
        lock (_sync) _backup.TryGetValue(key, out original);
        int n = (original ?? current).Points.Count;
        return n >= MinPointCount && n <= MaxPointCount ? n : DefaultPointCount;
    }

    static IEnumerable<double> FixedModeTemps(int pointCount)
    {
        if (pointCount == FixedModeTemps8.Length + 1) return FixedModeTemps8;
        int flat = pointCount - 1;
        return Enumerable.Range(0, flat).Select(k => Math.Round(70.0 * k / (flat - 1)));
    }

    FanCurve BuildCurve(string key, Dictionary<string, object> request, FanCurve current)
    {
        int pointCount = PointCountFor(key, current);
        string mode = Json.GetString(request, "mode", "");
        int tempInput = Json.GetInt(request, "tempInput", current.TempInput);
        if (tempInput < 0 || tempInput >= _bridge.TempInputTitles.Length) throw new ArgumentException("Sensor de temperatura inválido.");
        int minPwm = (int)Math.Ceiling(_cfg.MinDutyPercent * 2.55);
        var curve = new FanCurve { Index = current.Index, Key = key, Title = current.Title, TempInput = tempInput, DeltaT = Math.Max(1, current.DeltaT) };

        if (mode == "fixed")
        {
            int percent = AppConfig.Clamp(Json.GetInt(request, "percent", 100), _cfg.MinDutyPercent, 100);
            int pwm = Math.Max(minPwm, (int)Math.Round(percent * 2.55));
            curve.Mode = "SmartFanMode";
            foreach (double t in FixedModeTemps(pointCount)) curve.Points.Add(new[] { t, (double)pwm });
            // Safety: the Super I/O still ramps to 100% at this temperature, even if this app dies.
            curve.Points.Add(new[] { (double)_cfg.FixedModeSafetyTemp, 255.0 });
            return curve;
        }
        if (mode != "curve") throw new ArgumentException("Modo inválido.");

        curve.Mode = Json.GetString(request, "style", "SmartFanMode") == "StairStepMode" ? "StairStepMode" : "SmartFanMode";
        var raw = Json.ToList(Json.Get(request, "points"));
        if (raw.Count != pointCount) throw new ArgumentException("A curva deste fan precisa de exatamente " + pointCount + " pontos.");
        double prevT = -1, prevPwm = 0;
        for (int i = 0; i < raw.Count; i++)
        {
            var xy = Json.ToList(raw[i]);
            double t = Math.Round(Json.ToDouble(xy[0]));
            double pct = Json.ToDouble(xy[1]);
            if (i == 0) t = 0;                                  // never let the chip switch the fan off
            if (t <= prevT) throw new ArgumentException("As temperaturas da curva precisam ser crescentes.");
            if (t > _cfg.MaxFullSpeedTemp) throw new ArgumentException("Temperatura máxima permitida na curva: " + _cfg.MaxFullSpeedTemp + " °C.");
            double pwm = i == raw.Count - 1 ? 255 : Math.Round(Math.Max(0, Math.Min(100, pct)) * 2.55);
            pwm = Math.Max(pwm, Math.Max(minPwm, prevPwm));    // floor + monotonic: hotter never means slower
            curve.Points.Add(new[] { t, pwm });
            prevT = t;
            prevPwm = pwm;
        }
        return curve;
    }

    public FanCurve Restore(int index)
    {
        EnsureNotTurbo(TurboActive);
        return RestoreCore(index);
    }

    FanCurve RestoreCore(int index)
    {
        string key = KeyAt(index);
        FanCurve original;
        lock (_sync)
        {
            if (!_backup.TryGetValue(key, out original)) throw new ArgumentException("Sem backup para este fan.");
        }
        var result = _bridge.SetCurve(At(original, index));
        lock (_sync) { _desired.Remove(key); SaveDesired(); }
        Log.Info("Fan " + index + " restored to original curve");
        return result;
    }

    // A saved configuration whose header is gone is forgotten on request (it was never applied).
    public void DiscardOrphan(string key)
    {
        EnsureLoaded();
        lock (_sync)
        {
            if (Array.IndexOf(_keys, key) >= 0) throw new ArgumentException("Esse fan existe nesta placa: use \"Restaurar original\" nele.");
            if (!_desired.Remove(key)) throw new ArgumentException("Configuração não encontrada.");
            _turboPrev.Remove(key);
            SaveDesired();
            SaveTurbo();
        }
        Log.Info("Configuração órfã descartada: " + key);
    }

    // A saved curve (or backup) may come from another position: written to where its header is now.
    static FanCurve At(FanCurve curve, int index)
    {
        var c = FanCurve.FromDictionary(curve.ToDictionary());
        c.Index = index;
        return c;
    }

    static FanCurve SavedCurve(Dictionary<string, object> desired, int index)
    {
        return At(FanCurve.FromDictionary((Dictionary<string, object>)Json.Get(desired, "curve")), index);
    }

    void SaveDesired() { DataFile.WriteAtomic(_desiredPath, Versioned(_desired.ToDictionary(kv => kv.Key, kv => (object)kv.Value))); }

    void SaveBackup() { DataFile.WriteAtomic(_backupPath, Versioned(_backup.ToDictionary(kv => kv.Key, kv => (object)kv.Value.ToDictionary()))); }

    void SaveTurbo()
    {
        DataFile.WriteAtomic(_turboPath, new Dictionary<string, object> {
            { "version", FormatVersion }, { "active", _turbo }, { "prev", new Dictionary<string, object>(_turboPrev) } });
    }

    static Dictionary<string, object> Versioned(Dictionary<string, object> fans)
    {
        return new Dictionary<string, object> { { "version", FormatVersion }, { "fans", fans } };
    }

    // internal (like Watchdog): the tests call them directly instead of waiting for Start()'s timer.
    internal void ReapplyDesired()
    {
        try { EnsureLoaded(); }
        catch (Exception ex) { Log.Warn("Fans: não foi possível reaplicar as curvas salvas: " + ex.Message); return; }
        List<KeyValuePair<string, Dictionary<string, object>>> items;
        lock (_sync) items = _desired.ToList();
        int applied = 0;
        foreach (var kv in items)
        {
            int index = Array.IndexOf(_keys, kv.Key);
            if (index < 0) continue; // an orphan: its header is gone
            try
            {
                var result = _bridge.SetCurve(SavedCurve(kv.Value, index));
                lock (_sync) if (_desired.ContainsKey(kv.Key)) { kv.Value["signature"] = result.Signature(); SaveDesired(); }
                applied++;
            }
            catch (Exception ex) { Log.Warn("Fan " + index + ": não foi possível reaplicar a curva salva: " + ex.Message); }
        }
        if (applied > 0) Log.Info(applied + " curva(s) salvas reaplicadas pelo FanPilot.");
    }

    internal void Watchdog()
    {
        try { EnsureLoaded(); }
        catch (Exception ex) { Log.Warn("Watchdog: " + ex.Message); return; }
        List<KeyValuePair<string, Dictionary<string, object>>> items;
        lock (_sync) items = _desired.ToList();
        foreach (var kv in items)
        {
            int index = Array.IndexOf(_keys, kv.Key);
            if (index < 0) continue;
            try
            {
                var actual = _bridge.GetCurve(index);
                if (actual.Signature() == Json.GetString(kv.Value, "signature", "")) continue;
                var result = _bridge.SetCurve(SavedCurve(kv.Value, index));
                lock (_sync)
                {
                    // Keep whatever the hardware reports for this curve, so a lossy readback cannot cause a loop.
                    if (_desired.ContainsKey(kv.Key)) { kv.Value["signature"] = result.Signature(); SaveDesired(); }
                }
                Log.Warn("Fan " + index + " curve was changed externally; re-applied.");
            }
            catch (Exception ex) { Log.Warn("Watchdog fan " + index + ": " + ex.Message); }
        }
    }
}
