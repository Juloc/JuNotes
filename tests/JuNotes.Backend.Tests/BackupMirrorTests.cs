using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JuNotes.Backend.Backup;
using Microsoft.Extensions.Logging.Abstractions;

namespace JuNotes.Backend.Tests
{
    public sealed class BackupCryptoTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "junotes-crypto-" + Guid.NewGuid().ToString("N"));

        public BackupCryptoTests() => Directory.CreateDirectory(_folder);

        public void Dispose() => Directory.Delete(_folder, recursive: true);

        private string File(string name) => Path.Combine(_folder, name);

        private async Task<(string Plain, string Encrypted)> EncryptedAsync(byte[] content, string passphrase = "geheim")
        {
            var plain = File("plain");
            var encrypted = File("plain.enc");
            await System.IO.File.WriteAllBytesAsync(plain, content);
            await BackupCrypto.EncryptAsync(plain, encrypted, passphrase, CancellationToken.None);
            return (plain, encrypted);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(1000)]
        [InlineData(BackupCrypto.ChunkSize)]
        [InlineData(BackupCrypto.ChunkSize + 1)]
        [InlineData((2 * BackupCrypto.ChunkSize) + 12345)]
        public async Task Every_size_comes_back_exactly(int size)
        {
            var content = RandomNumberGenerator.GetBytes(size);
            var (_, encrypted) = await EncryptedAsync(content);

            await BackupCrypto.DecryptAsync(encrypted, File("back"), "geheim", CancellationToken.None);

            Assert.Equal(content, await System.IO.File.ReadAllBytesAsync(File("back")));
        }

        [Fact]
        public async Task The_plain_text_is_not_in_the_encrypted_file_and_each_run_differs()
        {
            var content = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Geheime Notizen über Brüche. ", 200)));
            var (_, first) = await EncryptedAsync(content);
            var firstBytes = await System.IO.File.ReadAllBytesAsync(first);
            await BackupCrypto.EncryptAsync(File("plain"), File("second.enc"), "geheim", CancellationToken.None);

            Assert.False(firstBytes.AsSpan().IndexOf("Geheime"u8) >= 0);
            Assert.NotEqual(firstBytes, await System.IO.File.ReadAllBytesAsync(File("second.enc")));
        }

        [Fact]
        public async Task A_wrong_passphrase_is_refused_and_leaves_nothing()
        {
            var (_, encrypted) = await EncryptedAsync([1, 2, 3, 4]);

            await Assert.ThrowsAsync<BackupDecryptionException>(() => BackupCrypto.DecryptAsync(encrypted, File("out"), "falsch", CancellationToken.None));

            Assert.False(System.IO.File.Exists(File("out")));
            Assert.False(System.IO.File.Exists(File("out.partial")));
        }

        [Fact]
        public async Task A_changed_byte_anywhere_is_noticed()
        {
            var (_, encrypted) = await EncryptedAsync(RandomNumberGenerator.GetBytes(5000));
            var bytes = await System.IO.File.ReadAllBytesAsync(encrypted);

            foreach (var position in new[] { 30, bytes.Length / 2, bytes.Length - 1 })
            {
                var damaged = (byte[])bytes.Clone();
                damaged[position] ^= 0x01;
                await System.IO.File.WriteAllBytesAsync(File("damaged"), damaged);

                await Assert.ThrowsAsync<BackupDecryptionException>(() => BackupCrypto.DecryptAsync(File("damaged"), File("out"), "geheim", CancellationToken.None));
            }
        }

        [Fact]
        public async Task A_file_cut_off_in_the_middle_of_a_chunk_is_noticed()
        {
            var (_, encrypted) = await EncryptedAsync(RandomNumberGenerator.GetBytes(5000));
            var bytes = await System.IO.File.ReadAllBytesAsync(encrypted);
            await System.IO.File.WriteAllBytesAsync(File("cut"), bytes[..^20]);

            await Assert.ThrowsAsync<BackupDecryptionException>(() => BackupCrypto.DecryptAsync(File("cut"), File("out"), "geheim", CancellationToken.None));
        }

        [Fact]
        public async Task A_file_cut_off_between_chunks_is_not_taken_for_a_whole_one()
        {
            var content = RandomNumberGenerator.GetBytes((2 * BackupCrypto.ChunkSize) + 10);
            var (_, encrypted) = await EncryptedAsync(content);
            var bytes = await System.IO.File.ReadAllBytesAsync(encrypted);
            // Header (25) plus the first chunk: its length field, its data and its tag.
            var firstChunkEnd = 25 + 4 + BackupCrypto.ChunkSize + 16;
            await System.IO.File.WriteAllBytesAsync(File("cut"), bytes[..firstChunkEnd]);

            await Assert.ThrowsAsync<BackupDecryptionException>(() => BackupCrypto.DecryptAsync(File("cut"), File("out"), "geheim", CancellationToken.None));
        }

        private static async Task<(int ExitCode, string Output)> RunCommandAsync(string passphrase, params string[] arguments)
        {
            // The program itself, as the owner would run it to open a copy: dotnet JuNotes.Frontend.dll decrypt-backup <in> <out>
            var start = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            if (passphrase.Length > 0)
            {
                start.Environment["Backup__Passphrase"] = passphrase;
            }

            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await output + await errors);
        }

        [Fact]
        public async Task The_program_decrypts_a_copy_from_the_command_line()
        {
            var content = RandomNumberGenerator.GetBytes(3000);
            var (_, encrypted) = await EncryptedAsync(content);

            var ok = await RunCommandAsync("geheim", "decrypt-backup", encrypted, File("opened"));
            var wrong = await RunCommandAsync("falsch", "decrypt-backup", encrypted, File("never"));
            var missing = await RunCommandAsync("", "decrypt-backup", encrypted, File("never"));

            Assert.Equal(0, ok.ExitCode);
            Assert.Equal(content, await System.IO.File.ReadAllBytesAsync(File("opened")));
            Assert.Equal(1, wrong.ExitCode);
            Assert.Contains("wrong", wrong.Output, StringComparison.Ordinal);
            Assert.Equal(2, missing.ExitCode);
            Assert.False(System.IO.File.Exists(File("never")));
        }

        [Fact]
        public async Task A_file_that_is_not_an_encrypted_backup_is_said_to_be_one_of_those()
        {
            await System.IO.File.WriteAllTextAsync(File("text"), "this is a plain text file that is long enough to have a header");

            var error = await Assert.ThrowsAsync<BackupDecryptionException>(() => BackupCrypto.DecryptAsync(File("text"), File("out"), "geheim", CancellationToken.None));

            Assert.Contains("not an encrypted", error.Message, StringComparison.Ordinal);
        }
    }

    public sealed class BackupMirrorTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "junotes-mirror-" + Guid.NewGuid().ToString("N"));

        public BackupMirrorTests() => Directory.CreateDirectory(_folder);

        public void Dispose() => Directory.Delete(_folder, recursive: true);

        private string Source(string name, string content = "data")
        {
            var path = Path.Combine(_folder, "source", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        private sealed class Recording(string name = "Test") : IBackupDestination
        {
            public Dictionary<string, byte[]> Files { get; } = [];

            public bool Fails { get; set; }

            public string Name { get; } = name;

            public Task UploadAsync(string localPath, string name, CancellationToken ct)
            {
                if (Fails)
                {
                    throw new IOException("kaputt");
                }

                Files[name] = File.ReadAllBytes(localPath);
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Files.Keys.ToList());

            public Task DeleteAsync(string name, CancellationToken ct)
            {
                Files.Remove(name);
                return Task.CompletedTask;
            }
        }

        [Fact]
        public void Only_the_newest_backups_stay_and_a_backup_is_all_its_files()
        {
            var names = new[]
            {
                "junotes-20261117-030000-automatic.dump", "junotes-20261117-030000-automatic.assets.zip",
                "junotes-20261118-030000-automatic.dump.enc", "junotes-20261118-030000-automatic.assets.zip.enc",
                "junotes-20261119-030000-automatic.dump", "junotes-20261119-030000-automatic.assets.zip",
                "junotes-20261101-120000-manual.dump", "notes.txt",
            };

            var obsolete = BackupMirror.Obsolete(names, keep: 2);

            Assert.Equal(["junotes-20261117-030000-automatic.assets.zip", "junotes-20261117-030000-automatic.dump"], obsolete.Order());
        }

        [Fact]
        public void Manual_backups_and_foreign_files_are_never_obsolete()
        {
            var obsolete = BackupMirror.Obsolete(["junotes-20200101-000000-manual.dump", "junotes-20200101-000000-manual.assets.zip", "family-photo.jpg"], keep: 1);

            Assert.Empty(obsolete);
        }

        [Fact]
        public async Task Without_a_passphrase_the_files_go_out_as_they_are()
        {
            var destination = new Recording();
            var mirror = new BackupMirror([destination], null, 7, NullLogger<BackupMirror>.Instance);

            var results = await mirror.MirrorAsync([Source("junotes-20261119-030000-automatic.dump", "inhalt")], prune: false, CancellationToken.None);

            Assert.True(Assert.Single(results).Ok);
            Assert.Equal("inhalt", Encoding.UTF8.GetString(destination.Files["junotes-20261119-030000-automatic.dump"]));
        }

        [Fact]
        public async Task With_a_passphrase_they_go_out_encrypted_and_can_be_read_again()
        {
            var destination = new Recording();
            var mirror = new BackupMirror([destination], "geheim", 7, NullLogger<BackupMirror>.Instance);
            var source = Source("junotes-20261119-030000-automatic.dump", "streng vertraulich");

            await mirror.MirrorAsync([source], prune: false, CancellationToken.None);

            var sent = destination.Files["junotes-20261119-030000-automatic.dump.enc"];
            Assert.False(sent.AsSpan().IndexOf("vertraulich"u8) >= 0);
            var received = Path.Combine(_folder, "received.enc");
            await File.WriteAllBytesAsync(received, sent);
            await BackupCrypto.DecryptAsync(received, Path.Combine(_folder, "back"), "geheim", CancellationToken.None);
            Assert.Equal("streng vertraulich", await File.ReadAllTextAsync(Path.Combine(_folder, "back")));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, "*.partial"));
        }

        [Fact]
        public async Task One_destination_failing_does_not_stop_the_other_and_nothing_is_thrown()
        {
            var broken = new Recording("Kaputt") { Fails = true };
            var fine = new Recording("Gut");
            var mirror = new BackupMirror([broken, fine], null, 7, NullLogger<BackupMirror>.Instance);

            var results = await mirror.MirrorAsync([Source("junotes-20261119-030000-automatic.dump")], prune: false, CancellationToken.None);

            Assert.False(results[0].Ok);
            Assert.Equal("kaputt", results[0].Error);
            Assert.True(results[1].Ok);
            Assert.Single(fine.Files);
            Assert.Equal(results, mirror.Last);
        }

        [Fact]
        public async Task Pruning_removes_old_backups_at_the_destination_after_a_new_one_arrived()
        {
            var destination = new Recording();
            var mirror = new BackupMirror([destination], null, 2, NullLogger<BackupMirror>.Instance);
            foreach (var day in new[] { "17", "18", "19", "20" })
            {
                await mirror.MirrorAsync([Source($"junotes-202611{day}-030000-automatic.dump")], prune: true, CancellationToken.None);
            }

            Assert.Equal(["junotes-20261119-030000-automatic.dump", "junotes-20261120-030000-automatic.dump"], destination.Files.Keys.Order());
        }

        [Fact]
        public async Task A_manual_backup_is_copied_but_never_pruned_on_its_account()
        {
            var destination = new Recording();
            var mirror = new BackupMirror([destination], null, 1, NullLogger<BackupMirror>.Instance);
            await mirror.MirrorAsync([Source("junotes-20261101-030000-automatic.dump")], prune: false, CancellationToken.None);

            await mirror.MirrorAsync([Source("junotes-20261119-030000-manual.dump")], prune: false, CancellationToken.None);

            Assert.Equal(2, destination.Files.Count);
        }

        [Fact]
        public async Task The_folder_destination_copies_lists_and_deletes_plain_names_only()
        {
            var target = Path.Combine(_folder, "nas");
            var destination = new FolderDestination(target);

            await destination.UploadAsync(Source("a.dump", "eins"), "junotes-a.dump", CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(target, "junotes-b.dump.partial"), "halb");

            Assert.Equal(["junotes-a.dump"], await destination.ListAsync(CancellationToken.None));
            Assert.Equal("eins", await File.ReadAllTextAsync(Path.Combine(target, "junotes-a.dump")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => destination.DeleteAsync("../escape", CancellationToken.None));
            await destination.DeleteAsync("junotes-a.dump", CancellationToken.None);
            Assert.Empty(await destination.ListAsync(CancellationToken.None));
        }

        [Fact]
        public async Task A_missing_folder_is_created_and_listing_a_missing_one_is_empty()
        {
            var destination = new FolderDestination(Path.Combine(_folder, "not", "yet"));

            Assert.Empty(await destination.ListAsync(CancellationToken.None));
            await destination.UploadAsync(Source("x.dump"), "junotes-x.dump", CancellationToken.None);
            Assert.Single(await destination.ListAsync(CancellationToken.None));
        }

        private sealed class FakeDrive(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
        {
            public List<(HttpMethod Method, string Url, string? Body)> Calls { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                Calls.Add((request.Method, request.RequestUri!.ToString(), body));
                return respond(request, body);
            }
        }

        private static readonly DriveOptions Drive = new() { ClientId = "id", ClientSecret = "secret", RefreshToken = "refresh", FolderId = "folder1" };

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        [Fact]
        public async Task A_file_goes_to_drive_in_one_resumable_upload_into_the_folder()
        {
            var handler = new FakeDrive((request, _) => request.RequestUri!.Host switch
            {
                "oauth2.googleapis.com" => Json("""{"access_token":"tok","expires_in":3600}"""),
                _ when request.Method == HttpMethod.Post => new HttpResponseMessage(HttpStatusCode.OK) { Headers = { Location = new Uri("https://upload.example/session/1") } },
                _ => Json("""{"id":"new"}"""),
            });
            var destination = new GoogleDriveDestination(new HttpClient(handler), Drive);

            await destination.UploadAsync(Source("junotes-a.dump", "Inhalt der Datei"), "junotes-a.dump", CancellationToken.None);

            Assert.Equal(3, handler.Calls.Count);
            Assert.Contains("grant_type=refresh_token", handler.Calls[0].Body, StringComparison.Ordinal);
            Assert.Contains("refresh_token=refresh", handler.Calls[0].Body, StringComparison.Ordinal);
            Assert.Contains("uploadType=resumable", handler.Calls[1].Url, StringComparison.Ordinal);
            using var metadata = JsonDocument.Parse(handler.Calls[1].Body!);
            Assert.Equal("junotes-a.dump", metadata.RootElement.GetProperty("name").GetString());
            Assert.Equal("folder1", metadata.RootElement.GetProperty("parents")[0].GetString());
            Assert.Equal(HttpMethod.Put, handler.Calls[2].Method);
            Assert.Equal("https://upload.example/session/1", handler.Calls[2].Url);
            Assert.Equal("Inhalt der Datei", handler.Calls[2].Body);
        }

        [Fact]
        public async Task The_access_token_is_asked_for_once_while_it_is_valid()
        {
            var handler = new FakeDrive((request, _) => request.RequestUri!.Host == "oauth2.googleapis.com" ? Json("""{"access_token":"tok","expires_in":3600}""") : Json("""{"files":[]}"""));
            var destination = new GoogleDriveDestination(new HttpClient(handler), Drive);

            await destination.ListAsync(CancellationToken.None);
            await destination.ListAsync(CancellationToken.None);

            Assert.Single(handler.Calls, call => call.Url.Contains("oauth2", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Listing_follows_the_pages_and_deleting_uses_the_id_of_the_name()
        {
            var handler = new FakeDrive((request, _) =>
            {
                var url = request.RequestUri!.ToString();
                if (url.Contains("oauth2", StringComparison.Ordinal))
                {
                    return Json("""{"access_token":"tok","expires_in":3600}""");
                }

                if (request.Method == HttpMethod.Delete)
                {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                return url.Contains("pageToken=p2", StringComparison.Ordinal)
                    ? Json("""{"files":[{"id":"id3","name":"junotes-c.dump"}]}""")
                    : Json("""{"files":[{"id":"id1","name":"junotes-a.dump"},{"id":"id2","name":"junotes-b.dump"}],"nextPageToken":"p2"}""");
            });
            var destination = new GoogleDriveDestination(new HttpClient(handler), Drive);

            var names = await destination.ListAsync(CancellationToken.None);
            await destination.DeleteAsync("junotes-b.dump", CancellationToken.None);
            await destination.DeleteAsync("unknown.dump", CancellationToken.None);

            Assert.Equal(["junotes-a.dump", "junotes-b.dump", "junotes-c.dump"], names);
            Assert.Contains(handler.Calls, call => call.Method == HttpMethod.Delete && call.Url.EndsWith("/files/id2", StringComparison.Ordinal));
            Assert.Single(handler.Calls, call => call.Method == HttpMethod.Delete);
            Assert.Contains(handler.Calls, call => call.Url.Contains("%27folder1%27", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_refusal_by_drive_is_an_error_that_names_the_step_but_not_the_answer()
        {
            var handler = new FakeDrive((request, _) => Json("""{"error":"invalid_grant","error_description":"account jemand@example.com revoked"}""", HttpStatusCode.BadRequest));
            var destination = new GoogleDriveDestination(new HttpClient(handler), Drive);

            var error = await Assert.ThrowsAsync<IOException>(() => destination.ListAsync(CancellationToken.None));

            Assert.Contains("signing in", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("jemand", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Drive_is_only_used_when_everything_is_set()
        {
            Assert.True(Drive.IsComplete);
            Assert.False(new DriveOptions { ClientId = "a", ClientSecret = "b", RefreshToken = "c" }.IsComplete);
            Assert.False(new DriveOptions().IsComplete);
        }
    }

    public sealed class BackupMirrorIntegrationTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "junotes-mirror-it-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_backup_is_copied_encrypted_to_the_second_folder_and_decrypts_to_a_valid_dump()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var assets = Path.Combine(_folder, "assets");
            Directory.CreateDirectory(assets);
            await File.WriteAllBytesAsync(Path.Combine(assets, "abc"), [0xFF, 0xD8, 0xFF, 9]);
            var nas = Path.Combine(_folder, "nas");
            var options = new BackupOptions { Directory = Path.Combine(_folder, "backups"), PgDump = PostgresTools.PgDump, AssetsDirectory = assets };
            var mirror = new BackupMirror([new FolderDestination(nas)], "geheim", 7, NullLogger<BackupMirror>.Instance);
            using var service = new BackupService(database.ConnectionString, options, TimeProvider.System, NullLogger<BackupService>.Instance, mirror);

            var backup = await service.RunAsync(BackupKind.Automatic, CancellationToken.None);

            var copies = Directory.GetFiles(nas).Select(Path.GetFileName).Order().ToList();
            Assert.Equal([Path.GetFileName(BackupFiles.AssetsArchiveFor(backup.Path)) + ".enc", Path.GetFileName(backup.Path) + ".enc"], copies);
            var restored = Path.Combine(_folder, "restored.dump");
            await BackupCrypto.DecryptAsync(Path.Combine(nas, Path.GetFileName(backup.Path) + ".enc"), restored, "geheim", CancellationToken.None);
            Assert.Equal(await File.ReadAllBytesAsync(backup.Path), await File.ReadAllBytesAsync(restored));
            Assert.True(Assert.Single(mirror.Last).Ok);
        }

        [RequiresDatabaseFact]
        public async Task A_failing_copy_never_fails_the_backup_itself()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var blocked = Path.Combine(_folder, "blocked");
            Directory.CreateDirectory(_folder);
            await File.WriteAllTextAsync(blocked, "a file where a folder should be");
            var options = new BackupOptions { Directory = Path.Combine(_folder, "backups"), PgDump = PostgresTools.PgDump };
            var mirror = new BackupMirror([new FolderDestination(blocked)], null, 7, NullLogger<BackupMirror>.Instance);
            using var service = new BackupService(database.ConnectionString, options, TimeProvider.System, NullLogger<BackupService>.Instance, mirror);

            var backup = await service.RunAsync(BackupKind.Automatic, CancellationToken.None);

            Assert.True(File.Exists(backup.Path));
            Assert.False(Assert.Single(mirror.Last).Ok);
        }
    }
}
