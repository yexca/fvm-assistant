using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FvmAssistant.Automation;

namespace FvmAssistant.Tests
{
    internal static class AgentTests
    {
        private const string Plan = "{\"level\":\"Test/Stage\",\"trackId\":\"preset:Test.txt\",\"cards\":[{\"trackCard\":\"Card\",\"learnedCard\":\"Mine\"}]}";
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static Dictionary<string, object> Obj(object value) => value as Dictionary<string, object> ?? Json.Parse(Json.Serialize(value));
        private static object Field(object value, string key) => Obj(value)[key];
        private static void Code(Action action, string expected)
        {
            try { action(); }
            catch (AgentException e) { Check(e.Code == expected, "Expected " + expected + ", got " + e.Code); return; }
            throw new Exception("Expected rejection: " + expected);
        }

        public static void Plans()
        {
            var plan = AgentPlan.Parse(Plan);
            Check(plan.Rounds == 1 && !plan.Boss && plan.Cards[0].mine == "Mine", "Plan defaults and mappings");
            foreach (var invalid in new[] { "null", "[]", "{}", Plan.Replace("\"cards\":", "\"unexpected\":"),
                Plan.Replace("\"level\":", "\"rounds\":1.5,\"level\":"),
                Plan.Replace("\"level\":", "\"rounds\":501,\"level\":"),
                Plan.Replace("\"level\":", "\"boss\":\"true\",\"level\":"),
                Plan.Replace("Test/Stage", "Stage"), Plan.Replace("\"Mine\"", "null"),
                Plan.Replace("}]", "},{\"trackCard\":\"Other\",\"learnedCard\":\"mine\"}]") })
                Code(() => AgentPlan.Parse(invalid), "INVALID_PLAN");
            var explicitDefaults = AgentPlan.Parse(Plan.Replace("\"level\":", "\"rounds\":1,\"boss\":false,\"level\":"));
            Check(plan.Fingerprint == explicitDefaults.Fingerprint, "Equivalent defaults must deduplicate");
            Check(AgentRun.Status("finished") == "completed" && AgentRun.Status("finished-early") == "stopped" &&
                AgentRun.Status("failed") == "failed" && AgentRun.Status("returning to town") == "running", "Terminal semantics");
        }

        public static void RequestIdentity()
        {
            var registry = new AgentRunRegistry();
            int launches = 0;
            var gate = new TaskCompletionSource<bool>();
            Func<AgentRun, Task> launch = async _ => { Interlocked.Increment(ref launches); await gate.Task; throw new AgentException("SCREEN_BLOCKED", "Private detail"); };
            var first = registry.Start(registry.InstanceId, "request-1", "plan", 2, launch);
            Parallel.For(0, 20, _ => Check(ReferenceEquals(first, registry.Start(registry.InstanceId, "request-1", "plan", 2, launch)), "Retry returns original run"));
            Check(SpinWait.SpinUntil(() => launches == 1, 2000), "One asynchronous launch");
            Code(() => registry.Start(registry.InstanceId, "request-1", "changed", 2, launch), "REQUEST_CONFLICT");
            Code(() => registry.Start("old-instance", "request-1", "plan", 2, launch), "INSTANCE_CHANGED");
            Check(ReferenceEquals(first, registry.Find(null, "request-1")), "Lost start response is recoverable by request ID");
            Code(() => registry.Find(first.Id, "request-1"), "RUN_NOT_FOUND");
            gate.SetResult(true);
            Check(SpinWait.SpinUntil(() => (string)Field(first.Snapshot(), "errorCode") == "SCREEN_BLOCKED", 2000), "Failed start remains queryable");
            Check(ReferenceEquals(first, registry.Start(registry.InstanceId, "request-1", "plan", 2, launch)) && launches == 1, "Failure is not replayed");
            Check(!Json.Serialize(first.Snapshot()).Contains("Private detail"), "Exception text stays private");
            for (int i = 1; i < 64; i++) registry.Start(registry.InstanceId, "r" + i, "plan", 1, _ => Task.CompletedTask);
            Code(() => registry.Start(registry.InstanceId, "overflow", "plan", 1, launch), "RUN_HISTORY_FULL");
            Check(ReferenceEquals(first, registry.Start(registry.InstanceId, "request-1", "plan", 2, launch)), "History limit must not evict deduplication");
        }

        public static void StaleStop()
        {
            var controller = new GrindController(null, null, AssistantOptions.Parse(new string[0]));
            var old = (GrindLoop)FormatterServices.GetUninitializedObject(typeof(GrindLoop));
            var replacement = (GrindLoop)FormatterServices.GetUninitializedObject(typeof(GrindLoop));
            Set(controller, "Current", replacement);
            Code(() => controller.StopAgentRunAsync(old, false).GetAwaiter().GetResult(), "RUN_REPLACED");
            Check(ReferenceEquals(controller.Current, replacement) && controller.OperationLock.CurrentCount == 1, "Replacement preserved and lock released");
            controller.OperationLock.Wait();
            try { Code(() => controller.StopAgentRunAsync(replacement, false).GetAwaiter().GetResult(), "ACCOUNT_BUSY"); }
            finally { controller.OperationLock.Release(); }
        }

        private static void Set(object target, string property, object value) => target.GetType()
            .GetField("<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        public static void Catalogs(string scratch)
        {
            string root = Path.Combine(scratch, "agent-data");
            var paths = (AppPaths)FormatterServices.GetUninitializedObject(typeof(AppPaths));
            foreach (var key in new[] { "Cards", "Tracks", "EditedTracks" })
            {
                string directory = Path.Combine(root, key);
                Directory.CreateDirectory(directory);
                Set(paths, key, directory);
            }
            string presets = Path.Combine(root, "预制轨道"), text = Path.Combine(root, "附加程序", "文本");
            Directory.CreateDirectory(presets);
            Directory.CreateDirectory(text);
            File.WriteAllText(Path.Combine(text, "普通关卡.txt"), "##Test\t1\t1\nStage\t2\t2\t无\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(presets, "Test.txt"), "人物1位置=33\nCard\t7[3]补极\t000|11", new UTF8Encoding(false));
            using (var icon = new Bitmap(42, 50)) icon.Save(Path.Combine(paths.Cards, "Mine.png"));
            var options = AssistantOptions.Parse(new[] { "--mode", "osr", "--profile", "synthetic", "--track-dir", root });
            var controller = new GrindController(null, paths, options);
            var api = new AgentApi(null, options, controller);
            object Request(string route, string query = "", string profile = "synthetic") => api.HandleAsync(HttpRequest.Parse(
                "GET /agent/v1/" + route + "?profile=" + profile + query + " HTTP/1.1\r\n\r\n")).GetAwaiter().GetResult();
            var mismatch = Request("start", "&plan=" + Uri.EscapeDataString(Plan), "other");
            Check((string)Field(Field(mismatch, "error"), "code") == "PROFILE_MISMATCH", "Profile is checked before any work");
            var catalog = Request("tracks");
            Check((bool)Field(catalog, "ok") && !Json.Serialize(catalog).Contains(scratch.Replace("\\", "\\\\")), "Catalog hides absolute paths");
            var validation = Request("validate", "&plan=" + Uri.EscapeDataString(Plan));
            Check((bool)Field(validation, "ok") && (bool)Field(Field(validation, "data"), "canStart"), "Synthetic plan validates with no browser");
            File.Delete(Path.Combine(paths.Cards, "Mine.png"));
            var missing = Request("validate", "&plan=" + Uri.EscapeDataString(Plan));
            Check(!(bool)Field(Field(missing, "data"), "canStart") && Json.Serialize(missing).Contains("card-not-learned"), "Preflight checks account icons");
            var traversal = Request("validate", "&plan=" + Uri.EscapeDataString(Plan.Replace("preset:Test.txt", "preset:../Test.txt")));
            Check((string)Field(Field(traversal, "error"), "code") == "TRACK_NOT_FOUND", "Unlisted paths are rejected");
            var pagination = Request("levels", "&limit=0");
            Check((string)Field(Field(pagination, "error"), "code") == "INVALID_PAGINATION", "Pagination is bounded");
            Check(controller.Current == null, "Catalog and validation must never start a run");
        }

        public static void Mcp(string scratch)
        {
            int requests = 0;
            string lastPlan = null;
            using (var server = new ControlServer(0, request =>
            {
                Interlocked.Increment(ref requests);
                Check(request.Q("profile") == "synthetic", "Profile is preserved");
                Check(request.Headers.ContainsKey(ControlServer.RequiredHeader), "Local header required");
                lastPlan = request.Q("plan");
                if (request.Path == "/agent/v1/capture")
                    return Task.FromResult<object>(new { ok = true, apiVersion = 1, profile = "synthetic", instanceId = "fixture", data = new { png = "c3ludGhldGlj", mimeType = "image/png", isPrivate = true } });
                return Task.FromResult<object>(new { ok = request.Path != "/agent/v1/stop", apiVersion = 1, profile = "synthetic", instanceId = "fixture",
                    data = new { route = request.Path }, error = new { code = "RUN_REPLACED", message = "Synthetic replacement" } });
            }))
            {
                server.Start();
                var listener = (TcpListener)typeof(ControlServer).GetField("_listener", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(server);
                int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                string config = Path.Combine(scratch, "agent-accounts.json");
                File.WriteAllText(config, Json.Serialize(new { Accounts = new[] { new { Profile = "synthetic", Port = port, EntryUrl = "PRIVATE_SENTINEL" } } }));
                var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (directory != null && !File.Exists(Path.Combine(directory.FullName, "scripts", "agent-mcp.ps1"))) directory = directory.Parent;
                Check(directory != null, "Bridge script located");
                var start = new ProcessStartInfo("powershell.exe", "-NoLogo -NoProfile -NonInteractive -File \"" +
                    Path.Combine(directory.FullName, "scripts", "agent-mcp.ps1") + "\" -AccountsFile \"" + config + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                using (var process = Process.Start(start))
                {
                    var input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
                    var stderr = process.StandardError.ReadToEndAsync();
                    int id = 0;
                    object Exchange(string method, object parameters = null)
                    {
                        input.WriteLine(Json.Serialize(new { jsonrpc = "2.0", id = ++id, method, @params = parameters }));
                        var read = process.StandardOutput.ReadLineAsync();
                        Check(read.Wait(8000) && read.Result != null, "MCP response deadline: " + method);
                        var reply = Json.Parse(read.Result);
                        Check(reply.TryGetValue("id", out var replyId) && replyId is int number && number == id,
                            "MCP preserves request IDs and sends no stray stdout (" + method + "): " + read.Result);
                        return reply;
                    }
                    object Call(string name, object arguments) => Field(Exchange("tools/call", new { name, arguments }), "result");
                    try
                    {
                        Check(Obj(Exchange("tools/list")).ContainsKey("error"), "Handshake required");
                        var init = Exchange("initialize", new { protocolVersion = "unsupported", capabilities = new { }, clientInfo = new { name = "test", version = "1" } });
                        Check((string)Field(Field(init, "result"), "protocolVersion") == "2025-11-25", "Version negotiation");
                        input.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                        var tools = (IEnumerable)Field(Field(Exchange("tools/list"), "result"), "tools");
                        Check(tools.Cast<object>().Count() == 11, "Complete tool catalog");
                        var accounts = Call("list_accounts", new { });
                        Check(!(bool)Field(accounts, "isError") && !Json.Serialize(accounts).Contains("PRIVATE_SENTINEL"), "Only public account metadata returned");
                        var invalid = Call("start_run", new { profile = "synthetic", plan = new { }, instanceId = "fixture", requestId = "r1" });
                        Check((bool)Field(invalid, "isError") && requests == 0, "Invalid plan never reaches HTTP");
                        var state = Call("get_account_state", new { profile = "synthetic" });
                        Check(!(bool)Field(state, "isError") && requests == 1, "Read routed to local API");
                        var translated = Json.Parse(Plan.Replace("Mine", "日光炉"));
                        var accepted = Call("start_run", new { profile = "synthetic", plan = translated, instanceId = "fixture", requestId = "r1" });
                        Check(!(bool)Field(accepted, "isError") && AgentPlan.Parse(lastPlan).Cards[0].mine == "日光炉", "UTF-8 structured plan round trip");
                        var stop = Call("stop_run", new { profile = "synthetic", runId = "old", mode = "immediate" });
                        Check((bool)Field(stop, "isError") && Json.Serialize(stop).Contains("RUN_REPLACED"), "API errors remain structured tool errors");
                        var capture = Call("capture_screen", new { profile = "synthetic" });
                        Check(!(bool)Field(capture, "isError") && !Json.Serialize(Field(capture, "structuredContent")).Contains("c3ludGhldGlj") &&
                            ((IEnumerable)Field(capture, "content")).Cast<object>().Any(c => (string)Field(c, "type") == "image"), "Private capture uses a native MCP image without duplicate base64 text");
                        int before = requests;
                        var unknown = Call("get_account_state", new { profile = "unconfigured" });
                        var extra = Call("get_account_state", new { profile = "synthetic", url = "http://example.invalid" });
                        var identifiers = Call("get_run", new { profile = "synthetic", requestId = "r1", runId = "old" });
                        Check((bool)Field(unknown, "isError") && (bool)Field(extra, "isError") && (bool)Field(identifiers, "isError") && requests == before, "Invalid routing and query arguments rejected locally");
                        input.Close();
                        Check(process.WaitForExit(5000) && process.ExitCode == 0, "Clean shutdown on stdin EOF");
                        Check(string.IsNullOrWhiteSpace(stderr.Result), "No PowerShell errors");
                    }
                    finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
                }
            }
        }
    }
}
