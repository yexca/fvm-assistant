using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FvmAssistant.Automation;

namespace FvmAssistant
{
    internal sealed class GrindRequest
    {
        /// <summary>"岛/关卡" from 普通关卡.txt to walk to; null = already in the level's room.</summary>
        public string Level { get; set; }

        /// <summary>Path relative to 预制轨道, or absolute.</summary>
        public string Track { get; set; }

        /// <summary>Track card → my card, in deck order. When set, the room's deck is rebuilt first.</summary>
        public List<(string track, string mine)> Cards { get; set; }

        /// <summary>Only when <see cref="Cards"/> is null: track card → slot of the deck already in the room.</summary>
        public Dictionary<string, int> SlotMap { get; set; }
        public int SlotCount { get; set; }

        public int Rounds { get; set; } = 1;
        public bool Shovel { get; set; }
        public bool Boss { get; set; }

        /// <summary>The account picks up flames by itself: the sweep leaves the flame producers' columns out.</summary>
        public bool AutoFlame { get; set; }

        /// <summary>Run under the local duo coordinator, with optional stage-based room pairing.</summary>
        public DuoRole? DuoRole { get; set; }
        public int DuoRoom { get; set; }
        public string DuoPassword { get; set; }
    }

    /// <summary>
    /// Owns the grind run shared by the operation panel and the control API: validates a request,
    /// starts the loop in the background, stops it, and relays its messages.
    /// </summary>
    internal sealed class GrindController
    {
        private readonly MainForm _form;
        private readonly AppPaths _paths;
        private readonly AssistantOptions _options;
        internal readonly SemaphoreSlim OperationLock = new SemaphoreSlim(1, 1);
        private ScreenDetector _detector;
        private Dictionary<string, (int cooldown, int layer)> _cardDefaults;
        private CancellationTokenSource _cts;
        private Task _task;
        private Timer _duoTimer;

        public GrindController(MainForm form, AppPaths paths, AssistantOptions options)
        {
            _form = form;
            _paths = paths;
            _options = options;
        }

        /// <summary>A line from the running loop (already timestamped). Raised on a worker thread.</summary>
        public event Action<string> Message;

        /// <summary>Raised on a worker thread when a run starts (true) or ends (false).</summary>
        public event Action<bool> RunningChanged;

        public GrindLoop Current { get; private set; }
        public DuoSession Duo { get; private set; }
        public bool Running => _task != null && !_task.IsCompleted;
        public CardLibrary Cards => new CardLibrary(_paths.Cards);
        public string CardsDirectory => _paths.Cards;

        public string TrackDir =>
            _options.ResolveTrackDir() ?? throw new InvalidOperationException("找不到轨道编辑器数据目录：用 --track-dir 指定，或放到 vendor\\track");

        public string PresetTrackDir => Path.Combine(TrackDir, "预制轨道");

        /// <summary>Tracks made from recordings belong to the current account.</summary>
        public const string UserTrackFolder = "我的轨道";
        public string UserTrackDir => _paths.Tracks;
        public string EditedTrackDir => _paths.EditedTracks;

        public IReadOnlyList<LevelEntry> Levels() =>
            LevelCatalog.Load(Path.Combine(TrackDir, "附加程序", "文本", "普通关卡.txt"));

        public ScreenDetector Detector()
        {
            if (_detector != null) return _detector;
            var library = new TemplateLibrary();
            int fromTrack = library.LoadDirectory(Path.Combine(TrackDir, "附加程序", "图片", "界面"));
            int ours = library.LoadDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates"));
            Log.Info($"templates: {fromTrack} from 轨道编辑器, {ours} own");
            return _detector = new ScreenDetector(library);
        }

        public async Task<OsrGameIo> CreateGameIoAsync()
        {
            if (!(_form.View is OsrView osr)) throw new InvalidOperationException("自动操作需要离屏模式（--mode osr）");
            var browser = osr.BrowserOrNull ?? throw new InvalidOperationException("浏览器还没准备好");
            var flash = (await FlashLocator.LocateAsync(browser).ConfigureAwait(false)).Best
                        ?? throw new InvalidOperationException("页面上还没有游戏（Flash 未加载）");
            return new OsrGameIo(osr, flash.StageRect);
        }

        public string ResolveTrackPath(string track)
        {
            if (string.IsNullOrWhiteSpace(track)) throw new ArgumentException("没有选择轨道");
            if (Path.IsPathRooted(track)) return File.Exists(track) ? track : throw new FileNotFoundException("轨道文件不存在：" + track);
            var root = PresetTrackDir;
            var path = Path.GetFullPath(Path.Combine(root, track.Replace('/', '\\')));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("轨道路径必须在 预制轨道 里");
            return File.Exists(path) ? path : throw new FileNotFoundException("轨道文件不存在：" + path);
        }

        /// <summary>Tracks whose file name mentions the level: the user's own first, then 通用轨道.</summary>
        public IReadOnlyList<string> SuggestTracks(LevelEntry level)
        {
            if (level == null || !Directory.Exists(PresetTrackDir)) return new List<string>();
            var presets = Directory.GetFiles(PresetTrackDir, "*.txt", SearchOption.AllDirectories)
                .Select(p => p.Substring(PresetTrackDir.Length + 1))
                .Where(rel => Path.GetFileNameWithoutExtension(rel).Contains(level.Name) &&
                              (Path.GetDirectoryName(rel).EndsWith(level.Island) || IsUserTrack(rel)))
                .OrderBy(rel => IsUserTrack(rel) ? 0 : rel.StartsWith("通用轨道") ? 1 : 2)
                .ThenBy(rel => rel.Contains('+') ? 1 : 0) // "+" files are 2P companions
                .ThenBy(rel => rel)
                .ToList();
            var mine = Directory.GetFiles(UserTrackDir, "*.txt")
                .Concat(Directory.GetFiles(EditedTrackDir, "*.txt"))
                .Where(p => Path.GetFileNameWithoutExtension(p).Contains(level.Name))
                .OrderBy(p => p).ToList();
            mine.AddRange(presets);
            return mine;
        }

        private static bool IsUserTrack(string relative) => relative.StartsWith(UserTrackFolder + "\\", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Cooldown and layer (承载, 护罩, ...) the preset tracks give a card name, taking the most
        /// common pair; null for cards the presets never use.
        /// </summary>
        public (int cooldown, int layer)? CardDefaults(string name)
        {
            if (_cardDefaults == null)
            {
                var seen = new Dictionary<string, Dictionary<(int, int), int>>();
                foreach (var path in Directory.GetFiles(PresetTrackDir, "*.txt", SearchOption.AllDirectories))
                {
                    if (IsUserTrack(path.Substring(PresetTrackDir.Length + 1))) continue;
                    TrackFile track;
                    try { track = TrackFile.Load(path); }
                    catch (Exception e) when (e is IOException || e is FormatException || e is OverflowException) { continue; }
                    foreach (var card in track.Waves.SelectMany(w => w.DeckCards))
                    {
                        if (!seen.TryGetValue(card.Name, out var pairs)) seen[card.Name] = pairs = new Dictionary<(int, int), int>();
                        var key = (card.CooldownSec, card.Layer);
                        pairs[key] = pairs.TryGetValue(key, out var n) ? n + 1 : 1;
                    }
                }
                _cardDefaults = seen.ToDictionary(p => p.Key, p => p.Value.OrderByDescending(kv => kv.Value).First().Key);
            }
            if (_cardDefaults.TryGetValue(name, out var found)) return found;
            // A variant carries the base card's name: 友情木盘子 is a 木盘子.
            var baseName = _cardDefaults.Keys.Where(k => k.Length >= 2 && name.Contains(k)).OrderByDescending(k => k.Length).FirstOrDefault();
            return baseName != null ? _cardDefaults[baseName] : ((int, int)?)null;
        }

        /// <summary>The newest recording folder (captures\rec-*), or null.</summary>
        public string LatestRecording() =>
            Directory.GetDirectories(_paths.Captures, "rec-*")
                .Where(d => File.Exists(Path.Combine(d, "events.jsonl")))
                .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
                .FirstOrDefault();

        public string RecordingPath(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return LatestRecording() ?? throw new InvalidOperationException("还没有录制");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("录制名不对：" + name);
            var dir = Path.Combine(_paths.Captures, name);
            return Directory.Exists(dir) ? dir : throw new DirectoryNotFoundException("没有这个录制：" + name);
        }

        /// <summary>Every battle of a recording read back as a track (slow: decodes frames; run off the UI thread).</summary>
        public List<RecordedBattle> ReadRecording(string directory, Func<string, (int cooldown, int layer)?> cardDefaults = null) =>
            new RecordingReader(directory, Detector(), Cards, cardDefaults ?? CardDefaults).Read();

        /// <summary>Saves a generated track in this account's private track folder.</summary>
        public string SaveUserTrack(string name, string text, bool overwrite)
        {
            name = (name ?? "").Trim();
            if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException($"轨道名“{name}”不能用作文件名");
            Directory.CreateDirectory(UserTrackDir);
            var path = Path.Combine(UserTrackDir, name + ".txt");
            if (File.Exists(path) && !overwrite) throw new IOException("已经有同名轨道：" + path);
            EditorText.WriteAllText(path, text);
            return path;
        }

        public async Task StartAsync(GrindRequest request)
        {
            if (!await OperationLock.WaitAsync(0).ConfigureAwait(false))
                throw new InvalidOperationException("Another automation action is using this account.");
            try { await StartCoreAsync(request).ConfigureAwait(false); }
            finally { OperationLock.Release(); }
        }

        public async Task<MissionObservation> MissionActionAsync(string action, int rowY = 0, string listHash = null, string selectedHash = null)
        {
            if (!await OperationLock.WaitAsync(0).ConfigureAwait(false))
                throw new InvalidOperationException("Another automation action is using this account.");
            bool reserved = false;
            try
            {
                if (Running) throw new InvalidOperationException("Finish or stop the battle before handling missions.");
                if (action != "inspect" && action != "publish" && action != "accept" && action != "claim")
                    throw new ArgumentException("Mission state must be inspect, accept, claim or publish.");
                var io = await CreateGameIoAsync().ConfigureAwait(false);
                var templates = new TemplateLibrary();
                templates.LoadDirectory(Path.Combine(TrackDir, "附加程序", "图片", "界面"));
                _form.SetGrinding(true);
                RunningChanged?.Invoke(true);
                reserved = true;
                return await new MissionActions(io, Detector(), templates, _paths.Captures)
                    .ExecuteAsync(action, rowY, listHash, selectedHash).ConfigureAwait(false);
            }
            finally
            {
                if (reserved) { _form.SetGrinding(false); RunningChanged?.Invoke(false); }
                OperationLock.Release();
            }
        }

        private async Task StartCoreAsync(GrindRequest request)
        {
            if (Running) throw new InvalidOperationException("已经在刷图了");
            if (request.DuoRole.HasValue && !string.IsNullOrWhiteSpace(request.Level))
            {
                if (!Regex.IsMatch(request.DuoPassword ?? "", "^[0-9]{4}$"))
                    throw new ArgumentException("Automatic duo rooms require a four-digit temporary password.");
                if (request.DuoRole == Automation.DuoRole.Guest && (request.DuoRoom < 1 || request.DuoRoom > 9999))
                    throw new ArgumentException("The guest needs the host's room number.");
                if (request.DuoRole == Automation.DuoRole.Host && request.DuoRoom != 0)
                    throw new ArgumentException("The host creates a new room and must not specify a room number.");
            }
            else if (request.DuoRole.HasValue && (request.DuoRoom != 0 || request.DuoPassword != null))
                throw new ArgumentException("Manual duo mode does not accept a room number or password.");

            var level = string.IsNullOrWhiteSpace(request.Level) ? null : FindLevel(request.Level);
            var preflight = PrepareTrack(request.Track, level);
            var selected = request.Cards != null ? request.Cards.Select(c => c.track) : request.SlotMap?.Keys;
            var compatibility = preflight.Inspect(selected ?? Enumerable.Empty<string>(), request.Shovel,
                request.Cards != null ? DeckBuilder.DeckSlots : 18, request.DuoRole.HasValue);
            compatibility.RequireCompatible();
            if (request.DuoRole.HasValue)
                DuoTrackPlan.Build(preflight, selected ?? Enumerable.Empty<string>(), request.Shovel);

            var settings = new GrindSettings
            {
                TrackPath = preflight.Track.Path,
                Track = preflight.Track,
                FlameReader = preflight.FlameReader,
                CarrierMap = preflight.Carriers,
                Rounds = Math.Max(1, Math.Min(500, request.Rounds)),
                UseShovel = request.Shovel,
                AutoFlame = request.AutoFlame,
                Boss = request.Boss,
                Level = level,
                Duo = request.DuoRole.HasValue ? new DuoSession(request.DuoRole.Value, request.DuoRoom) : null,
                DuoPassword = request.DuoPassword,
                RoomNumbers = request.DuoRole.HasValue && level != null
                    ? RoomNumberReader.Load(TrackDir) ?? throw new FileNotFoundException("Local room-number digit masks are required for automatic pairing.")
                    : null,
            };
            if (settings.Boss && settings.Level != null && !settings.Level.HasBoss)
                throw new ArgumentException($"{settings.Level} 没有 boss 关");
            if (request.Cards != null)
            {
                if (request.Cards.Count == 0) throw new ArgumentException("至少要带一张卡");
                if (request.Cards.Count > DeckBuilder.DeckSlots) throw new ArgumentException("The room deck supports at most " + DeckBuilder.DeckSlots + " cards.");
                if (request.Cards.GroupBy(c => c.track).Any(g => g.Count() > 1)) throw new ArgumentException("Track card mappings must be unique.");
                var duplicate = request.Cards.GroupBy(c => c.mine).FirstOrDefault(g => g.Count() > 1);
                if (duplicate != null) throw new ArgumentException($"{duplicate.Key} 被对应到了多张轨道卡");
                var library = Cards;
                settings.Deck = request.Cards.Select(c => (c.mine, library.Get(c.mine))).ToList();
                settings.SlotOfCard = request.Cards.Select((c, i) => (c.track, slot: i + 1)).ToDictionary(p => p.track, p => p.slot);
                settings.SlotCount = request.Cards.Count;
            }
            else
            {
                if (request.SlotMap == null || request.SlotCount < 1) throw new ArgumentException("没有卡片对应");
                if (request.SlotCount > 18 || request.SlotMap.Values.Any(s => s < 1 || s > request.SlotCount) ||
                    request.SlotMap.Values.Distinct().Count() != request.SlotMap.Count)
                    throw new ArgumentException("Mapped slots must be unique and within the declared deck (1-18).");
                settings.SlotOfCard = request.SlotMap;
                settings.SlotCount = request.SlotCount;
            }

            var detector = Detector();
            var io = await CreateGameIoAsync().ConfigureAwait(false);
            var runName = $"grind-{DateTime.Now:yyyyMMdd-HHmmss}" +
                (settings.Duo == null ? "" : "-" + settings.Duo.RunId.Substring(0, 8));
            var loop = new GrindLoop(io, detector, settings, Path.Combine(_paths.Captures, runName));
            loop.MessageLogged += line => Message?.Invoke(line);
            Current = loop;
            Duo = settings.Duo;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var runCancellation = _cts;
            _duoTimer?.Dispose();
            _duoTimer = Duo == null ? null : new Timer(_ =>
            {
                if (!settings.Duo.ContactExpired(TimeSpan.FromSeconds(30))) return;
                Log.Warn("duo coordinator heartbeat expired; stopping this account");
                runCancellation.Cancel();
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            _form.SetGrinding(true);
            RunningChanged?.Invoke(true);
            _task = Task.Run(async () =>
            {
                try
                {
                    await loop.RunAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    _duoTimer?.Dispose();
                    _duoTimer = null;
                    _form.SetGrinding(false);
                    _form.SetStatus($"刷图结束：{loop.State}，完成 {loop.RoundsDone} 局");
                    RunningChanged?.Invoke(false);
                }
            });
        }

        /// <summary>Reads track and support data without accessing a browser or account images.</summary>
        public TrackPreflight PrepareTrack(string track, LevelEntry level = null)
        {
            var path = ResolveTrackPath(track);
            return new TrackPreflight(TrackFile.Load(path), CarrierMap.Find(PresetTrackDir, level, path), FlameReader.Load(TrackDir));
        }

        public void Stop() => _cts?.Cancel();

        public void ReleaseDuo(string phase, string runId, int round)
        {
            if (!Running || Duo == null || Current == null)
                throw new InvalidOperationException("No duo run is active.");
            if (phase == "room") Duo.ReleaseRoom(runId, round, Current.RoundsDone + 1, Current.State);
            else if (phase == "battle") Duo.ReleaseBattle(runId, round, Current.RoundsDone + 1, Current.State);
            else if (phase == "boss") Duo.ReleaseBoss(runId, round, Current.RoundsDone + 1, Current.State);
            else throw new ArgumentException("Duo phase must be room, battle or boss.");
        }

        public async Task StopAndWaitAsync(TimeSpan limit)
        {
            Stop();
            if (_task != null) await Task.WhenAny(_task, Task.Delay(limit)).ConfigureAwait(false);
        }

        /// <summary>The room screen and which deck slots hold a card; throws when not in a room.</summary>
        public async Task<(StageFrame frame, List<int> filled)> ReadDeckAsync()
        {
            var io = await CreateGameIoAsync().ConfigureAwait(false);
            var frame = io.Grab();
            if (Detector().Detect(frame) != GameScreen.Room) throw new InvalidOperationException("请先进入关卡房间（能看到“战斗卡片包”的界面）");
            return (frame, DeckBuilder.FilledSlots(frame));
        }

        public void LearnCard(string name, StageFrame roomFrame, int slot)
        {
            name = name.Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException($"卡名“{name}”不能用作文件名");
            Cards.Save(name, roomFrame.Crop(DeckBuilder.DeckSlot(slot)));
            Log.Info($"learned card {name} from deck slot {slot}");
        }

        /// <summary>Empties the room's deck and adds these learned cards in order.</summary>
        public async Task BuildDeckAsync(IReadOnlyList<string> names, Action<string> log)
        {
            if (Running) throw new InvalidOperationException("正在刷图，先停止");
            var library = Cards;
            var cards = names.Select(n => (n, library.Get(n))).ToList();
            var io = await CreateGameIoAsync().ConfigureAwait(false);
            if (Detector().Detect(io.Grab()) != GameScreen.Room) throw new InvalidOperationException("请先进入关卡房间");
            var detector = Detector();
            await new DeckBuilder(io, m => { log(m); Log.Info("deck: " + m); }, f => detector.Detect(f) == GameScreen.Room)
                .EnsureAsync(cards, CancellationToken.None).ConfigureAwait(false);
        }

        public LevelEntry FindLevel(string name) =>
            LevelCatalog.Find(Levels(), name) ?? throw new ArgumentException($"普通关卡.txt 里没有 {name}（写成 岛/关卡，如 美味/曲奇）");
    }
}
