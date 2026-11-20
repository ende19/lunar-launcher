using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LunarLauncher;

/// <summary>
/// Minimal HTTP listener on 127.0.0.1 for the Discord OAuth redirect. Uses a raw socket rather than
/// HttpListener so it needs no admin rights or URL ACL. Only GET requests with small headers are handled.
/// </summary>
internal sealed class LoopbackServer : IDisposable
{
    private readonly TcpListener listener;

    public LoopbackServer(int port)
    {
        listener = new TcpListener(IPAddress.Loopback, port);
        try { listener.Start(); }
        catch (SocketException error)
        {
            throw new LauncherException($"La porta {port} è già in uso: chiudi altri launcher aperti e riprova.", error);
        }
    }

    public async Task<LoopbackRequest> NextAsync(CancellationToken cancel)
    {
        while (true)
        {
            var client = await listener.AcceptTcpClientAsync(cancel);
            try
            {
                var stream = client.GetStream();
                var head = await ReadHeadAsync(stream, cancel);
                var line = head.Split("\r\n", 2)[0].Split(' ');
                if (line.Length < 2 || line[0] != "GET") { await LoopbackRequest.WriteAsync(stream, 405, "", cancel); client.Dispose(); continue; }
                var target = line[1];
                var split = target.IndexOf('?');
                var path = split < 0 ? target : target[..split];
                var query = ParseQuery(split < 0 ? "" : target[(split + 1)..]);
                return new LoopbackRequest(client, path, query);
            }
            catch (Exception) when (!cancel.IsCancellationRequested)
            {
                client.Dispose();
            }
        }
    }

    private static async Task<string> ReadHeadAsync(NetworkStream stream, CancellationToken cancel)
    {
        var buffer = new byte[16 * 1024];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancel);
            if (read == 0) break;
            length += read;
            if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n")) break;
        }
        return Encoding.ASCII.GetString(buffer, 0, length);
    }

    public static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
            var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result[key] = value;
        }
        return result;
    }

    public void Dispose() => listener.Stop();
}

internal sealed class LoopbackRequest(TcpClient client, string path, Dictionary<string, string> query) : IDisposable
{
    public string Path { get; } = path;
    public IReadOnlyDictionary<string, string> Query { get; } = query;

    public string? Get(string key) => Query.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    public async Task RespondAsync(int status, string html, CancellationToken cancel)
    {
        try { await WriteAsync(client.GetStream(), status, html, cancel); }
        finally { client.Dispose(); }
    }

    internal static async Task WriteAsync(NetworkStream stream, int status, string html, CancellationToken cancel)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Error" };
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancel);
        await stream.WriteAsync(body, cancel);
        await stream.FlushAsync(cancel);
    }

    public void Dispose() => client.Dispose();
}
