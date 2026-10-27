using BadgeScan.Web.Data;
using Npgsql;

namespace BadgeScan.Web.Services;

/// <summary>
/// Accounts from the server's shell, for when nobody can sign in yet (the very first account)
/// or everybody is locked out. Day to day, accounts are made on the Accounts page.
/// </summary>
public static class AccountCommand
{
    private const string Usage = """
        Run inside the site's container on the robotics server, for example:
          sudo robotics-team docker 1 exec live-web-1 dotnet BadgeScan.Web.dll account list

          account add <username> "<Full Name>" <mentor|coach|lead>
          account reset <username>
          account list

        add and reset print a temporary password, once. It works for 7 days, and the person
        chooses their own the first time they sign in.
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var connectionString = DatabaseOptions.ConnectionStringFromEnvironment(configuration);
        if (connectionString is null)
        {
            Console.Error.WriteLine("PGHOST is not set: there is no database here. Run this inside the site's container.");
            return 1;
        }

        await using var db = NpgsqlDataSource.Create(connectionString);
        using (var logs = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true)))
        {
            await Migrator.RunAsync(db, logs.CreateLogger("Migrations"), CancellationToken.None);
        }
        var store = new PostgresAccountStore(db);
        var accounts = new AccountService(store, TimeProvider.System);
        var ct = CancellationToken.None;

        switch (args)
        {
            case ["add", var username, var name, var title]:
                try
                {
                    var (account, temporary) = await accounts.CreateAsync(username, name, title, ct);
                    Console.WriteLine($"Added {account.DisplayName} ({account.Title}) as {account.Username}.");
                    Console.WriteLine($"Temporary password: {temporary}");
                    return 0;
                }
                catch (Exception ex) when (ex is ArgumentException or Models.UsernameTakenException)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }

            case ["reset", var username]:
            {
                var account = AccountService.NormalizeUsername(username) is { } u ? await store.FindAccountAsync(u, ct) : null;
                if (account is null)
                {
                    Console.Error.WriteLine($"No account {username}.");
                    return 1;
                }
                Console.WriteLine($"New temporary password for {account.DisplayName}: {await accounts.ResetPasswordAsync(account.Id, ct)}");
                if (account.Disabled) Console.WriteLine("This account is turned off. Turn it back on from the Accounts page.");
                return 0;
            }

            case ["list"]:
                foreach (var a in await store.ListAccountsAsync(ct))
                    Console.WriteLine($"{a.Username,-20} {a.DisplayName,-28} {a.Title,-7} {(a.Disabled ? "off" : a.MustChangePassword ? "temporary password" : "")}");
                return 0;

            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }
}
