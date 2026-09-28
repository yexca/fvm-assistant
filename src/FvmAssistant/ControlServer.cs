using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FvmAssistant
{
    internal sealed class HttpError : Exception
    {
        public HttpError(int status, string message) : base(message)
        {
            Status = status;
        }

        public int Status { get; }
    }

    internal sealed class HttpRequest
    {
        public string Method { get; private set; }
        public string Path { get; private set; }
        public Dictionary<string, string> Query { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Q(string name, string fallback = null) => Query.TryGetValue(name, out var v) && v.Length > 0 ? v : fallback;

        public bool Flag(string name) => Query.TryGetValue(name, out var v) && (v == "" || v == "1" || v == "true");

        public int QInt(string name, int fallback) =>
            int.TryParse(Q(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public double QDouble(string name)
        {
            var raw = Q(name) ?? throw new HttpError(400, $"missing '{name}'");
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new HttpError(400, $"'{name}' is not a number");
            return v;
        }

        public static HttpRequest Parse(string head)
        {
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var parts = lines[0].Split(' ');
            if (parts.Length < 2) throw new InvalidDataException("bad request line");

            var req = new HttpRequest { Method = parts[0] };
            var target = parts[1];
            int q = target.IndexOf('?');
            req.Path = Uri.UnescapeDataString(q < 0 ? target : target.Substring(0, q));
            if (q >= 0)
            {
                foreach (var pair in target.Substring(q + 1).Split('&'))
                {
                    if (pair.Length == 0) continue;
                    int eq = pair.IndexOf('=');
                    string key = Unescape(eq < 0 ? pair : pair.Substring(0, eq));
                    req.Query[key] = eq < 0 ? "" : Unescape(pair.Substring(eq + 1));
                }
            }

            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) req.Headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
            return req;
        }

        private static string Unescape(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
    }

    /// <summary>
    /// Tiny loopback-only HTTP endpoint used to drive the assistant from scripts.
    /// Requests must carry the X-Fvm-Assistant header (or its legacy alias) and no Origin header, so web pages
    /// (including the game's) cannot reach it through the browser.
    /// </summary>
    internal sealed class ControlServer : IDisposable
    {
        public const string RequiredHeader = "X-Fvm-Assistant";
        public const string LegacyHeader = "X-Fvm-Spike";

        private readonly TcpListener _listener;
        private readonly Func<HttpRequest, Task<object>> _handler;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public ControlServer(int port, Func<HttpRequest, Task<object>> handler)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _handler = handler;
        }

        public void Start()
        {
            _listener.Start();
            Task.Run(AcceptLoopAsync);
            Log.Info($"control server on http://{_listener.LocalEndpoint}/");
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    if (_cts.IsCancellationRequested) return;
                    continue;
                }
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                int status = 200;
                object body;
                try
                {
                    var req = await ReadRequestAsync(stream).ConfigureAwait(false);
                    if ((!req.Headers.ContainsKey(RequiredHeader) && !req.Headers.ContainsKey(LegacyHeader)) ||
                        req.Headers.ContainsKey("Origin"))
                        throw new HttpError(403, $"send the {RequiredHeader} header from a local script");
                    if (req.Method != "GET")
                        throw new HttpError(405, "only GET is supported");
                    body = await _handler(req).ConfigureAwait(false);
                }
                catch (HttpError e)
                {
                    status = e.Status;
                    body = new { error = e.Message };
                }
                catch (Exception e)
                {
                    status = 500;
                    body = new { error = e.GetType().Name + ": " + e.Message };
                    Log.Error("control request failed", e);
                }

                try
                {
                    await WriteAsync(stream, status, body).ConfigureAwait(false);
                }
                catch (IOException)
                {
                }
            }
        }

        private static async Task<HttpRequest> ReadRequestAsync(NetworkStream stream)
        {
            var buf = new byte[16 * 1024];
            int len = 0;
            while (true)
            {
                if (len == buf.Length) throw new HttpError(431, "request head too large");
                int n = await stream.ReadAsync(buf, len, buf.Length - len).ConfigureAwait(false);
                if (n == 0) throw new EndOfStreamException("connection closed before request head");
                len += n;
                for (int i = 3; i < len; i++)
                {
                    if (buf[i - 3] == '\r' && buf[i - 2] == '\n' && buf[i - 1] == '\r' && buf[i] == '\n')
                        return HttpRequest.Parse(Encoding.ASCII.GetString(buf, 0, i - 3));
                }
            }
        }

        private static async Task WriteAsync(NetworkStream stream, int status, object body)
        {
            var payload = Encoding.UTF8.GetBytes(Json.Serialize(body));
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} {Reason(status)}\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                $"Content-Length: {payload.Length}\r\n" +
                "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
        }

        private static string Reason(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 409: return "Conflict";
                case 431: return "Request Header Fields Too Large";
                case 503: return "Service Unavailable";
                default: return "Error";
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }
}
