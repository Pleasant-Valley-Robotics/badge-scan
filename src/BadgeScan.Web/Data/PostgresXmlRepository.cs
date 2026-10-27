using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace BadgeScan.Web.Data;

/// <summary>
/// Keeps ASP.NET Core's data protection keys (what sign-in cookies and forms are signed
/// with) in the data_protection_keys table. Without this they'd live inside the container
/// and every release or restart would sign everyone out, kiosks included.
/// </summary>
public sealed class PostgresXmlRepository(NpgsqlDataSource db) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var cmd = db.CreateCommand("SELECT xml FROM data_protection_keys ORDER BY id");
        using var reader = cmd.ExecuteReader();
        var list = new List<XElement>();
        while (reader.Read()) list.Add(XElement.Parse(reader.GetString(0)));
        return list;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var cmd = db.CreateCommand("INSERT INTO data_protection_keys (friendly_name, xml) VALUES ($1, $2)");
        cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = friendlyName });
        cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = element.ToString(SaveOptions.DisableFormatting) });
        cmd.ExecuteNonQuery();
    }
}
