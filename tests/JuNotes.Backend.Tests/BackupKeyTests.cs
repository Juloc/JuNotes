using System.Diagnostics;
using JuNotes.Backend.Backup;
using JuNotes.Frontend.Admin;
using Microsoft.Extensions.Logging.Abstractions;

namespace JuNotes.Backend.Tests
{
    // The key of the server (the file of the base) goes with a copy only when the copy is encrypted, and the page says where it is kept. A stand-in for pg_dump
    // (a script that writes a small file) is used, so these run where the real pg_dump is of another version.
    public sealed class BackupKeyTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "junotes-keytest-" + Guid.NewGuid().ToString("N"));

        public BackupKeyTests() => Directory.CreateDirectory(_folder);

        public void Dispose() => Directory.Delete(_folder, recursive: true);

        private string FakePgDump()
        {
            var path = Path.Combine(_folder, "pg_dump");
            File.WriteAllText(path, "#!/bin/sh\nfor a in \"$@\"; do case \"$a\" in --file=*) printf 'dump' > \"${a#--file=}\";; esac; done\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return path;
        }

        private async Task<(BackupFile Backup, string Nas, string Key, string[] Everything)> RunAsync(string? passphrase)
        {
            var key = Path.Combine(_folder, "base", "secret.key");
            Directory.CreateDirectory(Path.GetDirectoryName(key)!);
            await File.WriteAllTextAsync(key, "the-secret-key-bytes-of-this-test\n");
            var nas = Path.Combine(_folder, "nas");
            var options = new BackupOptions { Directory = Path.Combine(_folder, "backups"), PgDump = FakePgDump(), KeyFile = key };
            var mirror = new BackupMirror([new FolderDestination(nas)], passphrase, 7, NullLogger<BackupMirror>.Instance);
            using var service = new BackupService("Host=127.0.0.1;Port=1;Username=x;Password=y;Database=z", options, TimeProvider.System, NullLogger<BackupService>.Instance, mirror);

            var backup = await service.RunAsync(BackupKind.Automatic, CancellationToken.None);

            var everything = Directory.GetFiles(Path.Combine(_folder, "backups")).Concat(Directory.GetFiles(nas)).ToArray();
            return (backup, nas, key, everything);
        }

        [Fact]
        public async Task With_a_passphrase_the_key_goes_along_encrypted_under_the_name_of_the_backup_and_nowhere_in_the_clear()
        {
            var (backup, nas, key, everything) = await RunAsync("geheim");

            var copy = Path.Combine(nas, BackupFiles.KeyFileFor(backup.Path) + ".enc");
            Assert.True(File.Exists(copy));
            var restored = Path.Combine(_folder, "restored.key");
            await BackupCrypto.DecryptAsync(copy, restored, "geheim", CancellationToken.None);
            Assert.Equal(await File.ReadAllBytesAsync(key), await File.ReadAllBytesAsync(restored));
            // Not in the clear: not next to the dumps and not in the second folder, and the temporary copy is gone.
            Assert.DoesNotContain(everything, file => file.EndsWith(".key", StringComparison.Ordinal));
            Assert.DoesNotContain(Directory.GetFiles(Path.GetTempPath(), "junotes-*.key"), file => file.Contains(Path.GetFileName(backup.Path)[..^".dump".Length], StringComparison.Ordinal));
        }

        [Fact]
        public async Task Without_a_passphrase_the_key_goes_nowhere()
        {
            var (_, _, _, everything) = await RunAsync(null);

            Assert.DoesNotContain(everything, file => file.Contains(".key", StringComparison.Ordinal));
            Assert.DoesNotContain(everything, file => File.ReadAllText(file).Contains("the-secret-key-bytes", StringComparison.Ordinal));
        }

        [Fact]
        public void The_key_copy_is_pruned_together_with_its_backup()
        {
            var names = new[] { "junotes-20260101T000000Z-auto.dump.enc", "junotes-20260101T000000Z-auto.key.enc", "junotes-20260102T000000Z-auto.dump.enc", "junotes-20260102T000000Z-auto.key.enc" };

            var old = BackupMirror.Obsolete(names, keep: 1);

            Assert.Equal(["junotes-20260101T000000Z-auto.dump.enc", "junotes-20260101T000000Z-auto.key.enc"], old.Order());
        }

        [RequiresDatabaseFact]
        public async Task The_backup_page_says_where_the_key_is_kept_and_warns_when_it_is_in_no_backup()
        {
            var database = await TestDatabase.CreateAsync();
            var fake = new FakeCodex("exit 0");
            await using (database)
            using (fake)
            using (var host = new AdminHost(database, fake.Executable))
            {
                var admin = await host.SignedInAsync();

                // The key comes from a setting here (as in the tests): nothing to keep.
                Assert.Contains("nicht aus einer Datei", await admin.GetStringAsync(new Uri("/Admin/Backups", UriKind.Relative)), StringComparison.Ordinal);

                // A key file and no encrypted copy anywhere: the page warns.
                var key = Path.Combine(host.BackupDirectory, "base", "secret.key");
                Directory.CreateDirectory(Path.GetDirectoryName(key)!);
                await File.WriteAllTextAsync(key, "x");
                var page = await admin.GetStringAsync(new Uri("/Admin/Backups", UriKind.Relative));
                Assert.Contains("ist in <strong>keiner</strong> Sicherung", page, StringComparison.Ordinal);
            }
        }
    }
}
