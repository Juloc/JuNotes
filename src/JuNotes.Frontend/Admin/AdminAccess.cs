using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using JuNotes.Integrations.Codex;

namespace JuNotes.Frontend.Admin
{
    public sealed class AdminOptions
    {
        // The admin area is off until this is set (environment variable Admin__Password).
        public string? Password { get; set; }
    }

    public enum LoginResult
    {
        Accepted,
        Refused,
        LockedOut,
        Disabled,
    }

    // The one password of the admin area. Five wrong tries from one address lock that address out for ten minutes, so the password cannot be guessed.
    public sealed class AdminAccess(AdminOptions options, TimeProvider time)
    {
        public const int MaxFailures = 5;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

        private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset Since)> _failures = new();

        public bool Enabled => !string.IsNullOrEmpty(options.Password);

        public LoginResult TryLogin(string? password, string address)
        {
            if (!Enabled)
            {
                return LoginResult.Disabled;
            }

            var now = time.GetUtcNow();
            if (_failures.TryGetValue(address, out var known) && now - known.Since < Window && known.Failures >= MaxFailures)
            {
                return LoginResult.LockedOut;
            }

            // Hashes first, so the comparison takes the same time whatever was typed.
            var typed = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? ""));
            var expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.Password!));
            if (CryptographicOperations.FixedTimeEquals(typed, expected))
            {
                _failures.TryRemove(address, out _);
                return LoginResult.Accepted;
            }

            _failures.AddOrUpdate(address, _ => (1, now), (_, old) => now - old.Since >= Window ? (1, now) : (old.Failures + 1, old.Since));
            return _failures[address].Failures >= MaxFailures ? LoginResult.LockedOut : LoginResult.Refused;
        }
    }

    // The device login of Codex that is running right now, if any: one at a time, and it ends after the CLI's own sixteen minutes.
    public sealed class CodexLoginSession(TimeProvider time) : IDisposable
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(16);
        private readonly object _gate = new();
        private DeviceLogin? _current;
        private DateTimeOffset _startedAt;

        public async Task<DeviceCode?> StartAsync(CodexAccount account, CancellationToken ct)
        {
            Cancel();
            var login = await account.StartLoginAsync(ct);
            if (login is null)
            {
                return null;
            }

            lock (_gate)
            {
                _current = login;
                _startedAt = time.GetUtcNow();
            }

            return login.Code;
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

        public void Dispose() => Cancel();
    }
}
