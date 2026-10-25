// Item 02: no web client and no slow motherboard read may hold up the sampler or the GPU's safety rule.
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class SseHubTests
{
    static byte[] Frame(string s) { return Encoding.UTF8.GetBytes(s); }

    static bool WaitFor(Func<bool> condition, int ms = 3000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < ms) { if (condition()) return true; Thread.Sleep(10); }
        return condition();
    }

    [Fact]
    public void PublishNeverWaitsForAClientThatStoppedReading()
    {
        var hub = new SseHub();
        var stuck = new TaskCompletionSource<bool>(); // a write that never finishes: the phone stopped reading
        var healthy = new ConcurrentQueue<string>();
        hub.Add(_ => stuck.Task, () => { }, null);
        hub.Add(b => { healthy.Enqueue(Encoding.UTF8.GetString(b)); return Task.FromResult(true); }, () => { }, null);

        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++) hub.Publish(Frame("snap " + i));
        Assert.True(watch.ElapsedMilliseconds < 500, "Publish levou " + watch.ElapsedMilliseconds + " ms");
        // Eventually: what matters above is that Publish returned at once.
        Assert.True(WaitFor(() => healthy.Contains("snap 49"), 10000), "o cliente saudável não recebeu o último frame");
    }

    [Fact]
    public void ASlowClientGetsTheNewestFrameNotABacklog()
    {
        var hub = new SseHub();
        var gate = new TaskCompletionSource<bool>();
        var written = new ConcurrentQueue<string>();
        hub.Add(b =>
        {
            written.Enqueue(Encoding.UTF8.GetString(b));
            return written.Count == 1 ? gate.Task : Task.FromResult(true); // the first write hangs a while
        }, () => { }, Frame("first"));
        Assert.True(WaitFor(() => written.Count == 1));
        hub.Publish(Frame("old"));
        hub.Publish(Frame("newer"));
        hub.Publish(Frame("newest"));
        gate.SetResult(true);
        Assert.True(WaitFor(() => written.Count == 2));
        Thread.Sleep(100);
        Assert.Equal(new[] { "first", "newest" }, written.ToArray());
    }

    [Fact]
    public void AClientStuckLongerThanTheTimeoutIsDropped()
    {
        var hub = new SseHub(TimeSpan.FromMilliseconds(200));
        bool aborted = false;
        hub.Add(_ => new TaskCompletionSource<bool>().Task, () => aborted = true, Frame("first"));
        Assert.True(WaitFor(() => aborted && hub.Count == 0), "o cliente travado não foi derrubado");
    }

    [Fact]
    public void AFailedWriteDropsTheClient()
    {
        var hub = new SseHub();
        bool aborted = false;
        hub.Add(_ => { var t = new TaskCompletionSource<bool>(); t.SetException(new System.IO.IOException("conexão fechada")); return t.Task; }, () => aborted = true, Frame("first"));
        Assert.True(WaitFor(() => aborted && hub.Count == 0));
    }

    [Fact]
    public void HubIsLimitedToSixteenClients()
    {
        var hub = new SseHub();
        var aborts = new Action[SseHub.MaxClients];
        for (int i = 0; i < SseHub.MaxClients; i++) Assert.True(hub.Add(_ => Task.FromResult(true), () => { }, null));
        Assert.False(hub.Add(_ => Task.FromResult(true), () => { }, null));
        hub.CloseAll();
        Assert.Equal(0, hub.Count);
        Assert.True(hub.Add(_ => Task.FromResult(true), () => { }, null));
    }
}

public sealed class GpuSafetyTests : IDisposable
{
    readonly TempDir _dir = new TempDir();

    public void Dispose() { _dir.Dispose(); }

    [Fact]
    public void ManualFanGoesBackToAutoWhenTheGpuOverheats()
    {
        var gpu = new FakeGpu(83) { Temp = 70 };
        gpu.SetFixed(50);
        gpu.Tick();
        Assert.Equal(50, gpu.Manual);
        gpu.Temp = 84;
        gpu.Tick();
        Assert.Null(gpu.Manual);
        Assert.Equal("auto", gpu.Latest["fanMode"]);
        var note = (System.Collections.Generic.Dictionary<string, object>)gpu.Latest["safetyNote"];
        Assert.Equal("gpu.safetyNote", note["code"]);
        Assert.Contains("84", (string)note["text"]);
    }

    // The motherboard read (Gigabyte's IPC) hangs for 10 s per sample: the GPU rule still acts within its own second.
    [Fact]
    public void GpuSafetyDoesNotWaitForASlowMotherboard()
    {
        var board = new FakeBoard(4) { ReadDelayMs = 10000 };
        var gpu = new FakeGpu(83) { Temp = 70 };
        gpu.SetFixed(50);
        var sampler = new Sampler(board, gpu);
        gpu.Start();
        sampler.Start();
        try
        {
            gpu.Temp = 90;
            var watch = Stopwatch.StartNew();
            while (gpu.Manual != null && watch.ElapsedMilliseconds < 4000) Thread.Sleep(50);
            Assert.Null(gpu.Manual);
        }
        finally
        {
            sampler.Stop();
            gpu.Stop();
        }
    }
}
