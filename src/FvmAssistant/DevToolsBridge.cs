using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.Callback;

namespace FvmAssistant
{
    /// <summary>
    /// Minimal request/response wrapper over CEF's DevTools protocol channel (CefSharp 84 only
    /// exposes raw ExecuteDevToolsMethod + an observer).
    /// </summary>
    internal sealed class DevToolsBridge : IDevToolsMessageObserver
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pending = new ConcurrentDictionary<int, TaskCompletionSource<string>>();
        private readonly SemaphoreSlim _registerLock = new SemaphoreSlim(1, 1);
        private IRegistration _registration;
        private int _registeredBrowserId;
        private int _nextId = 1000;

        public async Task<string> CallAsync(IBrowser browser, string method, IDictionary<string, object> parameters, TimeSpan timeout)
        {
            await EnsureObservingAsync(browser).ConfigureAwait(false);

            int id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;
            try
            {
                int sent = await browser.ExecuteDevToolsMethodAsync(id, method, parameters).ConfigureAwait(false);
                if (sent == 0) throw new InvalidOperationException($"ExecuteDevToolsMethod({method}) was rejected");

                var finished = await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(false);
                if (finished != tcs.Task) throw new TimeoutException($"{method} got no reply within {timeout.TotalSeconds:0}s");
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        private async Task EnsureObservingAsync(IBrowser browser)
        {
            await _registerLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_registration != null && _registeredBrowserId == browser.Identifier) return;
                _registration?.Dispose();
                _registration = await Cef.UIThreadTaskFactory.StartNew(() => browser.GetHost().AddDevToolsMessageObserver(this)).ConfigureAwait(false);
                _registeredBrowserId = browser.Identifier;
            }
            finally
            {
                _registerLock.Release();
            }
        }

        public bool OnDevToolsMessage(IBrowser browser, Stream message) => false;

        public void OnDevToolsMethodResult(IBrowser browser, int messageId, bool success, Stream result)
        {
            if (!_pending.TryGetValue(messageId, out var tcs)) return;
            string json;
            // The stream is only valid during this callback and is owned by CefSharp.
            using (var reader = new StreamReader(result, Encoding.UTF8, false, 4096, leaveOpen: true))
                json = reader.ReadToEnd();
            if (success) tcs.TrySetResult(json);
            else tcs.TrySetException(new InvalidOperationException("DevTools error: " + json));
        }

        public void OnDevToolsEvent(IBrowser browser, string method, Stream parameters) { }

        public void OnDevToolsAgentAttached(IBrowser browser) { }

        public void OnDevToolsAgentDetached(IBrowser browser) { }

        public void Dispose() => _registration?.Dispose();
    }
}
