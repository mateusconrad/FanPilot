// The fan of Uniwill/Tongfang notebooks (Avell G1513 and its relatives), through the WMI interface the vendor's own
// app (MyAPP / MyFanBoost) uses: root\WMI, AcpiTest_MULong.GetSetULong, one byte of the embedded controller's RAM per
// call. No driver: the BIOS's ACPI does the port I/O. See docs/NOTEBOOK-UNIWILL.md.
//
// The EC offers no curve and no duty cycle here, only a byte at 0x0751: 0x00 the EC decides (the "BIOS"), 0x40 boost
// (full speed), 0x80 + n a fixed level n = 1..5. LhmBoard runs FanPilot's curve as usual and UniwillFanOutput turns
// each percentage into the level that cools at least as much. The EC forgets the byte across sleep and boot (the
// vendor app writes it again on every PowerModeChanged), so the value is read back every second and rewritten.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

// One byte of EC RAM at a time; the tests use a fake.
interface IUniwillEc
{
    int Read(int address);
    void Write(int address, int value);
}

sealed class UniwillWmiEc : IUniwillEc, IDisposable
{
    const string Scope = @"root\WMI", ClassName = "AcpiTest_MULong", Method = "GetSetULong";
    // The instance MyFanBoost asks for by name; any other instance of the class is the fallback.
    const string PreferredInstance = @"ACPI\PNP0C14\1_1";
    // Data for a read: 2^40 + address; for a write: (value << 16) + address. The value comes back in the low byte.
    const ulong ReadFlag = 1UL << 40;

    readonly ManagementObject _obj;
    readonly object _sync = new object();

    UniwillWmiEc(ManagementObject obj) { _obj = obj; }

    // The interface, or null on any PC without it (desktops, other notebooks). Never throws.
    public static UniwillWmiEc TryOpen()
    {
        try
        {
            using (var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM " + ClassName))
            {
                var all = searcher.Get().Cast<ManagementObject>().ToList();
                if (all.Count == 0) return null;
                var chosen = all.FirstOrDefault(o => (o["InstanceName"] as string) == PreferredInstance) ?? all[0];
                foreach (var o in all) if (o != chosen) o.Dispose();
                return new UniwillWmiEc(chosen);
            }
        }
        catch (ManagementException) { return null; } // the class doesn't exist here
        catch (Exception ex)
        {
            Log.Warn("Uniwill EC: WMI query failed: " + ex.Message);
            return null;
        }
    }

    public int Read(int address)
    {
        return (int)(Call(ReadFlag + (ulong)address) & 0xFF);
    }

    public void Write(int address, int value)
    {
        Call(((ulong)(value & 0xFF) << 16) + (ulong)address);
    }

    ulong Call(ulong data)
    {
        lock (_sync)
        {
            using (var input = _obj.GetMethodParameters(Method))
            {
                input["Data"] = data;
                using (var output = _obj.InvokeMethod(Method, input, null))
                    return Convert.ToUInt64(output["Return"]);
            }
        }
    }

    public void Dispose() { _obj.Dispose(); }
}

static class UniwillFan
{
    public const int ControlRegister = 0x0751;
    // Bit 7 of this byte is what the vendor app checks before offering its fan control ("FanBoost" support).
    public const int SupportRegister = 0x0765, SupportFanBoost = 0x80;
    public const int Auto = 0x00, Boost = 0x40, LevelBase = 0x80, Levels = 5;

    // The vendor's control app: running alongside FanPilot, both would write 0x0751.
    public static readonly string[] VendorProcesses = { "MyFanBoost", "MyAPPTray", "MyAPP" };

    // The byte for a duty of percent (0..100): the six steps (levels 1..5, then boost) split 0..100% evenly, and the
    // step chosen is the one at or above the percentage, so the fan never cools less than the curve asks.
    // Assumed until the probe measures each level's speed on the notebook.
    public static int ValueFor(double percent)
    {
        // The tolerance keeps a step's own top (PercentOf) on that step despite floating point.
        int step = (int)Math.Ceiling(percent * (Levels + 1) / 100.0 - 1e-9);
        step = Math.Max(1, Math.Min(Levels + 1, step));
        return step > Levels ? Boost : LevelBase + step;
    }

    // What the panel shows for a byte: the top of its step; null for the EC's own control (or an unknown byte).
    public static double? PercentOf(int value)
    {
        if (value == Boost) return 100;
        int level = value - LevelBase;
        if (level >= 1 && level <= Levels) return level * 100.0 / (Levels + 1);
        return null;
    }

    // The notebook's fan when this is a Uniwill EC that has the fan control; null otherwise. supportByte: for the log.
    public static bool HasFanControl(IUniwillEc ec, out int supportByte)
    {
        supportByte = ec.Read(SupportRegister);
        return (supportByte & SupportFanBoost) != 0;
    }
}

// The notebook's single fan as an LhmBoard output.
sealed class UniwillFanOutput : IFanOutput
{
    readonly IUniwillEc _ec;
    int? _written;

    public UniwillFanOutput(IUniwillEc ec) { _ec = ec; }

    // Writes only when the byte is not already what is wanted: the EC's own value is read every time, so a sleep or
    // the vendor app that changed it is undone on the next tick.
    public double Set(double percent)
    {
        int value = UniwillFan.ValueFor(percent);
        Ensure(value);
        return UniwillFan.PercentOf(value) ?? 0;
    }

    public void Default() { Ensure(UniwillFan.Auto); }

    void Ensure(int value)
    {
        int now = _ec.Read(UniwillFan.ControlRegister);
        if (now != value || _written != value)
        {
            _ec.Write(UniwillFan.ControlRegister, value);
            _written = value;
        }
    }

    // The fan's speed is not known yet (the vendor app never reads it): 0, as a header without a tachometer.
    public float Rpm() { return 0; }
}
