using Npgsql;

namespace BadgeScan.Web.Data;

/// <summary>
/// The robotics server hands every team's containers PGHOST, PGPORT, PGUSER, PGPASSWORD
/// and PGDATABASE (compose.yaml passes them in). This turns them into a connection string.
/// </summary>
public static class DatabaseOptions
{
    /// <summary>Null when PGHOST is not set: there is no database to talk to.</summary>
    public static string? ConnectionStringFromEnvironment(IConfiguration configuration)
    {
        var host = configuration["PGHOST"];
        if (string.IsNullOrWhiteSpace(host)) return null;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(configuration["PGPORT"], out var port) ? port : 5432,
            Username = configuration["PGUSER"],
            Password = configuration["PGPASSWORD"],
            Database = configuration["PGDATABASE"],
            SslMode = SslMode.Prefer,
            // The server's PostgreSQL doesn't use Kerberos, and the container has no library
            // for it; without this every start logs "libgssapi_krb5.so.2" as an error.
            GssEncryptionMode = GssEncryptionMode.Disable,
            ApplicationName = "badge-scan",
        }.ConnectionString;
    }
}
