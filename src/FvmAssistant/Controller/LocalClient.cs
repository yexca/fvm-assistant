using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FvmAssistant.Controller
{
    internal static class LocalClient
    {
        public static async Task<Dictionary<string, object>> GetAsync(int port, string route,
            IDictionary<string, string> query = null, int timeoutMs = 8000)
        {
            var suffix = query == null || query.Count == 0 ? "" : "?" + string.Join("&",
                query.OrderBy(p => p.Key).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value ?? "")));
            var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + route + suffix);
            request.Method = "GET";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.Headers[ControlServer.RequiredHeader] = "1";
            request.Proxy = null;
            try
            {
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    return Json.Parse(await reader.ReadToEndAsync().ConfigureAwait(false));
            }
            catch (WebException e) when (e.Response is HttpWebResponse response)
            {
                using (response)
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    var body = Json.Parse(await reader.ReadToEndAsync().ConfigureAwait(false));
                    throw new InvalidOperationException(route + ": " + Value(body, "error", response.StatusDescription));
                }
            }
        }

        public static string Value(Dictionary<string, object> data, string key, string fallback = "") =>
            data != null && data.TryGetValue(key, out var value) && value != null ? Convert.ToString(value) : fallback;

        public static bool Flag(Dictionary<string, object> data, string key) =>
            data != null && data.TryGetValue(key, out var value) && Convert.ToBoolean(value);

        public static int Number(Dictionary<string, object> data, string key) =>
            data != null && data.TryGetValue(key, out var value) ? Convert.ToInt32(value) : 0;
    }
}
