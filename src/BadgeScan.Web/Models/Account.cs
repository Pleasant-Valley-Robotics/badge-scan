namespace BadgeScan.Web.Models;

/// <summary>
/// A lead, coach or mentor who can sign in. Everyone with an account may do everything;
/// Title only says who they are.
/// </summary>
public sealed record Account
{
    public required long Id { get; init; }
    public required string Username { get; init; }
    public required string DisplayName { get; init; }
    public required string Title { get; init; }
    public required string PasswordHash { get; init; }
    public required bool MustChangePassword { get; init; }
    public required DateTimeOffset? PasswordExpiresAt { get; init; }
    public required string SecurityStamp { get; init; }
    public required bool Disabled { get; init; }
    public required int FailedSignIns { get; init; }
    public required DateTimeOffset? LockedUntil { get; init; }
    public required DateTimeOffset? LastSignInAt { get; init; }

    public static readonly string[] Titles = ["mentor", "coach", "lead"];
}

/// <summary>A new account, before the store gives it an id.</summary>
public sealed record NewAccount(string Username, string DisplayName, string Title, string PasswordHash, DateTimeOffset PasswordExpiresAt, string SecurityStamp);

/// <summary>Thrown when the username is already someone's.</summary>
public sealed class UsernameTakenException(string username) : Exception($"The username {username} is already taken.")
{
    public string Username { get; } = username;
}

/// <summary>One browser turned into the kiosk, from when someone opened it until it ends.</summary>
public sealed record Meeting
{
    public required long Id { get; init; }
    public required long OpenedBy { get; init; }
    public required string OpenedByName { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required DateTimeOffset EndsUtc { get; init; }
    public required DateTimeOffset? ClosedUtc { get; init; }
    public required DateOnly CentralDate { get; init; }

    public bool IsOpenAt(DateTimeOffset now) => ClosedUtc is null && now < EndsUtc;
}

/// <summary>A key another program reads the scans with. The key itself is never stored, only its hash.</summary>
public sealed record ApiKey
{
    public required long Id { get; init; }
    public required string Label { get; init; }
    public required string KeyPrefix { get; init; }
    public required string CreatedByName { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset? LastUsedAt { get; init; }
    public required DateTimeOffset? RevokedAt { get; init; }
}
