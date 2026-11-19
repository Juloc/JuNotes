using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace JuNotes.Backend.Backup
{
    public interface IBackupDestination
    {
        string Name { get; }

        // Puts one file there under `name` and returns once it is complete.
        Task UploadAsync(string localPath, string name, CancellationToken ct);

        // The names of what is already there, so old ones can be removed.
        Task<IReadOnlyList<string>> ListAsync(CancellationToken ct);

        Task DeleteAsync(string name, CancellationToken ct);
    }

    public sealed class DriveOptions
    {
        public string? ClientId { get; set; }

        public string? ClientSecret { get; set; }

        // From a one-time sign-in of the owner; the server only ever asks Drive for access with it.
        public string? RefreshToken { get; set; }

        // The Drive folder the backups go into.
        public string? FolderId { get; set; }

        public bool IsComplete => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) && !string.IsNullOrWhiteSpace(RefreshToken) && !string.IsNullOrWhiteSpace(FolderId);
    }

    // A second folder, typically a share of the NAS mounted into the container.
    public sealed class FolderDestination(string directory, string name = "NAS-Ordner") : IBackupDestination
    {
        public string Name { get; } = name;

        public async Task UploadAsync(string localPath, string name, CancellationToken ct)
        {
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, name);
            var partial = target + ".partial";
            try
            {
                await using (var input = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await input.CopyToAsync(output, ct);
                }

                File.Move(partial, target, overwrite: true);
            }
            finally
            {
                File.Delete(partial);
            }
        }

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(Directory.Exists(directory) ? Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>().Where(file => !file.EndsWith(".partial", StringComparison.Ordinal)).ToList() : []);

        public Task DeleteAsync(string name, CancellationToken ct)
        {
            // Only a plain file name inside the folder: a name from outside never leaves it.
            if (name != Path.GetFileName(name))
            {
                throw new InvalidOperationException("Not a plain file name.");
            }

            File.Delete(Path.Combine(directory, name));
            return Task.CompletedTask;
        }
    }

    // Google Drive over its REST API: the access token is asked with the refresh token, a file goes up in one resumable request.
    public sealed class GoogleDriveDestination(HttpClient client, DriveOptions options) : IBackupDestination
    {
        private string? _token;
        private DateTimeOffset _expires;

        public string Name => "Google Drive";

        public async Task UploadAsync(string localPath, string name, CancellationToken ct)
        {
            var token = await AccessTokenAsync(ct);
            var metadata = JsonSerializer.Serialize(new { name, parents = new[] { options.FolderId } });
            using var start = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable")
            {
                Content = new StringContent(metadata, Encoding.UTF8, "application/json"),
            };
            start.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var started = await client.SendAsync(start, ct);
            await EnsureAsync(started, "starting the upload");
            var session = started.Headers.Location ?? throw new IOException("Google Drive gave no upload address.");

            await using var file = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var upload = new HttpRequestMessage(HttpMethod.Put, session) { Content = new StreamContent(file) };
            upload.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            upload.Content.Headers.ContentLength = file.Length;
            using var done = await client.SendAsync(upload, ct);
            await EnsureAsync(done, "uploading");
        }

        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct)
        {
            var names = new List<string>();
            string? page = null;
            do
            {
                var query = Uri.EscapeDataString($"'{options.FolderId}' in parents and trashed = false and name contains 'junotes-'");
                var url = $"https://www.googleapis.com/drive/v3/files?q={query}&fields=nextPageToken,files(id,name)&pageSize=200" + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
                using var request = await AuthorizedAsync(HttpMethod.Get, url, ct);
                using var response = await client.SendAsync(request, ct);
                await EnsureAsync(response, "listing");
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                foreach (var item in body.RootElement.GetProperty("files").EnumerateArray())
                {
                    // Deleting needs the id; the name carries it after a bar, which a backup name never contains.
                    names.Add(item.GetProperty("name").GetString() + "|" + item.GetProperty("id").GetString());
                }

                page = body.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            }
            while (page is not null);
            return Remember(names);
        }

        private readonly Dictionary<string, string> _ids = [];

        private List<string> Remember(List<string> entries)
        {
            var plain = new List<string>();
            foreach (var entry in entries)
            {
                var bar = entry.LastIndexOf('|');
                _ids[entry[..bar]] = entry[(bar + 1)..];
                plain.Add(entry[..bar]);
            }

            return plain;
        }

        public async Task DeleteAsync(string name, CancellationToken ct)
        {
            if (!_ids.TryGetValue(name, out var id))
            {
                return;
            }

            using var request = await AuthorizedAsync(HttpMethod.Delete, $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(id)}", ct);
            using var response = await client.SendAsync(request, ct);
            await EnsureAsync(response, "deleting");
        }

        private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string url, CancellationToken ct)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
            return request;
        }

        private async Task<string> AccessTokenAsync(CancellationToken ct)
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expires - TimeSpan.FromMinutes(1))
            {
                return _token;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = options.ClientId!,
                    ["client_secret"] = options.ClientSecret!,
                    ["refresh_token"] = options.RefreshToken!,
                    ["grant_type"] = "refresh_token",
                }),
            };
            using var response = await client.SendAsync(request, ct);
            await EnsureAsync(response, "signing in");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            _token = body.RootElement.GetProperty("access_token").GetString();
            _expires = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(body.RootElement.TryGetProperty("expires_in", out var seconds) ? seconds.GetInt32() : 3000);
            return _token!;
        }

        // The answer's own text is left out: it can name the account.
        private static Task EnsureAsync(HttpResponseMessage response, string step)
            => response.IsSuccessStatusCode ? Task.CompletedTask : throw new IOException($"Google Drive refused {step} ({(int)response.StatusCode}).");
    }

    // Copies each finished backup to every destination, encrypted when a passphrase is set, and keeps only the newest ones there.
    public sealed partial class BackupMirror(IReadOnlyList<IBackupDestination> destinations, string? passphrase, int keep, ILogger<BackupMirror> logger)
    {
        public sealed record Result(string Destination, bool Ok, string? Error);

        public bool IsActive => destinations.Count > 0;

        public IReadOnlyList<string> Names => destinations.Select(destination => destination.Name).ToList();

        public bool Encrypts => !string.IsNullOrEmpty(passphrase);

        public IReadOnlyList<Result> Last { get; private set; } = [];

        public async Task<IReadOnlyList<Result>> MirrorAsync(IReadOnlyList<string> files, bool prune, CancellationToken ct)
        {
            var results = new List<Result>();
            foreach (var destination in destinations)
            {
                try
                {
                    foreach (var file in files)
                    {
                        await SendAsync(destination, file, ct);
                    }

                    if (prune)
                    {
                        foreach (var old in Obsolete(await destination.ListAsync(ct), keep))
                        {
                            await destination.DeleteAsync(old, ct);
                        }
                    }

                    results.Add(new Result(destination.Name, true, null));
                }
                catch (Exception error) when (error is IOException or HttpRequestException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    LogFailed(logger, destination.Name, error);
                    results.Add(new Result(destination.Name, false, error.Message));
                }
            }

            Last = results;
            return results;
        }

        private async Task SendAsync(IBackupDestination destination, string file, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(passphrase))
            {
                await destination.UploadAsync(file, Path.GetFileName(file), ct);
                return;
            }

            var encrypted = file + ".enc.partial";
            try
            {
                await BackupCrypto.EncryptAsync(file, encrypted, passphrase, ct);
                await destination.UploadAsync(encrypted, Path.GetFileName(file) + ".enc", ct);
            }
            finally
            {
                File.Delete(encrypted);
            }
        }

        // What to remove at a destination: all files of every backup but the newest `keep`. A backup is a dump and its scans, found by the name without its ending;
        // manual backups and files that are not ours stay.
        public static IReadOnlyList<string> Obsolete(IEnumerable<string> names, int keep)
        {
            var groups = names.Where(name => name.StartsWith("junotes-", StringComparison.Ordinal) && !name.Contains("-manual.", StringComparison.Ordinal))
                .GroupBy(Stem)
                .OrderByDescending(group => group.Key, StringComparer.Ordinal)
                .Skip(Math.Max(1, keep));
            return groups.SelectMany(group => group).ToList();
        }

        internal static string Stem(string name)
        {
            foreach (var ending in new[] { ".enc", ".assets.zip", ".dump" })
            {
                while (name.EndsWith(ending, StringComparison.Ordinal))
                {
                    name = name[..^ending.Length];
                }
            }

            return name;
        }

        [LoggerMessage(Level = LogLevel.Warning, Message = "Copying the backup to {Destination} failed")]
        private static partial void LogFailed(ILogger logger, string destination, Exception exception);
    }
}
