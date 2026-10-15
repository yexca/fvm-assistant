using System;
using System.Collections.Generic;
using System.Linq;

namespace FvmAssistant.Automation
{
    /// <summary>
    /// Duo mode runs independent ordinary formations, fixed bursts and fixed stage supports.
    /// A paired plan can be compared by the coordinator before either browser is touched.
    /// </summary>
    internal sealed class DuoTrackPlan
    {
        private DuoTrackPlan(Cell player, IReadOnlyList<Cell> cells)
        {
            Player = player;
            Cells = cells;
        }

        public Cell Player { get; }
        public IReadOnlyList<Cell> Cells { get; }

        public static DuoTrackPlan Build(TrackPreflight preflight, IEnumerable<string> selectedNames, bool shovel)
        {
            var names = new HashSet<string>(selectedNames);
            preflight.Inspect(names, shovel, DeckBuilder.DeckSlots, duo: true).RequireCompatible();
            if (shovel) throw new NotSupportedException("Duo mode does not coordinate shovel actions yet.");
            if (preflight.Track.Player1Cells.Count != 1)
                throw new NotSupportedException("Each duo track must specify exactly one 人物1位置 for its own account.");
            var selected = preflight.DeckCards.Where(c => names.Contains(c.Name)).ToList();
            var carriers = preflight.Carriers;
            if (carriers != null && (carriers.Cards.Count != 1 ||
                carriers.Cards.SelectMany(c => c.Steps).Any(s => s.TimeDeciseconds != 0)))
                throw new NotSupportedException("Duo mode supports one fixed stage support layout; timed or mixed layouts are not coordinated.");
            if (selected.Any(c => c.Layer != 3 && c.Layer != 5 && !(c.Layer == 1 && carriers?.Matches(c) == true)))
                throw new NotSupportedException("Duo mode supports ordinary layer-3 cards, fixed layer-5 bursts and the selected stage's fixed layer-1 support only.");
            if (selected.Where(c => c.Layer == 5).Any(c => !c.Fill || !c.Extreme ||
                c.Steps.Any(s => s.TimeDeciseconds != 0)))
                throw new NotSupportedException("Duo burst cards must repeat on fixed time-zero targets; timed bursts are not coordinated.");
            var supports = selected.Where(c => c.Layer == 1).ToList();
            if (supports.Any(c => c.Steps.Count > 0 && (!c.Fill || !c.Extreme ||
                c.Steps.Any(s => s.TimeDeciseconds != 0))))
                throw new NotSupportedException("Duo support cards must maintain a fixed formation; timed support steps are not coordinated.");
            if (carriers != null)
            {
                if (supports.Count == 0 && selected.Where(c => c.Layer == 3 && !c.RequiresBareWater)
                    .SelectMany(c => c.FormationCells).Any(c => carriers.Cells.Contains(c)))
                    throw new NotSupportedException("An account placing cards on support cells must carry its own matching support card.");
            }
            // Empty support rows repair only this account's ordinary targets. Inherited rows
            // already include their resolved formation, so every possible support click is covered.
            var cells = selected.SelectMany(c => c.FormationCells).Distinct().ToList();
            return new DuoTrackPlan(preflight.Track.Player1Cells[0], cells);
        }

        public static void RequireDisjoint(DuoTrackPlan host, DuoTrackPlan guest)
        {
            if (host.Player.ToString() == guest.Player.ToString())
                throw new InvalidOperationException("Both duo characters target the same cell.");
            var hostCells = new HashSet<string>(host.Cells.Select(c => c.ToString()));
            var guestCells = new HashSet<string>(guest.Cells.Select(c => c.ToString()));
            var overlap = hostCells.Intersect(guestCells).OrderBy(c => c).ToList();
            if (overlap.Count > 0)
                throw new InvalidOperationException("Duo tracks both place cards on: " + string.Join(", ", overlap));
            if (hostCells.Contains(guest.Player.ToString()) || guestCells.Contains(host.Player.ToString()))
                throw new InvalidOperationException("A duo card formation targets the other character's cell.");
        }
    }
}
