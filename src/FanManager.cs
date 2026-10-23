// Policy layer over an IBoard (Gigabyte Control Center or LibreHardwareMonitor): validation/safety, original-curve backup, persistence and a watchdog that
// re-applies the user's curves if something else (sleep/resume, GCC, BIOS) overwrites them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

sealed class FanManager
{
    // Curves keep the point count the board reports (8 on the B760M GAMING X AX); used when a readback is unusable.
    const int DefaultPointCount = 8, MinPointCount = 3, MaxPointCount = 16;
    // Flat part of the fixed-mode curve for 8 points; other counts spread their points over the same 0..70 C range.
    static readonly double[] FixedModeTemps8 = { 0, 20, 30, 40, 50, 60, 70 };

    readonly IBoard _bridge;
    readonly AppConfig _cfg;
    readonly string _backupPath, _desiredPath, _turboPath;
    readonly object _sync = new object();
    // Turbo = every header at 100% (GPU untouched). Remembers each fan's previous request (null = unmanaged)
    // so turning it off puts things back exactly as they were. Persisted so it survives a restart.
    bool _turbo;
    Dictionary<int, object> _turboPrev = new Dictionary<int, object>();
    Dictionary<int, FanCurve> _backup = new Dictionary<int, FanCurve>();
    // What the user asked for, plus the hardware readback signature we expect to keep seeing.
    Dictionary<int, Dictionary<string, object>> _desired = new Dictionary<int, Dictionary<string, object>>();
    Timer _watchdog;

    public FanManager(IBoard bridge, AppConfig cfg, string dataDir)
    {
        _bridge = bridge;
        _cfg = cfg;
        _backupPath = Path.Combine(dataDir, "backup-curves.json");
        _desiredPath = Path.Combine(dataDir, "desired-curves.json");
        _turboPath = Path.Combine(dataDir, "turbo.json");
        if (File.Exists(_desiredPath))
        {
            foreach (var kv in Json.ParseObject(File.ReadAllText(_desiredPath)))
                _desired[int.Parse(kv.Key)] = (Dictionary<string, object>)kv.Value;
        }
        if (File.Exists(_turboPath))
        {
            var t = Json.ParseObject(File.ReadAllText(_turboPath));
            _turbo = Convert.ToBoolean(Json.Get(t, "active"));
            var prev = Json.Get(t, "prev") as Dictionary<string, object>;
            if (prev != null) foreach (var kv in prev) _turboPrev[int.Parse(kv.Key)] = kv.Value;
        }
    }

    public bool TurboActive { get { lock (_sync) return _turbo; } }

    public void SetTurbo(bool on)
    {
        if (on == TurboActive) return;
        int n = _bridge.CountFans();
        if (on && n == 0) throw new ArgumentException("Esta placa-mãe não oferece controle de fans pelo FanPilot.");
        if (on)
        {
            lock (_sync)
            {
                _turboPrev = new Dictionary<int, object>();
                for (int i = 0; i < n; i++)
                {
                    Dictionary<string, object> d;
                    _turboPrev[i] = _desired.TryGetValue(i, out d) ? Json.Get(d, "request") : null;
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
        Dictionary<int, object> previous;
        lock (_sync) { previous = new Dictionary<int, object>(_turboPrev); _turbo = false; }
        for (int i = 0; i < n; i++)
        {
            object prev;
            previous.TryGetValue(i, out prev);
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

    void SaveTurbo()
    {
        var prev = new Dictionary<string, object>();
        foreach (var kv in _turboPrev) prev[kv.Key.ToString()] = kv.Value;
        File.WriteAllText(_turboPath, Json.Serialize(new Dictionary<string, object> { { "active", _turbo }, { "prev", prev } }), Encoding.UTF8);
    }

    static void EnsureNotTurbo(bool turbo)
    {
        if (turbo) throw new ArgumentException("Desligue o Turbo para ajustar um fan individualmente.");
    }

    public void Start()
    {
        EnsureBackup();
        // Software-curve boards start with every header on the BIOS: put the user's curves back right away instead
        // of letting the watchdog find them "changed externally" a few seconds later.
        if (!_bridge.HardwareCurves) ReapplyDesired();
        _watchdog = new Timer(_ => Watchdog(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
    }

    // The first time we ever see the hardware, its curves are saved so "restore" can always go back to them.
    void EnsureBackup()
    {
        lock (_sync)
        {
            if (_backup.Count > 0) return;
            try
            {
                if (File.Exists(_backupPath))
                {
                    foreach (var kv in Json.ParseObject(File.ReadAllText(_backupPath)))
                        _backup[int.Parse(kv.Key)] = FanCurve.FromDictionary((Dictionary<string, object>)kv.Value);
                    return;
                }
                var curves = _bridge.GetCurves();
                if (curves.Count == 0) return;
                var d = new Dictionary<string, object>();
                foreach (var c in curves) { _backup[c.Index] = c; d[c.Index.ToString()] = c.ToDictionary(); }
                File.WriteAllText(_backupPath, Json.Serialize(d), Encoding.UTF8);
                Log.Info("Saved original fan curves to " + _backupPath);
            }
            catch (Exception ex) { Log.Warn("Could not back up fan curves yet: " + ex.Message); }
        }
    }

    public Dictionary<string, object> Describe()
    {
        EnsureBackup();
        var curves = _bridge.GetCurves();
        var fans = new List<object>();
        lock (_sync)
        {
            foreach (var c in curves)
            {
                var d = c.ToDictionary();
                d["pointCount"] = PointCountFor(c.Index, c);
                Dictionary<string, object> desired;
                d["managed"] = _desired.TryGetValue(c.Index, out desired);
                d["request"] = desired == null ? null : Json.Get(desired, "request");
                d["hasBackup"] = _backup.ContainsKey(c.Index);
                fans.Add(d);
            }
        }
        return new Dictionary<string, object> {
            { "supported", _bridge.FanControlSupported },
            { "backend", _bridge.BackendName },
            // false: FanPilot runs the curves; the panel explains that closing it hands the fans to the BIOS.
            { "hardwareCurves", _bridge.HardwareCurves },
            { "notice", _bridge.Notice },
            { "board", MachineData.Board },
            { "fans", fans },
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
        if (index < 0 || index >= _bridge.CountFans()) throw new ArgumentException("Fan inexistente.");
        var current = _bridge.GetCurve(index);
        var curve = BuildCurve(index, request, current);
        var result = _bridge.SetCurve(curve);
        lock (_sync)
        {
            _desired[index] = new Dictionary<string, object> {
                { "request", request }, { "curve", curve.ToDictionary() }, { "signature", result.Signature() }
            };
            SaveDesired();
        }
        Log.Info("Fan " + index + " (" + result.Title + ") <- " + Json.Serialize(request));
        return result;
    }

    // The original curve's layout is what GCC/the board use; our own writes keep that count, so readbacks agree.
    int PointCountFor(int index, FanCurve current)
    {
        FanCurve original;
        lock (_sync) _backup.TryGetValue(index, out original);
        int n = (original ?? current).Points.Count;
        return n >= MinPointCount && n <= MaxPointCount ? n : DefaultPointCount;
    }

    static IEnumerable<double> FixedModeTemps(int pointCount)
    {
        if (pointCount == FixedModeTemps8.Length + 1) return FixedModeTemps8;
        int flat = pointCount - 1;
        return Enumerable.Range(0, flat).Select(k => Math.Round(70.0 * k / (flat - 1)));
    }

    FanCurve BuildCurve(int index, Dictionary<string, object> request, FanCurve current)
    {
        int pointCount = PointCountFor(index, current);
        string mode = Json.GetString(request, "mode", "");
        int tempInput = Json.GetInt(request, "tempInput", current.TempInput);
        if (tempInput < 0 || tempInput >= _bridge.TempInputTitles.Length) throw new ArgumentException("Sensor de temperatura inválido.");
        int minPwm = (int)Math.Ceiling(_cfg.MinDutyPercent * 2.55);
        var curve = new FanCurve { Index = index, Title = current.Title, TempInput = tempInput, DeltaT = Math.Max(1, current.DeltaT) };

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
        EnsureBackup();
        FanCurve original;
        lock (_sync)
        {
            if (!_backup.TryGetValue(index, out original)) throw new ArgumentException("Sem backup para este fan.");
        }
        var result = _bridge.SetCurve(original);
        lock (_sync) { _desired.Remove(index); SaveDesired(); }
        Log.Info("Fan " + index + " restored to original curve");
        return result;
    }

    void SaveDesired()
    {
        var d = new Dictionary<string, object>();
        foreach (var kv in _desired) d[kv.Key.ToString()] = kv.Value;
        File.WriteAllText(_desiredPath, Json.Serialize(d), Encoding.UTF8);
    }

    void ReapplyDesired()
    {
        List<KeyValuePair<int, Dictionary<string, object>>> items;
        lock (_sync) items = _desired.ToList();
        foreach (var kv in items)
        {
            try
            {
                var result = _bridge.SetCurve(FanCurve.FromDictionary((Dictionary<string, object>)Json.Get(kv.Value, "curve")));
                lock (_sync) if (_desired.ContainsKey(kv.Key)) { kv.Value["signature"] = result.Signature(); SaveDesired(); }
            }
            catch (Exception ex) { Log.Warn("Fan " + kv.Key + ": não foi possível reaplicar a curva salva: " + ex.Message); }
        }
        if (items.Count > 0) Log.Info(items.Count + " curva(s) salvas reaplicadas pelo FanPilot.");
    }

    void Watchdog()
    {
        List<KeyValuePair<int, Dictionary<string, object>>> items;
        lock (_sync) items = _desired.ToList();
        foreach (var kv in items)
        {
            try
            {
                var actual = _bridge.GetCurve(kv.Key);
                if (actual.Signature() == Json.GetString(kv.Value, "signature", "")) continue;
                var curve = FanCurve.FromDictionary((Dictionary<string, object>)Json.Get(kv.Value, "curve"));
                var result = _bridge.SetCurve(curve);
                lock (_sync)
                {
                    // Keep whatever the hardware reports for this curve, so a lossy readback cannot cause a loop.
                    if (_desired.ContainsKey(kv.Key)) { kv.Value["signature"] = result.Signature(); SaveDesired(); }
                }
                Log.Warn("Fan " + kv.Key + " curve was changed externally; re-applied.");
            }
            catch (Exception ex) { Log.Warn("Watchdog fan " + kv.Key + ": " + ex.Message); }
        }
    }
}
