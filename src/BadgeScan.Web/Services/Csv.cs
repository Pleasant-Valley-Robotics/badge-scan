using System.Text;

namespace BadgeScan.Web.Services;

/// <summary>Just enough CSV: quoted fields, commas and line breaks inside quotes, CRLF or LF.</summary>
public static class Csv
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var i = 0;
        if (text.Length > 0 && text[0] == '﻿') i = 1;   // a byte-order mark from Excel

        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': quoted = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n': row.Add(field.ToString()); field.Clear(); rows.Add(row.ToArray()); row.Clear(); break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }

    public static string Field(string? value)
    {
        value ??= "";
        return value.AsSpan().IndexOfAny("\",\r\n") >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    public static string Line(params string?[] values) => string.Join(',', values.Select(Field));
}
