using System;
using System.Collections.Generic;
using System.Linq;

namespace FvmAssistant.Automation
{
    /// <summary>
    /// The first duo mode runs independent ordinary-card formations on disjoint land cells.
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
            if (selected.Any(c => c.Layer != 3))
                throw new NotSupportedException("Duo mode currently supports ordinary layer-3 cards only.");
            if (preflight.Carriers != null)
                throw new NotSupportedException("Duo mode currently needs a stage without a support-card layout.");
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
