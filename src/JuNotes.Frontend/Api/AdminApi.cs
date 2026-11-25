using Base.Core;
using Base.Web;
using Base.Web.Auth;
using JuNotes.Backend.Ai;
using JuNotes.Backend.Backup;
using JuNotes.Backend.School;
using JuNotes.Backend.Service;
using JuNotes.Frontend.Admin;
using JuNotes.Integrations.Codex;

namespace JuNotes.Frontend.Api
{
    public sealed record SettingsRequest(string? Model, string? Effort, int? DailyLimit);

    public sealed record ProfileRequest(string? SystemPrompt, string? Difficulty, string? Tone, bool? Enabled, string? Model, string? Effort);

    public sealed record UsageDto(DateOnly Day, int Requests, int Failures);

    public sealed record BackupDto(DateTimeOffset At, string Kind, long Bytes, long? AssetBytes);

    // The administration, for the pages in the toolkit of the base: what the old Razor pages did, as JSON, behind the role admin (declared here, checked in the guard of the base).
    // Nothing school-specific by name and no secret leaves here: Codex is shown as signed in or not, and the device code only while a login waits for it.
    internal static class AdminApi
    {
        public static void MapAdminApi(this IEndpointRouteBuilder app)
        {
            var admin = app.MapGroup(WebApi.Prefix + "/admin").RequireRole("admin");

            admin.MapGet("/overview", async (HttpContext ctx, OwnerDirectory owners, HealthService health, BackupService backups, SchoolImportService school, AiUsageStore usage, AiOptions ai, AiSettingsStore settings, CodexAccount codex, TimeProvider time) =>
            {
                var ct = ctx.RequestAborted;
                var last = backups.Latest();
                var current = await settings.GetAsync(ct);
                var recent = await usage.RecentAsync(7, ct);
                var day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
                return Results.Ok(new
                {
                    databaseReady = await health.IsReadyAsync(ct),
                    claimFailed = owners.ClaimFailed,
                    backup = last is null ? null : new { at = last.At, bytes = last.Bytes },
                    backupOverdue = BackupSchedule.IsOverdue(last?.At, time.GetUtcNow()),
                    school = school.Status,
                    codex = await codex.StatusAsync(ct),
                    limit = current.DailyLimit ?? ai.DailyLimit,
                    model = current.Model,
                    effort = current.Effort,
                    today = recent.FirstOrDefault(item => item.Day == day) ?? new AiUsage(day, 0, 0),
                });
            });

            admin.MapPost("/school/import", async (HttpContext ctx, SchoolImportService school) => Results.Ok(await school.ImportAsync(ctx.RequestAborted)));

            admin.MapGet("/codex", async (HttpContext ctx, CodexAccount account, CodexLoginSession session, AiSettingsStore settingsStore, AiOptions ai, AiUsageStore usage, TimeProvider time) =>
            {
                var ct = ctx.RequestAborted;
                var settings = await settingsStore.GetAsync(ct);
                var status = await account.StatusAsync(ct);
                var day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
                var recent = (await usage.RecentAsync(14, ct)).Select(item => new UsageDto(item.Day, item.Requests, item.Failures)).ToList();
                return Results.Ok(new
                {
                    status,
                    // A finished login shows as signed in, so the waiting code is only shown while it is still needed.
                    waiting = status.SignedIn ? null : session.Waiting,
                    settings,
                    limit = settings.DailyLimit ?? ai.DailyLimit,
                    efforts = AiSettingsRules.Efforts,
                    maxDailyLimit = AiSettingsRules.MaxDailyLimit,
                    today = recent.FirstOrDefault(item => item.Day == day) ?? new UsageDto(day, 0, 0),
                    recent,
                });
            });

            admin.MapPost("/codex/login", async (HttpContext ctx, CodexAccount account, CodexLoginSession session) =>
            {
                try
                {
                    return await session.StartAsync(account, ctx.RequestAborted) is { } code
                        ? Results.Ok(code)
                        : Error.Conflict("codex.no_code", "The sign-in could not be started: Codex showed no code.").ToHttp();
                }
                catch (CodexUnavailableException error)
                {
                    return Error.Conflict("codex.unavailable", error.Message).ToHttp();
                }
            });

            admin.MapPost("/codex/cancel", (CodexLoginSession session) =>
            {
                session.Cancel();
                return Results.NoContent();
            });

            admin.MapPost("/codex/logout", async (HttpContext ctx, CodexAccount account, CodexLoginSession session) =>
            {
                session.Cancel();
                var done = await account.LogoutAsync(ctx.RequestAborted);
                return done && await account.StatusAsync(ctx.RequestAborted) is { SignedIn: false } ? Results.NoContent() : Error.Conflict("codex.logout_failed", "Signing out did not work.").ToHttp();
            });

            admin.MapPut("/codex/settings", async (SettingsRequest body, HttpContext ctx, AiSettingsStore store) =>
                await store.SaveAsync(new AiSettings(body.Model, body.Effort, body.DailyLimit), ctx.RequestAborted)
                    ? Results.NoContent()
                    : Error.Invalid("settings.invalid", $"The model may only have letters, digits and . _ : / - ; the effort must be from the list; the daily limit between 1 and {AiSettingsRules.MaxDailyLimit}.").ToHttp());

            admin.MapGet("/profiles", async (HttpContext ctx, AiProfileStore store) => Results.Ok(new
            {
                profiles = await store.ListAsync(ctx.RequestAborted),
                difficulties = AiDefaults.Difficulties,
                tones = AiDefaults.Tones,
                efforts = AiSettingsRules.Efforts,
            }));

            admin.MapPut("/profiles/{key}", async (string key, ProfileRequest body, HttpContext ctx, AiProfileStore store) =>
            {
                if (await store.GetAsync(key, ctx.RequestAborted) is not { } current)
                {
                    return Error.NotFound().ToHttp();
                }

                var saved = await store.UpdateAsync(current with { SystemPrompt = body.SystemPrompt ?? string.Empty, Difficulty = body.Difficulty ?? string.Empty, Tone = body.Tone ?? string.Empty, Enabled = body.Enabled ?? false, Model = body.Model, Effort = body.Effort }, ctx.RequestAborted);
                return saved ? Results.NoContent() : Error.Invalid("profile.invalid", "The instruction must not be empty, difficulty, tone and effort must be from the list and the model may only have letters, digits and . _ : / - .").ToHttp();
            });

            admin.MapPost("/profiles/{key}/reset", async (string key, HttpContext ctx, AiProfileStore store) =>
                await store.ResetAsync(key, ctx.RequestAborted) ? Results.NoContent() : Error.NotFound().ToHttp());

            admin.MapGet("/backups", (BackupService backups) =>
            {
                var options = backups.Options;
                var files = BackupFiles.List(options.Directory).Select(file => new BackupDto(file.At, file.Kind.ToString(), file.Bytes,
                    File.Exists(BackupFiles.AssetsArchiveFor(file.Path)) ? new FileInfo(BackupFiles.AssetsArchiveFor(file.Path)).Length : null)).ToList();
                var mirror = backups.Mirror;
                var keyState = options.KeyFile is not { } key || !File.Exists(key) ? "none" : mirror is { IsActive: true, Encrypts: true } ? "with-copies" : "nowhere";
                return Results.Ok(new
                {
                    hour = options.Hour,
                    keep = options.Keep,
                    files,
                    destinations = mirror?.Names ?? [],
                    encrypts = mirror?.Encrypts ?? false,
                    results = (mirror?.Last ?? []).Select(result => new { result.Destination, result.Ok, result.Error }),
                    keyState,
                });
            });

            admin.MapPost("/backups/run", async (HttpContext ctx, BackupService backups) =>
            {
                try
                {
                    var file = await backups.RunAsync(BackupKind.Manual, ctx.RequestAborted);
                    return Results.Ok(new BackupDto(file.At, file.Kind.ToString(), file.Bytes, null));
                }
                catch (BackupFailedException error)
                {
                    return Error.Conflict("backup.failed", error.Message).ToHttp();
                }
            });
        }
    }
}
