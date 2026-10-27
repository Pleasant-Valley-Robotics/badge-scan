using BadgeScan.Web.Models;

namespace BadgeScan.Web.Data;

/// <summary>
/// Where accounts, meetings and data keys are kept. PostgresAccountStore is the real one;
/// InMemoryAccountStore stands in for the tests and for running without a database.
/// </summary>
public interface IAccountStore
{
    Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken ct);
    Task<Account?> GetAccountAsync(long id, CancellationToken ct);
    /// <summary>By username, which is always lower case.</summary>
    Task<Account?> FindAccountAsync(string username, CancellationToken ct);
    /// <summary>Throws UsernameTakenException if the username is someone's already.</summary>
    Task<Account> AddAccountAsync(NewAccount account, CancellationToken ct);

    /// <summary>A new password. A new security stamp with it ends every earlier sign-in. Clears any lock.</summary>
    Task SetPasswordAsync(long id, string passwordHash, bool mustChange, DateTimeOffset? expiresAt, string securityStamp, CancellationToken ct);
    /// <summary>Turns an account off or back on, with a new security stamp.</summary>
    Task SetDisabledAsync(long id, bool disabled, string securityStamp, CancellationToken ct);
    Task RecordSignInAsync(long id, DateTimeOffset at, CancellationToken ct);
    /// <summary>One more wrong password. The one that reaches the limit locks the account until lockUntil and starts the count again.</summary>
    Task RecordFailedSignInAsync(long id, int limit, DateTimeOffset lockUntil, CancellationToken ct);

    Task<Meeting> StartMeetingAsync(long openedBy, string openedByName, DateTimeOffset start, DateTimeOffset ends, DateOnly centralDate, CancellationToken ct);
    Task<Meeting?> GetMeetingAsync(long id, CancellationToken ct);
    /// <summary>Meetings not closed and not yet past their end, oldest first.</summary>
    Task<IReadOnlyList<Meeting>> OpenMeetingsAsync(DateTimeOffset now, CancellationToken ct);
    /// <summary>False when the meeting was already closed or doesn't exist.</summary>
    Task<bool> CloseMeetingAsync(long id, long closedBy, DateTimeOffset at, CancellationToken ct);

    Task<ApiKey> AddApiKeyAsync(string label, string keyHash, string keyPrefix, long createdBy, DateTimeOffset at, CancellationToken ct);
    Task<IReadOnlyList<ApiKey>> ListApiKeysAsync(CancellationToken ct);
    /// <summary>The key with this hash, if it hasn't been revoked.</summary>
    Task<ApiKey?> FindApiKeyAsync(string keyHash, CancellationToken ct);
    Task TouchApiKeyAsync(long id, DateTimeOffset at, CancellationToken ct);
    Task<bool> RevokeApiKeyAsync(long id, DateTimeOffset at, CancellationToken ct);
}
