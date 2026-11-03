using System.Collections.Generic;
using Xunit;

// The Uniwill/Tongfang notebook fan (UniwillEc.cs) against a fake EC: no notebook needed.
public class UniwillTests
{
    sealed class FakeEc : IUniwillEc
    {
        public readonly Dictionary<int, int> Ram = new Dictionary<int, int>();
        public readonly List<KeyValuePair<int, int>> Writes = new List<KeyValuePair<int, int>>();

        public int Read(int address) { int v; return Ram.TryGetValue(address, out v) ? v : 0; }

        public void Write(int address, int value)
        {
            Writes.Add(new KeyValuePair<int, int>(address, value));
            Ram[address] = value;
        }
    }

    // Six even steps over 0..100%, rounded up: levels 1..5 (0x81..0x85), then boost (0x40).
    [Theory]
    [InlineData(0, 0x81)]
    [InlineData(10, 0x81)]
    [InlineData(100.0 / 6, 0x81)]
    [InlineData(17, 0x82)]
    [InlineData(25, 0x82)]
    [InlineData(50, 0x83)]
    [InlineData(51, 0x84)]
    [InlineData(80, 0x85)]
    [InlineData(500.0 / 6, 0x85)]
    [InlineData(84, 0x40)]
    [InlineData(100, 0x40)]
    [InlineData(150, 0x40)]
    [InlineData(-5, 0x81)]
    public void PercentBecomesTheLevelThatCoolsAtLeastAsMuch(double percent, int expected)
    {
        Assert.Equal(expected, UniwillFan.ValueFor(percent));
    }

    [Fact]
    public void EachLevelReportsTheTopOfItsStep()
    {
        Assert.Equal(16.67, UniwillFan.PercentOf(0x81).Value, 2);
        Assert.Equal(50.0, UniwillFan.PercentOf(0x83).Value, 6);
        Assert.Equal(83.33, UniwillFan.PercentOf(0x85).Value, 2);
        Assert.Equal(100.0, UniwillFan.PercentOf(0x40));
        Assert.Null(UniwillFan.PercentOf(0x00));
        Assert.Null(UniwillFan.PercentOf(0x80));
        Assert.Null(UniwillFan.PercentOf(0x86));
        // What a level reports maps back to the same level: the panel's speed and the EC agree.
        for (int v = 0x81; v <= 0x85; v++) Assert.Equal(v, UniwillFan.ValueFor(UniwillFan.PercentOf(v).Value));
    }

    [Fact]
    public void SetWritesTheLevelOnceAndReturnsWhatTheFanWillDo()
    {
        var ec = new FakeEc();
        var fan = new UniwillFanOutput(ec);
        Assert.Equal(50.0, fan.Set(40), 6);
        Assert.Equal(0x83, ec.Ram[UniwillFan.ControlRegister]);
        fan.Set(45); // same level: nothing to write
        Assert.Single(ec.Writes);
        fan.Set(60);
        Assert.Equal(0x84, ec.Ram[UniwillFan.ControlRegister]);
        Assert.Equal(2, ec.Writes.Count);
    }

    // The EC forgets the byte across sleep, and the vendor app may change it: the next second puts it back.
    [Fact]
    public void ALevelTheEcLostIsWrittenAgain()
    {
        var ec = new FakeEc();
        var fan = new UniwillFanOutput(ec);
        fan.Set(40);
        ec.Ram[UniwillFan.ControlRegister] = 0x00; // back from sleep
        fan.Set(40);
        Assert.Equal(0x83, ec.Ram[UniwillFan.ControlRegister]);
        Assert.Equal(2, ec.Writes.Count);
    }

    [Fact]
    public void DefaultHandsTheFanBackToTheEc()
    {
        var ec = new FakeEc();
        var fan = new UniwillFanOutput(ec);
        fan.Set(100);
        Assert.Equal(0x40, ec.Ram[UniwillFan.ControlRegister]);
        fan.Default();
        Assert.Equal(0x00, ec.Ram[UniwillFan.ControlRegister]);
        Assert.Equal(0f, fan.Rpm());
    }

    // FanPilot's first Default at startup must write even when the byte already reads 0: it may never have written.
    [Fact]
    public void TheFirstDefaultAlwaysWrites()
    {
        var ec = new FakeEc();
        new UniwillFanOutput(ec).Default();
        Assert.Single(ec.Writes);
    }

    [Theory]
    [InlineData(0x80, true)]
    [InlineData(0xC5, true)]
    [InlineData(0x7F, false)]
    [InlineData(0x00, false)]
    public void TheFanIsUsedOnlyWhenTheEcSaysItHasTheControl(int support, bool expected)
    {
        var ec = new FakeEc();
        ec.Ram[UniwillFan.SupportRegister] = support;
        int seen;
        Assert.Equal(expected, UniwillFan.HasFanControl(ec, out seen));
        Assert.Equal(support, seen);
        Assert.Empty(ec.Writes);
    }
}
