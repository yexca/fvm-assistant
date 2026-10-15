using System.Collections.Generic;
using System.Linq;

namespace FvmAssistant.Automation
{
    internal enum GameScreen
    {
        Unknown,
        /// <summary>美味镇 or an island map (both show 排行 and 跳转).</summary>
        Lobby,
        WorldMap,
        RoomList,
        /// <summary>Room / card selection, where 开始 is.</summary>
        Room,
        /// <summary>A blocking two-button system notice over an otherwise recognized room.</summary>
        RoomNotice,
        Battle,
        /// <summary>After the normal stage: 继续作战 (boss, costs a key) / 领取奖品.</summary>
        VictoryDialog,
        /// <summary>The guest's dialog while the room host chooses reward or boss continuation.</summary>
        AwaitHostDecision,
        /// <summary>"胜利" banner with the loot bar; transitions by itself.</summary>
        Loot,
        Settlement,
        /// <summary>翻宝箱 before shuffling (开始洗牌 visible).</summary>
        TreasureShuffle,
        /// <summary>翻宝箱 with face-down cards to pick.</summary>
        TreasurePick,
        /// <summary>翻宝箱 after a flip (完成 visible; further flips cost gold).</summary>
        TreasureDone,
        /// <summary>Anti-addiction notice shown after login (确定 / 取消, both just close it).</summary>
        HealthNotice,
        /// <summary>An activity window with the red close button at the top right (e.g. 佳节打卡活动).</summary>
        EventPopup,
        /// <summary>
        /// The loader at "加载中···100%": resources are in and the client is logging in. It stays here
        /// when a login server does not answer; only a reload gets it going again.
        /// </summary>
        Loading100,
        /// <summary>"和游戏服务器断开链接，请刷新重新登录" — the game server connection dropped; only a reload recovers.</summary>
        Disconnected,
    }

    internal enum RoomSeat
    {
        Unknown,
        Waiting,
        Closed,
    }

    /// <summary>Classifies a stage frame by checking fixed-position templates, most specific first.</summary>
    internal sealed class ScreenDetector
    {
        // Track templates render pixel-identical in our shell (diff 0.0 in the recording); ours are
        // cut from the same renderer. A small tolerance absorbs cursor hover and animation edges.
        private const double Tolerance = 10;

        /// <summary>
        /// Dialogs that slide in can settle a pixel off; these templates keep only pixels that were stable
        /// across openings and are matched within ±<see cref="Shift"/> px.
        /// </summary>
        private static readonly HashSet<string> Shiftable = new HashSet<string> { "房间列表_标题", "房间列表_创建通关房间", "断开连接", "密码框_空" };
        private const int Shift = 2;

        /// <summary>
        /// The room list dialog sits 20 px further right on levels with tabs beside it
        /// (神殿: 美味岛 / 多元奇遇).
        /// </summary>
        private static readonly int[] RoomListOffsets = { 0, 20 };

        private static readonly (GameScreen screen, string[] all)[] Rules =
        {
            // Notices and popups cover whatever screen was showing, whose own templates can still match
            // around them (the battle's exit button, the room's left panel, 美味镇's 排行/跳转), so they go first.
            // Popups can arrive late on a slow connection, e.g. after the room has opened.
            (GameScreen.Disconnected, new[] { "断开连接" }),
            (GameScreen.HealthNotice, new[] { "健康提示" }),
            (GameScreen.EventPopup, new[] { "活动弹窗_关闭" }),
            (GameScreen.VictoryDialog, new[] { "胜利弹窗" }),
            (GameScreen.TreasureShuffle, new[] { "翻宝箱", "翻宝箱_开始洗牌" }),
            (GameScreen.TreasureDone, new[] { "翻宝箱", "翻宝箱_完成" }),
            (GameScreen.TreasurePick, new[] { "翻宝箱" }),
            (GameScreen.Settlement, new[] { "战斗结算" }),
            (GameScreen.Loot, new[] { "战利品", "退出按钮" }),
            (GameScreen.Battle, new[] { "退出按钮" }),
            (GameScreen.Room, new[] { "选卡界面" }),
            // Title and header only: rooms other players opened can fill the list.
            (GameScreen.RoomList, new[] { "房间列表_标题", "房间列表_创建通关房间" }),
            (GameScreen.WorldMap, new[] { "世界地图" }),
            (GameScreen.Lobby, new[] { "排行", "跳转按钮" }),
            (GameScreen.Loading100, new[] { "加载中100%" }),
        };

        private readonly TemplateLibrary _templates;

        public ScreenDetector(TemplateLibrary templates)
        {
            _templates = templates;
            var missing = Rules.SelectMany(r => r.all).Concat(new[] { "房间_2P等待中", "房间_2P已关闭" })
                .Distinct().Where(n => !templates.Contains(n)).ToList();
            if (missing.Count > 0)
                throw new KeyNotFoundException("missing templates: " + string.Join(", ", missing));
        }

        public GameScreen Detect(StageFrame frame)
        {
            foreach (var (screen, all) in Rules)
            {
                // Pixel heuristics must not hide a positively recognized victory or notice.
                if (screen == GameScreen.Loot && AwaitingHostDecision(frame)) return GameScreen.AwaitHostDecision;
                bool match = screen == GameScreen.RoomList
                    ? RoomListOffset(frame) != null
                    : all.All(n => Diff(n, frame) < Tolerance);
                if (match) return screen == GameScreen.Room && RoomSystemNotice(frame) ? GameScreen.RoomNotice : screen;
            }
            return GameScreen.Unknown;
        }

        /// <summary>Recognize the compact system notice without interpreting or accepting its contents.</summary>
        internal static bool RoomSystemNotice(StageFrame frame)
        {
            if (frame.Width < 600 || frame.Height < 355) return false;
            bool Background(int x, int y)
            {
                int i = (y * frame.Width + x) * 4;
                return System.Math.Abs(frame.Bgra[i + 2] - 126) <= 6 &&
                    System.Math.Abs(frame.Bgra[i + 1] - 200) <= 6 &&
                    System.Math.Abs(frame.Bgra[i] - 245) <= 6;
            }
            bool Gold(int x, int y)
            {
                int i = (y * frame.Width + x) * 4;
                return frame.Bgra[i + 2] > 220 && frame.Bgra[i + 1] > 135 &&
                    frame.Bgra[i + 1] < 210 && frame.Bgra[i] < 65;
            }
            return Background(360, 264) && Background(594, 264) &&
                Background(360, 327) && Background(594, 327) &&
                Gold(394, 347) && Gold(451, 347) && Gold(494, 347) && Gold(551, 347);
        }

        /// <summary>Recognize the guest-only blue decision overlay without storing an account capture.</summary>
        private static bool AwaitingHostDecision(StageFrame frame)
        {
            if (frame.Width < 700 || frame.Height < 370) return false;
            bool Blue(int x, int y)
            {
                int i = (y * frame.Width + x) * 4;
                return frame.Bgra[i] > 175 && frame.Bgra[i + 1] > 125 &&
                    frame.Bgra[i] > frame.Bgra[i + 2] + 25;
            }
            int white = (350 * frame.Width + 500) * 4;
            return Blue(475, 290) && Blue(630, 225) &&
                frame.Bgra[white] > 235 && frame.Bgra[white + 1] > 235 && frame.Bgra[white + 2] > 235;
        }

        /// <summary>How far right of its 曲奇岛 position the room list dialog is, or null when it is not showing.</summary>
        public int? RoomListOffset(StageFrame frame)
        {
            foreach (int offset in RoomListOffsets)
                if (Diff("房间列表_标题", frame, offset) < Tolerance && Diff("房间列表_创建通关房间", frame, offset) < Tolerance)
                    return offset;
            return null;
        }

        public double Diff(string template, StageFrame frame, int offsetX = 0)
        {
            var t = _templates.Get(template);
            double best = t.Diff(frame, offsetX);
            if (best < Tolerance || !Shiftable.Contains(template)) return best;
            for (int dy = -Shift; dy <= Shift; dy++)
                for (int dx = -Shift; dx <= Shift; dx++)
                    if (dx != 0 || dy != 0) best = System.Math.Min(best, t.Diff(frame, offsetX + dx, dy));
            return best;
        }

        /// <summary>
        /// Columns of an empty-field template that hold something now (see <see cref="Template.ChangedColumns"/>),
        /// compared where the template fits best within ±<see cref="Shift"/> px.
        /// </summary>
        public int ChangedColumns(string template, StageFrame frame, int offsetX = 0)
        {
            var t = _templates.Get(template);
            var (bestX, bestY, bestDiff) = (offsetX, 0, t.Diff(frame, offsetX));
            for (int dy = -Shift; dy <= Shift; dy++)
                for (int dx = -Shift; dx <= Shift; dx++)
                {
                    double d = t.Diff(frame, offsetX + dx, dy);
                    if (d < bestDiff) (bestX, bestY, bestDiff) = (offsetX + dx, dy, d);
                }
            return t.ChangedColumns(frame, bestX, bestY);
        }

        public RoomSeat SecondSeat(StageFrame frame)
        {
            if (_templates.Get("房间_2P等待中").Diff(frame) < Tolerance) return RoomSeat.Waiting;
            if (_templates.Get("房间_2P已关闭").Diff(frame) < Tolerance) return RoomSeat.Closed;
            return RoomSeat.Unknown;
        }

        /// <summary>Every rule template's score, for debugging thresholds.</summary>
        public Dictionary<string, double> Scores(StageFrame frame) =>
            Rules.SelectMany(r => r.all).Concat(new[] { "房间_2P等待中", "房间_2P已关闭" }).Distinct()
                .ToDictionary(n => n, n => System.Math.Round(Diff(n, frame), 1));
    }
}
