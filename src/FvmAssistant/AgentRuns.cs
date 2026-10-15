using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FvmAssistant.Automation;

namespace FvmAssistant
{
    internal sealed class AgentException : Exception
    {
        public AgentException(string code, string message) : base(message) { Code = code; }
        public string Code { get; }
    }

    internal sealed class AgentRun
    {
        private readonly object _gate = new object();
        private GrindLoop _loop;
        private string _error;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string RequestId { get; set; }
        public string Fingerprint { get; set; }
        public int Rounds { get; set; }
        public string CreatedAt { get; } = DateTime.UtcNow.ToString("o");
        public GrindLoop Loop { get { lock (_gate) return _loop; } }
        public void Attach(GrindLoop loop) { lock (_gate) _loop = loop; }
        public void Fail(string code) { lock (_gate) _error = code; }

        internal static string Status(string phase) => phase == "finished" ? "completed" :
            phase == "finished-early" || phase == "stopped" ? "stopped" :
            phase == "failed" ? "failed" : "running";

        public object Snapshot()
        {
            lock (_gate)
            {
                string phase = _loop?.State;
                string status = _error != null ? "failed" : _loop == null ? "starting" : Status(phase);
                return new
                {
                    runId = Id, requestId = RequestId, createdAt = CreatedAt, status,
                    phase, roundsRequested = Rounds, roundsDone = _loop?.RoundsDone ?? 0,
                    bossRoundsDone = _loop?.BossRoundsDone ?? 0,
                    finishAfterRoundRequested = _loop?.FinishAfterRoundRequested ?? false,
                    screen = _loop?.LastScreen.ToString(),
                    errorCode = _error ?? (status == "failed" ? "RUN_FAILED" : null),
                    needsAttention = status == "failed",
                };
            }
        }
    }

    /// <summary>Process-lifetime deduplication; never evict an accepted request and accidentally replay it.</summary>
    internal sealed class AgentRunRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, AgentRun> _requests = new Dictionary<string, AgentRun>(StringComparer.Ordinal);
        private readonly Dictionary<string, AgentRun> _runs = new Dictionary<string, AgentRun>(StringComparer.Ordinal);
        public string InstanceId { get; } = Guid.NewGuid().ToString("N");

        public AgentRun Start(string instanceId, string requestId, string fingerprint, int rounds,
            Func<AgentRun, Task> launch)
        {
            lock (_gate)
            {
                if (instanceId != InstanceId)
                    throw new AgentException("INSTANCE_CHANGED", "Read account state before starting on this instance. Do not replay an old request after restart.");
                if (!Regex.IsMatch(requestId ?? "", "^[A-Za-z0-9_-]{1,80}$"))
                    throw new AgentException("INVALID_REQUEST_ID", "requestId must contain 1-80 letters, digits, underscores or hyphens.");
                if (_requests.TryGetValue(requestId, out var previous))
                {
                    if (previous.Fingerprint != fingerprint)
                        throw new AgentException("REQUEST_CONFLICT", "This requestId was already used for a different plan.");
                    return previous;
                }
                if (_runs.Count >= 64)
                    throw new AgentException("RUN_HISTORY_FULL", "This instance has accepted 64 requests. Finish all work before restarting it.");
                var run = new AgentRun { RequestId = requestId, Fingerprint = fingerprint, Rounds = rounds };
                _requests.Add(requestId, run);
                _runs.Add(run.Id, run);
                // The request remains queryable even if the caller loses its HTTP connection.
                _ = Task.Run(async () =>
                {
                    try { await launch(run).ConfigureAwait(false); }
                    catch (AgentException e) { run.Fail(e.Code); }
                    catch { run.Fail("START_FAILED"); }
                });
                return run;
            }
        }

        public AgentRun Find(string runId, string requestId)
        {
            lock (_gate)
            {
                if (!string.IsNullOrEmpty(runId) && string.IsNullOrEmpty(requestId) && _runs.TryGetValue(runId, out var run)) return run;
                if (!string.IsNullOrEmpty(requestId) && string.IsNullOrEmpty(runId) && _requests.TryGetValue(requestId, out run)) return run;
                throw new AgentException("RUN_NOT_FOUND", "Supply one known runId or requestId from this instance.");
            }
        }

        public string IdFor(GrindLoop loop)
        {
            lock (_gate)
            {
                if (loop == null) return null;
                foreach (var run in _runs.Values) if (ReferenceEquals(run.Loop, loop)) return run.Id;
                return null;
            }
        }
    }
}
