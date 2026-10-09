using System.Net;
using System.Threading.RateLimiting;
using Cloud;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
var cloud = builder.Configuration.GetSection("Cloud").Get<CloudOptions>() ?? new();
if (!Uri.TryCreate(cloud.PublicOrigin, UriKind.Absolute, out var origin) || origin.Scheme is not ("http" or "https") ||
    origin.AbsolutePath != "/" || cloud.MaxRooms is < 1 or > 32 || cloud.MaxUploadBytes is < 1048576 or > 536870912 ||
    cloud.MaxExpandedBytes < cloud.MaxUploadBytes || cloud.MaxStorageBytes < cloud.MaxUploadBytes || cloud.SessionDays is < 1 or > 30 ||
    (!builder.Environment.IsDevelopment() && (origin.Scheme != "https" || !cloud.SecureCookies)))
    throw new InvalidOperationException("Invalid Cloud configuration. Production requires HTTPS and secure cookies.");
builder.Services.AddSingleton(cloud);
var connectionString = builder.Configuration.GetConnectionString("Postgres") ?? throw new InvalidOperationException("Set ConnectionStrings__Postgres.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<Pg>(); builder.Services.AddSingleton<Auth>(); builder.Services.AddSingleton<ContentStore>();
builder.Services.AddSingleton<Ranking>(); builder.Services.AddSingleton<Rooms>(); builder.Services.AddSingleton<HubGuard>();
builder.Services.AddAuthentication("session").AddScheme<AuthenticationSchemeOptions, SessionHandler>("session", _ => { });
builder.Services.AddAuthorization(options => options.AddPolicy("admin", policy => policy.RequireRole("admin")));
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 8192;
    options.MaximumParallelInvocationsPerClient = 1;
    options.EnableDetailedErrors = false;
    options.AddFilter<HubGuard>();
});
builder.Services.AddHostedService<RoomTicker>();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = cloud.MaxUploadBytes + 65536;
    options.MemoryBufferThreshold = 65536;
    options.ValueLengthLimit = 8192;
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = cloud.MaxUploadBytes + 131072);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    if (IPAddress.TryParse(builder.Configuration["ReverseProxyAddress"], out var proxy)) options.KnownProxies.Add(proxy);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
        {
            PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
        }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
    {
        PermitLimit = builder.Environment.IsDevelopment() ? 100 : 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0,
    }));
});
var app = builder.Build();
Directory.CreateDirectory(Path.Combine(Path.GetFullPath(cloud.StoragePath), "tmp"));
var db = app.Services.GetRequiredService<Pg>();
await db.Migrate();
if (args is ["--grant-admin", var username])
{
    var rows = await db.Query("UPDATE users SET role='admin' WHERE lower(username)=lower(@name) RETURNING username", ("name", username));
    if (rows.Count != 1) throw new InvalidOperationException("Register the account before granting the admin role.");
    Console.WriteLine("Admin role granted to " + rows[0]["username"]);
    return;
}
if (args is ["--migrate"]) { Console.WriteLine("Database migration complete."); return; }
if (args.Length > 0) throw new InvalidOperationException("Use --migrate or --grant-admin username, or no arguments to serve.");
await db.Query("UPDATE matches SET state='interrupted',finished_at=now() WHERE state IN ('countdown','playing')");
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' blob:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    try
    {
        var requestOrigin = context.Request.Headers.Origin.ToString();
        var sizeLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false }) sizeLimit.MaxRequestBodySize = context.Request.Path == "/api/admin/packs"
            ? cloud.MaxUploadBytes + 131072 : context.Request.Path == "/api/me/avatar" ? 2097152 : 65536;
        if (requestOrigin.Length > 0 && !string.Equals(requestOrigin.TrimEnd('/'), origin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)) throw new ApiError(403, "Cross-origin requests are not accepted.");
        if (context.Request.Path.StartsWithSegments("/api") && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS") &&
            context.Request.Headers["X-LazerRave"] != "1" && !context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal))
            throw new ApiError(403, "Missing request protection header.");
        await next(context);
    }
    catch (Exception error) when (!context.Response.HasStarted)
    {
        int status = error switch
        {
            ApiError api => api.Status,
            PostgresException { SqlState: "23505" } => 409,
            BadHttpRequestException bad => bad.StatusCode,
            InvalidDataException or EndOfStreamException or ArgumentException => 400,
            OperationCanceledException => 408,
            _ => 500,
        };
        if (status == 500) app.Logger.LogError(error, "Cloud request failed");
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error = status == 500 ? "Server error. Please try again." : error is PostgresException ? "That username, email or record already exists." : error.Message });
    }
});
app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/api/health", async (Pg pg) => { await pg.Query("SELECT 1"); return Results.Ok(new { status = "ok", service = "LazerRave", protocol = 1 }); });
app.MapGet("/api/me", async (HttpContext context, Auth auth) => Results.Ok(await auth.Session(Auth.Token(context.Request))));
app.MapPost("/api/auth/register", async (RegisterInput input, HttpContext context, Auth auth) =>
{
    var user = await auth.Register(input);
    context.Response.Cookies.Append("lr_session", await auth.CreateSession(user.Id), auth.Cookie());
    return Results.Ok(user);
}).RequireRateLimiting("auth");
app.MapPost("/api/auth/login", async (LoginInput input, HttpContext context, Auth auth) =>
{
    var user = await auth.Login(input);
    context.Response.Cookies.Append("lr_session", await auth.CreateSession(user.Id), auth.Cookie());
    return Results.Ok(user);
}).RequireRateLimiting("auth");
app.MapPost("/api/auth/token", async (LoginInput input, Auth auth) =>
{
    var user = await auth.Login(input);
    return Results.Ok(new { user, token = await auth.CreateSession(user.Id), tokenType = "Bearer" });
}).RequireRateLimiting("auth");
app.MapPost("/api/auth/logout", async (HttpContext context, Pg pg, Auth auth) =>
{
    if (Auth.Token(context.Request) is { } token) await pg.Query("DELETE FROM sessions WHERE token_hash=@hash", ("hash", Auth.Digest(token)));
    context.Response.Cookies.Delete("lr_session", auth.Cookie());
    return Results.NoContent();
}).RequireAuthorization();
app.MapGet("/api/users/{username}", async (string username, Pg pg) =>
{
    var rows = await pg.Query("SELECT * FROM users WHERE lower(username)=lower(@name) AND NOT disabled", ("name", username));
    if (rows.Count == 0) throw new ApiError(404, "Player not found.");
    var user = Auth.Public(rows[0]);
    var scores = await pg.Query("SELECT s.id,s.ex_score,s.clear,s.verified,s.created_at,c.id AS chart_id,c.title FROM scores s JOIN charts c ON c.id=s.chart_id WHERE s.user_id=@id ORDER BY s.created_at DESC LIMIT 20", ("id", user.Id));
    return Results.Ok(new { user, scores });
});
app.MapPut("/api/me", async (ProfileInput input, HttpContext context, Pg pg) =>
{
    if (input.DisplayName?.Trim().Length is not (>= 1 and <= 40) || input.Signature is null || input.Signature.Length > 200 || input.Bio is null || input.Bio.Length > 2000)
        throw new ApiError(400, "Check the display name, signature and biography lengths.");
    var rows = await pg.Query("UPDATE users SET display_name=@name,signature=@signature,bio=@bio WHERE id=@id RETURNING *", ("id", Auth.Id(context.User)), ("name", input.DisplayName.Trim()), ("signature", input.Signature), ("bio", input.Bio));
    return Results.Ok(Auth.Public(rows.Single()));
}).RequireAuthorization();
app.MapPost("/api/me/avatar", async (HttpContext context, ContentStore content) =>
{
    await content.SaveAvatar(context.Request, Auth.Id(context.User), context.RequestAborted);
    return Results.NoContent();
}).RequireAuthorization();
app.MapGet("/api/users/{id:guid}/avatar", async (Guid id, Pg pg, ContentStore content) =>
{
    var rows = await pg.Query("SELECT avatar_key FROM users WHERE id=@id AND NOT disabled", ("id", id));
    if (rows.FirstOrDefault()?.GetValueOrDefault("avatarKey") is not string key || !File.Exists(content.FilePath(key))) throw new ApiError(404, "Avatar not found.");
    return Results.File(content.FilePath(key), key.EndsWith(".png") ? "image/png" : "image/jpeg");
});
app.MapGet("/api/packs", async (Pg pg) => Results.Ok(await pg.Query("SELECT id,title,description,sha256,size_bytes,created_at FROM packs WHERE published ORDER BY created_at DESC LIMIT 200")));
app.MapGet("/api/packs/{id:guid}", async (Guid id, Pg pg, HttpContext context) =>
{
    var rows = await pg.Query("SELECT id,title,description,sha256,size_bytes,published,created_at FROM packs WHERE id=@id AND (published OR @admin)", ("id", id), ("admin", context.User.IsInRole("admin")));
    if (rows.Count == 0) throw new ApiError(404, "Pack not found.");
    var charts = await pg.Query("SELECT c.*,pc.path FROM charts c JOIN pack_charts pc ON pc.chart_id=c.id WHERE pc.pack_id=@id ORDER BY c.title,c.level,pc.path", ("id", id));
    return Results.Ok(new { pack = rows[0], charts });
});
app.MapGet("/api/packs/{id:guid}/download", async (Guid id, Pg pg, ContentStore content, HttpContext context) =>
{
    var rows = await pg.Query("SELECT file_key,sha256 FROM packs WHERE id=@id AND (published OR @admin)", ("id", id), ("admin", context.User.IsInRole("admin")));
    if (rows.Count == 0 || !File.Exists(content.FilePath((string)rows[0]["fileKey"]!))) throw new ApiError(404, "Pack not found.");
    context.Response.Headers.ETag = $"\"{rows[0]["sha256"]}\"";
    return Results.File(content.FilePath((string)rows[0]["fileKey"]!), "application/zip", $"LazerRave-{id}.zip", enableRangeProcessing: true);
});
app.MapGet("/api/charts", async (string? q, int? keys, int? page, Pg pg) =>
{
    int current = page ?? 1;
    if (current is < 1 or > 10000 || q?.Length > 100 || keys is not null && !new[] { 5, 7, 9, 10, 14 }.Contains(keys.Value)) throw new ApiError(400, "Invalid chart filter.");
    return Results.Ok(await pg.Query("""
        SELECT c.*, (SELECT pc.pack_id FROM pack_charts pc JOIN packs p ON p.id=pc.pack_id WHERE pc.chart_id=c.id AND p.published ORDER BY p.created_at LIMIT 1) AS pack_id
        FROM charts c WHERE (@keys=0 OR c.keys=@keys) AND (c.title ILIKE @query OR c.artist ILIKE @query)
          AND EXISTS(SELECT 1 FROM pack_charts pc JOIN packs p ON p.id=pc.pack_id WHERE pc.chart_id=c.id AND p.published)
        ORDER BY c.title,c.level,c.id LIMIT 50 OFFSET @offset
        """, ("keys", keys ?? 0), ("query", "%" + (q ?? "") + "%"), ("offset", (current - 1) * 50)));
});
app.MapGet("/api/charts/{id:guid}", async (Guid id, Pg pg) =>
{
    var rows = await pg.Query("""
        SELECT c.* FROM charts c WHERE c.id=@id AND
        EXISTS(SELECT 1 FROM pack_charts pc JOIN packs p ON p.id=pc.pack_id WHERE pc.chart_id=c.id AND p.published)
        """, ("id", id));
    if (rows.Count == 0) throw new ApiError(404, "Chart not found.");
    return Results.Ok(rows[0]);
});
app.MapGet("/api/rankings/{chart:guid}", async (Guid chart, string? arrangement, string? gauge, bool? verified, int? page, Ranking rankings) =>
    Results.Ok(await rankings.Board(chart, arrangement ?? "off", gauge ?? "normal", verified ?? false, page ?? 1)));
app.MapPost("/api/scores", async (ScoreInput score, HttpContext context, Ranking rankings) => Results.Ok(await rankings.Submit(Auth.Id(context.User), score))).RequireAuthorization();
app.MapGet("/api/rooms", (Rooms rooms) => Results.Ok(rooms.List()));
app.MapGet("/api/chat/{channel}", async (string channel, HttpContext context, Rooms rooms, Pg pg) =>
{
    if (channel != "lobby" && rooms.Current(Auth.Id(context.User))?.Id.ToString() != channel) throw new ApiError(403, "Join the room first.");
    return Results.Ok(await pg.Query("SELECT m.id,m.channel,m.text,m.created_at,u.id AS user_id,u.username,u.display_name FROM chat_messages m JOIN users u ON u.id=m.user_id WHERE channel=@channel AND NOT u.disabled ORDER BY m.id DESC LIMIT 50", ("channel", channel)));
}).RequireAuthorization();
var admin = app.MapGroup("/api/admin").RequireAuthorization("admin");
admin.MapGet("/overview", async (Pg pg) => Results.Ok((await pg.Query("SELECT (SELECT count(*) FROM users) AS users,(SELECT count(*) FROM packs) AS packs,(SELECT count(*) FROM scores) AS scores,(SELECT count(*) FROM matches) AS matches")).Single()));
admin.MapGet("/packs", async (Pg pg) => Results.Ok(await pg.Query("SELECT id,title,description,size_bytes,published,created_at FROM packs ORDER BY created_at DESC LIMIT 200")));
admin.MapPost("/packs", async (HttpContext context, ContentStore content) => Results.Ok(new { id = await content.UploadPack(context.Request, Auth.Id(context.User), context.RequestAborted) }));
admin.MapPut("/packs/{id:guid}/publication", async (Guid id, PublicationInput input, HttpContext context, Pg pg) =>
{
    var rows = await pg.Query("UPDATE packs SET published=@published WHERE id=@id RETURNING id", ("id", id), ("published", input.Published));
    if (rows.Count == 0) throw new ApiError(404, "Pack not found.");
    await pg.Query("INSERT INTO audit_log(user_id,action,target) VALUES(@user,@action,@target)", ("user", Auth.Id(context.User)), ("action", input.Published ? "pack.publish" : "pack.unpublish"), ("target", id.ToString()));
    return Results.NoContent();
});
admin.MapGet("/users", async (Pg pg) => Results.Ok(await pg.Query("SELECT id,username,display_name,role,disabled,created_at FROM users ORDER BY created_at DESC LIMIT 200")));
admin.MapPut("/users/{id:guid}/disabled", async (Guid id, DisableInput input, HttpContext context, Pg pg) =>
{
    if (id == Auth.Id(context.User)) throw new ApiError(400, "You cannot disable your own account.");
    var rows = await pg.Query("UPDATE users SET disabled=@disabled WHERE id=@id RETURNING id", ("id", id), ("disabled", input.Disabled));
    if (rows.Count == 0) throw new ApiError(404, "Player not found.");
    if (input.Disabled) await pg.Query("DELETE FROM sessions WHERE user_id=@id", ("id", id));
    await pg.Query("INSERT INTO audit_log(user_id,action,target) VALUES(@user,@action,@target)", ("user", Auth.Id(context.User)), ("action", input.Disabled ? "user.disable" : "user.enable"), ("target", id.ToString()));
    return Results.NoContent();
});
admin.MapGet("/audit", async (Pg pg) => Results.Ok(await pg.Query("SELECT a.*,u.username FROM audit_log a JOIN users u ON u.id=a.user_id ORDER BY a.id DESC LIMIT 100")));
app.MapHub<RealtimeHub>("/hubs/realtime", options => { options.ApplicationMaxBufferSize = 65536; options.TransportMaxBufferSize = 65536; });
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs")) { context.Response.StatusCode = 404; return; }
    var index = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
    if (File.Exists(index)) { context.Response.ContentType = "text/html"; await context.Response.SendFileAsync(index); }
    else { context.Response.StatusCode = 503; await context.Response.WriteAsync("Build the React website before starting the published server."); }
});
await app.RunAsync();

public sealed record PublicationInput(bool Published);
public sealed record DisableInput(bool Disabled);
