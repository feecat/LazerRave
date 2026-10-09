using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Cloud;

public sealed record PublicUser(Guid Id, string Username, string DisplayName, string Signature, string Bio, string? AvatarUrl, string Role, DateTime CreatedAt, long Uid = 0);
public sealed record RegisterInput(string Username, string Email, string Password);
public sealed record LoginInput(string Username, string Password);
public sealed record PasswordInput(string CurrentPassword, string NewPassword);
public sealed record ProfileInput(string DisplayName, string Signature, string Bio);

public sealed class Auth(Pg db, CloudOptions options)
{
    private readonly PasswordHasher<string> hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = 210000 }));
    private readonly string dummyHash = new PasswordHasher<string>(Options.Create(new PasswordHasherOptions { IterationCount = 210000 })).HashPassword("dummy", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    public static string Digest(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    public static Guid Id(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static string? Token(HttpRequest request) => request.Headers.Authorization.ToString() is { } header && header.StartsWith("Bearer ", StringComparison.Ordinal)
        ? header[7..] : request.Cookies["lr_session"];
    public static PublicUser Public(Dictionary<string, object?> row) => new((Guid)row["id"]!, (string)row["username"]!,
        (string)row["displayName"]!, (string)row["signature"]!, (string)row["bio"]!, row["avatarKey"] is null ? null : $"/api/users/{row["id"]}/avatar?v={row["avatarKey"]}",
        (string)row["role"]!, (DateTime)row["createdAt"]!, (long)row["uid"]!);

    public async Task<PublicUser> Register(RegisterInput input)
    {
        string username = input.Username ?? "";
        if (!Regex.IsMatch(username, "\\A[A-Za-z0-9_]{3,24}\\z") || input.Email?.Length > 254 ||
            !System.Net.Mail.MailAddress.TryCreate(input.Email, out var email) || email.Address != input.Email || input.Password?.Length is not (>= 12 and <= 128))
            throw new ApiError(400, "Use a 3–24 character username, a valid email and a 12–128 character password.");
        var id = Guid.NewGuid();
        var rows = await db.Query("INSERT INTO users(id,username,email,password_hash,display_name) VALUES(@id,@name,@email,@hash,@name) RETURNING *",
            ("id", id), ("name", username), ("email", input.Email.ToLowerInvariant()), ("hash", hasher.HashPassword(username, input.Password)));
        return Public(rows.Single());
    }

    public async Task<PublicUser> Login(LoginInput input)
    {
        if (input.Username?.Length is not (>= 1 and <= 254) || input.Password?.Length is not (>= 1 and <= 128)) throw new ApiError(401, "Invalid username or password.");
        var rows = await db.Query("SELECT * FROM users WHERE lower(username)=lower(@name) OR lower(email)=lower(@name)", ("name", input.Username));
        var row = rows.FirstOrDefault();
        var result = hasher.VerifyHashedPassword(input.Username, row? ["passwordHash"] as string ?? dummyHash, input.Password);
        if (row is null || (bool)row["disabled"]! || result == PasswordVerificationResult.Failed) throw new ApiError(401, "Invalid username or password.");
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            await db.Query("UPDATE users SET password_hash=@hash WHERE id=@id", ("hash", hasher.HashPassword(input.Username, input.Password)), ("id", row["id"]));
        return Public(row);
    }

    public async Task<string> CreateSession(Guid userId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await db.Query("DELETE FROM sessions WHERE user_id=@id AND (expires_at < now() OR created_at < now()-interval '30 days'); INSERT INTO sessions(token_hash,user_id,expires_at) VALUES(@hash,@id,@expiry)",
            ("id", userId), ("hash", Digest(token)), ("expiry", DateTime.UtcNow.AddDays(options.SessionDays)));
        return token;
    }

    public async Task ChangePassword(Guid userId, PasswordInput input)
    {
        if (input.CurrentPassword?.Length is not (>= 1 and <= 128) || input.NewPassword?.Length is not (>= 12 and <= 128))
            throw new ApiError(400, "Enter your current password and a new password of 12–128 characters.");
        await using var connection = await db.Open();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var lookup = new NpgsqlCommand("SELECT username,password_hash FROM users WHERE id=@id AND NOT disabled FOR UPDATE", connection, transaction);
        Pg.Add(lookup, ("id", userId));
        var row = (await Pg.Read(lookup)).FirstOrDefault();
        if (row is null || hasher.VerifyHashedPassword((string)row["username"]!, (string)row["passwordHash"]!, input.CurrentPassword) == PasswordVerificationResult.Failed)
            throw new ApiError(400, "Current password is incorrect.");
        if (input.CurrentPassword == input.NewPassword) throw new ApiError(400, "Choose a password different from your current password.");
        await using var update = new NpgsqlCommand("UPDATE users SET password_hash=@hash WHERE id=@id; DELETE FROM sessions WHERE user_id=@id", connection, transaction);
        Pg.Add(update, ("id", userId), ("hash", hasher.HashPassword((string)row["username"]!, input.NewPassword)));
        await update.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task<PublicUser?> Session(string? token)
    {
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit)) return null;
        var rows = await db.Query("SELECT u.* FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=@hash AND s.expires_at>now() AND NOT u.disabled", ("hash", Digest(token)));
        return rows.Count == 0 ? null : Public(rows[0]);
    }

    public CookieOptions Cookie() => new() { HttpOnly = true, Secure = options.SecureCookies, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.FromDays(options.SessionDays), IsEssential = true };
}

public sealed class SessionHandler(IOptionsMonitor<AuthenticationSchemeOptions> settings, ILoggerFactory logger, UrlEncoder encoder, Auth auth)
    : AuthenticationHandler<AuthenticationSchemeOptions>(settings, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = await auth.Session(Auth.Token(Request));
        if (user is null) return AuthenticateResult.NoResult();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Username), new Claim(ClaimTypes.Role, user.Role) };
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
    }
}
