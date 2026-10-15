using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FvmAssistant.Automation;

namespace FvmAssistant
{
    internal sealed class AgentPlan
    {
        public string Level { get; private set; }
        public string TrackId { get; private set; }
        public int Rounds { get; private set; }
        public bool Boss { get; private set; }
        public bool Shovel { get; private set; }
        public bool AutoFlame { get; private set; }
        public List<(string track, string mine)> Cards { get; } = new List<(string, string)>();
        public string Fingerprint => Json.Serialize(new
        {
            Level, TrackId, Rounds, Boss, Shovel, AutoFlame,
            cards = Cards.Select(c => new { c.track, c.mine }).ToArray(),
        });

        public static AgentPlan Parse(string json)
        {
            Dictionary<string, object> fields;
            try { fields = Json.Parse(json ?? ""); }
            catch { throw new AgentException("INVALID_PLAN", "plan must be a JSON object."); }
            CheckFields(fields, "level", "trackId", "cards", "rounds", "boss", "shovel", "autoFlame");
            var plan = new AgentPlan
            {
                Level = Text(fields, "level"), TrackId = Text(fields, "trackId"),
                Rounds = fields.TryGetValue("rounds", out var count) && count is int n ? n : fields.ContainsKey("rounds") ? 0 : 1,
                Boss = Flag(fields, "boss"), Shovel = Flag(fields, "shovel"), AutoFlame = Flag(fields, "autoFlame"),
            };
            if (plan.Level.Split('/').Length != 2 || plan.Rounds < 1 || plan.Rounds > 500)
                throw new AgentException("INVALID_PLAN", "Use an island/stage level and an integer round count from 1 to 500.");
            if (!fields.TryGetValue("cards", out var items) || !(items is IEnumerable list) || items is string || items is IDictionary)
                throw new AgentException("INVALID_PLAN", "cards must be an ordered array of trackCard/learnedCard mappings.");
            foreach (var item in list)
            {
                var mapping = item as Dictionary<string, object>;
                CheckFields(mapping, "trackCard", "learnedCard");
                plan.Cards.Add((Text(mapping, "trackCard"), Text(mapping, "learnedCard")));
            }
            if (plan.Cards.Count < 1 || plan.Cards.Count > DeckBuilder.DeckSlots ||
                plan.Cards.Select(c => c.track).Distinct().Count() != plan.Cards.Count ||
                plan.Cards.Select(c => c.mine).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Cards.Count)
                throw new AgentException("INVALID_PLAN", "Supply 1-11 mappings with unique track cards and unique learned cards.");
            return plan;
        }

        private static void CheckFields(Dictionary<string, object> fields, params string[] allowed)
        {
            if (fields == null || fields.Keys.Any(k => !allowed.Contains(k)))
                throw new AgentException("INVALID_PLAN", "The plan or card mapping contains unknown fields or is not an object.");
        }
        private static string Text(Dictionary<string, object> fields, string key)
        {
            if (!fields.TryGetValue(key, out var value) || !(value is string text) || string.IsNullOrWhiteSpace(text))
                throw new AgentException("INVALID_PLAN", "Missing or invalid " + key + ".");
            return text;
        }
        private static bool Flag(Dictionary<string, object> fields, string key)
        {
            if (!fields.TryGetValue(key, out var value)) return false;
            if (!(value is bool flag)) throw new AgentException("INVALID_PLAN", key + " must be a boolean.");
            return flag;
        }
    }

    /// <summary>Versioned Agent facade. No cookies, URLs, log text or capture paths in ordinary results.</summary>
    internal sealed class AgentApi
    {
        private readonly MainForm _form;
        private readonly AssistantOptions _options;
        private readonly GrindController _grind;
        private readonly AgentRunRegistry _runs = new AgentRunRegistry();

        public AgentApi(MainForm form, AssistantOptions options, GrindController grind)
        {
            _form = form; _options = options; _grind = grind;
        }

        public async Task<object> HandleAsync(HttpRequest request)
        {
            try
            {
                if (!string.Equals(request.Q("profile"), _options.Profile, StringComparison.OrdinalIgnoreCase))
                    throw new AgentException("PROFILE_MISMATCH", "The configured profile does not match this instance.");
                var data = await DispatchAsync(request).ConfigureAwait(false);
                return new { ok = true, apiVersion = 1, profile = _options.Profile, instanceId = _runs.InstanceId, data };
            }
            catch (AgentException e) { return Error(e.Code, e.Message); }
            catch (Exception e)
            {
                // Legacy exceptions may contain paths or account diagnostics. Keep them out of the Agent channel.
                string code = e is IOException ? "LOCAL_DATA_UNAVAILABLE" :
                    e is ArgumentException || e is FormatException ? "INVALID_ARGUMENT" :
                    e is InvalidOperationException ? "ACTION_UNAVAILABLE" : "INTERNAL_ERROR";
                return Error(code, "The action could not complete. Inspect local state before retrying.");
            }
        }

        private object Error(string code, string message) => new
        {
            ok = false, apiVersion = 1, profile = _options.Profile, instanceId = _runs.InstanceId,
            error = new { code, message },
        };

        private async Task<object> DispatchAsync(HttpRequest r)
        {
            switch (r.Path.TrimEnd('/'))
            {
                case "/agent/v1/state": return await StateAsync().ConfigureAwait(false);
                case "/agent/v1/capture":
                    var io = await _grind.CreateGameIoAsync().ConfigureAwait(false);
                    using (var bitmap = io.Grab().ToBitmap())
                    using (var stream = new MemoryStream())
                    {
                        bitmap.Save(stream, ImageFormat.Png);
                        return new { mimeType = "image/png", png = Convert.ToBase64String(stream.ToArray()),
                            width = bitmap.Width, height = bitmap.Height, capturedAt = DateTime.UtcNow.ToString("o"), isPrivate = true };
                    }
                case "/agent/v1/levels":
                    return Page(_grind.Levels().Select(l => new { id = l.ToString(), name = l.Name, island = l.Island, hasBoss = l.HasBoss }), r);
                case "/agent/v1/tracks":
                    var tracks = Tracks();
                    if (r.Q("level") is string level)
                    {
                        var entry = _grind.FindLevel(level);
                        var suggested = new HashSet<string>(_grind.SuggestTracks(entry).Select(_grind.ResolveTrackPath), StringComparer.OrdinalIgnoreCase);
                        var associations = Controller.ControllerState.Load().TrackLevels;
                        tracks = tracks.Where(t => suggested.Contains(t.Path) ||
                            associations.TryGetValue(t.Path, out var associated) && associated == entry.ToString()).ToList();
                    }
                    return Page(tracks.Select(t => new { id = t.Id, name = Path.GetFileNameWithoutExtension(t.Path), source = t.Source }), r);
                case "/agent/v1/cards": return Page(_grind.Cards.Names.Select(n => new { name = n }), r);
                case "/agent/v1/track":
                    var selectedTrack = Tracks().FirstOrDefault(t => t.Id == r.Q("trackId"));
                    if (selectedTrack == null) throw new AgentException("TRACK_NOT_FOUND", "Select a trackId returned by list_tracks.");
                    var prepared = _grind.PrepareTrack(selectedTrack.Path, _grind.FindLevel(r.Q("level") ?? ""));
                    return new
                    {
                        trackId = selectedTrack.Id,
                        cards = prepared.DeckCards.Select(c => new { name = c.Name, layer = c.Layer, flags = c.Flags }),
                        player1 = prepared.Track.Player1Cells.Select(c => c.ToString()),
                        diagnostics = prepared.Inspect().Diagnostics,
                    };
                case "/agent/v1/validate":
                    var plan = AgentPlan.Parse(r.Q("plan"));
                    var validation = Validate(plan, out _);
                    return new { canStart = validation.CanStart, diagnostics = validation.Diagnostics, checksGameReadiness = false };
                case "/agent/v1/start":
                    var startPlan = AgentPlan.Parse(r.Q("plan"));
                    return _runs.Start(r.Q("instanceId"), r.Q("requestId"), startPlan.Fingerprint, startPlan.Rounds,
                        async run =>
                        {
                            var report = Validate(startPlan, out var grindRequest);
                            if (!report.CanStart) throw new AgentException("PLAN_INVALID", "Validate the plan before starting.");
                            if (_grind.Running || _grind.OperationLock.CurrentCount == 0)
                                throw new AgentException("ACCOUNT_BUSY", "Another action is using this account.");
                            var state = await ObserveAsync().ConfigureAwait(false);
                            if (!CanNavigate(state)) throw new AgentException("SCREEN_BLOCKED", "Resolve the current screen before starting.");
                            run.Attach(await _grind.StartAsync(grindRequest).ConfigureAwait(false));
                        }).Snapshot();
                case "/agent/v1/run": return _runs.Find(r.Q("runId"), r.Q("requestId")).Snapshot();
                case "/agent/v1/stop":
                    if (r.Q("mode") != "immediate" && r.Q("mode") != "finish-round")
                        throw new AgentException("INVALID_STOP_MODE", "mode must be immediate or finish-round.");
                    var stopping = _runs.Find(r.Q("runId"), null);
                    if (stopping.Loop == null)
                        throw new AgentException("RUN_NOT_STARTED", "Query this run; it is still starting or failed before execution.");
                    await _grind.StopAgentRunAsync(stopping.Loop, r.Q("mode") == "finish-round").ConfigureAwait(false);
                    return stopping.Snapshot();
                default: throw new AgentException("UNKNOWN_ROUTE", "Unknown Agent API route.");
            }
        }

        private async Task<object> StateAsync()
        {
            var view = _form.View as OsrView;
            bool rendererReady = view != null && view.RendererAvailable;
            bool browserReady = rendererReady && view.BrowserOrNull != null;
            string screen = browserReady ? await ObserveAsync().ConfigureAwait(false) : "unavailable";
            bool busy = _grind.Running || _grind.OperationLock.CurrentCount == 0;
            return new
            {
                mode = _options.Mode.ToString(), rendererReady, browserReady, busy, screen,
                activeRunId = _grind.Running ? _runs.IdFor(_grind.Current) : null,
                observedAt = DateTime.UtcNow.ToString("o"), canStart = browserReady && !busy && CanNavigate(screen),
                blockedReason = busy ? "ACCOUNT_BUSY" : !browserReady ? "BROWSER_UNAVAILABLE" : !CanNavigate(screen) ? "SCREEN_BLOCKED" : null,
                capabilities = new[] { "catalogs", "validate-solo-plan", "start-solo-run", "query-run", "stop-run", "finish-round", "capture-screen" },
            };
        }

        private async Task<string> ObserveAsync()
        {
            try
            {
                var io = await _grind.CreateGameIoAsync().ConfigureAwait(false);
                return _grind.Detector().Detect(io.Grab()).ToString();
            }
            catch { return "unavailable"; }
        }

        private static bool CanNavigate(string screen) => screen == "Lobby" || screen == "WorldMap" || screen == "Room";

        private TrackCompatibilityReport Validate(AgentPlan plan, out GrindRequest request)
        {
            var level = _grind.FindLevel(plan.Level);
            var track = Tracks().FirstOrDefault(t => t.Id == plan.TrackId);
            if (track == null) throw new AgentException("TRACK_NOT_FOUND", "Select a trackId returned by list_tracks for this account.");
            var preflight = _grind.PrepareTrack(track.Path, level);
            var report = preflight.Inspect(plan.Cards.Select(c => c.track), plan.Shovel, DeckBuilder.DeckSlots);
            void Problem(string code, string card, string message) => report.Diagnostics.Add(new TrackDiagnostic("error", code, 0, card, message));
            if (plan.Boss && !level.HasBoss) Problem("boss-unavailable", null, "The selected stage has no boss continuation.");
            var library = _grind.Cards;
            var names = new HashSet<string>(library.Names, StringComparer.OrdinalIgnoreCase);
            var trackCards = new HashSet<string>(preflight.DeckCards.Select(c => c.Name), StringComparer.Ordinal);
            foreach (var card in plan.Cards)
            {
                if (!trackCards.Contains(card.track)) Problem("unknown-track-card", card.track, "This card is absent from the effective track deck.");
                if (!names.Contains(card.mine)) Problem("card-not-learned", card.mine, "Learn this account's card icon before starting.");
                else
                {
                    try { library.Get(card.mine); }
                    catch { Problem("card-icon-unreadable", card.mine, "The learned card image could not be read."); }
                }
            }
            request = new GrindRequest
            {
                Level = level.ToString(), Track = track.Path, Rounds = plan.Rounds, Cards = plan.Cards,
                Boss = plan.Boss, Shovel = plan.Shovel, AutoFlame = plan.AutoFlame,
            };
            return report;
        }

        private sealed class TrackEntry
        {
            public string Id, Path, Source;
        }

        private List<TrackEntry> Tracks()
        {
            var result = new List<TrackEntry>();
            void Add(string source, string root)
            {
                if (!Directory.Exists(root)) return;
                foreach (var path in Directory.GetFiles(root, "*.txt", SearchOption.AllDirectories))
                    result.Add(new TrackEntry { Source = source, Path = path, Id = source + ":" + path.Substring(root.Length + 1).Replace('\\', '/') });
            }
            Add("preset", _grind.PresetTrackDir);
            Add("generated", _grind.UserTrackDir);
            Add("edited", _grind.EditedTrackDir);
            return result.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        }

        private static object Page<T>(IEnumerable<T> items, HttpRequest request)
        {
            int Number(string key, int fallback)
            {
                if (request.Q(key) == null) return fallback;
                if (!int.TryParse(request.Q(key), out int value) || value < 0)
                    throw new AgentException("INVALID_PAGINATION", "offset and limit must be nonnegative integers.");
                return value;
            }
            int offset = Number("offset", 0), limit = Number("limit", 50);
            if (limit < 1 || limit > 100) throw new AgentException("INVALID_PAGINATION", "limit must be between 1 and 100.");
            var all = items.ToList();
            var page = all.Skip(offset).Take(limit).ToList();
            return new { items = page, total = all.Count, nextOffset = offset < all.Count - page.Count ? (int?)(offset + page.Count) : null };
        }
    }
}
