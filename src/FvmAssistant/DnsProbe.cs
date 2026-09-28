using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;

namespace FvmAssistant
{
    /// <summary>
    /// Some CDN nodes accept TCP connections but stall subsequent requests, leaving a black page.
    /// Before CEF starts we handshake a few times with every address and pin each multi-address
    /// host to the most reliable, fastest one with --host-resolver-rules.
    /// </summary>
    internal static class DnsProbe
    {
        public static readonly (string host, int port)[] GameHosts =
        {
            ("td-4399.123u.com", 443),       // game iframe (agentlogin)
            ("miscbg-4399.123u.com", 80),    // login requests made by the Flash client
            ("q.ms.huanlecdn.com", 443),     // SWF and resource CDN
        };

        private const int Attempts = 3;
        private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(300);

        /// <summary>Returns a value for Chromium's --host-resolver-rules, or null when nothing needs pinning.</summary>
        public static string BuildResolverRules(IEnumerable<(string host, int port)> targets, TimeSpan timeout)
        {
            var probes = targets.Select(t => ProbeHostAsync(t.host, t.port, timeout)).ToArray();
            Task.WaitAll(probes);
            var rules = probes.Select(p => p.Result).Where(r => r != null).ToList();
            return rules.Count == 0 ? null : string.Join(", ", rules);
        }

        private static async Task<string> ProbeHostAsync(string host, int port, TimeSpan timeout)
        {
            IPAddress[] addresses;
            try
            {
                addresses = (await Dns.GetHostAddressesAsync(host).ConfigureAwait(false))
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();
            }
            catch (SocketException e)
            {
                Log.Warn($"dns {host}: lookup failed ({e.SocketErrorCode})");
                return null;
            }

            var scores = await Task.WhenAll(addresses.Select(a => ScoreAsync(host, a, port, timeout))).ConfigureAwait(false);
            var ranked = scores.OrderByDescending(s => s.Ok).ThenBy(s => s.MedianMs).ToList();
            Log.Info($"dns {host}:{port} " + string.Join("; ", ranked));

            // Pin whenever there is a choice and a usable address: Chromium only moves to the next
            // address after a ~21 s connect timeout and never after a stalled request.
            var best = ranked.FirstOrDefault();
            return ranked.Count > 1 && best.Ok > 0 ? $"MAP {host} {best.Address}" : null;
        }

        private struct Score
        {
            public IPAddress Address;
            public int Ok;
            public long MedianMs;
            public string Error;
            public override string ToString() =>
                (Ok == 0 ? $"{Address} 0/{Attempts}" : $"{Address} {Ok}/{Attempts} {MedianMs}ms") + (Error != null ? $" ({Error})" : "");
        }

        private static async Task<Score> ScoreAsync(string host, IPAddress address, int port, TimeSpan timeout)
        {
            var attempts = Enumerable.Range(0, Attempts).Select(async i =>
            {
                await Task.Delay(TimeSpan.FromTicks(Stagger.Ticks * i)).ConfigureAwait(false);
                return await RequestAsync(host, address, port, timeout).ConfigureAwait(false);
            });
            var results = await Task.WhenAll(attempts).ConfigureAwait(false);
            var times = results.Where(r => r.ms.HasValue).Select(r => r.ms.Value).OrderBy(t => t).ToList();
            return new Score
            {
                Address = address,
                Ok = times.Count,
                MedianMs = times.Count == 0 ? long.MaxValue : times[times.Count / 2],
                Error = results.Select(r => r.error).FirstOrDefault(e => e != null),
            };
        }

        /// <summary>
        /// Connects and completes a TLS handshake (443) or sends HEAD / and waits for the status line (80).
        /// Returns the elapsed milliseconds, or why it failed.
        /// </summary>
        private static async Task<(long? ms, string error)> RequestAsync(string host, IPAddress address, int port, TimeSpan timeout)
        {
            var client = new TcpClient(AddressFamily.InterNetwork);
            var watch = Stopwatch.StartNew();
            var work = ExchangeAsync(client, host, address, port);
            try
            {
                var finished = await Task.WhenAny(work, Task.Delay(timeout)).ConfigureAwait(false);
                if (finished != work) return (null, "timeout");
                var failure = await work.ConfigureAwait(false);
                return failure == null ? (watch.ElapsedMilliseconds, (string)null) : ((long?)null, failure);
            }
            catch (Exception e)
            {
                var inner = e.GetBaseException();
                return (null, $"{inner.GetType().Name}: {inner.Message}");
            }
            finally
            {
                client.Close();
                _ = work.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        /// <returns>null on success, otherwise what came back instead.</returns>
        private static async Task<string> ExchangeAsync(TcpClient client, string host, IPAddress address, int port)
        {
            await client.ConnectAsync(address, port).ConfigureAwait(false);
            Stream stream = client.GetStream();
            if (port == 443)
            {
                // Only timing is measured and nothing private is sent; Chromium validates the real connection.
                // A request would travel on to the origin behind the CDN; the handshake alone measures
                // the path to this node (several round trips, so packet loss shows).
                using (var tls = new SslStream(stream, false, (s, cert, chain, errors) => true))
                    await tls.AuthenticateAsClientAsync(host, null, SslProtocols.Tls12, false).ConfigureAwait(false);
                return null;
            }

            var request = Encoding.ASCII.GetBytes($"HEAD / HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(request, 0, request.Length).ConfigureAwait(false);
            var buffer = new byte[64];
            int read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            var status = Encoding.ASCII.GetString(buffer, 0, read).Split('\r')[0];
            return status.StartsWith("HTTP/") ? null : "reply: " + status;
        }
    }
}
