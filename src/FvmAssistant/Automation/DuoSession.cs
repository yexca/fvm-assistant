using System;

namespace FvmAssistant.Automation
{
    internal enum DuoRole { Host, Guest }

    /// <summary>
    /// Per-process gates for a two-player room. The external coordinator carries
    /// the run ID and round number on every command, so a late request cannot start a later run.
    /// </summary>
    internal sealed class DuoSession
    {
        private readonly object _gate = new object();
        private int _roomReleased;
        private int _battleReleased;
        private int _bossReleased;
        private DateTime _lastContactUtc = DateTime.UtcNow;

        public DuoSession(DuoRole role, int targetRoom = 0)
        {
            Role = role;
            TargetRoom = targetRoom;
            RunId = Guid.NewGuid().ToString("N");
        }

        public DuoRole Role { get; }
        public int TargetRoom { get; }
        public string RunId { get; }
        public int RoomNumber { get { lock (_gate) return _roomNumber; } }
        private int _roomNumber;

        public void SetRoomNumber(int number)
        {
            if (number < 1 || number > 9999) throw new ArgumentOutOfRangeException(nameof(number));
            lock (_gate)
            {
                if (_roomNumber != 0 && _roomNumber != number)
                    throw new InvalidOperationException("The paired room number changed during this run.");
                if (TargetRoom != 0 && TargetRoom != number)
                    throw new InvalidOperationException("Joined room number does not match the host's room.");
                _roomNumber = number;
            }
        }

        public void Touch()
        {
            lock (_gate) _lastContactUtc = DateTime.UtcNow;
        }

        public bool ContactExpired(TimeSpan limit)
        {
            lock (_gate) return DateTime.UtcNow - _lastContactUtc > limit;
        }

        public void ReleaseRoom(string runId, int round, int currentRound, string state)
        {
            lock (_gate)
            {
                Verify(runId, round, currentRound, state, "duo-room-ready");
                _roomReleased = round;
                _lastContactUtc = DateTime.UtcNow;
            }
        }

        public void ReleaseBattle(string runId, int round, int currentRound, string state)
        {
            lock (_gate)
            {
                Verify(runId, round, currentRound, state, "duo-battle-ready");
                _battleReleased = round;
                _lastContactUtc = DateTime.UtcNow;
            }
        }

        public void ReleaseBoss(string runId, int round, int currentRound, string state)
        {
            lock (_gate)
            {
                Verify(runId, round, currentRound, state, "duo-boss-ready");
                _bossReleased = round;
                _lastContactUtc = DateTime.UtcNow;
            }
        }

        public bool RoomReleased(int round) { lock (_gate) return _roomReleased == round; }
        public bool BattleReleased(int round) { lock (_gate) return _battleReleased == round; }
        public bool BossReleased(int round) { lock (_gate) return _bossReleased == round; }

        private void Verify(string runId, int round, int currentRound, string state, string requiredState)
        {
            if (runId != RunId) throw new InvalidOperationException("Duo run ID does not match the active run.");
            if (round != currentRound || round < 1)
                throw new InvalidOperationException("Duo round does not match the active round.");
            if (state != requiredState)
                throw new InvalidOperationException("Duo gate is not ready: " + state);
        }
    }
}
