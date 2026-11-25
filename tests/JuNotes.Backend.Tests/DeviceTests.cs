using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Base.Accounts;
using Base.Core;
using JuNotes.Frontend.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace JuNotes.Backend.Tests
{
    // Devices and pairing (milestone 3, slice 3a): a device token stands for one person and only for their rows; a pairing code is made against a fresh second factor, works
    // once and ten minutes, and says nothing about codes that are not valid. Every test with a second person has two people and two devices.
    public class DeviceTests
    {
        private static readonly Guid Subject = Guid.Parse("30000000-0000-0000-0000-000000000001");
        private static readonly Guid Scan = Guid.Parse("30000000-0000-0000-0000-0000000000d1");
        private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1];

        private static Uri Url(string path) => new(path, UriKind.Relative);

        private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

        private static string Change(Guid id, string name) => $$"""{"changes":[{"kind":"subject","id":"{{id}}","parentId":null,"payload":{"name":"{{name}}"},"deleted":false}]}""";

        private static string[] Strings(JsonElement list, string property) => list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty(property).GetString()!).ToArray();

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        // Two people (the first an administrator, the second a member) with a session each, a throwaway database and a second factor whose key the tests know.
        private sealed class Scene : IAsyncDisposable
        {
            private readonly TestSecondFactor _factor;
            private readonly FakeCodex _fake;

            private Scene(AdminHost host, TestDatabase database, FakeCodex fake, Guid a, HttpClient aClient, Guid b, HttpClient bClient)
            {
                Host = host;
                Database = database;
                _fake = fake;
                _factor = new TestSecondFactor(database.Data);
                A = a;
                AClient = aClient;
                B = b;
                BClient = bClient;
            }

            public AdminHost Host { get; }

            public TestDatabase Database { get; }

            public Guid A { get; }

            public HttpClient AClient { get; }

            public Guid B { get; }

            public HttpClient BClient { get; }

            public static async Task<Scene> StartAsync()
            {
                var database = await TestDatabase.CreateAsync();
                var fake = new FakeCodex("exit 0");
                var host = new AdminHost(database, fake.Executable);
                var (a, aClient) = await host.SignedInPersonAsync();
                var (b, bClient) = await host.SignedInPersonAsync("member");
                return new Scene(host, database, fake, a, aClient, b, bClient);
            }

            public async ValueTask DisposeAsync()
            {
                Host.Dispose();
                _fake.Dispose();
                await Database.DisposeAsync();
            }

            public string SecondFactor(Guid person) => _factor.Next(person);

            public static HttpRequestMessage Web(HttpMethod method, string path, string? json = null, string? remote = null)
            {
                var request = new HttpRequestMessage(method, Url("/api/web/v1" + path)) { Content = json is null ? null : Json(json) };
                request.Headers.Add("X-Base", "1");
                request.Headers.Add("Origin", "http://localhost");
                if (remote is not null)
                {
                    request.Headers.Add("X-Test-Remote", remote);
                }

                return request;
            }

            // A pairing code, made by the person with a fresh code of the second factor.
            public async Task<string> MakeCodeAsync(Guid person, HttpClient client)
            {
                var response = await client.SendAsync(Web(HttpMethod.Post, "/devices/pairing", $$"""{"code":"{{SecondFactor(person)}}"}"""));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                return (await ReadAsync(response)).GetProperty("code").GetString()!;
            }

            // What the app does with the code: POST /api/v1/pair without any token.
            public async Task<HttpResponseMessage> RedeemAsync(string code, string name = "iPad", string path = "/api/v1/pair", string? remote = null, Uri? uri = null, string? forwardedProto = null)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, uri ?? Url(path)) { Content = Json($$"""{"code":"{{code}}","deviceName":"{{name}}"}""") };
                if (remote is not null)
                {
                    request.Headers.Add("X-Test-Remote", remote);
                }

                if (forwardedProto is not null)
                {
                    request.Headers.Add("X-Forwarded-Proto", forwardedProto);
                }

                return await Host.Browser().SendAsync(request);
            }

            public async Task<string> PairAsync(Guid person, HttpClient client, string name)
            {
                var response = await RedeemAsync(await MakeCodeAsync(person, client), name);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                return (await ReadAsync(response)).GetProperty("token").GetString()!;
            }

            public HttpClient Device(string token)
            {
                var client = Host.Browser();
                client.DefaultRequestHeaders.Authorization = new("Bearer", token);
                return client;
            }

            public async Task<string?> OwnerOfAsync(Guid id)
            {
                await using var command = Database.Data.CreateCommand($"select owner_id::text from entities where id = '{id}'");
                return await command.ExecuteScalarAsync() as string;
            }

            public async Task<long> CountAsync(string table, string where = "true")
            {
                await using var command = Database.Data.CreateCommand($"select count(*) from {table} where {where}");
                return (long)(await command.ExecuteScalarAsync())!;
            }
        }

        // ---------- what a device token is ----------

        [RequiresDatabaseFact]
        public async Task A_device_writes_and_reads_only_for_its_person_and_a_foreign_row_stays_as_it_was()
        {
            await using var scene = await Scene.StartAsync();
            var tokenA = await scene.PairAsync(scene.A, scene.AClient, "iPad A");
            var tokenB = await scene.PairAsync(scene.B, scene.BClient, "iPad B");
            using var deviceA = scene.Device(tokenA);
            using var deviceB = scene.Device(tokenB);
            var rowA = Guid.NewGuid();
            var rowB = Guid.NewGuid();

            Assert.Equal(HttpStatusCode.OK, (await deviceA.PostAsync(Url("/api/v1/sync/push"), Json(Change(rowA, "Von A")))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await deviceB.PostAsync(Url("/api/v1/sync/push"), Json(Change(rowB, "Von B")))).StatusCode);
            Assert.Equal(scene.A.ToString(), await scene.OwnerOfAsync(rowA));
            Assert.Equal(scene.B.ToString(), await scene.OwnerOfAsync(rowB));

            // B's device tries to take A's row over, and A's device to overwrite B's: both are left alone and the answer says nothing was accepted.
            var attack = await ReadAsync(await deviceB.PostAsync(Url("/api/v1/sync/push"), Json(Change(rowA, "Überschrieben"))));
            Assert.Equal(0, attack.GetProperty("accepted").GetInt32());
            Assert.Equal(0, (await ReadAsync(await deviceA.PostAsync(Url("/api/v1/sync/push"), Json(Change(rowB, "Überschrieben"))))).GetProperty("accepted").GetInt32());
            Assert.Equal(scene.A.ToString(), await scene.OwnerOfAsync(rowA));
            Assert.Equal(1, await scene.CountAsync("entities", $"id = '{rowA}' and payload->>'name' = 'Von A'"));

            // Each device reads only its person's rows.
            var pullA = await ReadAsync(await deviceA.GetAsync(Url("/api/v1/sync/pull")));
            var pullB = await ReadAsync(await deviceB.GetAsync(Url("/api/v1/sync/pull")));
            Assert.Equal([rowA.ToString()], pullA.GetProperty("changes").EnumerateArray().Select(change => change.GetProperty("id").GetString()!).ToArray());
            Assert.Equal([rowB.ToString()], pullB.GetProperty("changes").EnumerateArray().Select(change => change.GetProperty("id").GetString()!).ToArray());

            // Files: A's scan is not found, not readable and not replaceable by B's device.
            using var upload = new ByteArrayContent(Jpeg);
            Assert.Equal(HttpStatusCode.NoContent, (await deviceA.PutAsync(Url($"/api/v1/assets/{Scan}"), upload)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await deviceA.GetAsync(Url($"/api/v1/assets/{Scan}"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await deviceB.GetAsync(Url($"/api/v1/assets/{Scan}"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await deviceB.SendAsync(new HttpRequestMessage(HttpMethod.Head, Url($"/api/v1/assets/{Scan}")))).StatusCode);

            // The old shared token still works and stands for the first administrator, not for the member.
            using var shared = scene.Device(AdminHost.Token);
            var rowShared = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.OK, (await shared.PostAsync(Url("/api/v1/sync/push"), Json(Change(rowShared, "Mit dem alten Token")))).StatusCode);
            Assert.Equal(scene.A.ToString(), await scene.OwnerOfAsync(rowShared));
        }

        [RequiresDatabaseFact]
        public async Task A_device_token_opens_nothing_outside_api_v1_and_a_session_opens_nothing_in_it()
        {
            await using var scene = await Scene.StartAsync();
            using var device = scene.Device(await scene.PairAsync(scene.B, scene.BClient, "iPad B"));

            foreach (var path in new[] { "/api/web/v1/folders", "/api/web/v1/devices", "/api/people", "/api/auth/sessions", "/api/web/v1/admin/overview" })
            {
                var response = await device.GetAsync(Url(path));
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{path}: {(int)response.StatusCode}");
            }

            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.AClient.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
        }

        [RequiresDatabaseFact]
        public async Task A_removed_device_gets_401_at_once_and_a_person_can_remove_only_their_own_devices()
        {
            await using var scene = await Scene.StartAsync();
            var tokenA1 = await scene.PairAsync(scene.A, scene.AClient, "iPad A");
            var tokenA2 = await scene.PairAsync(scene.A, scene.AClient, "iPhone A");
            await scene.PairAsync(scene.B, scene.BClient, "iPad B");
            using var a1 = scene.Device(tokenA1);
            using var a2 = scene.Device(tokenA2);
            Assert.Equal(HttpStatusCode.OK, (await a1.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);

            // Everybody sees their own list, and only that.
            var listA = await ReadAsync(await scene.AClient.SendAsync(Scene.Web(HttpMethod.Get, "/devices")));
            var listB = await ReadAsync(await scene.BClient.SendAsync(Scene.Web(HttpMethod.Get, "/devices")));
            Assert.Equal(["iPhone A", "iPad A"], Strings(listA, "name"));
            Assert.Equal(["iPad B"], Strings(listB, "name"));
            var idA1 = listA.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "iPad A").GetProperty("id").GetString();
            Assert.DoesNotContain("hash", listA.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("nt1_", listA.GetRawText(), StringComparison.Ordinal);

            // B cannot remove A's device: the answer is the one for an id that does not exist, and the device goes on working.
            Assert.Equal(HttpStatusCode.NotFound, (await scene.BClient.SendAsync(Scene.Web(HttpMethod.Delete, $"/devices/{idA1}"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await scene.BClient.SendAsync(Scene.Web(HttpMethod.Delete, $"/devices/{Guid.NewGuid()}"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await a1.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await scene.AClient.SendAsync(Scene.Web(HttpMethod.Delete, $"/devices/{idA1}"))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await a1.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await a2.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
            // A second removal is a 404, and the removed device does not come back in the list.
            Assert.Equal(HttpStatusCode.NotFound, (await scene.AClient.SendAsync(Scene.Web(HttpMethod.Delete, $"/devices/{idA1}"))).StatusCode);
            Assert.Equal(1, (await ReadAsync(await scene.AClient.SendAsync(Scene.Web(HttpMethod.Get, "/devices")))).GetProperty("items").GetArrayLength());
        }

        [RequiresDatabaseFact]
        public async Task A_person_can_remove_all_their_own_devices_at_once_and_nobody_elses()
        {
            await using var scene = await Scene.StartAsync();
            using var a1 = scene.Device(await scene.PairAsync(scene.A, scene.AClient, "iPad A"));
            using var a2 = scene.Device(await scene.PairAsync(scene.A, scene.AClient, "iPhone A"));
            using var b = scene.Device(await scene.PairAsync(scene.B, scene.BClient, "iPad B"));

            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.Host.Browser().SendAsync(Scene.Web(HttpMethod.Delete, "/devices"))).StatusCode);
            var answer = await scene.AClient.SendAsync(Scene.Web(HttpMethod.Delete, "/devices"));

            Assert.Equal(2, (await ReadAsync(answer)).GetProperty("removed").GetInt32());
            Assert.Equal(HttpStatusCode.Unauthorized, (await a1.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await a2.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await b.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
            Assert.Equal(0, (await ReadAsync(await scene.AClient.SendAsync(Scene.Web(HttpMethod.Delete, "/devices")))).GetProperty("removed").GetInt32());
            var entry = (await scene.Host.Services.GetRequiredService<AuditStore>().ListAsync(50)).First(line => line.Event == "device.removed_all");
            Assert.Equal("0 device(s)", entry.Detail);
        }

        [RequiresDatabaseFact]
        public async Task Device_tokens_survive_a_new_password_and_a_new_second_factor_and_signing_out_the_other_sessions_on_purpose()
        {
            await using var scene = await Scene.StartAsync();
            using var a = scene.Device(await scene.PairAsync(scene.A, scene.AClient, "iPad A"));
            var users = scene.Host.Services.GetRequiredService<UserStore>();

            // What the person does to their account: a new password (other sessions end), a new second factor (all sessions end), signing the others out.
            await users.SetPasswordAsync(scene.A, "not-a-hash-either", endSessions: true);
            await users.ReplaceTotpAsync(scene.A, "OTHERSECRET", [], default);
            await scene.Host.Services.GetRequiredService<SessionStore>().DeleteOthersAsync(scene.A, Guid.NewGuid());

            // The key of the device is a key of its own and is not touched: this is the decision (STATE.md), and "remove all devices" is the way to end it.
            Assert.Equal(HttpStatusCode.OK, (await a.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.AClient.GetAsync(Url("/api/web/v1/devices"))).StatusCode);
        }

        [RequiresDatabaseFact]
        public async Task The_device_of_a_blocked_person_gets_401_and_the_other_persons_device_goes_on()
        {
            await using var scene = await Scene.StartAsync();
            using var a = scene.Device(await scene.PairAsync(scene.A, scene.AClient, "iPad A"));
            using var b = scene.Device(await scene.PairAsync(scene.B, scene.BClient, "iPad B"));

            await scene.Host.Services.GetRequiredService<UserStore>().SetStateAsync(scene.B, PersonState.Blocked);

            Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await a.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
        }

        [RequiresDatabaseFact]
        public async Task A_wrong_device_token_is_401_is_counted_like_any_wrong_token_and_a_right_one_still_gets_through_from_that_address()
        {
            await using var scene = await Scene.StartAsync();
            using var device = scene.Device(await scene.PairAsync(scene.A, scene.AClient, "iPad A"));
            var brake = scene.Host.Services.GetRequiredService<TokenBrake>();

            for (var i = 0; i < TokenBrake.FailuresPerMinute + 2; i++)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/v1/sync/pull"));
                request.Headers.Add("X-Test-Remote", "203.0.113.7");
                request.Headers.Authorization = new("Bearer", "nt1_" + new string('x', 43));
                Assert.Equal(HttpStatusCode.Unauthorized, (await scene.Host.Browser().SendAsync(request)).StatusCode);
            }

            Assert.True(brake.IsLocked("203.0.113.7"));
            var right = new HttpRequestMessage(HttpMethod.Get, Url("/api/v1/sync/pull"));
            right.Headers.Add("X-Test-Remote", "203.0.113.7");
            right.Headers.Authorization = new("Bearer", await scene.PairAsync(scene.A, scene.AClient, "iPhone A"));
            Assert.Equal(HttpStatusCode.OK, (await scene.Host.Browser().SendAsync(right)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await device.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
        }

        [RequiresDatabaseFact]
        public async Task Only_the_hash_of_a_token_and_of_a_code_is_stored_and_a_used_code_is_deleted_not_marked()
        {
            await using var scene = await Scene.StartAsync();
            var code = await scene.MakeCodeAsync(scene.A, scene.AClient);
            Assert.Equal(10, code.Length);
            Assert.Equal(1, await scene.CountAsync("pairing_codes", $"hash = '{Hash(code)}'"));
            Assert.Equal(0, await scene.CountAsync("pairing_codes", $"hash = '{code}'"));

            var answer = await ReadAsync(await scene.RedeemAsync(code, "iPad A"));
            var token = answer.GetProperty("token").GetString()!;
            Assert.StartsWith("nt1_", token, StringComparison.Ordinal);
            Assert.True(token.Length >= 47, "256 bits are at least 43 characters after the prefix");
            Assert.Equal(["token", "deviceId", "personName", "api"], answer.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.Equal(0, await scene.CountAsync("pairing_codes"));
            Assert.Equal(1, await scene.CountAsync("device_tokens", $"hash = '{Hash(token)}'"));
            Assert.Equal(0, await scene.CountAsync("device_tokens", $"hash = '{token}'"));
        }

        // ---------- pairing codes ----------

        [RequiresDatabaseFact]
        public async Task A_code_works_exactly_once_and_a_new_code_replaces_the_old_one_of_the_person()
        {
            await using var scene = await Scene.StartAsync();
            var code = await scene.MakeCodeAsync(scene.A, scene.AClient);
            Assert.Equal(HttpStatusCode.OK, (await scene.RedeemAsync(code)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.RedeemAsync(code)).StatusCode);

            var first = await scene.MakeCodeAsync(scene.A, scene.AClient);
            var second = await scene.MakeCodeAsync(scene.A, scene.AClient);
            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.RedeemAsync(first)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await scene.RedeemAsync(second)).StatusCode);
            Assert.Equal(2, await scene.CountAsync("device_tokens"));

            // Typed as a person types it: small letters, with a dash and blanks.
            var typed = await scene.MakeCodeAsync(scene.B, scene.BClient);
            var spelled = typed[..5].ToLowerInvariant() + " - " + typed[5..].ToLowerInvariant();
            Assert.Equal(HttpStatusCode.OK, (await scene.RedeemAsync(spelled)).StatusCode);
        }

        [RequiresDatabaseFact]
        public async Task Two_requests_with_the_same_code_at_once_get_one_token()
        {
            await using var scene = await Scene.StartAsync();
            var code = await scene.MakeCodeAsync(scene.A, scene.AClient);

            var answers = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => scene.RedeemAsync(code, remote: "127.0.0." + (100 + i))));

            Assert.Equal(1, answers.Count(answer => answer.StatusCode == HttpStatusCode.OK));
            Assert.Equal(5, answers.Count(answer => answer.StatusCode == HttpStatusCode.Unauthorized));
            Assert.Equal(1, await scene.CountAsync("device_tokens"));
        }

        [RequiresDatabaseFact]
        public async Task A_code_works_for_ten_minutes_and_not_a_second_longer_and_old_codes_are_swept()
        {
            await using var scene = await Scene.StartAsync();
            var clock = new ManualTime(DateTimeOffset.UtcNow);
            var store = new DeviceStore(scene.Database.Data, clock);

            var (late, expires) = await store.NewCodeAsync(scene.A, default);
            var (early, _) = await store.NewCodeAsync(scene.B, default);
            Assert.Equal(clock.GetUtcNow() + DeviceStore.CodeLifetime, expires);

            clock.Advance(DeviceStore.CodeLifetime - TimeSpan.FromSeconds(1));
            Assert.NotNull(await store.RedeemAsync(early, "iPad B", "198.51.100.1", default));

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Null(await store.RedeemAsync(late, "iPad A", "198.51.100.1", default));

            // The expired code is still a row until somebody makes a new one; then it goes (and so does any other expired one).
            Assert.Equal(1, await scene.CountAsync("pairing_codes"));
            await store.NewCodeAsync(scene.B, default);
            Assert.Equal(1, await scene.CountAsync("pairing_codes"));
        }

        [RequiresDatabaseFact]
        public async Task A_code_of_a_person_who_has_been_blocked_since_gets_no_token_and_is_used_up()
        {
            await using var scene = await Scene.StartAsync();
            var code = await scene.MakeCodeAsync(scene.B, scene.BClient);
            await scene.Host.Services.GetRequiredService<UserStore>().SetStateAsync(scene.B, PersonState.Blocked);

            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.RedeemAsync(code)).StatusCode);
            Assert.Equal(0, await scene.CountAsync("device_tokens"));
            Assert.Equal(0, await scene.CountAsync("pairing_codes"));
        }

        [RequiresDatabaseFact]
        public async Task The_answer_is_the_same_for_a_code_that_never_was_one_that_ran_out_one_used_and_one_over_http()
        {
            await using var scene = await Scene.StartAsync();
            var used = await scene.MakeCodeAsync(scene.A, scene.AClient);
            Assert.Equal(HttpStatusCode.OK, (await scene.RedeemAsync(used)).StatusCode);
            var ranOut = await scene.MakeCodeAsync(scene.A, scene.AClient);
            await using (var expire = scene.Database.Data.CreateCommand("update pairing_codes set expires_at = now() - interval '1 second'"))
            {
                await expire.ExecuteNonQueryAsync();
            }

            var live = await scene.MakeCodeAsync(scene.B, scene.BClient);
            var answers = new List<(HttpStatusCode Status, string Body, string Type)>();
            foreach (var response in new[]
            {
                await scene.RedeemAsync("ABCDEFGHJK", remote: "127.0.0.1"),
                await scene.RedeemAsync(ranOut, remote: "127.0.0.2"),
                await scene.RedeemAsync(used, remote: "127.0.0.3"),
                await scene.RedeemAsync(live, uri: new Uri("http://notes.example/api/v1/pair"), remote: "203.0.113.4"),
                await scene.RedeemAsync("", remote: "127.0.0.5"),
            })
            {
                answers.Add((response.StatusCode, await response.Content.ReadAsStringAsync(), response.Content.Headers.ContentType?.MediaType ?? string.Empty));
            }

            Assert.All(answers, answer => Assert.Equal(HttpStatusCode.Unauthorized, answer.Status));
            // The body carries a trace id of the request and nothing else that differs.
            var shapes = answers.Select(answer => System.Text.RegularExpressions.Regex.Replace(answer.Body, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"-\"")).Distinct().ToList();
            Assert.Single(shapes);
            Assert.Single(answers.Select(answer => answer.Type).Distinct());
            Assert.DoesNotContain("expired", shapes[0].Replace("has expired", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        }

        [RequiresDatabaseFact]
        public async Task Only_exactly_POST_api_v1_pair_is_open_and_every_other_spelling_leaves_the_code_unspent()
        {
            await using var scene = await Scene.StartAsync();
            var code = await scene.MakeCodeAsync(scene.A, scene.AClient);
            var client = scene.Host.Browser();

            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Head, HttpMethod.Patch, HttpMethod.Options })
            {
                var response = await client.SendAsync(new HttpRequestMessage(method, Url("/api/v1/pair")) { Content = method == HttpMethod.Get || method == HttpMethod.Head ? null : Json($$"""{"code":"{{code}}"}""") });
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method}: {(int)response.StatusCode}");
            }

            foreach (var path in new[] { "/API/V1/PAIR", "/api/V1/pair", "/api/v1/Pair", "/api/v1/pair/", "/api/v1/pair/x", "/api/v1/pairs", "/api/v1//pair", "/api/v1/pair%2F", "/api/v1/status/pair" })
            {
                var response = await scene.RedeemAsync(code, path: path);
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{path}: {(int)response.StatusCode}");
            }

            // The code was not touched by any of them.
            Assert.Equal(HttpStatusCode.OK, (await scene.RedeemAsync(code)).StatusCode);
        }

        [RequiresDatabaseFact]
        public async Task Pairing_is_refused_over_http_with_the_code_left_unspent_unless_a_trusted_proxy_says_https_or_it_is_localhost()
        {
            await using var scene = await Scene.StartAsync();
            var code = await scene.MakeCodeAsync(scene.A, scene.AClient);
            var outside = new Uri("http://notes.example/api/v1/pair");

            // Plain http under another name: refused. A proxy that is not trusted (a public address) saying https changes nothing.
            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.RedeemAsync(code, uri: outside, remote: "203.0.113.10")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.RedeemAsync(code, uri: outside, remote: "203.0.113.10", forwardedProto: "https")).StatusCode);
            Assert.Equal(1, await scene.CountAsync("pairing_codes"));

            // The same header from a proxy the server believes (a private address): https.
            Assert.Equal(HttpStatusCode.OK, (await scene.RedeemAsync(code, uri: outside, remote: "10.0.0.5", forwardedProto: "https")).StatusCode);

            // localhost itself (development): allowed, but not under that name from a far address.
            var second = await scene.MakeCodeAsync(scene.A, scene.AClient);
            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.RedeemAsync(second, uri: new Uri("http://localhost/api/v1/pair"), remote: "203.0.113.11")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await scene.RedeemAsync(second, uri: new Uri("http://localhost/api/v1/pair"), remote: "127.0.0.1")).StatusCode);
        }

        [RequiresDatabaseFact]
        public async Task Three_wrong_codes_from_an_address_lock_it_even_for_a_right_code_and_other_addresses_are_not_affected()
        {
            await using var scene = await Scene.StartAsync();
            var code = await scene.MakeCodeAsync(scene.A, scene.AClient);

            for (var i = 0; i < TokenBrake.PairFailuresBeforeLock; i++)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await scene.RedeemAsync("WRONGCODE" + i, remote: "127.0.0.20")).StatusCode);
            }

            var locked = await scene.RedeemAsync(code, remote: "127.0.0.20");
            Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
            Assert.InRange(locked.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
            Assert.Equal(1, await scene.CountAsync("pairing_codes"));

            // Another address is free, and the code that was refused for the first is still good.
            Assert.Equal(HttpStatusCode.OK, (await scene.RedeemAsync(code, remote: "127.0.0.21")).StatusCode);

            // The lock is written to the log once, naming the address and never a code.
            AuditEntry? line = null;
            for (var i = 0; i < 40 && line is null; i++)
            {
                line = (await scene.Host.Services.GetRequiredService<AuditStore>().ListAsync(50)).FirstOrDefault(entry => entry.Event == "api.pair_refused");
                await Task.Delay(50);
            }

            Assert.NotNull(line);
            Assert.Equal("127.0.0.20", line!.Ip);
            Assert.DoesNotContain("WRONGCODE", line.Detail ?? string.Empty, StringComparison.Ordinal);
        }

        [Fact]
        public void The_pairing_brake_locks_on_the_third_wrong_code_until_the_minute_is_over_and_then_forgets_it()
        {
            var clock = new ManualTime(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
            // The line for the log is written without waiting (and here nowhere: the database is not there; a log that cannot be written never fails a request).
            using var nowhere = Npgsql.NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=none;Timeout=1");
            var brake = new TokenBrake(new Base.Web.RateLimit(clock), new AuditStore(nowhere, clock), clock);

            brake.PairFailed("198.51.100.1");
            brake.PairFailed("198.51.100.1");
            Assert.Equal(0, brake.PairWait("198.51.100.1"));
            brake.PairFailed("198.51.100.1");

            Assert.True(brake.PairWait("198.51.100.1") is > 0 and <= 60);
            Assert.Equal(0, brake.PairWait("198.51.100.2"));
            clock.Advance(TimeSpan.FromSeconds(61));
            Assert.Equal(0, brake.PairWait("198.51.100.1"));
        }

        [RequiresDatabaseFact]
        public async Task A_body_that_is_too_large_or_not_ours_is_refused_like_a_wrong_code()
        {
            await using var scene = await Scene.StartAsync();
            var client = scene.Host.Browser();
            var bodies = new[] { new string('x', 5000), "not json", "null", "[]", "{\"code\":1}", "{\"code\":\"" + new string('A', 3000) + "\"}" };

            foreach (var (body, index) in bodies.Select((body, index) => (body, index)))
            {
                var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/v1/pair")) { Content = Json(body) };
                request.Headers.Add("X-Test-Remote", "127.0.0." + (30 + index));
                var response = await client.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{body[..Math.Min(20, body.Length)]}: {(int)response.StatusCode}");
            }
        }

        // ---------- making a code ----------

        [RequiresDatabaseFact]
        public async Task A_code_needs_a_fresh_code_of_the_second_factor_that_works_once_and_a_session_alone_is_not_enough()
        {
            await using var scene = await Scene.StartAsync();
            var anonymous = scene.Host.Browser();

            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Scene.Web(HttpMethod.Post, "/devices/pairing", """{"code":"123456"}"""))).StatusCode);
            var wrong = await scene.AClient.SendAsync(Scene.Web(HttpMethod.Post, "/devices/pairing", """{"code":"000000"}"""));
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
            Assert.Equal("totp.wrong", (await ReadAsync(wrong)).GetProperty("error").GetString());
            Assert.Equal(HttpStatusCode.Forbidden, (await scene.AClient.SendAsync(Scene.Web(HttpMethod.Post, "/devices/pairing", "{}"))).StatusCode);
            Assert.Equal(0, await scene.CountAsync("pairing_codes"));

            var right = scene.SecondFactor(scene.A);
            Assert.Equal(HttpStatusCode.OK, (await scene.AClient.SendAsync(Scene.Web(HttpMethod.Post, "/devices/pairing", $$"""{"code":"{{right}}"}"""))).StatusCode);
            // The same code of the second factor a second time is spent.
            Assert.Equal(HttpStatusCode.Forbidden, (await scene.AClient.SendAsync(Scene.Web(HttpMethod.Post, "/devices/pairing", $$"""{"code":"{{right}}"}"""))).StatusCode);
            Assert.Equal(1, await scene.CountAsync("pairing_codes"));
            // A member can pair too (it is their own device).
            Assert.Equal(10, (await scene.MakeCodeAsync(scene.B, scene.BClient)).Length);
        }

        [RequiresDatabaseFact]
        public async Task Wrong_codes_of_the_second_factor_lock_pairing_for_that_account_and_a_right_one_does_not_get_through_meanwhile()
        {
            await using var scene = await Scene.StartAsync();

            for (var i = 0; i < 5; i++)
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await scene.AClient.SendAsync(Scene.Web(HttpMethod.Post, "/devices/pairing", """{"code":"000000"}"""))).StatusCode);
            }

            var locked = await scene.AClient.SendAsync(Scene.Web(HttpMethod.Post, "/devices/pairing", $$"""{"code":"{{scene.SecondFactor(scene.A)}}"}"""));
            Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
            Assert.Equal(0, await scene.CountAsync("pairing_codes"));
        }

        [RequiresDatabaseFact]
        public async Task A_person_without_a_second_factor_here_gets_a_clear_answer_and_no_code()
        {
            await using var scene = await Scene.StartAsync();
            // As a person who signs in only through the hub: active, but no second factor of their own in this account.
            var (hubPerson, client) = await scene.Host.SignedInPersonAsync("member");
            await using (var unconfirm = scene.Database.Data.CreateCommand($"update users set totp_confirmed = false, hub_sub = 'hub-test-subject' where id = '{hubPerson}'"))
            {
                await unconfirm.ExecuteNonQueryAsync();
            }

            var response = await client.SendAsync(Scene.Web(HttpMethod.Post, "/devices/pairing", """{"code":"123456"}"""));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("pairing.needs_totp", (await ReadAsync(response)).GetProperty("error").GetString());
            Assert.Equal(0, await scene.CountAsync("pairing_codes"));
        }

        // ---------- the log ----------

        [RequiresDatabaseFact]
        public async Task The_security_log_names_the_device_that_broke_a_rule_and_the_pairing_and_removal_are_logged()
        {
            await using var scene = await Scene.StartAsync();
            using var a = scene.Device(await scene.PairAsync(scene.A, scene.AClient, "iPad A"));
            using var b = scene.Device(await scene.PairAsync(scene.B, scene.BClient, "iPad von Bea"));
            var row = Guid.NewGuid();
            await a.PostAsync(Url("/api/v1/sync/push"), Json(Change(row, "Von A")));
            await b.PostAsync(Url("/api/v1/sync/push"), Json(Change(row, "Fremd")));

            var audit = scene.Host.Services.GetRequiredService<AuditStore>();
            IReadOnlyList<AuditEntry> all = [];
            for (var i = 0; i < 40 && !all.Any(entry => entry.Detail?.Contains("iPad von Bea", StringComparison.Ordinal) == true && entry.Event != "device.paired"); i++)
            {
                all = await audit.ListAsync(100);
                await Task.Delay(50);
            }

            var broken = Assert.Single(all, entry => entry.Detail?.Contains("iPad von Bea", StringComparison.Ordinal) == true && entry.Event != "device.paired");
            Assert.NotEqual("app-token", broken.UserName);
            Assert.Contains(all, entry => entry.Event == "device.paired" && entry.Detail == "device iPad A");
            Assert.Contains(all, entry => entry.Event == "pairing.code_made");
            // No token and no code in any line.
            Assert.DoesNotContain(all, entry => (entry.Detail ?? string.Empty).Contains("nt1_", StringComparison.Ordinal));

            var id = (await ReadAsync(await scene.AClient.SendAsync(Scene.Web(HttpMethod.Get, "/devices")))).GetProperty("items")[0].GetProperty("id").GetString();
            await scene.AClient.SendAsync(Scene.Web(HttpMethod.Delete, $"/devices/{id}"));
            Assert.Contains(await audit.ListAsync(100), entry => entry.Event == "device.removed" && entry.Detail == "device iPad A");
        }

        [RequiresDatabaseFact]
        public async Task A_failure_while_the_token_is_made_leaves_the_code_as_it_was_and_makes_no_device()
        {
            await using var scene = await Scene.StartAsync();
            var store = scene.Host.Services.GetRequiredService<DeviceStore>();
            var (code, _) = await store.NewCodeAsync(scene.A, default);
            foreach (var sql in new[]
            {
                "create function refuse_device() returns trigger language plpgsql as $$ begin raise exception 'no device today'; end $$",
                "create trigger refuse_device before insert on device_tokens for each row execute function refuse_device()",
            })
            {
                await using var command = scene.Database.Data.CreateCommand(sql);
                await command.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => store.RedeemAsync(code, "iPad", "198.51.100.1", default));

            Assert.Equal(1, await scene.CountAsync("pairing_codes"));
            Assert.Equal(0, await scene.CountAsync("device_tokens"));
            await using (var drop = scene.Database.Data.CreateCommand("drop trigger refuse_device on device_tokens"))
            {
                await drop.ExecuteNonQueryAsync();
            }

            Assert.NotNull(await store.RedeemAsync(code, "iPad", "198.51.100.1", default));
            Assert.Equal(0, await scene.CountAsync("pairing_codes"));
        }

        [RequiresDatabaseFact]
        public async Task A_flood_of_addresses_gets_three_lines_of_its_own_an_hour_and_one_line_that_counts_the_rest()
        {
            await using var scene = await Scene.StartAsync();
            await scene.AClient.GetAsync(Url("/api/web/v1/devices"));
            var clock = new ManualTime(DateTimeOffset.UtcNow);
            var audit = scene.Host.Services.GetRequiredService<AuditStore>();
            var brake = new TokenBrake(new Base.Web.RateLimit(clock), audit, clock);

            for (var address = 1; address <= 6; address++)
            {
                for (var wrong = 0; wrong < TokenBrake.PairFailuresBeforeLock; wrong++)
                {
                    brake.PairFailed("203.0.113." + address);
                }
            }

            IReadOnlyList<AuditEntry> log = [];
            for (var i = 0; i < 40 && log.Count(entry => entry.Event == "api.pair_refused") < 3; i++)
            {
                await Task.Delay(50);
                log = await audit.ListAsync(100);
            }

            await Task.Delay(300);
            log = await audit.ListAsync(100);
            Assert.Equal(3, log.Count(entry => entry.Event == "api.pair_refused"));
            Assert.DoesNotContain(log, entry => entry.Event == "api.pair_refused_summary");

            // The next hour: the three that were left out are counted in one line, before the new line of its own.
            clock.Advance(TimeSpan.FromMinutes(61));
            for (var wrong = 0; wrong < TokenBrake.PairFailuresBeforeLock; wrong++)
            {
                brake.PairFailed("203.0.113.7");
            }

            AuditEntry? summary = null;
            for (var i = 0; i < 40 && summary is null; i++)
            {
                await Task.Delay(50);
                summary = (await audit.ListAsync(100)).FirstOrDefault(entry => entry.Event == "api.pair_refused_summary");
            }

            Assert.NotNull(summary);
            Assert.Contains("3 more address(es)", summary!.Detail, StringComparison.Ordinal);
            Assert.Equal(4, (await audit.ListAsync(100)).Count(entry => entry.Event == "api.pair_refused"));
        }

        // ---------- small parts ----------

        [Fact]
        public void A_device_name_is_cleaned_and_an_address_is_shortened()
        {
            Assert.Equal("Gerät", DeviceStore.CleanName(null));
            Assert.Equal("Gerät", DeviceStore.CleanName("  \t\n "));
            Assert.Equal("iPad von Ada", DeviceStore.CleanName("  iPad\u0000 von\n Ada "));
            Assert.Equal(DeviceStore.MaxNameLength, DeviceStore.CleanName(new string('a', 200)).Length);
            Assert.Equal("203.0.113.x", DeviceStore.Shorten("203.0.113.9"));
            Assert.Equal("2001:db8:1::", DeviceStore.Shorten("2001:db8:1:2::/64"));
            Assert.Null(DeviceStore.Shorten(null));
            Assert.Equal("ABCDEFGHIJ", DeviceStore.Normalise("abcde-fghij"));
            Assert.Equal("ABCDEFGHIJ", DeviceStore.Normalise(" abcde fghij "));

            // Marks that turn text around or hide it are gone; the joiner that an emoji sequence needs is not.
            Assert.Equal("evil", DeviceStore.CleanName("\u202Eevil\u202C"));
            Assert.Equal("a\u200Bb".Replace("\u200B", string.Empty, StringComparison.Ordinal), DeviceStore.CleanName("a\u200Bb\u2067\u2028"));
            const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
            Assert.Equal(Family, DeviceStore.CleanName(Family));
            // Cut between text elements, never inside one: an emoji that stands at place 60 is kept whole, one that would be the 61st is left out.
            Assert.Equal(new string('a', 59) + Family, DeviceStore.CleanName(new string('a', 59) + Family + "bbb"));
            Assert.Equal(new string('a', 60), DeviceStore.CleanName(new string('a', 60) + Family));
            // A single "letter" with hundreds of marks is one element but not a name.
            Assert.Equal("Gerät", DeviceStore.CleanName("a" + new string('\u0301', 300)));
        }

        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
