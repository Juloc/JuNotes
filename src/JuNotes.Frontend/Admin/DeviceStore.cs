using System.Security.Cryptography;
using System.Text;
using Base.Accounts;
using Npgsql;

namespace JuNotes.Frontend.Admin
{
    // A device of a person as the person sees it. Never the token and never its hash.
    public sealed record DeviceInfo(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastSeen, string? LastAddress);

    // What the door needs when a device token is sent: the device and the person it stands for (active, not blocked).
    public sealed record DeviceHit(Guid DeviceId, string DeviceName, Guid PersonId, string PersonName);

    // The answer to a pairing: the token (shown once, never stored in the clear), the device and the person.
    public sealed record Paired(string Token, Guid DeviceId, string PersonName);

    // Device tokens and the one-time codes that pair them (ARCHITECTURE 10.1). Both are random and kept only as their SHA-256.
    //  - A pairing code: 10 characters of the Base32 alphabet (50 bit), ten minutes, one use, deleted at its first hit. A person has at most one live code, a new one replaces
    //    the old. It is looked for in constant time: the hash of what was sent is compared with every live code (there are a few at most), not with a query whose time
    //    depends on the hit.
    //  - A device token: "nt1_" and 256 random bits. The prefix lets a lost token be recognised in a find. The row of a removed device stays (revoked_at) and stops working.
    public sealed class DeviceStore(NpgsqlDataSource data, TimeProvider time)
    {
        public const string TokenPrefix = "nt1_";
        public const int CodeLength = 10;
        public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
        public const int MaxNameLength = 60;

        // At most once a minute for a device: the door should not write on every request.
        private static readonly TimeSpan SeenGap = TimeSpan.FromMinutes(1);

        // ----- Codes -----

        // A new code for the person (any live code of theirs is gone); expired codes of everybody are swept at the same time.
        public async Task<(string Code, DateTimeOffset ExpiresAt)> NewCodeAsync(Guid person, CancellationToken ct)
        {
            var now = time.GetUtcNow();
            var code = Base32.Encode(RandomNumberGenerator.GetBytes(7))[..CodeLength];
            var expires = now + CodeLifetime;
            await using var connection = await data.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var sweep = new NpgsqlCommand("delete from pairing_codes where person_id = $1 or expires_at <= $2", connection, transaction))
            {
                sweep.Parameters.Add(new NpgsqlParameter { Value = person });
                sweep.Parameters.Add(new NpgsqlParameter { Value = now });
                await sweep.ExecuteNonQueryAsync(ct);
            }

            await using (var insert = new NpgsqlCommand("insert into pairing_codes (id, person_id, hash, expires_at, created_at) values ($1, $2, $3, $4, $5)", connection, transaction))
            {
                insert.Parameters.Add(new NpgsqlParameter { Value = Guid.NewGuid() });
                insert.Parameters.Add(new NpgsqlParameter { Value = person });
                insert.Parameters.Add(new NpgsqlParameter { Value = HashOf(code) });
                insert.Parameters.Add(new NpgsqlParameter { Value = expires });
                insert.Parameters.Add(new NpgsqlParameter { Value = now });
                await insert.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return (code, expires);
        }

        // The code as typed or scanned: capitals, without dashes and blanks.
        public static string Normalise(string? code) => new((code ?? string.Empty).Where(c => c is not ('-' or ' ')).Select(char.ToUpperInvariant).ToArray());

        // Null for every code that does not work: never existed, expired, already used, or of a person who is no longer active. The work is the same in all of these cases.
        // A hit is spent in the same statement that finds the owner (delete ... returning), so two requests with the same code never both get a token.
        public async Task<Paired?> RedeemAsync(string? sent, string? deviceName, string address, CancellationToken ct)
        {
            var now = time.GetUtcNow();
            var sentHash = Convert.FromHexString(HashOf(Normalise(sent)));
            Guid? hit = null;
            await using (var live = data.CreateCommand("select id, hash from pairing_codes where expires_at > $1"))
            {
                live.Parameters.Add(new NpgsqlParameter { Value = now });
                await using var reader = await live.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    // Every live code is compared, whether or not an earlier one matched.
                    if (CryptographicOperations.FixedTimeEquals(sentHash, Convert.FromHexString(reader.GetString(1))))
                    {
                        hit = reader.GetGuid(0);
                    }
                }
            }

            Guid? person = null;
            if (hit is { } id)
            {
                await using var spend = data.CreateCommand("delete from pairing_codes where id = $1 and expires_at > $2 returning person_id");
                spend.Parameters.Add(new NpgsqlParameter { Value = id });
                spend.Parameters.Add(new NpgsqlParameter { Value = now });
                person = await spend.ExecuteScalarAsync(ct) as Guid?;
            }

            // The same query and the same work whether or not there was a hit (the person of the code, or a made-up one that is not there).
            await using var who = data.CreateCommand("select name from users where id = $1 and state = 'active'");
            who.Parameters.Add(new NpgsqlParameter { Value = person ?? Guid.Empty });
            var personName = await who.ExecuteScalarAsync(ct) as string;
            if (person is not { } personId || personName is null)
            {
                return null;
            }

            var token = TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
            var deviceId = Guid.NewGuid();
            await using var insert = data.CreateCommand("insert into device_tokens (id, person_id, name, hash, created_at, last_seen, last_address) values ($1, $2, $3, $4, $5, $5, $6)");
            insert.Parameters.Add(new NpgsqlParameter { Value = deviceId });
            insert.Parameters.Add(new NpgsqlParameter { Value = personId });
            insert.Parameters.Add(new NpgsqlParameter { Value = CleanName(deviceName) });
            insert.Parameters.Add(new NpgsqlParameter { Value = HashOf(token) });
            insert.Parameters.Add(new NpgsqlParameter { Value = now });
            insert.Parameters.Add(new NpgsqlParameter { Value = address });
            await insert.ExecuteNonQueryAsync(ct);
            return new Paired(token, deviceId, personName);
        }

        // ----- Tokens -----

        // The device a token belongs to, when it is one that works: not removed, and its person active. The last sighting is written at most once a minute.
        public async Task<DeviceHit?> FindAsync(string token, string address, CancellationToken ct)
        {
            await using var find = data.CreateCommand("select d.id, d.name, d.person_id, u.name, d.last_seen from device_tokens d join users u on u.id = d.person_id where d.hash = $1 and d.revoked_at is null and u.state = 'active'");
            find.Parameters.Add(new NpgsqlParameter { Value = HashOf(token) });
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            var hit = new DeviceHit(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3));
            var seen = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(4);
            await reader.DisposeAsync();
            var now = time.GetUtcNow();
            if (seen is null || now - seen >= SeenGap)
            {
                await using var touch = data.CreateCommand("update device_tokens set last_seen = $2, last_address = $3 where id = $1");
                touch.Parameters.Add(new NpgsqlParameter { Value = hit.DeviceId });
                touch.Parameters.Add(new NpgsqlParameter { Value = now });
                touch.Parameters.Add(new NpgsqlParameter { Value = address });
                await touch.ExecuteNonQueryAsync(ct);
            }

            return hit;
        }

        // ----- Lists and removal -----

        // The devices of one person that still work, newest first.
        public async Task<IReadOnlyList<DeviceInfo>> ListAsync(Guid person, CancellationToken ct)
        {
            await using var command = data.CreateCommand("select id, name, created_at, last_seen, last_address from device_tokens where person_id = $1 and revoked_at is null order by created_at desc, id");
            command.Parameters.Add(new NpgsqlParameter { Value = person });
            await using var reader = await command.ExecuteReaderAsync(ct);
            var list = new List<DeviceInfo>();
            while (await reader.ReadAsync(ct))
            {
                list.Add(new DeviceInfo(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }

            return list;
        }

        // Removes a device of this person; the name for the log, or null when the device is not one of theirs (or is already gone): the answer is then the same as for an unknown id.
        public async Task<string?> RemoveAsync(Guid person, Guid device, CancellationToken ct)
        {
            await using var command = data.CreateCommand("update device_tokens set revoked_at = $3 where id = $1 and person_id = $2 and revoked_at is null returning name");
            command.Parameters.Add(new NpgsqlParameter { Value = device });
            command.Parameters.Add(new NpgsqlParameter { Value = person });
            command.Parameters.Add(new NpgsqlParameter { Value = time.GetUtcNow() });
            return await command.ExecuteScalarAsync(ct) as string;
        }

        // ----- Helpers -----

        // The name a person gives a device: no control characters, at most 60 characters, never empty.
        public static string CleanName(string? name)
        {
            var text = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (text.Length > MaxNameLength)
            {
                text = text[..MaxNameLength].TrimEnd();
            }

            return text.Length == 0 ? "Gerät" : text;
        }

        // The address as shown to the person: the last part is cut off (a device is recognised by its network, not by a full address).
        public static string? Shorten(string? address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return null;
            }

            var slash = address.IndexOf('/', StringComparison.Ordinal);
            var bare = slash > 0 ? address[..slash] : address;
            var dot = bare.LastIndexOf('.');
            if (dot > 0 && !bare.Contains(':', StringComparison.Ordinal))
            {
                return bare[..dot] + ".x";
            }

            var colons = bare.Split(':');
            return colons.Length > 3 ? string.Join(':', colons.Take(3)) + "::" : bare;
        }

        private static string HashOf(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
