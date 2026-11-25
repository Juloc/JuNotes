using System.Net;
using System.Text;
using System.Text.Json;
using Base.Accounts;
using JuNotes.Frontend.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace JuNotes.Backend.Tests
{
    // The administration of devices and the switch of the shared token (milestone 3, slice 3d). Ada is the administrator, Bea and Cleo are members.
    public class DevicesAdminTests
    {
        private static Uri Url(string path) => new(path, UriKind.Relative);

        private static HttpRequestMessage Admin(HttpMethod method, string path, string? json = null)
        {
            var request = new HttpRequestMessage(method, Url("/api/web/v1/admin" + path)) { Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-Base", "1");
            request.Headers.Add("Origin", "http://localhost");
            return request;
        }

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        private sealed class Scene : IAsyncDisposable
        {
            private readonly FakeCodex _fake;

            private Scene(AdminHost host, TestDatabase database, FakeCodex fake, (Guid Id, HttpClient Client) ada, (Guid Id, HttpClient Client) bea, (Guid Id, HttpClient Client) cleo)
            {
                Host = host;
                Database = database;
                _fake = fake;
                Factor = new TestSecondFactor(database.Data);
                Ada = ada.Id;
                AdaClient = ada.Client;
                Bea = bea.Id;
                BeaClient = bea.Client;
                Cleo = cleo.Id;
                CleoClient = cleo.Client;
            }

            public AdminHost Host { get; }

            public TestDatabase Database { get; }

            public TestSecondFactor Factor { get; }

            public Guid Ada { get; }

            public HttpClient AdaClient { get; }

            public Guid Bea { get; }

            public HttpClient BeaClient { get; }

            public Guid Cleo { get; }

            public HttpClient CleoClient { get; }

            public static async Task<Scene> StartAsync(string? sharedToken = AdminHost.Token)
            {
                var database = await TestDatabase.CreateAsync();
                var fake = new FakeCodex("exit 0");
                var host = new AdminHost(database, fake.Executable) { SharedToken = sharedToken };
                return new Scene(host, database, fake, await host.SignedInPersonAsync(), await host.SignedInPersonAsync("member"), await host.SignedInPersonAsync("member"));
            }

            public async ValueTask DisposeAsync()
            {
                Host.Dispose();
                _fake.Dispose();
                await Database.DisposeAsync();
            }

            // A device of a person, made as the pairing makes it (the pairing itself is tested with DeviceTests).
            public async Task<string> DeviceAsync(Guid person, string name)
            {
                var store = Host.Services.GetRequiredService<DeviceStore>();
                var (code, _) = await store.NewCodeAsync(person, default);
                return (await store.RedeemAsync(code, name, "198.51.100.1", default))!.Token;
            }

            public async Task<HttpStatusCode> PullAsync(string token, string? remote = null)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/v1/sync/pull"));
                request.Headers.Authorization = new("Bearer", token);
                if (remote is not null)
                {
                    request.Headers.Add("X-Test-Remote", remote);
                }

                return (await Host.Browser().SendAsync(request)).StatusCode;
            }

            public async Task<HttpStatusCode> StatusAsync(string token, HttpMethod method, string path)
            {
                var request = new HttpRequestMessage(method, Url(path)) { Content = method == HttpMethod.Post ? new StringContent("{}", Encoding.UTF8, "application/json") : null };
                request.Headers.Authorization = new("Bearer", token);
                return (await Host.Browser().SendAsync(request)).StatusCode;
            }

            public async Task<long> CountAsync(string table, string where = "true")
            {
                await using var command = Database.Data.CreateCommand($"select count(*) from {table} where {where}");
                return (long)(await command.ExecuteScalarAsync())!;
            }

            public async Task<HttpResponseMessage> SwitchAsync(string which, string code) => await AdaClient.SendAsync(Admin(HttpMethod.Post, $"/shared-token/{which}", $$"""{"code":"{{code}}"}"""));
        }

        // ---------- the devices of the people ----------

        [RequiresDatabaseFact]
        public async Task The_administrator_sees_how_many_devices_each_person_has_and_nothing_about_them()
        {
            await using var scene = await Scene.StartAsync();
            var bea1 = await scene.DeviceAsync(scene.Bea, "iPad von Bea");
            await scene.DeviceAsync(scene.Bea, "iPhone von Bea");
            await scene.DeviceAsync(scene.Cleo, "iPad von Cleo");

            var response = await scene.AdaClient.SendAsync(Admin(HttpMethod.Get, "/devices"));
            var body = await ReadAsync(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var counts = body.GetProperty("people").EnumerateArray().ToDictionary(person => person.GetProperty("id").GetString()!, person => person.GetProperty("devices").GetInt32());
            Assert.Equal(0, counts[scene.Ada.ToString()]);
            Assert.Equal(2, counts[scene.Bea.ToString()]);
            Assert.Equal(1, counts[scene.Cleo.ToString()]);
            Assert.All(body.GetProperty("people").EnumerateArray(), person => Assert.Equal(["id", "name", "state", "devices"], person.EnumerateObject().Select(property => property.Name).ToArray()));
            Assert.DoesNotContain("iPad", body.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("nt1_", body.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(bea1, body.GetRawText(), StringComparison.Ordinal);

            // Only for the role admin.
            Assert.Equal(HttpStatusCode.Forbidden, (await scene.BeaClient.SendAsync(Admin(HttpMethod.Get, "/devices"))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.Host.Browser().SendAsync(Admin(HttpMethod.Get, "/devices"))).StatusCode);
        }

        [RequiresDatabaseFact]
        public async Task Revoking_all_devices_of_a_person_works_at_once_touches_nobody_else_and_is_logged()
        {
            await using var scene = await Scene.StartAsync();
            var bea1 = await scene.DeviceAsync(scene.Bea, "iPad von Bea");
            var bea2 = await scene.DeviceAsync(scene.Bea, "iPhone von Bea");
            var cleo = await scene.DeviceAsync(scene.Cleo, "iPad von Cleo");
            Assert.Equal(HttpStatusCode.OK, await scene.PullAsync(bea1));

            // A member cannot, nobody without a session can, and a person who is not there is a 404.
            Assert.Equal(HttpStatusCode.Forbidden, (await scene.BeaClient.SendAsync(Admin(HttpMethod.Post, $"/people/{scene.Cleo}/devices/revoke"))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.Host.Browser().SendAsync(Admin(HttpMethod.Post, $"/people/{scene.Cleo}/devices/revoke"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await scene.AdaClient.SendAsync(Admin(HttpMethod.Post, $"/people/{Guid.NewGuid()}/devices/revoke"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, await scene.PullAsync(cleo));

            var answer = await scene.AdaClient.SendAsync(Admin(HttpMethod.Post, $"/people/{scene.Bea}/devices/revoke"));

            Assert.Equal(2, (await ReadAsync(answer)).GetProperty("revoked").GetInt32());
            Assert.Equal(HttpStatusCode.Unauthorized, await scene.PullAsync(bea1));
            Assert.Equal(HttpStatusCode.Unauthorized, await scene.PullAsync(bea2));
            Assert.Equal(HttpStatusCode.OK, await scene.PullAsync(cleo));
            Assert.Equal(0, (await ReadAsync(await scene.AdaClient.SendAsync(Admin(HttpMethod.Get, "/devices")))).GetProperty("people").EnumerateArray().Single(person => person.GetProperty("id").GetString() == scene.Bea.ToString()).GetProperty("devices").GetInt32());

            var users = scene.Host.Services.GetRequiredService<UserStore>();
            var entry = Assert.Single(await scene.Host.Services.GetRequiredService<AuditStore>().ListAsync(50), line => line.Event == "device.revoked_by_admin");
            Assert.Equal((await users.FindByIdAsync(scene.Ada))!.Name, entry.UserName);
            Assert.Equal($"2 device(s) of {(await users.FindByIdAsync(scene.Bea))!.Name}", entry.Detail);
        }

        // ---------- the shared token ----------

        [RequiresDatabaseFact]
        public async Task The_addresses_that_use_the_shared_token_are_counted_for_thirty_days_and_a_row_is_written_once_a_minute()
        {
            await using var scene = await Scene.StartAsync();
            Assert.Equal(0, (await ReadAsync(await scene.AdaClient.SendAsync(Admin(HttpMethod.Get, "/shared-token")))).GetProperty("recentAddresses").GetInt32());

            Assert.Equal(HttpStatusCode.OK, await scene.PullAsync(AdminHost.Token, "203.0.113.1"));
            var first = await ReadAsync(await scene.AdaClient.SendAsync(Admin(HttpMethod.Get, "/shared-token")));
            await scene.PullAsync(AdminHost.Token, "203.0.113.1");
            await scene.PullAsync(AdminHost.Token, "203.0.113.2");
            var state = await ReadAsync(await scene.AdaClient.SendAsync(Admin(HttpMethod.Get, "/shared-token")));

            Assert.True(first.GetProperty("configured").GetBoolean());
            Assert.True(first.GetProperty("enabled").GetBoolean());
            Assert.Equal(1, first.GetProperty("recentAddresses").GetInt32());
            Assert.Equal(2, state.GetProperty("recentAddresses").GetInt32());
            Assert.Equal(2, await scene.CountAsync("shared_token_seen"));
            // Nothing but the address and two times is kept.
            Assert.Equal(3, await scene.CountAsync("information_schema.columns", "table_name = 'shared_token_seen'"));

            // Devices with their own token and wrong tokens are not written down.
            await scene.PullAsync(await scene.DeviceAsync(scene.Bea, "iPad"), "203.0.113.3");
            await scene.PullAsync("a-wrong-token-that-is-long-enough-to-be-wrong", "203.0.113.4");
            Assert.Equal(2, await scene.CountAsync("shared_token_seen"));

            // An address not seen for 31 days is not counted, and goes when the next row is written.
            await using (var old = scene.Database.Data.CreateCommand("update shared_token_seen set last_seen = now() - interval '31 days' where address = '203.0.113.2'"))
            {
                await old.ExecuteNonQueryAsync();
            }

            Assert.Equal(1, (await ReadAsync(await scene.AdaClient.SendAsync(Admin(HttpMethod.Get, "/shared-token")))).GetProperty("recentAddresses").GetInt32());
            await scene.PullAsync(AdminHost.Token, "203.0.113.5");
            Assert.Equal(2, await scene.CountAsync("shared_token_seen"));
        }

        [RequiresDatabaseFact]
        public async Task Switching_the_shared_token_off_needs_a_fresh_code_and_then_the_right_token_is_refused_but_devices_go_on()
        {
            await using var scene = await Scene.StartAsync();
            var device = await scene.DeviceAsync(scene.Bea, "iPad von Bea");
            Assert.Equal(HttpStatusCode.OK, await scene.PullAsync(AdminHost.Token, "203.0.113.1"));

            // Not for a member, not without a session, not without a right code of the second factor.
            Assert.Equal(HttpStatusCode.Forbidden, (await scene.BeaClient.SendAsync(Admin(HttpMethod.Post, "/shared-token/off", $$"""{"code":"{{scene.Factor.Next(scene.Bea)}}"}"""))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.Host.Browser().SendAsync(Admin(HttpMethod.Post, "/shared-token/off", """{"code":"123456"}"""))).StatusCode);
            var wrong = await scene.SwitchAsync("off", "000000");
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
            Assert.Equal("totp.wrong", (await ReadAsync(wrong)).GetProperty("error").GetString());
            Assert.Equal(0, await scene.CountAsync("app_instance_settings", "key = 'shared_token_off'"));
            Assert.Equal(HttpStatusCode.OK, await scene.PullAsync(AdminHost.Token, "203.0.113.1"));

            var code = scene.Factor.Next(scene.Ada);
            var off = await scene.SwitchAsync("off", code);

            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
            var state = await ReadAsync(off);
            Assert.False(state.GetProperty("enabled").GetBoolean());
            Assert.True(state.GetProperty("configured").GetBoolean());
            Assert.Equal(1, await scene.CountAsync("app_instance_settings", "key = 'shared_token_off'"));
            // The same code of the second factor is spent.
            Assert.Equal(HttpStatusCode.Forbidden, (await scene.SwitchAsync("off", code)).StatusCode);

            // The right token is refused on every path of the door (status stays open), a wrong one is refused as before, and the device goes on.
            Assert.Equal(HttpStatusCode.Unauthorized, await scene.PullAsync(AdminHost.Token, "203.0.113.1"));
            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.Host.Browser().SendAsync(WithToken(HttpMethod.Post, "/api/v1/sync/push"))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.Host.Browser().SendAsync(WithToken(HttpMethod.Get, "/api/v1/sync/info"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await scene.Host.Browser().GetAsync(Url("/api/v1/status"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, await scene.PullAsync(device));
        }

        [RequiresDatabaseFact]
        public async Task The_shared_token_cannot_be_switched_off_until_a_device_with_its_own_key_has_been_seen_in_the_last_thirty_days()
        {
            await using var scene = await Scene.StartAsync();
            Assert.Equal(HttpStatusCode.OK, await scene.PullAsync(AdminHost.Token, "203.0.113.1"));
            var none = await ReadAsync(await scene.AdaClient.SendAsync(Admin(HttpMethod.Get, "/shared-token")));
            Assert.Equal(0, none.GetProperty("devicesSeen").GetInt32());

            // No device yet: refused with a sentence, and the code of the second factor is not spent on it (a right code works at once afterwards).
            var code = scene.Factor.Next(scene.Ada);
            var refused = await scene.SwitchAsync("off", code);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("sharedtoken.no_device", (await ReadAsync(refused)).GetProperty("error").GetString());
            Assert.Equal(0, await scene.CountAsync("app_instance_settings", "key = 'shared_token_off'"));
            Assert.Equal(HttpStatusCode.OK, await scene.PullAsync(AdminHost.Token, "203.0.113.1"));

            // A device that was not seen for 31 days does not count, and neither does a removed one.
            var token = await scene.DeviceAsync(scene.Bea, "iPad von Bea");
            await using (var old = scene.Database.Data.CreateCommand("update device_tokens set last_seen = now() - interval '31 days'"))
            {
                await old.ExecuteNonQueryAsync();
            }

            Assert.Equal(HttpStatusCode.Conflict, (await scene.SwitchAsync("off", scene.Factor.Next(scene.Ada))).StatusCode);
            await using (var seen = scene.Database.Data.CreateCommand("update device_tokens set last_seen = now() - interval '29 days'"))
            {
                await seen.ExecuteNonQueryAsync();
            }

            Assert.Equal(1, (await ReadAsync(await scene.AdaClient.SendAsync(Admin(HttpMethod.Get, "/shared-token")))).GetProperty("devicesSeen").GetInt32());
            await scene.AdaClient.SendAsync(Admin(HttpMethod.Post, $"/people/{scene.Bea}/devices/revoke"));
            Assert.Equal(HttpStatusCode.Conflict, (await scene.SwitchAsync("off", scene.Factor.Next(scene.Ada))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, await scene.PullAsync(token));

            // A device seen within 30 days is enough.
            await scene.DeviceAsync(scene.Cleo, "iPad von Cleo");
            Assert.Equal(HttpStatusCode.OK, (await scene.SwitchAsync("off", scene.Factor.Next(scene.Ada))).StatusCode);
        }

        [RequiresDatabaseFact]
        public async Task A_right_token_that_is_switched_off_is_not_counted_as_a_wrong_one_and_shows_up_once_in_the_log()
        {
            await using var scene = await Scene.StartAsync();
            await scene.DeviceAsync(scene.Bea, "iPad von Bea");
            Assert.Equal(HttpStatusCode.OK, (await scene.SwitchAsync("off", scene.Factor.Next(scene.Ada))).StatusCode);
            var brake = scene.Host.Services.GetRequiredService<TokenBrake>();

            for (var i = 0; i < TokenBrake.FailuresPerMinute + 5; i++)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, await scene.PullAsync(AdminHost.Token, "203.0.113.9"));
            }

            // It is not an attack: the address is not locked and the log does not say "wrong tokens".
            Assert.False(brake.IsLocked("203.0.113.9"));
            var log = await WaitForAsync(scene, entry => entry.Event == "api.token_switched_off");
            Assert.Single(log, entry => entry.Event == "api.token_switched_off");
            Assert.DoesNotContain(log, entry => entry.Event == "api.token_refused");
            Assert.Equal("203.0.113.9", log.First(entry => entry.Event == "api.token_switched_off").Ip);
            Assert.DoesNotContain(log, entry => (entry.Detail ?? string.Empty).Contains(AdminHost.Token, StringComparison.Ordinal));
            // A wrong token still counts, as ever.
            for (var i = 0; i < TokenBrake.FailuresPerMinute + 2; i++)
            {
                await scene.PullAsync("a-wrong-token-that-is-long-enough-to-be-wrong", "203.0.113.10");
            }

            Assert.True(brake.IsLocked("203.0.113.10"));
        }

        [RequiresDatabaseFact]
        public async Task Switching_it_on_again_needs_a_fresh_code_and_an_environment_that_still_has_the_token_and_both_ways_are_logged()
        {
            await using var scene = await Scene.StartAsync();
            await scene.DeviceAsync(scene.Bea, "iPad von Bea");
            Assert.Equal(HttpStatusCode.OK, await scene.PullAsync(AdminHost.Token));
            Assert.Equal(HttpStatusCode.OK, (await scene.SwitchAsync("off", scene.Factor.Next(scene.Ada))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, await scene.PullAsync(AdminHost.Token));

            Assert.Equal(HttpStatusCode.Forbidden, (await scene.SwitchAsync("on", "000000")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, await scene.PullAsync(AdminHost.Token));
            var on = await scene.SwitchAsync("on", scene.Factor.Next(scene.Ada));

            Assert.Equal(HttpStatusCode.OK, on.StatusCode);
            Assert.True((await ReadAsync(on)).GetProperty("enabled").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, await scene.PullAsync(AdminHost.Token));
            Assert.Equal(0, await scene.CountAsync("app_instance_settings", "key = 'shared_token_off'"));

            var users = scene.Host.Services.GetRequiredService<UserStore>();
            var log = await scene.Host.Services.GetRequiredService<AuditStore>().ListAsync(50);
            var ada = (await users.FindByIdAsync(scene.Ada))!.Name;
            Assert.Equal(ada, Assert.Single(log, entry => entry.Event == "sharedtoken.off").UserName);
            Assert.Contains("address(es)", log.First(entry => entry.Event == "sharedtoken.off").Detail, StringComparison.Ordinal);
            Assert.Equal(ada, Assert.Single(log, entry => entry.Event == "sharedtoken.on").UserName);

            // Not for a member, and not without a session.
            Assert.Equal(HttpStatusCode.Forbidden, (await scene.BeaClient.SendAsync(Admin(HttpMethod.Post, "/shared-token/on", $$"""{"code":"{{scene.Factor.Next(scene.Bea)}}"}"""))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.Host.Browser().SendAsync(Admin(HttpMethod.Post, "/shared-token/on", """{"code":"123456"}"""))).StatusCode);
        }

        [RequiresDatabaseFact]
        public async Task A_token_that_the_environment_does_not_carry_cannot_be_switched_on()
        {
            await using var scene = await Scene.StartAsync(sharedToken: null);
            await scene.DeviceAsync(scene.Bea, "iPad von Bea");

            var state = await ReadAsync(await scene.AdaClient.SendAsync(Admin(HttpMethod.Get, "/shared-token")));
            Assert.False(state.GetProperty("configured").GetBoolean());
            Assert.True(state.GetProperty("enabled").GetBoolean());

            var on = await scene.SwitchAsync("on", scene.Factor.Next(scene.Ada));

            Assert.Equal(HttpStatusCode.Conflict, on.StatusCode);
            Assert.Equal("sharedtoken.not_configured", (await ReadAsync(on)).GetProperty("error").GetString());
            // The code of the second factor was not spent on it.
            Assert.Equal(HttpStatusCode.OK, (await scene.SwitchAsync("off", scene.Factor.Next(scene.Ada))).StatusCode);
        }

        // ---------- what the server holds once for everybody ----------

        [RequiresDatabaseFact]
        public async Task The_AI_the_school_feed_and_the_archive_are_not_for_the_device_of_an_ordinary_member_but_for_the_shared_token_the_pinned_person_and_administrators()
        {
            await using var scene = await Scene.StartAsync();
            var (second, _) = await scene.Host.SignedInPersonAsync();
            var pinned = await scene.DeviceAsync(scene.Ada, "iPad von Ada");
            var member = await scene.DeviceAsync(scene.Bea, "iPad von Bea");
            var otherAdmin = await scene.DeviceAsync(second, "iPad des zweiten Administrators");
            // The shared token is written down for the first administrator by its first use.
            Assert.Equal(HttpStatusCode.OK, await scene.PullAsync(AdminHost.Token));
            var paths = new[]
            {
                (HttpMethod.Get, "/api/v1/ai/profiles"), (HttpMethod.Post, "/api/v1/ai/quiz"), (HttpMethod.Post, "/api/v1/ai/anything"), (HttpMethod.Get, "/api/v1/school/status"),
                (HttpMethod.Get, "/api/v1/school/calendar"), (HttpMethod.Get, "/api/v1/school/news"), (HttpMethod.Get, "/api/v1/export/status"), (HttpMethod.Post, "/api/v1/export/paperless?title=x"),
            };

            foreach (var (method, path) in paths)
            {
                Assert.True(await scene.StatusAsync(member, method, path) == HttpStatusCode.Forbidden, $"member {method} {path}");
                foreach (var (who, token) in new[] { ("pinned person", pinned), ("shared token", AdminHost.Token), ("second administrator", otherAdmin) })
                {
                    var status = await scene.StatusAsync(token, method, path);
                    Assert.True(status != HttpStatusCode.Forbidden && status != HttpStatusCode.Unauthorized, $"{who} {method} {path}: {(int)status}");
                }
            }

            // What the app needs to sync is the member's as before.
            Assert.Equal(HttpStatusCode.OK, await scene.PullAsync(member));
            Assert.Equal(HttpStatusCode.OK, await scene.StatusAsync(member, HttpMethod.Get, "/api/v1/sync/info"));
        }

        private static HttpRequestMessage WithToken(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, Url(path));
            request.Headers.Authorization = new("Bearer", AdminHost.Token);
            return request;
        }

        // The log lines are written without waiting; look for one for a moment.
        private static async Task<IReadOnlyList<AuditEntry>> WaitForAsync(Scene scene, Func<AuditEntry, bool> wanted)
        {
            var audit = scene.Host.Services.GetRequiredService<AuditStore>();
            IReadOnlyList<AuditEntry> log = [];
            for (var i = 0; i < 40 && !log.Any(wanted); i++)
            {
                log = await audit.ListAsync(100);
                await Task.Delay(50);
            }

            return log;
        }
    }
}
