using System.Collections.Concurrent;
using Base.Accounts;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.Service;
using Npgsql;

namespace JuNotes.Frontend.Admin
{
    // Who the instance token of the iPad app stands for: the first administrator, as long as the app knows no per-person token (milestone 3). Rows and files that belong
    // to nobody yet (everything made before the first account existed) are given to that person once, as soon as the first account is there.
    public sealed class OwnerDirectory(NpgsqlDataSource data, SyncService sync, AssetService assets)
    {
        private volatile bool _claimed;

        public async Task<SyncOwner?> TokenOwnerAsync(CancellationToken ct)
        {
            await using var command = data.CreateCommand(
                "select id, name from users where 'admin' = any(roles) and state = 'active' and (totp_confirmed or hub_sub is not null) order by created_at, id limit 1");
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            var owner = new SyncOwner(reader.GetGuid(0), reader.GetString(1), "app-token");
            await reader.DisposeAsync();
            if (!_claimed)
            {
                await sync.ClaimAsync(owner.Id, ct);
                await assets.ClaimFilesAsync(owner.Id, ct);
                _claimed = true;
            }

            return owner;
        }

        // Called whenever somebody signs in or the server starts, so the claim does not wait for the first request of the app.
        public async Task EnsureClaimedAsync(CancellationToken ct)
        {
            if (!_claimed)
            {
                _ = await TokenOwnerAsync(ct);
            }
        }
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
