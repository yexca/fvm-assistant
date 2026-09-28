using System;
using System.Collections.Generic;
using System.Linq;

namespace FvmAssistant.Automation
{
    /// <summary>Tracks individual timed actions so a failed action cannot consume later events.</summary>
    internal sealed class PlacementSchedule
    {
        private readonly IReadOnlyList<TrackStep> _steps;
        private readonly bool[] _complete;
        private readonly DateTime?[] _firstAttempt;
        private readonly DateTime[] _retryAt;

        public PlacementSchedule(IReadOnlyList<TrackStep> steps)
        {
            _steps = steps;
            _complete = new bool[steps.Count];
            _firstAttempt = new DateTime?[steps.Count];
            _retryAt = new DateTime[steps.Count];
        }

        public bool Done => _complete.All(c => c);
        public int Pending => _complete.Count(c => !c);

        public int? Next(double elapsed, DateTime now, bool extreme)
        {
            // Newly due events take precedence over old repairs, even within the same card slot.
            for (int i = 0; i < _steps.Count; i++)
                if (!_complete[i] && !_firstAttempt[i].HasValue && elapsed >= _steps[extreme ? 0 : i].TimeSec)
                    return i;
            for (int i = 0; i < _steps.Count; i++)
                if (!_complete[i] && _firstAttempt[i].HasValue && now >= _retryAt[i])
                    return i;
            return null;
        }

        public void Attempt(int index, DateTime now)
        {
            if (!_firstAttempt[index].HasValue) _firstAttempt[index] = now;
        }

        public void Complete(int index, bool supersedeEarlierCell = false)
        {
            _complete[index] = true;
            if (supersedeEarlierCell)
                for (int i = 0; i < index; i++)
                    if (_steps[i].Cell.Equals(_steps[index].Cell)) _complete[i] = true;
        }

        public void Retry(int index, DateTime now)
        {
            Attempt(index, now);
            _complete[index] = false;
            // Give even a late first attempt a full rapid-retry window. Then keep the event pending
            // at a lower rate; a transient obstruction must not permanently lose a support.
            _retryAt[index] = now + TimeSpan.FromMilliseconds(
                now - _firstAttempt[index].Value < TimeSpan.FromSeconds(6) ? 500 : 5000);
        }
    }
}
