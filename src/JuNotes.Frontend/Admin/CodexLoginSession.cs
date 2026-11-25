using JuNotes.Integrations.Codex;

namespace JuNotes.Frontend.Admin
{
    // The device login of Codex that is running right now, if any: one at a time, and it ends after the CLI's own sixteen minutes.
    public sealed class CodexLoginSession(TimeProvider time) : IDisposable
    {
        // One login at a time, also when two people press the button together: the second waits for the first and then replaces it.
        private readonly SemaphoreSlim _starting = new(1, 1);
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(16);
        private readonly object _gate = new();
        private DeviceLogin? _current;
        private DateTimeOffset _startedAt;

        public async Task<DeviceCode?> StartAsync(CodexAccount account, CancellationToken ct)
        {
            await _starting.WaitAsync(ct);
            try
            {
                Cancel();
                var login = await account.StartLoginAsync(ct);
                if (login is null)
                {
                    return null;
                }

                lock (_gate)
                {
                    _current?.Dispose();
                    _current = login;
                    _startedAt = time.GetUtcNow();
                }

                return login.Code;
            }
            finally
            {
                _starting.Release();
            }
        }

        // The code to show, or null when none is running, it ended or it ran out.
        public DeviceCode? Waiting
        {
            get
            {
                lock (_gate)
                {
                    if (_current is null || _current.HasEnded || time.GetUtcNow() - _startedAt > Lifetime)
                    {
                        return null;
                    }

                    return _current.Code;
                }
            }
        }

        public void Cancel()
        {
            lock (_gate)
            {
                _current?.Dispose();
                _current = null;
            }
        }

        public void Dispose()
        {
            Cancel();
            _starting.Dispose();
        }
    }
}
