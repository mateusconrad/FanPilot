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
    readonly string _backupPath, _desiredPath, _turboPath, _modePath, _metaPath;
    readonly object _sync = new object();
    // Everything that changes fans runs under this lock, one operation at a time: Turbo, editing a fan, Restore, the
    // watchdog and the re-apply never interleave (two quick taps on Turbo used to be able to save a wrong "previous"
    // state). _sync only guards the dictionaries; the order is always _opLock, then _sync.
    readonly object _opLock = new object();
    // The state files as read, until the board's headers are known: a version 1 file needs them to be migrated.
    readonly Dictionary<string, object> _rawBackup, _rawDesired, _rawTurbo, _rawMode;
    // The board's headers by position, i.e. what the API's indexes mean this session. Null until the board answered.
    string[] _keys, _titles;
    // The global mode (item 18): "custom" (the user's own settings, which _desired holds) or "profile:<name>" (a preset
    // on every fan but the pumps). Turbo (every header at 100%, the GPU untouched) is a layer on top of either. While a
    // profile or Turbo is on, _personal keeps the user's own request for each header (null: FanPilot did not manage
    // it), so going back puts things exactly as they were. All of it is in global-mode.json and survives a restart.
    string _mode = "custom";
    bool _turbo;
    Dictionary<string, object> _personal = new Dictionary<string, object>();
    Dictionary<string, FanCurve> _backup = new Dictionary<string, FanCurve>();
    // What the user asked for, plus the hardware readback signature we expect to keep seeing. An entry whose key no
    // header has any more is an orphan: kept, shown in the panel, never applied.
    Dictionary<string, Dictionary<string, object>> _desired = new Dictionary<string, Dictionary<string, object>>();
    // Names and roles the user gave (FanMeta), by key; a header without an entry uses FanMeta.Default.
    readonly Dictionary<string, Dictionary<string, object>> _meta = new Dictionary<string, Dictionary<string, object>>();
    Timer _watchdog;
    // How many times the watchdog found a curve overwritten (by GCC, the BIOS…) and put it back; --hardware-test
    // expects none while it watches a curve.
    internal int ExternalChanges;

    public FanManager(IBoard bridge, AppConfig cfg, string dataDir)
    {
        _bridge = bridge;
        _cfg = cfg;
        _backupPath = Path.Combine(dataDir, "backup-curves.json");
        _desiredPath = Path.Combine(dataDir, "desired-curves.json");
        _turboPath = Path.Combine(dataDir, "turbo.json");
        _modePath = Path.Combine(dataDir, "global-mode.json");
        _metaPath = Path.Combine(dataDir, "fan-meta.json");
        _rawBackup = DataFile.Read(_backupPath);
        _rawDesired = DataFile.Read(_desiredPath);
        _rawMode = DataFile.Read(_modePath);
        // Written before the global modes: its Turbo state is converted once the board's headers are known.
        _rawTurbo = _rawMode == null ? DataFile.Read(_turboPath) : null;
        _turbo = _rawMode != null ? Json.Get(_rawMode, "turbo") is bool on && on : _rawTurbo != null && Json.Get(_rawTurbo, "active") is bool active && active;
        string mode = Json.GetString(_rawMode, "mode", "custom");
        _mode = FanPresets.IsProfile(mode) ? mode : "custom";
        // Written by this version on: keyed from the start, nothing to migrate.
        var meta = Json.Get(DataFile.Read(_metaPath), "fans") as Dictionary<string, object>;
        if (meta != null)
            foreach (var kv in meta)
                if (kv.Value is Dictionary<string, object> entry) _meta[kv.Key] = entry;
    }

    public bool TurboActive { get { lock (_sync) return _turbo; } }

    public string BackendName { get { return _bridge.BackendName; } }

    // The first call that needs the board reads its headers, then the state files (migrating version 1 ones), and
    // saves the original curve of every header seen for the first time. Throws while the board does not answer;
    // the next call tries again. A board that reported no header is asked again on every call until it reports some:
    // at boot, Gigabyte's service answers "no fan control" for a while (GigabyteBridge), and the saved curves must not
    // stay orphans for the whole session.
    void EnsureLoaded()
    {
        lock (_sync)
        {
            if (_keys != null && (_keys.Length > 0 || _bridge.CountFans() == 0)) return;
            var curves = _bridge.GetCurves();
            _titles = curves.Select(c => c.Title ?? "").ToArray();
            _keys = curves.Select(KeyOf).ToArray();

            bool migrated = false;
            _desired = ByKey(_rawDesired, v => v as Dictionary<string, object>, e => TitleOf(e), ref migrated);
            _backup = ByKey(_rawBackup, v => v is Dictionary<string, object> d ? FanCurve.FromDictionary(d) : null, c => c.Title, ref migrated);
            var prev = (Json.Get(_rawMode, "personal") ?? Json.Get(_rawTurbo, "prev")) as Dictionary<string, object>;
            bool turboV1 = _rawTurbo != null && Json.GetInt(_rawTurbo, "version", 1) < FormatVersion;
            if (prev != null)
                foreach (var kv in prev)
                {
                    int index;
                    if (!turboV1) _personal[kv.Key] = kv.Value;
                    else if (int.TryParse(kv.Key, out index) && index >= 0 && index < _keys.Length) _personal[_keys[index]] = kv.Value;
                }
            if (migrated || turboV1)
            {
                // Builds older than the keys cannot read version 2: the version 1 files stay next to it as .v1.
                foreach (var path in new[] { _desiredPath, _backupPath, _turboPath })
                    if (File.Exists(path) && !File.Exists(path + ".v1")) File.Copy(path, path + ".v1");
                SaveDesired();
                SaveBackup();
                Log.Info("Curve files converted to the format keyed by fan (the old ones were kept as .v1).");
            }
            if (_rawTurbo != null)
            {
                SaveMode();
                File.Delete(_turboPath);
                Log.Info("turbo.json converted to global-mode.json.");
            }
            // With no header at all, the board has not shown its fans (yet): nothing to say about the saved settings.
            if (_keys.Length > 0)
                foreach (var kv in _desired)
                    if (Array.IndexOf(_keys, kv.Key) < 0)
                        Log.Warn("The settings saved for '" + TitleOf(kv.Value) + "' match no fan on this board and were not applied.");
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
            AppNotices.Add(new Msg("notice.backupLost", "fans", string.Join(", ", missing), "count", missing.Count));
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
        if (index < 0 || index >= _keys.Length) throw new UserError("err.fanMissing");
        return _keys[index];
    }

    public void SetTurbo(bool on)
    {
        lock (_opLock) SetTurboCore(on);
    }

    void SetTurboCore(bool on)
    {
        if (on == TurboActive) return;
        EnsureLoaded();
        int n = _keys.Length;
        if (on && n == 0) throw new UserError("err.noFanControl");
        if (on)
        {
            lock (_sync)
            {
                if (_mode == "custom") _personal = Personal();
                _turbo = true;
                SaveMode();
            }
            for (int i = 0; i < n; i++)
            {
                var req = new Dictionary<string, object> { { "mode", "fixed" }, { "percent", 100 } };
                try { ApplyCore(i, req); } catch (Exception ex) { Log.Warn("Turbo fan " + i + ": " + ex.Message); }
            }
            Log.Info("Turbo ON");
            return;
        }
        lock (_sync) { _turbo = false; SaveMode(); }
        ApplyMode(true);
        Log.Info("Turbo OFF");
    }

    // "custom" or "profile:<name>" (FanPresets). Under Turbo, only remembered: it takes effect when Turbo turns off.
    public string Mode { get { lock (_sync) return _mode; } }

    public void SetMode(string mode)
    {
        lock (_opLock)
        {
            if (mode != "custom" && !FanPresets.IsProfile(mode)) throw new UserError("err.globalMode");
            EnsureLoaded();
            if (_keys.Length == 0) throw new UserError("err.noFanControl");
            bool turbo;
            lock (_sync)
            {
                if (mode == _mode) return;
                // Leaving the user's own settings: they are what "Personalizado" brings back.
                if (_mode == "custom" && !_turbo) _personal = Personal();
                _mode = mode;
                turbo = _turbo;
                SaveMode();
            }
            Log.Info("Mode: " + mode + (turbo ? " (takes effect when Turbo goes off)" : ""));
            if (!turbo) ApplyMode(false);
        }
    }

    // Writes the current mode to the headers, Turbo off: a profile's curve on each fan that is not a pump, or the user's
    // own settings back. Pumps keep the user's own setting in a profile, so they are written only after Turbo, which
    // had put them at 100%.
    void ApplyMode(bool afterTurbo)
    {
        string mode;
        Dictionary<string, object> personal;
        lock (_sync) { mode = _mode; personal = new Dictionary<string, object>(_personal); }
        for (int i = 0; i < _keys.Length; i++)
        {
            try
            {
                bool pump = IsPump(i);
                if (mode != "custom" && !pump) { ApplyCore(i, ProfileRequest(mode, i)); continue; }
                if (pump && !afterTurbo) continue;
                object prev;
                personal.TryGetValue(_keys[i], out prev);
                var req = prev as Dictionary<string, object>;
                if (req != null) ApplyCore(i, req); else RestoreCore(i);
            }
            catch (Exception ex) { Log.Warn("Mode " + mode + ", fan " + i + ": " + ex.Message); }
        }
        if (mode == "custom") lock (_sync) { _personal.Clear(); SaveMode(); }
    }

    // Editing or restoring one fan while a profile is on: the mode becomes custom, and the other fans keep the profile's
    // curve as their own setting (the panel says so before).
    void LeaveProfile()
    {
        lock (_sync)
        {
            if (_mode == "custom") return;
            Log.Info("Mode " + _mode + " left by editing a fan: its curves stay as the user's own settings.");
            _mode = "custom";
            _personal.Clear();
            SaveMode();
        }
    }

    // The user's own request for every header right now (null: FanPilot does not manage it). Call with _sync held.
    Dictionary<string, object> Personal()
    {
        var personal = new Dictionary<string, object>();
        foreach (var key in _keys)
        {
            Dictionary<string, object> d;
            personal[key] = _desired.TryGetValue(key, out d) ? Json.Get(d, "request") : null;
        }
        return personal;
    }

    bool IsPump(int index) { lock (_sync) return MetaFor(_keys[index], _titles[index]).IsPump; }

    // A profile's curve for header i: the preset at the header's point count and limits, on the sensor and with the
    // response it uses now. "profile" in the request tells the panel where the curve came from.
    Dictionary<string, object> ProfileRequest(string mode, int index)
    {
        string preset = mode.Substring("profile:".Length);
        var current = _bridge.GetCurve(index);
        Dictionary<string, object> desired;
        lock (_sync) _desired.TryGetValue(_keys[index], out desired);
        var now = Json.Get(desired, "request") as Dictionary<string, object>;
        int tempInput = Json.GetInt(now, "tempInput", current.TempInput);
        var points = FanPresets.Points(preset, PointCountFor(_keys[index], current), _cfg.MaxFullSpeedTemp, _cfg.MinDutyPercent);
        return new Dictionary<string, object> {
            { "mode", "curve" }, { "style", "SmartFanMode" }, { "points", points.Select(p => (object)new object[] { p[0], p[1] }).ToList() },
            { "tempInput", tempInput }, { "response", Json.GetString(now, "response", SoftwareCurveEngine.DefaultResponse) },
            { "source", Json.GetString(now, "source", SoftwareCurveEngine.SensorSource) }, { "profile", preset }
        };
    }

    static void EnsureNotTurbo(bool turbo)
    {
        if (turbo) throw new UserError("err.turboOn");
    }

    public void Start()
    {
        try { EnsureLoaded(); }
        catch (Exception ex) { Log.Warn("Fans: the board has not answered yet (" + ex.Message + "); trying again on the next request."); }
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
                foreach (var kv in MetaFor(key, c.Title).ToDictionary()) d[kv.Key] = kv.Value;
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
            { "notice", _bridge.Notice == null ? null : _bridge.Notice.ToJson() },
            { "board", MachineData.Board },
            { "fans", fans },
            // Saved for a header this board no longer has: the panel offers to discard them.
            { "orphans", orphans },
            { "tempInputs", _bridge.TempInputTitles },
            { "turbo", TurboActive },
            { "mode", Mode },
            { "presets", FanPresets.Describe() },
            { "limits", new Dictionary<string, object> {
                { "minDutyPercent", _cfg.MinDutyPercent }, { "fixedModeSafetyTemp", _cfg.FixedModeSafetyTemp },
                { "maxFullSpeedTemp", _cfg.MaxFullSpeedTemp } } }
        };
    }

    // request: { mode: "fixed", percent, tempInput? }
    //        | { mode: "curve", style: "SmartFanMode"|"StairStepMode", points: [[t,%]...], tempInput, response?,
    //            source?: "sensor" (default) | "gpu" | "max" }
    public FanCurve Apply(int index, Dictionary<string, object> request)
    {
        lock (_opLock)
        {
            EnsureNotTurbo(TurboActive);
            LeaveProfile();
            return ApplyCore(index, request);
        }
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
        if (tempInput < 0 || tempInput >= _bridge.TempInputTitles.Length) throw new UserError("err.tempInput");
        // A pump's protection holds for every request (editor, Turbo, profiles, re-applying): "full" is 100% whatever
        // was asked, "min" raises the floor.
        FanMeta meta;
        lock (_sync) meta = MetaFor(key, current.Title);
        int floor = meta.IsPump && meta.PumpPolicy == "min" ? Math.Max(_cfg.MinDutyPercent, meta.PumpMinPercent) : _cfg.MinDutyPercent;
        if (meta.IsPump && meta.PumpPolicy == "full")
        {
            mode = "fixed";
            request = new Dictionary<string, object> { { "mode", "fixed" }, { "percent", 100 } };
        }
        int minPwm = (int)Math.Ceiling(floor * 2.55);
        var curve = new FanCurve { Index = current.Index, Key = key, Title = current.Title, TempInput = tempInput, DeltaT = Math.Max(1, current.DeltaT) };

        if (mode == "fixed")
        {
            int percent = AppConfig.Clamp(Json.GetInt(request, "percent", 100), floor, 100);
            int pwm = Math.Max(minPwm, (int)Math.Round(percent * 2.55));
            curve.Mode = "SmartFanMode";
            foreach (double t in FixedModeTemps(pointCount)) curve.Points.Add(new[] { t, (double)pwm });
            // Safety: the Super I/O still ramps to 100% at this temperature, even if this app dies.
            curve.Points.Add(new[] { (double)_cfg.FixedModeSafetyTemp, 255.0 });
            return curve;
        }
        if (mode != "curve") throw new UserError("err.mode");

        curve.Mode = Json.GetString(request, "style", "SmartFanMode") == "StairStepMode" ? "StairStepMode" : "SmartFanMode";
        curve.Response = Json.GetString(request, "response", SoftwareCurveEngine.DefaultResponse);
        if (!SoftwareCurveEngine.Responses.ContainsKey(curve.Response)) throw new UserError("err.response");
        // The GPU as the reference (item 25): only where FanPilot runs the curve; the chip only reads its own sensors.
        string source = Json.GetString(request, "source", SoftwareCurveEngine.SensorSource);
        if (source != SoftwareCurveEngine.SensorSource && source != SoftwareCurveEngine.GpuSource && source != SoftwareCurveEngine.MaxSource)
            throw new UserError("err.source");
        if (source != SoftwareCurveEngine.SensorSource && _bridge.HardwareCurves) throw new UserError("err.source.chip");
        curve.Source = source == SoftwareCurveEngine.SensorSource ? null : source;
        var raw = Json.ToList(Json.Get(request, "points"));
        if (raw.Count != pointCount) throw new UserError("err.curve.points", "n", pointCount);
        double prevT = -1, prevPwm = 0;
        for (int i = 0; i < raw.Count; i++)
        {
            var xy = Json.ToList(raw[i]);
            double t = Math.Round(Json.ToDouble(xy[0]));
            double pct = Json.ToDouble(xy[1]);
            if (i == 0) t = 0;                                  // never let the chip switch the fan off
            if (t <= prevT) throw new UserError("err.curve.increasing");
            if (t > _cfg.MaxFullSpeedTemp) throw new UserError("err.curve.maxTemp", "max", _cfg.MaxFullSpeedTemp);
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
        lock (_opLock)
        {
            EnsureNotTurbo(TurboActive);
            LeaveProfile();
            return RestoreCore(index);
        }
    }

    FanCurve RestoreCore(int index)
    {
        string key = KeyAt(index);
        FanCurve original;
        lock (_sync)
        {
            if (!_backup.TryGetValue(key, out original)) throw new UserError("err.noBackup");
        }
        var result = _bridge.SetCurve(At(original, index));
        lock (_sync) { _desired.Remove(key); SaveDesired(); }
        Log.Info("Fan " + index + " restored to original curve");
        return result;
    }

    // POST /api/fans/{i}/meta. A new role or pump protection is written to the header at once (its saved request
    // again, or 100% for a pump that FanPilot did not manage yet); a new name only shows.
    public Dictionary<string, object> SetMeta(int index, Dictionary<string, object> body)
    {
        lock (_opLock)
        {
            string key = KeyAt(index);
            string title = _titles[index];
            var meta = FanMeta.Parse(body, title);
            FanMeta before;
            Dictionary<string, object> desired;
            lock (_sync)
            {
                before = MetaFor(key, title);
                if (!meta.SameProtection(before) && _turbo) throw new UserError("err.turboPump");
                _meta[key] = meta.ToDictionary();
                SaveMeta();
                _desired.TryGetValue(key, out desired);
            }
            Log.Info("Fan " + index + " (" + title + "): " + Json.Serialize(meta.ToDictionary()));
            if (!meta.SameProtection(before))
            {
                var request = Json.Get(desired, "request") as Dictionary<string, object>;
                if (request != null) ApplyCore(index, request);
                else if (meta.IsPump && meta.PumpPolicy == "full") ApplyCore(index, new Dictionary<string, object> { { "mode", "fixed" }, { "percent", 100 } });
            }
            return meta.ToDictionary();
        }
    }

    // A saved configuration whose header is gone is forgotten on request (it was never applied).
    public void DiscardOrphan(string key)
    {
        lock (_opLock)
        {
            EnsureLoaded();
            lock (_sync)
            {
                if (Array.IndexOf(_keys, key) >= 0) throw new UserError("err.orphan.exists");
                if (!_desired.Remove(key)) throw new UserError("err.orphan.missing");
                _personal.Remove(key);
                SaveDesired();
                SaveMode();
            }
        }
        Log.Info("Orphan settings discarded: " + key);
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
        var curve = At(FanCurve.FromDictionary((Dictionary<string, object>)Json.Get(desired, "curve")), index);
        // Saved before responses existed: a curve gets the default one, a fixed speed stays as it is.
        if (curve.Response == null && Json.GetString(Json.Get(desired, "request") as Dictionary<string, object>, "mode", "") == "curve")
            curve.Response = SoftwareCurveEngine.DefaultResponse;
        return curve;
    }

    void SaveDesired() { DataFile.WriteAtomic(_desiredPath, Versioned(_desired.ToDictionary(kv => kv.Key, kv => (object)kv.Value))); }

    void SaveMeta()
    {
        DataFile.WriteAtomic(_metaPath, new Dictionary<string, object> {
            { "version", 1 }, { "fans", _meta.ToDictionary(kv => kv.Key, kv => (object)kv.Value) } });
    }

    // The name and role the user gave this header, or the default for its title. Call with _sync held.
    FanMeta MetaFor(string key, string title)
    {
        Dictionary<string, object> saved;
        if (!_meta.TryGetValue(key, out saved)) return FanMeta.Default(title);
        try { return FanMeta.Parse(saved, title); }
        catch (ArgumentException ex)
        {
            Log.Warn("fan-meta.json: the settings for '" + title + "' are invalid (" + ex.Message + "); using the defaults.");
            return FanMeta.Default(title);
        }
    }

    void SaveBackup() { DataFile.WriteAtomic(_backupPath, Versioned(_backup.ToDictionary(kv => kv.Key, kv => (object)kv.Value.ToDictionary()))); }

    void SaveMode()
    {
        DataFile.WriteAtomic(_modePath, new Dictionary<string, object> {
            { "version", 1 }, { "mode", _mode }, { "turbo", _turbo }, { "personal", new Dictionary<string, object>(_personal) } });
    }

    static Dictionary<string, object> Versioned(Dictionary<string, object> fans)
    {
        return new Dictionary<string, object> { { "version", FormatVersion }, { "fans", fans } };
    }

    // internal (like Watchdog): the tests call them directly instead of waiting for Start()'s timer.
    internal void ReapplyDesired()
    {
        lock (_opLock) ReapplyDesiredCore();
    }

    void ReapplyDesiredCore()
    {
        try { EnsureLoaded(); }
        catch (Exception ex) { Log.Warn("Fans: could not apply the saved curves again: " + ex.Message); return; }
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
            catch (Exception ex) { Log.Warn("Fan " + index + ": could not apply the saved curve again: " + ex.Message); }
        }
        if (applied > 0) Log.Info(applied + " saved curve(s) applied again by FanPilot.");
    }

    internal void Watchdog()
    {
        lock (_opLock) WatchdogCore();
    }

    void WatchdogCore()
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
                ExternalChanges++;
                Log.Warn("Fan " + index + " curve was changed externally; re-applied.");
            }
            catch (Exception ex) { Log.Warn("Watchdog fan " + index + ": " + ex.Message); }
        }
    }
}

// The headers' curves, names and roles (FanManager.Describe), refreshed every 30 s: what each fan is told to do, for
// readers that look every second (the alerts) or at every scrape (the integrations, item 23) without asking the board
// each time.
sealed class FanListCache
{
    static readonly TimeSpan For = TimeSpan.FromSeconds(30);

    readonly FanManager _fans;
    readonly Func<DateTime> _now;
    readonly object _sync = new object();
    List<Dictionary<string, object>> _list;
    DateTime _at = DateTime.MinValue;

    public FanListCache(FanManager fans) : this(fans, () => DateTime.UtcNow) { }

    internal FanListCache(FanManager fans, Func<DateTime> now) { _fans = fans; _now = now; }

    // null until the board answers once.
    public List<Dictionary<string, object>> Get()
    {
        lock (_sync)
        {
            if (_list != null && _now() - _at < For) return _list;
            try
            {
                _list = ((List<object>)_fans.Describe()["fans"]).Cast<Dictionary<string, object>>().ToList();
                _at = _now();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException) { }
            return _list;
        }
    }

    // The speed (0..100%) a fan of the list is told to run at now: its curve at the temperature it follows (item 25's
    // sources included), from a snapshot's tempInputs and GPU reading. null when the BIOS runs it or nothing reads.
    // Where FanPilot runs the curves, the speed actually set follows this one smoothed (SoftwareCurveEngine).
    public static double? DutyNow(Dictionary<string, object> fan, IList<double> temps, double? gpu)
    {
        var curve = FanCurve.FromDictionary(fan);
        if (curve.IsBios || temps == null) return null;
        double t = SoftwareCurveEngine.SourceTemp(curve, temps.Select(v => (float)v).ToList(), gpu);
        return double.IsNaN(t) ? (double?)null : curve.PwmAt(t) / 2.55;
    }
}
