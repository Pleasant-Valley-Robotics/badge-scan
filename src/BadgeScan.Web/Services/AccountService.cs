using System.Security.Cryptography;
using System.Text;
using BadgeScan.Web.Data;
using BadgeScan.Web.Models;
using Microsoft.AspNetCore.Identity;

namespace BadgeScan.Web.Services;

/// <summary>What a sign-in attempt came to. Account is set only when it worked.</summary>
public sealed record SignInOutcome(Account? Account, string? Error)
{
    public static SignInOutcome Wrong { get; } = new(null, "Wrong username or password.");
}

/// <summary>
/// Accounts and their passwords, and the keys other programs read scans with. Passwords
/// are kept as PBKDF2 hashes (ASP.NET Core Identity's hasher); keys as SHA-256 hashes.
/// </summary>
public sealed class AccountService(IAccountStore store, TimeProvider clock)
{
    /// <summary>This many wrong passwords in a row locks the account for LockFor.</summary>
    public const int MaxFailedSignIns = 5;
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);
    /// <summary>How long a temporary password (new account, reset) works before it must be reset again.</summary>
    public static readonly TimeSpan TemporaryPasswordLife = TimeSpan.FromDays(7);
    public const int MinPasswordLength = 10;
    public const string KeyStart = "bsk_";

    private static readonly PasswordHasher<object> Hasher = new();
    // Checked against when the username doesn't exist, so a wrong name takes as long as a wrong password.
    private static readonly string NobodysHash = Hasher.HashPassword(null!, "nobody has this password");

    /// <summary>Usernames are lower case, letters, digits and . _ -, 2 to 32 long, starting with a letter or digit.</summary>
    public static string? NormalizeUsername(string? username)
    {
        var u = (username ?? "").Trim().ToLowerInvariant();
        return System.Text.RegularExpressions.Regex.IsMatch(u, "^[a-z0-9][a-z0-9._-]{1,31}$") ? u : null;
    }

    public async Task<SignInOutcome> SignInAsync(string? username, string? password, CancellationToken ct)
    {
        var name = NormalizeUsername(username);
        var account = name is null ? null : await store.FindAccountAsync(name, ct);
        if (account is null || string.IsNullOrEmpty(password))
        {
            Hasher.VerifyHashedPassword(null!, NobodysHash, password ?? "");
            return SignInOutcome.Wrong;
        }

        var now = clock.GetUtcNow();
        if (account.LockedUntil > now)
        {
            var until = CentralTime.From(account.LockedUntil.Value).Wall;
            return new(null, $"Too many wrong passwords. This account is locked until {until:h:mm tt}.");
        }

        if (Hasher.VerifyHashedPassword(null!, account.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            await store.RecordFailedSignInAsync(account.Id, MaxFailedSignIns, now + LockFor, ct);
            return SignInOutcome.Wrong;
        }
        // Checked only after the password, so these don't tell a stranger the account exists.
        if (account.Disabled) return SignInOutcome.Wrong;
        if (account.MustChangePassword && account.PasswordExpiresAt <= now)
            return new(null, "That temporary password has expired. Ask another mentor to reset it on the Accounts page.");

        await store.RecordSignInAsync(account.Id, now, ct);
        return new(account, null);
    }

    /// <summary>A new account with a temporary password, which is returned once and never stored.</summary>
    public async Task<(Account Account, string TemporaryPassword)> CreateAsync(string? username, string? displayName, string? title, CancellationToken ct)
    {
        var name = NormalizeUsername(username)
            ?? throw new ArgumentException("A username is 2 to 32 letters, digits, dots, dashes or underscores.");
        var display = (displayName ?? "").Trim();
        if (display.Length == 0) throw new ArgumentException("A name is needed.");
        var t = (title ?? "").Trim().ToLowerInvariant();
        if (!Account.Titles.Contains(t)) throw new ArgumentException("The title is mentor, coach or lead.");

        var temporary = TemporaryPassword();
        var account = await store.AddAccountAsync(
            new NewAccount(name, display, t, Hasher.HashPassword(null!, temporary), clock.GetUtcNow() + TemporaryPasswordLife, NewStamp()), ct);
        return (account, temporary);
    }

    /// <summary>A new temporary password for someone who forgot theirs. Signs them out everywhere.</summary>
    public async Task<string> ResetPasswordAsync(long id, CancellationToken ct)
    {
        var temporary = TemporaryPassword();
        await store.SetPasswordAsync(id, Hasher.HashPassword(null!, temporary), mustChange: true, clock.GetUtcNow() + TemporaryPasswordLife, NewStamp(), ct);
        return temporary;
    }

    /// <summary>Someone choosing their own password. Returns what's wrong with it, or null when it's saved.</summary>
    public async Task<string?> ChangePasswordAsync(Account account, string? current, string? chosen, string? repeated, CancellationToken ct)
    {
        if (Hasher.VerifyHashedPassword(null!, account.PasswordHash, current ?? "") == PasswordVerificationResult.Failed)
            return "Your current password isn't right.";
        if (chosen != repeated) return "The two new passwords don't match.";
        var problem = CheckNewPassword(account, chosen, current);
        if (problem is not null) return problem;

        await store.SetPasswordAsync(account.Id, Hasher.HashPassword(null!, chosen!), mustChange: false, expiresAt: null, NewStamp(), ct);
        return null;
    }

    public static string? CheckNewPassword(Account account, string? chosen, string? current)
    {
        if (string.IsNullOrEmpty(chosen) || chosen.Length < MinPasswordLength) return $"A password needs at least {MinPasswordLength} characters.";
        if (chosen.Length > 200) return "That password is too long.";
        if (chosen == current) return "Choose a password different from the one you have now.";
        if (account.Username.Length >= 4 ? chosen.Contains(account.Username, StringComparison.OrdinalIgnoreCase) : chosen.Equals(account.Username, StringComparison.OrdinalIgnoreCase))
            return "A password can't contain your username.";
        return null;
    }

    public Task SetDisabledAsync(long id, bool disabled, CancellationToken ct) => store.SetDisabledAsync(id, disabled, NewStamp(), ct);

    /// <summary>A new data key. The key is returned once; only its hash is kept.</summary>
    public async Task<(ApiKey Key, string Secret)> CreateApiKeyAsync(string? label, long createdBy, CancellationToken ct)
    {
        var l = (label ?? "").Trim();
        if (l.Length == 0) throw new ArgumentException("Say what the key is for, like \"Project-Tracker\".");
        var secret = KeyStart + Base64Url(RandomNumberGenerator.GetBytes(32));
        var key = await store.AddApiKeyAsync(l, HashKey(secret), secret[..(KeyStart.Length + 6)], createdBy, clock.GetUtcNow(), ct);
        return (key, secret);
    }

    /// <summary>The key a program sent, if it is one we made and haven't revoked.</summary>
    public async Task<ApiKey?> FindApiKeyAsync(string? secret, CancellationToken ct)
    {
        if (secret is null || !secret.StartsWith(KeyStart, StringComparison.Ordinal) || secret.Length > 100) return null;
        var key = await store.FindApiKeyAsync(HashKey(secret), ct);
        if (key is not null) await store.TouchApiKeyAsync(key.Id, clock.GetUtcNow(), ct);
        return key;
    }

    /// <summary>Like ab3k-7mxp-q2rd: no 0/o, 1/l/i, so it can be read out loud and typed.</summary>
    public static string TemporaryPassword()
    {
        var chars = RandomNumberGenerator.GetItems<char>("abcdefghjkmnpqrstuvwxyz23456789", 12);
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}-{new string(chars, 8, 4)}";
    }

    private static string NewStamp() => Base64Url(RandomNumberGenerator.GetBytes(16));
    private static string HashKey(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
