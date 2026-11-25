using System.Collections.Concurrent;
using Base.Accounts;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.Service;
using Npgsql;

namespace JuNotes.Frontend.Admin
{
    // What the instance token stands for right now.
    public enum TokenState
    {
        // No account exists at all yet: everything is as it was and the rows belong to nobody (until the first account claims them).
        Ownerless,

        // A person: the first administrator, written down once and read from there ever after.
        Person,

        // The person written down is gone, blocked or no longer active, or no administrator exists although accounts do: the token opens nothing, and nobody else is chosen.
        Refused,

        // The giving of the old rows to the person did not work (it is logged and tried again at the next start): the door says so instead of working with rows nobody owns.
        Pending,
    }

    public sealed record TokenOwner(TokenState State, SyncOwner? Person = null);

    // A connection of its own without a time limit for the work that may take long on a big table: the claim and the upgrade of the schema.
    public sealed record MaintenanceData(NpgsqlDataSource Source);

    // Who the instance token of the iPad app stands for: the first administrator, as long as the app knows no token per person (milestone 3). The person is written down once
    // (a line in app_instance_settings, key token_owner) and read from there; it is never chosen again, also not when that person is demoted, blocked or deleted: then the token
    // opens nothing (401), because the iPad would otherwise write as somebody else. Rows and files that belong to nobody yet (everything made before the first account existed)
    // are given to that person once, in one statement on a connection without a time limit.
    public sealed partial class OwnerDirectory(NpgsqlDataSource data, MaintenanceData maintenance, AssetService assets, ILogger<OwnerDirectory> log) : IDisposable
    {
        private const string Key = "token_owner";
        private readonly SemaphoreSlim _gate = new(1, 1);
        private volatile bool _claimed;
        private volatile bool _failed;

        // True when giving the old rows to their owner failed: the door of the app answers 503 until the server is restarted (the administration shows it).
        public bool ClaimFailed => _failed;

        public async Task<TokenOwner> TokenOwnerAsync(CancellationToken ct)
        {
            var pinned = await ReadPinAsync(ct);
            if (pinned is null)
            {
                if (!await AnyAccountAsync(ct))
                {
                    return new TokenOwner(TokenState.Ownerless);
                }

                await using var first = data.CreateCommand("select id from users where 'admin' = any(roles) and state = 'active' and (totp_confirmed or hub_sub is not null) order by created_at, id limit 1");
                if (await first.ExecuteScalarAsync(ct) is not Guid candidate)
                {
                    return new TokenOwner(TokenState.Refused);
                }

                await using (var pin = data.CreateCommand("insert into app_instance_settings (key, value) values ($1, $2) on conflict (key) do nothing"))
                {
                    pin.Parameters.Add(new NpgsqlParameter { Value = Key });
                    pin.Parameters.Add(new NpgsqlParameter { Value = candidate.ToString() });
                    await pin.ExecuteNonQueryAsync(ct);
                }

                pinned = await ReadPinAsync(ct);
            }

            await using var who = data.CreateCommand("select name, state from users where id = $1");
            who.Parameters.Add(new NpgsqlParameter { Value = pinned!.Value });
            await using var reader = await who.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.GetString(1) != "active")
            {
                return new TokenOwner(TokenState.Refused);
            }

            var owner = new SyncOwner(pinned.Value, reader.GetString(0), "app-token");
            await reader.DisposeAsync();
            return await ClaimAsync(owner.Id, ct) ? new TokenOwner(TokenState.Person, owner) : new TokenOwner(TokenState.Pending);
        }

        // Called whenever somebody signs in or the server starts, so the claim does not wait for the first request of the app. It never fails the caller.
        public async Task EnsureClaimedAsync(CancellationToken ct)
        {
            if (_claimed || _failed)
            {
                return;
            }

            try
            {
                _ = await TokenOwnerAsync(ct);
            }
            catch (Exception error) when (error is NpgsqlException or InvalidOperationException)
            {
                LogFailed(log, error.Message);
            }
        }

        private async Task<Guid?> ReadPinAsync(CancellationToken ct)
        {
            await using var command = data.CreateCommand("select value from app_instance_settings where key = $1");
            command.Parameters.Add(new NpgsqlParameter { Value = Key });
            return await command.ExecuteScalarAsync(ct) is string text && Guid.TryParse(text, out var id) ? id : null;
        }

        // Any account that can sign in, whatever its state: from then on the server is no longer "new".
        private async Task<bool> AnyAccountAsync(CancellationToken ct)
        {
            await using var command = data.CreateCommand("select exists (select 1 from users where totp_confirmed or hub_sub is not null)");
            return (bool)(await command.ExecuteScalarAsync(ct))!;
        }

        // One at a time; a failed claim is written to the log and not tried again before the next start (false: the door answers 503).
        private async Task<bool> ClaimAsync(Guid owner, CancellationToken ct)
        {
            if (_claimed)
            {
                return true;
            }

            if (_failed)
            {
                return false;
            }

            await _gate.WaitAsync(ct);
            try
            {
                if (_claimed)
                {
                    return true;
                }

                if (_failed)
                {
                    return false;
                }

                try
                {
                    await SyncService.ClaimAsync(owner, maintenance.Source, ct);
                    await assets.ClaimFilesAsync(owner, maintenance.Source, ct);
                    _claimed = true;
                    return true;
                }
                catch (Exception error) when (error is NpgsqlException or IOException or InvalidOperationException)
                {
                    _failed = true;
                    LogFailed(log, error.Message);
                    return false;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose() => _gate.Dispose();

        [LoggerMessage(Level = LogLevel.Error, Message = "Giving the rows without an owner to the first administrator failed. The app cannot sync until it works: fix the cause (see the message) and restart the server, it is tried again at the start: {Message}")]
        private static partial void LogFailed(ILogger logger, string message);
    }

    // The security log of the base takes the broken rules: who (the person), from which device, which rule, how many. At most one line a minute for each person and rule,
    // so a device that keeps sending the same thing cannot fill the log; the lines that were left out are counted in the next one.
    public sealed partial class SecurityLog(AuditStore audit, TimeProvider time, ILogger<SecurityLog> log) : ISecurityLog
    {
        private static readonly TimeSpan Gap = TimeSpan.FromMinutes(1);
        private readonly ConcurrentDictionary<(Guid, string), (DateTimeOffset Last, int Left)> _seen = new();

        public void RuleBroken(SyncOwner owner, string rule, int count)
        {
            var now = time.GetUtcNow();
            var key = (owner.Id, rule);
            var left = 0;
            var write = false;
            _seen.AddOrUpdate(
                key,
                _ =>
                {
                    write = true;
                    return (now, 0);
                },
                (_, old) =>
                {
                    if (now - old.Last >= Gap)
                    {
                        write = true;
                        left = old.Left;
                        return (now, 0);
                    }

                    write = false;
                    return (old.Last, old.Left + count);
                });
            if (!write)
            {
                return;
            }

            _ = WriteAsync(owner, rule, count, left);
        }

        [LoggerMessage(Level = LogLevel.Warning, Message = "The security log could not be written: {Message}")]
        private static partial void LogFailed(ILogger logger, string message);

        private async Task WriteAsync(SyncOwner owner, string rule, int count, int left)
        {
            try
            {
                var detail = $"{count} change(s) refused, device {owner.Device}" + (left > 0 ? $"; {left} more since the last line" : string.Empty);
                await audit.AddAsync(owner.Name, rule, null, detail);
            }
            catch (Exception error) when (error is NpgsqlException or InvalidOperationException)
            {
                LogFailed(log, error.Message);
            }
        }
    }
}

namespace JuNotes.Frontend.Admin
{
    // Too many wrong tokens from one address in a minute lock that address for the rest of the minute, even for the right token; the first lock is written to the security log
    // (never the token that was tried).
    public sealed class TokenBrake(Base.Web.RateLimit limit, AuditStore audit, TimeProvider time)
    {
        public const int FailuresPerMinute = 10;

        private readonly ConcurrentDictionary<string, DateTimeOffset> _locked = new();

        public bool IsLocked(string address) => _locked.TryGetValue(address, out var until) && until > time.GetUtcNow();

        public void Failed(string address)
        {
            if (limit.Allow("v1-token", address, FailuresPerMinute, out var wait))
            {
                return;
            }

            var until = time.GetUtcNow().AddSeconds(wait);
            if (!_locked.TryGetValue(address, out var known) || known <= time.GetUtcNow())
            {
                _locked[address] = until;
                _ = audit.AddAsync("app-token", "api.token_refused", address, $"more than {FailuresPerMinute} wrong tokens in a minute; this address is locked for {wait} seconds");
            }
        }
    }
}
