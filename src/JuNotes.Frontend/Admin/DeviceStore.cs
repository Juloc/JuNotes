using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Base.Accounts;
using Npgsql;

namespace JuNotes.Frontend.Admin
{
    // A device of a person as the person sees it. Never the token and never its hash.
    public sealed record DeviceInfo(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastSeen, string? LastAddress);

    // What the door needs when a device token is sent: the device and the person it stands for (active, not blocked).
    public sealed record DeviceHit(Guid DeviceId, string DeviceName, Guid PersonId, string PersonName, bool IsAdmin);

    // What the administrator sees of a person's devices: how many, nothing else (no name, no address, never a token).
    public sealed record PersonDevices(Guid Id, string Name, string State, int Devices);

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

            // The code is spent and the token made in one transaction: a failure in between leaves the code as it was (and no token), never a spent code without a device.
            await using var connection = await data.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            Guid? person = null;
            if (hit is { } id)
            {
                await using var spend = new NpgsqlCommand("delete from pairing_codes where id = $1 and expires_at > $2 returning person_id", connection, transaction);
                spend.Parameters.Add(new NpgsqlParameter { Value = id });
                spend.Parameters.Add(new NpgsqlParameter { Value = now });
                person = await spend.ExecuteScalarAsync(ct) as Guid?;
            }

            // The same query and the same work whether or not there was a hit (the person of the code, or a made-up one that is not there).
            await using var who = new NpgsqlCommand("select name from users where id = $1 and state = 'active'", connection, transaction);
            who.Parameters.Add(new NpgsqlParameter { Value = person ?? Guid.Empty });
            var personName = await who.ExecuteScalarAsync(ct) as string;
            if (person is not { } personId || personName is null)
            {
                // A code of a person who is not active any more is spent all the same.
                await transaction.CommitAsync(ct);
                return null;
            }

            var token = TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
            var deviceId = Guid.NewGuid();
            await using (var insert = new NpgsqlCommand("insert into device_tokens (id, person_id, name, hash, created_at, last_seen, last_address) values ($1, $2, $3, $4, $5, $5, $6)", connection, transaction))
            {
                insert.Parameters.Add(new NpgsqlParameter { Value = deviceId });
                insert.Parameters.Add(new NpgsqlParameter { Value = personId });
                insert.Parameters.Add(new NpgsqlParameter { Value = CleanName(deviceName) });
                insert.Parameters.Add(new NpgsqlParameter { Value = HashOf(token) });
                insert.Parameters.Add(new NpgsqlParameter { Value = now });
                insert.Parameters.Add(new NpgsqlParameter { Value = address });
                await insert.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return new Paired(token, deviceId, personName);
        }

        // ----- Tokens -----

        // The device a token belongs to, when it is one that works: not removed, and its person active. The last sighting is written at most once a minute.
        public async Task<DeviceHit?> FindAsync(string token, string address, CancellationToken ct)
        {
            await using var find = data.CreateCommand("select d.id, d.name, d.person_id, u.name, d.last_seen, 'admin' = any(u.roles) from device_tokens d join users u on u.id = d.person_id where d.hash = $1 and d.revoked_at is null and u.state = 'active'");
            find.Parameters.Add(new NpgsqlParameter { Value = HashOf(token) });
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            var hit = new DeviceHit(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3), !reader.IsDBNull(5) && reader.GetBoolean(5));
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

        // ----- The administrator -----

        // Every person who can sign in, with the number of their devices that still work.
        public async Task<IReadOnlyList<PersonDevices>> CountsAsync(CancellationToken ct)
        {
            await using var command = data.CreateCommand("select u.id, u.name, u.state, count(d.id) from users u left join device_tokens d on d.person_id = u.id and d.revoked_at is null where u.totp_confirmed or u.hub_sub is not null group by u.id, u.name, u.state order by lower(u.name), u.id");
            await using var reader = await command.ExecuteReaderAsync(ct);
            var list = new List<PersonDevices>();
            while (await reader.ReadAsync(ct))
            {
                list.Add(new PersonDevices(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), (int)reader.GetInt64(3)));
            }

            return list;
        }

        // How many devices with a key of their own were seen in the last 30 days (a device counts as seen when it is paired). The shared token can only be switched off when
        // there is at least one: without it nothing but the shared token has been used, and switching it off would shut the app out.
        public async Task<int> SeenRecentlyAsync(CancellationToken ct)
        {
            await using var command = data.CreateCommand("select count(*) from device_tokens where revoked_at is null and last_seen >= $1");
            command.Parameters.Add(new NpgsqlParameter { Value = time.GetUtcNow() - TimeSpan.FromDays(30) });
            return (int)(long)(await command.ExecuteScalarAsync(ct))!;
        }

        // Revokes every device of a person (a lost iPad: the person pairs the others again). How many there were, and the name for the log; null when there is no such person.
        public async Task<(int Revoked, string Name)?> RevokeAllAsync(Guid person, CancellationToken ct)
        {
            await using var who = data.CreateCommand("select name from users where id = $1");
            who.Parameters.Add(new NpgsqlParameter { Value = person });
            if (await who.ExecuteScalarAsync(ct) is not string name)
            {
                return null;
            }

            await using var revoke = data.CreateCommand("update device_tokens set revoked_at = $2 where person_id = $1 and revoked_at is null");
            revoke.Parameters.Add(new NpgsqlParameter { Value = person });
            revoke.Parameters.Add(new NpgsqlParameter { Value = time.GetUtcNow() });
            return (await revoke.ExecuteNonQueryAsync(ct), name);
        }

        // ----- Helpers -----

        public const int MaxNameCharacters = 240;

        // The name a person gives a device: no control, format (bidi marks, zero-width marks, except the joiners that emoji need) or line separator characters, at most 60 text
        // elements (a letter with its accents, an emoji with its modifiers are one) and 240 UTF-16 units, cut between elements and never inside one; never empty.
        public static string CleanName(string? name)
        {
            var kept = new StringBuilder();
            foreach (var rune in (name ?? string.Empty).EnumerateRunes())
            {
                var category = Rune.GetUnicodeCategory(rune);
                if (Rune.IsControl(rune) || (category == UnicodeCategory.Format && rune.Value is not (0x200C or 0x200D)) || category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                {
                    continue;
                }

                kept.Append(rune);
            }

            var text = new StringBuilder();
            var elements = 0;
            var enumerator = StringInfo.GetTextElementEnumerator(kept.ToString().Trim());
            while (enumerator.MoveNext())
            {
                var element = (string)enumerator.Current;
                if (elements == MaxNameLength || text.Length + element.Length > MaxNameCharacters)
                {
                    break;
                }

                text.Append(element);
                elements++;
            }

            var result = text.ToString().TrimEnd();
            return result.Length == 0 ? "Gerät" : result;
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
