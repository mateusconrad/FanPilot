// Item 13: the --diagnose file is attached to a public issue, so what identifies the machine itself is left out,
// and it carries the link to the compatibility form.
using System;
using System.Linq;
using Xunit;

public class DiagnoseTests
{
    // Lines as LibreHardwareMonitor 0.9.6 writes them, with made-up values.
    const string Sample =
        "Mainboard Manufacturer: Gigabyte Technology Co., Ltd.\r\n" +
        "Mainboard Name: B760M GAMING X AX\r\n" +
        "Motherboard Serial: 230612345678\r\n" +
        "System Enclosure Serial: Default string\r\n" +
        "System Enclosure Asset Tag: PATRIMONIO-0042\r\n" +
        "System Enclosure SKU: Default string\r\n" +
        "Processor Serial: BFEBFBFF000B0671\r\n" +
        "Processor Serial:\r\n" +
        "Memory Device [0] Part Number: CMK32GX5M2B5600C36\r\n" +
        "System UUID: 03000200-0400-0500-0006-000700080009\r\n" +
        "SerialNumber: SN-XYZ-123\r\n" +
        "Device: \\\\?\\HID#VID_1E71&PID_2007#7&1a2b3c4d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}\r\n";

    [Fact]
    public void SerialNumbersAndUuidsAreLeftOut()
    {
        string r = Diagnose.Redact(Sample);
        foreach (string value in new[] { "230612345678", "PATRIMONIO-0042", "BFEBFBFF000B0671", "03000200-0400", "SN-XYZ-123", "4d1e55b2" })
            Assert.DoesNotContain(value, r);
        Assert.Contains("Motherboard Serial: (omitido)", r);
        Assert.Contains("System Enclosure Asset Tag: (omitido)", r);
        Assert.Contains("System UUID: (omitido)", r);
        // An empty value stays empty: nothing was there to hide.
        Assert.Contains("Processor Serial:\r\n", r);
    }

    [Fact]
    public void TheModelStays()
    {
        string r = Diagnose.Redact(Sample);
        Assert.Contains("Mainboard Name: B760M GAMING X AX", r);
        Assert.Contains("Memory Device [0] Part Number: CMK32GX5M2B5600C36", r);
        Assert.Contains("System Enclosure SKU: Default string", r);
    }

    [Fact]
    public void ReportLinkOpensTheFormWithTheBoardAndTheVersion()
    {
        const string board = "ASUSTeK COMPUTER INC. ROG STRIX B550-F GAMING (WI-FI)";
        var uri = new Uri(Diagnose.ReportUrl(board));
        Assert.Equal("https://github.com/mateusconrad/FanPilot/issues/new", uri.GetLeftPart(UriPartial.Path));
        var query = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Assert.Equal("compatibilidade.yml", query["template"]);
        Assert.Equal("[Compatibilidade] " + board, query["title"]);
        Assert.Equal(board, query["placa"]);
        Assert.Equal(AppVersion.Text, query["versao"]);
        Assert.Contains("placa%20desconhecida", Diagnose.ReportUrl(""));
    }
}
