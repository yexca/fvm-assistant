using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace FvmAssistant.Automation
{
    internal enum MissionState { Unknown, InProgress, Complete, Claimed }

    internal sealed class MissionObservation
    {
        public string View { get; set; }
        public string SelectedState { get; set; }
        public string ListHash { get; set; }
        public string SelectedHash { get; set; }
        public bool VisibleRewardsPending { get; set; }
        public List<int> CompletedRewardRows { get; set; }
        public bool CanClaim { get; set; }
        public bool CanPublish { get; set; }
        public string Capture { get; set; }
        public string Result { get; set; }
    }

    internal static class MissionEvidence
    {
        internal static readonly Rectangle List = new Rectangle(100, 132, 302, 400);
        internal static readonly Rectangle Selected = new Rectangle(437, 95, 407, 265);
        internal static readonly Rectangle Banner = new Rectangle(550, 237, 165, 29);
        internal static readonly Rectangle RewardStamp = new Rectangle(550, 384, 182, 130);
        internal static readonly Rectangle RewardButton = new Rectangle(598, 518, 90, 20);

        internal static int Count(StageFrame frame, Rectangle region, Func<int, int, int, bool> predicate)
        {
            if (!new Rectangle(0, 0, frame.Width, frame.Height).Contains(region)) return 0;
            int count = 0;
            for (int pixelY = region.Top; pixelY < region.Bottom; pixelY++)
                for (int pixelX = region.Left; pixelX < region.Right; pixelX++)
                {
                    int offset = (pixelY * frame.Width + pixelX) * 4;
                    if (predicate(frame.Bgra[offset + 2], frame.Bgra[offset + 1], frame.Bgra[offset])) count++;
                }
            return count;
        }

        internal static bool Red(int red, int green, int blue) => red > 180 && green < 130 && blue < 140;
        internal static bool Cyan(int red, int green, int blue) => red < 150 && green > 150 && blue > 190;
        internal static bool Orange(int red, int green, int blue) => red > 190 && green > 85 && green < 195 && blue < 90;
        internal static bool GuildPublishEnabled(StageFrame frame) => Count(frame, new Rectangle(88, 44, 74, 20),
            (red, green, blue) => red > 190 && green > 85 && green < 235 && blue < 130 && red - blue > 80) > 180;
        private static bool RewardGreen(int red, int green, int blue) => green > 145 && red < 180 && blue < 110;
        internal static bool RowRewardPending(StageFrame frame, int rowY) => rowY >= 140 && rowY <= 526 &&
            Count(frame, new Rectangle(357, rowY - 7, 42, 16), RewardGreen) > 45;

        internal static List<int> CompletedRewardRows(StageFrame frame)
        {
            var rows = new List<int>();
            int first = -1, last = -1;
            for (int y = 140; y < 530; y++)
            {
                bool ink = Count(frame, new Rectangle(357, y, 42, 1), RewardGreen) > 2;
                if (ink)
                {
                    if (first < 0) first = y;
                    last = y;
                }
                if (first >= 0 && ((!ink && y - last >= 2) || y == 529))
                {
                    int center = (first + last) / 2;
                    if (RowRewardPending(frame, center)) rows.Add(center);
                    first = last = -1;
                }
            }
            return rows;
        }

        internal static MissionState State(StageFrame frame)
        {
            if (Count(frame, Banner, Red) > 80)
                return Count(frame, RewardStamp, Red) > 900 ? MissionState.Claimed : MissionState.Complete;
            return Count(frame, Banner, Cyan) > 80 ? MissionState.InProgress : MissionState.Unknown;
        }

        internal static bool CanClaim(StageFrame frame) => State(frame) == MissionState.Complete && Count(frame, RewardButton, Orange) > 180;
        internal static bool RowInProgress(StageFrame frame, int rowY) => rowY >= 140 && rowY <= 526 &&
            Count(frame, new Rectangle(355, rowY - 7, 45, 16), Cyan) > 45;
        internal static bool GuildMenuOpen(StageFrame frame) => Count(frame, new Rectangle(646, 266, 205, 86),
            (red, green, blue) => green > 130 && blue < 130 && red < 240) > 1000;
        internal static bool Pending(StageFrame frame) => Count(frame, new Rectangle(357, 140, 42, 390), RewardGreen) > 45;

        internal static string Hash(StageFrame frame, Rectangle region)
        {
            ulong hash = 14695981039346656037UL;
            foreach (byte value in frame.Crop(region).Bgra) { hash ^= value; hash *= 1099511628211UL; }
            return hash.ToString("x16");
        }

        internal static string ListHash(StageFrame frame)
        {
            var labels = frame.Crop(new Rectangle(List.X, List.Y, 252, List.Height)).Bgra;
            ulong hash = 14695981039346656037UL;
            for (int offset = 0; offset < labels.Length; offset += 4)
            {
                int blue = labels[offset], green = labels[offset + 1], red = labels[offset + 2];
                byte ink = red > 220 && green > 220 && blue > 220 ? (byte)1 :
                    red > 200 && green > 120 && blue < 110 ? (byte)2 : (byte)0;
                hash ^= ink;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }

        internal static void RequireGuard(string expected, string actual)
        {
            if (string.IsNullOrEmpty(expected) || !string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidOperationException("Mission evidence changed or is missing. Inspect the board again before acting.");
        }
    }

    internal sealed class MissionActions
    {
        private readonly IGameIo _io;
        private readonly ScreenDetector _detector;
        private readonly TemplateLibrary _templates;
        private readonly string _captures;

        internal MissionActions(IGameIo io, ScreenDetector detector, TemplateLibrary templates, string captures)
        { _io = io; _detector = detector; _templates = templates; _captures = captures; }

        private bool Match(string name, StageFrame frame) => _templates.Contains(name) && _templates.Get(name).Diff(frame) < 10;

        private string View(StageFrame frame)
        {
            var gameScreen = _detector.Detect(frame);
            if (gameScreen == GameScreen.Disconnected || gameScreen == GameScreen.HealthNotice ||
                gameScreen == GameScreen.EventPopup || gameScreen == GameScreen.AwaitHostDecision) return "Unknown";
            if (Match("发布任务", frame)) return "GuildPublish";
            if (Match("公会任务", frame)) return "GuildTasks";
            if (Match("任务看板", frame)) return "AccountTasks";
            if (Match("公会", frame)) return "Guild";
            return gameScreen == GameScreen.Lobby ? "Lobby" : "Unknown";
        }

        private static bool Board(string view) => view == "AccountTasks" || view == "GuildTasks";

        private MissionObservation Observe(string result = null)
        {
            var frame = _io.Grab();
            var view = View(frame);
            string capture = SaveCapture(frame, "missions");
            return new MissionObservation
            {
                View = view, SelectedState = Board(view) ? MissionEvidence.State(frame).ToString() : "Unknown",
                ListHash = Board(view) ? MissionEvidence.ListHash(frame) : null,
                SelectedHash = Board(view) ? MissionEvidence.Hash(frame, MissionEvidence.Selected) : null,
                VisibleRewardsPending = Board(view) && MissionEvidence.Pending(frame),
                CompletedRewardRows = Board(view) ? MissionEvidence.CompletedRewardRows(frame) : new List<int>(),
                CanClaim = Board(view) && MissionEvidence.CanClaim(frame),
                CanPublish = view == "GuildTasks" && MissionEvidence.GuildPublishEnabled(frame),
                Capture = capture, Result = result
            };
        }

        private string SaveCapture(StageFrame frame, string label)
        {
            string path = Path.Combine(_captures, label + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");
            using (var bitmap = frame.ToBitmap()) bitmap.Save(path, ImageFormat.Png);
            return path;
        }

        private async Task ClickAsync(Point point, int delay = 500, bool park = true)
        {
            await _io.ClickAsync(point).ConfigureAwait(false);
            if (park) _io.Move(new Point(945, 590));
            await Task.Delay(delay).ConfigureAwait(false);
        }

        private async Task RequireViewAsync(string expected)
        {
            for (int attempt = 0; attempt < 15; attempt++)
            {
                if (View(_io.Grab()) == expected) return;
                await Task.Delay(300).ConfigureAwait(false);
            }
            Observe("unrecognized transition");
            throw new InvalidOperationException("The expected mission screen did not appear: " + expected + ". No further clicks were sent.");
        }

        internal async Task<MissionObservation> ExecuteAsync(string action, int rowY, string listHash, string selectedHash)
        {
            if (action == "inspect") return Observe();
            if (action == "open") return await OpenAccountAsync().ConfigureAwait(false);
            if (action == "open-guild") return await OpenGuildAsync().ConfigureAwait(false);
            if (action == "publish") return await PublishAsync().ConfigureAwait(false);
            var frame = _io.Grab();
            var view = View(frame);
            if (!Board(view)) throw new InvalidOperationException("Open the account or guild mission board first.");
            if (action == "accept" || action == "select")
            {
                if (rowY < 140 || rowY > 526) throw new ArgumentOutOfRangeException(nameof(rowY));
                MissionEvidence.RequireGuard(listHash, MissionEvidence.ListHash(frame));
                if (MissionEvidence.Pending(frame) && (action == "accept" || !MissionEvidence.RowRewardPending(frame, rowY)))
                    throw new InvalidOperationException("Select and claim the visible completed rewards before selecting another task.");
                await ClickAsync(new Point(225, rowY)).ConfigureAwait(false);
                await RequireViewAsync(view).ConfigureAwait(false);
                var accepted = Observe();
                if (action == "select")
                {
                    accepted.Result = "task row selected; the game may accept it automatically; inspect its state before battle or reward collection";
                    return accepted;
                }
                if (accepted.SelectedState != MissionState.InProgress.ToString() || !MissionEvidence.RowInProgress(_io.Grab(), rowY))
                    throw new InvalidOperationException("The selected task was not confirmed as 任务进行中. Do not start its battle.");
                accepted.Result = "accepted and verified in progress";
                return accepted;
            }
            if (action != "claim") throw new ArgumentException("Mission state must be open, open-guild, inspect, select, accept, claim or publish.");
            MissionEvidence.RequireGuard(selectedHash, MissionEvidence.Hash(frame, MissionEvidence.Selected));
            if (!MissionEvidence.CanClaim(frame)) throw new InvalidOperationException("The selected task has no verified, unclaimed reward.");
            await ClickAsync(new Point(641, 531)).ConfigureAwait(false);
            var attemptFrame = _io.Grab();
            for (int attempt = 0; attempt < 15; attempt++)
            {
                var after = _io.Grab();
                if (View(after) == view && (MissionEvidence.State(after) == MissionState.Claimed ||
                    MissionEvidence.Hash(after, MissionEvidence.Selected) != selectedHash && !MissionEvidence.CanClaim(after)))
                    return Observe("reward claimed; inspect and click any newly unlocked task before its battle");
                await Task.Delay(300).ConfigureAwait(false);
            }
            string attemptCapture = SaveCapture(attemptFrame, "missions-attempt");
            var final = Observe("reward outcome uncertain");
            throw new InvalidOperationException("Reward collection was not confirmed. Inspect before retrying; no task was accepted. " +
                "Private attempt capture: " + attemptCapture + "; current capture: " + final.Capture);
        }

        private async Task<MissionObservation> OpenAccountAsync()
        {
            var view = View(_io.Grab());
            if (view == "AccountTasks") return Observe("account task board already open");
            if (view == "GuildTasks")
            {
                await ClickAsync(new Point(853, 56)).ConfigureAwait(false);
                await RequireViewAsync("Guild").ConfigureAwait(false);
                view = "Guild";
            }
            if (view == "Guild")
            {
                await ClickAsync(new Point(904, 57)).ConfigureAwait(false);
                await RequireViewAsync("Lobby").ConfigureAwait(false);
                view = "Lobby";
            }
            if (view != "Lobby") throw new InvalidOperationException("Return to a lobby map or close the current popup before opening account tasks.");
            await ClickAsync(new Point(641, 559)).ConfigureAwait(false);
            await RequireViewAsync("AccountTasks").ConfigureAwait(false);
            return Observe("account task board opened; inspect rewards and requirements before selecting a task");
        }

        private async Task<MissionObservation> OpenGuildAsync()
        {
            var view = View(_io.Grab());
            if (view == "GuildTasks") return Observe("guild task board already open");
            if (view == "AccountTasks")
            {
                await ClickAsync(new Point(853, 53)).ConfigureAwait(false);
                await RequireViewAsync("Lobby").ConfigureAwait(false);
                view = "Lobby";
            }
            if (view == "Lobby")
            {
                await ClickAsync(new Point(778, 555)).ConfigureAwait(false);
                await RequireViewAsync("Guild").ConfigureAwait(false);
                view = "Guild";
            }
            if (view == "Guild")
            {
                if (!MissionEvidence.GuildMenuOpen(_io.Grab()))
                    await ClickAsync(new Point(748, 419), park: false).ConfigureAwait(false);
                var opened = _io.Grab();
                if (View(opened) != "Guild" || !MissionEvidence.GuildMenuOpen(opened))
                    throw new InvalidOperationException("The guild activities menu was not confirmed.");
                await ClickAsync(new Point(696, 310)).ConfigureAwait(false);
                await RequireViewAsync("GuildTasks").ConfigureAwait(false);
                view = "GuildTasks";
            }
            if (view != "GuildTasks") throw new InvalidOperationException("Return to a lobby map or close the current popup before opening guild tasks.");
            return Observe("guild task board opened; inspect rewards and requirements before selecting a task");
        }

        private async Task<MissionObservation> PublishAsync()
        {
            var view = View(_io.Grab());
            if (view != "GuildPublish")
            {
                await OpenGuildAsync().ConfigureAwait(false);
                view = "GuildTasks";
            }
            if (view == "GuildTasks")
            {
                var observation = Observe();
                if (!observation.CanPublish) { observation.Result = "publication unavailable or already published; no draw was sent"; return observation; }
                await ClickAsync(new Point(125, 57)).ConfigureAwait(false);
                await RequireViewAsync("GuildPublish").ConfigureAwait(false);
                view = "GuildPublish";
            }
            if (view != "GuildPublish") throw new InvalidOperationException("Return to town or open the guild task board before publishing.");
            if (MissionEvidence.Count(_io.Grab(), new Rectangle(436, 425, 96, 24), MissionEvidence.Orange) < 180)
                return Observe("publication draw unavailable; no click was sent");
            await ClickAsync(new Point(484, 440), 5000).ConfigureAwait(false);
            await RequireViewAsync("GuildPublish").ConfigureAwait(false);
            if (MissionEvidence.Count(_io.Grab(), new Rectangle(436, 425, 96, 24), MissionEvidence.Orange) >= 180)
                throw new InvalidOperationException("Publication was not confirmed. Inspect before retrying the draw.");
            await ClickAsync(new Point(635, 200)).ConfigureAwait(false);
            await RequireViewAsync("GuildTasks").ConfigureAwait(false);
            var published = Observe();
            if (published.CanPublish) throw new InvalidOperationException("Guild publication remains available; its outcome is uncertain.");
            published.Result = "guild-leader tasks published and publication disabled";
            return published;
        }
    }
}
