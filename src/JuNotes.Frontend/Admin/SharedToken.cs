using System.Collections.Concurrent;
using Base.Accounts;
using Npgsql;

namespace JuNotes.Frontend.Admin
{
    public sealed record SharedTokenState(int RecentAddresses, DateTimeOffset? LastUse);

    // The shared instance token of the iPad app (Sync__Token) and whether it still counts. It can be switched off in the database (app_instance_settings, key shared_token_off),
    // which needs no change of the environment: from then on the door says 401 to it, also when it is right, and device tokens go on working. It can be switched on again only
    // while the environment still carries the token (a token that is not there cannot be switched on). While it is on, the addresses that use it are written down (at most one row
    // a minute for an address, kept 30 days), so the administration can say how many there were before somebody switches it off.
    public sealed class SharedToken(NpgsqlDataSource data, AuditStore audit, TimeProvider time)
    {
        public const string OffKey = "shared_token_off";
        public static readonly TimeSpan Window = TimeSpan.FromDays(30);
        private static readonly TimeSpan WriteGap = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan LogGap = TimeSpan.FromHours(1);

        private readonly ConcurrentDictionary<string, DateTimeOffset> _written = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> _logged = new();

        // Asked at every request that comes with the shared token and nothing else: the answer is the table, never a copy.
        public async Task<bool> IsOffAsync(CancellationToken ct)
        {
            await using var command = data.CreateCommand("select exists (select 1 from app_instance_settings where key = $1)");
            command.Parameters.Add(new NpgsqlParameter { Value = OffKey });
            return (bool)(await command.ExecuteScalarAsync(ct))!;
        }

        public async Task SetOffAsync(bool off, CancellationToken ct)
        {
            await using var command = data.CreateCommand(off
                ? "insert into app_instance_settings (key, value) values ($1, '1') on conflict (key) do nothing"
                : "delete from app_instance_settings where key = $1");
            command.Parameters.Add(new NpgsqlParameter { Value = OffKey });
            await command.ExecuteNonQueryAsync(ct);
        }

        // A request with the right shared token from this address, while it is on.
        public async Task SeenAsync(string address, CancellationToken ct)
        {
            var now = time.GetUtcNow();
            if (_written.TryGetValue(address, out var last) && now - last < WriteGap)
            {
                return;
            }

            if (_written.Count > 1000)
            {
                _written.Clear();
            }

            _written[address] = now;
            await using var write = data.CreateCommand("insert into shared_token_seen (address, first_seen, last_seen) values ($1, $2, $2) on conflict (address) do update set last_seen = $2");
            write.Parameters.Add(new NpgsqlParameter { Value = address });
            write.Parameters.Add(new NpgsqlParameter { Value = now });
            await write.ExecuteNonQueryAsync(ct);
            await using var sweep = data.CreateCommand("delete from shared_token_seen where last_seen < $1");
            sweep.Parameters.Add(new NpgsqlParameter { Value = now - Window });
            await sweep.ExecuteNonQueryAsync(ct);
        }

        // The right token, but switched off: refused without counting as a wrong token (it is not an attack), and written to the log once an hour for an address, so a device that
        // was forgotten shows up.
        public void Refused(string address)
        {
            var now = time.GetUtcNow();
            if (_logged.TryGetValue(address, out var last) && now - last < LogGap)
            {
                return;
            }

            if (_logged.Count > 1000)
            {
                _logged.Clear();
            }

            _logged[address] = now;
            _ = audit.AddAsync("app-token", "api.token_switched_off", address, "the shared token is switched off, a device still sends it (at most one line an hour for an address)");
        }

        public async Task<SharedTokenState> StateAsync(CancellationToken ct)
        {
            await using var command = data.CreateCommand("select count(*), max(last_seen) from shared_token_seen where last_seen >= $1");
            command.Parameters.Add(new NpgsqlParameter { Value = time.GetUtcNow() - Window });
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return new SharedTokenState((int)reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
        }
    }

    // A fresh code of the person's own second factor, asked before something that opens or closes a door. The code is spent (it works once), wrong ones count for the account
    // and the address with the limiter of the base (the same count as the sign-in), and a person without a second factor of their own gets a clear answer.
    public sealed class FreshSecondFactor(UserStore users, LoginLimiter limiter, TimeProvider time)
    {
        public async Task<Base.Core.Error?> CheckAsync(HttpContext ctx, string? code)
        {
            var who = Base.Web.Auth.RequestIdentity.Person(ctx);
            var address = Base.Web.Auth.RequestIdentity.ClientIp(ctx);
            string[] keys = ["acct:" + who.UserId, "ip:" + address];
            if (limiter.WaitSeconds(keys) is > 0 and var wait)
            {
                return Base.Core.Error.TooMany(wait);
            }

            var user = await users.FindByIdAsync(who.UserId, ctx.RequestAborted);
            if (user is null || user.State != PersonState.Active)
            {
                return Base.Core.Error.Unauthorized();
            }

            // A person who signs in only through the hub has no second factor here to ask for.
            if (!user.TotpConfirmed)
            {
                return Base.Core.Error.Conflict("pairing.needs_totp", "This needs a second factor in this account.");
            }

            var step = Totp.Check(user.TotpSecret, (code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal), user.TotpLastStep, time.GetUtcNow());
            if (step is null || !await users.TryAdvanceTotpStepAsync(user.Id, step.Value, ctx.RequestAborted))
            {
                foreach (var key in keys)
                {
                    limiter.Fail(key);
                }

                return new Base.Core.Error(Base.Core.ErrorKind.Forbidden, "totp.wrong", "The code is not right.");
            }

            limiter.Succeed("acct:" + user.Id);
            return null;
        }
    }
}
