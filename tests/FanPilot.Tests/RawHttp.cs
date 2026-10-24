// Minimal HTTP/1.1 client over a socket to 127.0.0.1: full control of the Host header (HttpClient on .NET
// Framework rewrites it), and lines read as they arrive for the SSE stream.
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

static class RawHttp
{
    public sealed class Response
    {
        public int Status;
        public string Head;
        public string Body;
    }

    public static Response Send(int port, string method, string path, string host = null, string json = null, params string[] headers)
    {
        using (var tcp = new TcpClient())
        {
            tcp.Connect(IPAddress.Loopback, port);
            var stream = tcp.GetStream();
            stream.ReadTimeout = 10000;
            WriteRequest(stream, port, method, path, host, json, headers);
            string text = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
            int split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            string head = split < 0 ? text : text.Substring(0, split);
            return new Response
            {
                Status = int.Parse(head.Split(' ')[1]),
                Head = head,
                Body = split < 0 ? "" : text.Substring(split + 4)
            };
        }
    }

    // An open /api/stream connection that has received its response headers (the caller keeps it, then closes it).
    public static TcpClient OpenStream(int port, string path)
    {
        var tcp = new TcpClient();
        tcp.Connect(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        stream.ReadTimeout = 10000;
        WriteRequest(stream, port, "GET", path, null, null, new string[0]);
        var reader = new StreamReader(stream, Encoding.ASCII);
        string status = reader.ReadLine();
        if (status == null || !status.Contains(" 200 ")) { tcp.Close(); throw new IOException("stream recusado: " + status); }
        return tcp;
    }

    // First "data:" line of a Server-Sent Events stream that satisfies the predicate, or null on timeout.
    public static string FirstEvent(int port, string path, Func<string, bool> accept, TimeSpan timeout)
    {
        using (var tcp = new TcpClient())
        {
            tcp.Connect(IPAddress.Loopback, port);
            var stream = tcp.GetStream();
            stream.ReadTimeout = (int)timeout.TotalMilliseconds;
            WriteRequest(stream, port, "GET", path, null, null, new string[0]);
            var reader = new StreamReader(stream, Encoding.UTF8);
            var deadline = DateTime.UtcNow + timeout;
            try
            {
                while (DateTime.UtcNow < deadline)
                {
                    string line = reader.ReadLine();
                    if (line == null) return null;
                    if (line.StartsWith("data: ", StringComparison.Ordinal) && accept(line.Substring(6))) return line.Substring(6);
                }
            }
            catch (IOException) { }
            return null;
        }
    }

    static void WriteRequest(NetworkStream stream, int port, string method, string path, string host, string json, string[] headers)
    {
        var body = json == null ? new byte[0] : Encoding.UTF8.GetBytes(json);
        var sb = new StringBuilder();
        sb.Append(method).Append(' ').Append(path).Append(" HTTP/1.1\r\n");
        sb.Append("Host: ").Append(host ?? "localhost:" + port).Append("\r\n");
        sb.Append("Connection: close\r\n");
        foreach (var h in headers) sb.Append(h).Append("\r\n");
        if (json != null) sb.Append("Content-Type: application/json\r\nContent-Length: ").Append(body.Length).Append("\r\n");
        sb.Append("\r\n");
        var head = Encoding.ASCII.GetBytes(sb.ToString());
        stream.Write(head, 0, head.Length);
        stream.Write(body, 0, body.Length);
    }
}
