using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

public class FanCurveTests
{
    // tests/fixtures/curves.json is also checked against curveAt() in the panel by tests/js/curve.test.mjs.
    [Fact]
    public void PwmAtMatchesTheFixtureSharedWithThePanel()
    {
        var fixture = Json.ParseObject(File.ReadAllText(Path.Combine(TestEnv.OutputDir, "fixtures", "curves.json")));
        var curves = (Dictionary<string, object>)fixture["curves"];
        var cases = Json.ToList(fixture["cases"]);
        Assert.NotEmpty(cases);
        foreach (Dictionary<string, object> c in cases)
        {
            var def = (Dictionary<string, object>)curves[(string)c["curve"]];
            var curve = new FanCurve { Mode = (string)def["mode"] };
            foreach (object p in Json.ToList(def["points"]))
            {
                var xy = Json.ToList(p);
                curve.Points.Add(new[] { Json.ToDouble(xy[0]), Json.ToDouble(xy[1]) });
            }
            double t = Json.ToDouble(c["t"]), expected = Json.ToDouble(c["pwm"]), actual = curve.PwmAt(t);
            Assert.True(Math.Abs(actual - expected) < 1e-9, c["curve"] + " a " + t + " °C: esperado " + expected + ", veio " + actual);
        }
    }

    // Unlike the panel (which shows "–"), the backend treats a curve it cannot read as full speed.
    [Fact]
    public void CurveWithoutPointsMeansFullSpeed()
    {
        Assert.Equal(255, new FanCurve().PwmAt(50));
    }

    [Fact]
    public void SignatureCoversTheSensorAndTheRoundedPoints()
    {
        var c = new FanCurve { TempInput = 2, Mode = "SmartFanMode" };
        c.Points.Add(new[] { 0.0, 51.4 });
        c.Points.Add(new[] { 30.6, 255.0 });
        Assert.Equal("2|0,51;31,255", c.Signature());
        Assert.Equal("bios|0|", new FanCurve { Mode = FanCurve.BiosMode }.Signature());
    }

    [Fact]
    public void DictionaryRoundTripKeepsEverything()
    {
        var c = FakeBoard.DefaultCurve(3, 2);
        c.Title = "System 3";
        c.Mode = "StairStepMode";
        c.DeltaT = 4;
        var back = FanCurve.FromDictionary(Json.ParseObject(Json.Serialize(c.ToDictionary())));
        Assert.Equal(c.Signature(), back.Signature());
        Assert.Equal("System 3", back.Title);
        Assert.Equal("StairStepMode", back.Mode);
        Assert.Equal(4, back.DeltaT);
        Assert.Equal(3, back.Index);
    }
}
