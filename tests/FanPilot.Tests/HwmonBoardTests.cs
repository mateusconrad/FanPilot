// Item 28b: the motherboard on Linux through hwmon, against a fake /sys in a scratch folder laid out as the kernel's
// (sys/class/hwmon/hwmonN a link to the chip's folder under sys/devices, with a "device" link next to its files).
// The chips follow the kernel's documentation of nct6775, k10temp and amdgpu; real boards come in through --diagnose.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

sealed class FakeSys : IDisposable
{
    readonly TempDir _dir = new TempDir();

    public string Root { get { return _dir.Path; } }

    public void Dispose() { _dir.Dispose(); }

    // hwmonN of the chip "name" on the device "device", with these files; returns the chip's folder.
    public string Chip(int n, string name, string device, Dictionary<string, string> files)
    {
        string deviceDir = Path.Combine(Root, "sys", "devices", "platform", device);
        string chipDir = Path.Combine(deviceDir, "hwmon", "hwmon" + n);
        Directory.CreateDirectory(chipDir);
        File.WriteAllText(Path.Combine(chipDir, "name"), name + "\n");
        // "device/…" files are the device's own (gpu_busy_percent, mem_info_*), reached through the link.
        foreach (var f in files)
        {
            string path = f.Key.StartsWith("device/") ? Path.Combine(deviceDir, f.Key.Substring("device/".Length)) : Path.Combine(chipDir, f.Key);
            File.WriteAllText(path, f.Value + "\n");
        }
        Directory.CreateSymbolicLink(Path.Combine(chipDir, "device"), deviceDir);
        string classDir = Path.Combine(Root, "sys", "class", "hwmon");
        Directory.CreateDirectory(classDir);
        Directory.CreateSymbolicLink(Path.Combine(classDir, "hwmon" + n), chipDir);
        return chipDir;
    }

    public void Modules(params string[] names)
    {
        Directory.CreateDirectory(Path.Combine(Root, "proc"));
        File.WriteAllText(Path.Combine(Root, "proc", "modules"), string.Concat(names.Select(m => m + " 81920 0 - Live 0x0000000000000000\n")));
    }

    // The board of most tests: an NCT6798D with two controllable headers (on SmartFan IV, 5), and a Ryzen.
    public string Nct6798(int n = 2)
    {
        return Chip(n, "nct6798", "nct6775.656", new Dictionary<string, string> {
            { "temp1_input", "36000" }, { "temp1_label", "SYSTIN" }, { "temp2_input", "41500" }, { "temp2_label", "CPUTIN" },
            { "fan1_input", "812" }, { "fan2_input", "1340" }, { "in0_input", "1032" }, { "in0_label", "Vcore" },
            { "pwm1", "102" }, { "pwm1_enable", "5" }, { "pwm2", "153" }, { "pwm2_enable", "5" },
        });
    }

    public string K10temp(int n = 1)
    {
        return Chip(n, "k10temp", "0000:00:18.3", new Dictionary<string, string> {
            { "temp1_input", "55250" }, { "temp1_label", "Tctl" }, { "temp3_input", "52000" }, { "temp3_label", "Tccd1" } });
    }

    public static string Read(string chipDir, string file) { return File.ReadAllText(Path.Combine(chipDir, file)).Trim(); }
    public static void Write(string chipDir, string file, string value) { File.WriteAllText(Path.Combine(chipDir, file), value); }
}

public sealed class HwmonBoardTests : IDisposable
{
    readonly FakeSys _sys = new FakeSys();

    public void Dispose() { _sys.Dispose(); }

    HwmonBoard Open() { return new HwmonBoard(_sys.Root, true, false); }

    static FanCurve Curve(int index, int tempInput)
    {
        return new FanCurve { Index = index, TempInput = tempInput, Mode = "SmartFanMode", DeltaT = 1,
            Points = new List<double[]> { new[] { 30.0, 80 }, new[] { 60.0, 160 }, new[] { 80.0, 255 } } };
    }

    string RestoreFile { get { return HwmonBoard.RestorePath(_sys.Root); } }

    [Fact]
    public void FindsTheBoardsSensorsAndHeadersAndTheCpu()
    {
        _sys.K10temp(1);
        _sys.Nct6798(2);
        _sys.Chip(3, "amdgpu", "0000:03:00.0", new Dictionary<string, string> { { "temp1_input", "48000" }, { "pwm1", "80" }, { "pwm1_enable", "2" } });
        _sys.Chip(4, "nvme", "nvme0", new Dictionary<string, string> { { "temp1_input", "39850" } });
        var b = Open();
        Assert.Equal("Linux hwmon", b.BackendName);
        Assert.False(b.HardwareCurves);
        Assert.Null(b.Notice);
        Assert.Equal(2, b.CountFans()); // the GPU's fan is the GPU's, not the board's
        Assert.Equal(new[] { "CPU", "SYSTIN", "CPUTIN" }, b.TempInputTitles);
        var curves = b.GetCurves();
        Assert.Equal(new[] { "hwmon/nct6798@nct6775.656/pwm1", "hwmon/nct6798@nct6775.656/pwm2" }, curves.Select(c => c.Key));
        Assert.All(curves, c => Assert.True(c.IsBios));
        Assert.Equal("Fan 1", curves[0].Title);

        List<float> temps, rpms;
        b.ReadFanTach(out temps, out rpms);
        Assert.Equal(new[] { 55.25f, 36f, 41.5f }, temps);
        Assert.Equal(new[] { 812f, 1340f }, rpms);
        var readings = b.ReadSensors();
        var cpu = Assert.Single(readings, r => r.Role == CpuRoles.Main);
        Assert.Equal(55.25, cpu.Value);
        Assert.Contains(readings, r => r.Type == "Fan" && r.Value == 1340 && r.Id == "fan-hwmon/nct6798@nct6775.656/fan2");
        Assert.Contains(readings, r => r.Type == "Voltage" && r.Title == "Vcore" && r.Value == 1.032);
        Assert.DoesNotContain(readings, r => r.Value == 39.85 || r.Value == 48); // NVMe and GPU stay out of the board
        b.Shutdown();
        Assert.Equal("5", FakeSys.Read(Path.Combine(_sys.Root, "sys", "devices", "platform", "nct6775.656", "hwmon", "hwmon2"), "pwm1_enable"));
    }

    [Fact]
    public void AHeaderKeepsItsKeyWhenTheHwmonNumbersChange()
    {
        string before;
        using (var other = new FakeSys())
        {
            other.K10temp(1);
            other.Nct6798(2);
            before = new HwmonBoard(other.Root, true, false).GetCurve(1).Key;
        }
        _sys.Nct6798(0); // another boot: the board's chip registered first
        _sys.K10temp(5);
        var b = Open();
        Assert.Equal(before, b.GetCurve(1).Key);
        Assert.Equal("CPU", b.TempInputTitles[0]);
    }

    [Fact]
    public void ACurveTakesTheHeaderAndTheBiosGetsItBackAsItWas()
    {
        _sys.K10temp(1);
        string chip = _sys.Nct6798(2);
        var b = Open();
        b.SetCurve(Curve(0, 0)); // follows the CPU, 55 °C
        Assert.Equal("1", FakeSys.Read(chip, "pwm1_enable"));
        int duty = int.Parse(FakeSys.Read(chip, "pwm1"));
        Assert.InRange(duty, 1, 255);
        Assert.Equal("5", FakeSys.Read(chip, "pwm2_enable")); // the other header is left alone
        Assert.Contains("pwm1_enable", File.ReadAllText(RestoreFile));
        Assert.False(b.GetCurve(0).IsBios);
        Assert.Equal(duty, Math.Round(b.Duties()[0].Value * 255 / 100)); // what the terminal shows (item 29), exact here
        Assert.Null(b.Duties()[1]);

        var bios = new FanCurve { Index = 0, Mode = FanCurve.BiosMode };
        b.SetCurve(bios);
        Assert.Equal("5", FakeSys.Read(chip, "pwm1_enable"));
        Assert.True(b.GetCurve(0).IsBios);
        Assert.False(File.Exists(RestoreFile));
        Assert.Null(b.Duties()[0]);
        b.Shutdown();
    }

    [Fact]
    public void TheCurveIsReassertedEverySecond()
    {
        _sys.K10temp(1);
        string chip = _sys.Nct6798(2);
        var b = Open();
        b.SetCurve(Curve(0, 0));
        FakeSys.Write(chip, "pwm1_enable", "5"); // the BIOS took it back (after sleep, for instance)
        b.Tick();
        Assert.Equal("1", FakeSys.Read(chip, "pwm1_enable"));
        b.Shutdown();
        Assert.Equal("5", FakeSys.Read(chip, "pwm1_enable")); // and still goes back to what it was before FanPilot
    }

    [Fact]
    public void ShutdownHandsEveryHeaderBack()
    {
        _sys.K10temp(1);
        string chip = _sys.Nct6798(2);
        var b = Open();
        b.SetCurve(Curve(0, 0));
        b.SetCurve(Curve(1, 1));
        b.Shutdown();
        Assert.Equal("5", FakeSys.Read(chip, "pwm1_enable"));
        Assert.Equal("5", FakeSys.Read(chip, "pwm2_enable"));
        Assert.False(File.Exists(RestoreFile));
        Assert.Throws<Unavailable>(() => b.SetCurve(Curve(0, 0)));
    }

    [Fact]
    public void WhatAKilledFanPilotLeftIsUndoneByRestoreFans()
    {
        _sys.K10temp(1);
        string chip = _sys.Nct6798(2);
        Open().SetCurve(Curve(1, 0)); // and then kill -9: no Shutdown
        Assert.Equal("1", FakeSys.Read(chip, "pwm2_enable"));
        Assert.Equal(1, HwmonBoard.RestoreFans(_sys.Root));
        Assert.Equal("5", FakeSys.Read(chip, "pwm2_enable"));
        Assert.False(File.Exists(RestoreFile));
        Assert.Equal(0, HwmonBoard.RestoreFans(_sys.Root)); // nothing left: ExecStopPost after a clean stop
    }

    [Fact]
    public void WhatAKilledFanPilotLeftIsUndoneByTheNextOne()
    {
        _sys.K10temp(1);
        string chip = _sys.Nct6798(2);
        Open().SetCurve(Curve(0, 0));
        var next = Open();
        Assert.Equal("5", FakeSys.Read(chip, "pwm1_enable"));
        next.SetCurve(Curve(0, 0));
        next.Shutdown();
        Assert.Equal("5", FakeSys.Read(chip, "pwm1_enable")); // the original is still SmartFan IV, not manual
    }

    [Fact]
    public void ADiagnoseDoesNotUndoWhatARunningFanPilotHolds()
    {
        _sys.K10temp(1);
        string chip = _sys.Nct6798(2);
        var running = Open();
        running.SetCurve(Curve(0, 0));
        var readOnly = new HwmonBoard(_sys.Root, false, false);
        readOnly.Shutdown();
        Assert.Equal("1", FakeSys.Read(chip, "pwm1_enable"));
        Assert.True(File.Exists(RestoreFile));
        running.Shutdown();
    }

    [Fact]
    public void AHeaderAlreadyOnManualGoesBackAtFullSpeed()
    {
        _sys.K10temp(1);
        string chip = _sys.Nct6798(2);
        FakeSys.Write(chip, "pwm1_enable", "1"); // some other tool left it on manual: no automatic mode is known
        var b = Open();
        b.SetCurve(Curve(0, 0));
        b.Shutdown();
        Assert.Equal("1", FakeSys.Read(chip, "pwm1_enable"));
        Assert.Equal("255", FakeSys.Read(chip, "pwm1"));
    }

    [Fact]
    public void AReferenceSensorThatStopsReadingSendsTheHeaderToTheBios()
    {
        _sys.K10temp(1);
        string chip = _sys.Nct6798(2);
        var b = Open();
        b.SetCurve(Curve(0, 1)); // SYSTIN
        FakeSys.Write(chip, "temp1_input", "-273000"); // an open thermistor
        b.Tick();
        Assert.Equal("5", FakeSys.Read(chip, "pwm1_enable"));
        Assert.Equal("board.problem.sensor", Assert.Single(b.ControlProblems()).Key);
        FakeSys.Write(chip, "temp1_input", "37000");
        b.Tick();
        Assert.Equal("1", FakeSys.Read(chip, "pwm1_enable"));
        Assert.Empty(b.ControlProblems());
        b.Shutdown();
    }

    [Fact]
    public void AHeaderTheChipRefusesStaysOnTheBiosWithTheReason()
    {
        _sys.K10temp(1);
        string chip = _sys.Nct6798(2);
        File.Delete(Path.Combine(chip, "pwm1"));
        // Still listed, but writing it fails (a link to nowhere), as a driver refusing would, even for root.
        File.CreateSymbolicLink(Path.Combine(chip, "pwm1"), Path.Combine(_sys.Root, "nowhere", "pwm1"));
        var b = Open();
        var ex = Assert.Throws<Unavailable>(() => b.SetCurve(Curve(0, 0)));
        Assert.Equal("board.hwmon.writeFailed", ex.Msg.Key);
        Assert.True(b.GetCurve(0).IsBios);
        Assert.Equal("5", FakeSys.Read(chip, "pwm1_enable"));
        b.Shutdown();
    }

    [Fact]
    public void WithoutAControllableChipTheNoticeSaysWhatToDo()
    {
        _sys.K10temp(1);
        var b = Open();
        Assert.Equal("board.hwmon.noChip", b.Notice.Key);
        Assert.False(b.FanControlSupported);
        Assert.Equal(new[] { "CPU" }, b.TempInputTitles);
        Assert.Contains(b.ReadSensors(), r => r.Role == CpuRoles.Main);

        _sys.Modules("nct6775", "k10temp"); // loaded, no chip: the BIOS keeps it through ACPI
        Assert.Equal("module", Open().Notice.Args.Keys.Single());
        Assert.Equal("board.hwmon.loadedNoChip", Open().Notice.Key);

        _sys.Chip(2, "it8689", "it87.2608", new Dictionary<string, string> { { "temp1_input", "30000" }, { "fan1_input", "900" }, { "pwm1", "128" } });
        var noEnable = Open();
        Assert.Equal("board.hwmon.noControl", noEnable.Notice.Key);
        Assert.Equal(0, noEnable.CountFans()); // pwm without pwm_enable: no way to hand it back, so not offered
        Assert.Contains(noEnable.ReadSensors(), r => r.Type == "Fan" && r.Value == 900);
    }

    [Fact]
    public void TheDiagnoseReportListsEveryChipAndChangesNothing()
    {
        _sys.K10temp(1);
        string chip = _sys.Nct6798(2);
        _sys.Modules("nct6775", "k10temp");
        using (var data = new TempDir())
        {
            Assert.Equal(0, Diagnose.Run(data.Path, new AppConfig(), false, _sys.Root));
            string report = File.ReadAllText(Directory.GetFiles(data.Path, "diagnostico-*.txt").Single());
            Assert.Contains("hwmon2: nct6798 @ nct6775.656", report);
            Assert.Contains("pwm2_enable = 5", report);
            Assert.Contains("temp1_label = Tctl", report);
            Assert.Contains("nct6775", report);
            Assert.DoesNotContain("name =", report);
        }
        Assert.Equal("5", FakeSys.Read(chip, "pwm1_enable"));
    }
}

public sealed class HwmonGpuTests : IDisposable
{
    readonly FakeSys _sys = new FakeSys();

    public void Dispose() { _sys.Dispose(); }

    [Fact]
    public void AnAmdCardIsReadAndItsFanSetAndGivenBack()
    {
        string chip = _sys.Chip(3, "amdgpu", "0000:03:00.0", new Dictionary<string, string> {
            { "temp1_input", "61000" }, { "pwm1", "128" }, { "pwm1_enable", "2" }, { "power1_average", "152300000" },
            { "freq1_input", "2450000000" }, { "device/gpu_busy_percent", "37" }, { "device/mem_info_vram_used", "2147483648" },
            { "device/mem_info_vram_total", "17163091968" } });
        var gpu = HwmonGpu.Find(83, _sys.Root);
        Assert.NotNull(gpu);
        var r = gpu.Read();
        Assert.Equal(true, r["ok"]);
        Assert.Equal(61, r["temp"]);
        Assert.Equal(new List<int> { 50 }, r["fans"]);
        Assert.Equal(152.3, r["power"]);
        Assert.Equal(2450, r["clock"]);
        Assert.Equal(37, r["util"]);
        Assert.Equal(2048L, r["memUsed"]);
        Assert.Equal("auto", r["fanMode"]);

        gpu.SetFixed(70);
        Assert.Equal("1", FakeSys.Read(chip, "pwm1_enable"));
        Assert.Equal("178", FakeSys.Read(chip, "pwm1")); // 70% of 255
        Assert.Equal("fixed", gpu.Read()["fanMode"]);
        gpu.Shutdown();
        Assert.Equal("2", FakeSys.Read(chip, "pwm1_enable"));
    }

    [Fact]
    public void AnIntelCardIsReadOnlyAndNoCardIsNull()
    {
        Assert.Null(HwmonGpu.Find(83, _sys.Root));
        _sys.Chip(1, "xe", "0000:00:02.0", new Dictionary<string, string> { { "power1_input", "9000000" } });
        var gpu = HwmonGpu.Find(83, _sys.Root);
        Assert.Equal(9.0, gpu.Read()["power"]);
        Assert.Empty((List<int>)gpu.Read()["fans"]);
        Assert.Equal("gpu.noFans", Assert.Throws<Unavailable>(() => gpu.SetFixed(50)).Msg.Key);
    }
}
