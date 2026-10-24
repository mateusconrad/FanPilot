// Server-Sent Events fan-out for /api/stream. Publish only swaps each client's pending frame and signals it, so a
// phone that stops reading (screen off, weak Wi-Fi) can never hold up the sampler. Each client has its own writer;
// one stuck on a write for longer than the timeout is dropped.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

sealed class SseHub
{
    public const int MaxClients = 16;

    sealed class Client
    {
        public Func<byte[], Task> Write;
        public Action Abort;
        public byte[] Pending; // newest frame not yet written; a newer one replaces it (Interlocked)
        public readonly SemaphoreSlim Signal = new SemaphoreSlim(0, 1);
        public int Closed;
    }

    readonly object _sync = new object();
    readonly List<Client> _clients = new List<Client>();
    readonly TimeSpan _writeTimeout;

    public SseHub() : this(TimeSpan.FromSeconds(10)) { }

    // writeTimeout: the tests use a short one.
    internal SseHub(TimeSpan writeTimeout) { _writeTimeout = writeTimeout; }

    public int Count { get { lock (_sync) return _clients.Count; } }

    // write sends one frame to the client; abort drops its connection. Returns false when the hub is full.
    public bool Add(Func<byte[], Task> write, Action abort, byte[] first)
    {
        var c = new Client { Write = write, Abort = abort, Pending = first };
        lock (_sync)
        {
            if (_clients.Count >= MaxClients) return false;
            _clients.Add(c);
        }
        Task.Run(() => Pump(c));
        if (first != null) Wake(c);
        return true;
    }

    public void Publish(byte[] frame)
    {
        Client[] clients;
        lock (_sync) clients = _clients.ToArray();
        foreach (var c in clients)
        {
            Volatile.Write(ref c.Pending, frame);
            Wake(c);
        }
    }

    public void CloseAll()
    {
        Client[] clients;
        lock (_sync) clients = _clients.ToArray();
        foreach (var c in clients) Remove(c);
    }

    static void Wake(Client c)
    {
        try { if (c.Signal.CurrentCount == 0) c.Signal.Release(); }
        catch (SemaphoreFullException) { } // already signalled
    }

    async Task Pump(Client c)
    {
        try
        {
            while (Volatile.Read(ref c.Closed) == 0)
            {
                await c.Signal.WaitAsync().ConfigureAwait(false);
                var frame = Interlocked.Exchange(ref c.Pending, null);
                if (frame == null) continue;
                var write = c.Write(frame);
                if (await Task.WhenAny(write, Task.Delay(_writeTimeout)).ConfigureAwait(false) != write) return; // stuck
                await write.ConfigureAwait(false); // a failed write (client gone) ends the loop
            }
        }
        catch (Exception) { }
        finally { Remove(c); }
    }

    void Remove(Client c)
    {
        if (Interlocked.Exchange(ref c.Closed, 1) == 1) return;
        lock (_sync) _clients.Remove(c);
        Wake(c); // lets a Pump waiting for a frame notice the close
        try { c.Abort(); } catch { }
    }
}
