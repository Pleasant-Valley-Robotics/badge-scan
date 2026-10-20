using BadgeScan.Web.Services;

namespace BadgeScan.Web.Tests;

public class BadgeNumberTests
{
    [Theory]
    [InlineData("0000000027", "27")]
    [InlineData(" 27 ", "27")]
    [InlineData("000", "0")]
    [InlineData("A0012", "A0012")]
    public void Leading_zeros_go(string raw, string expected) => Assert.Equal(expected, BadgeNumber.Normalize(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Nothing_is_null(string? raw) => Assert.Null(BadgeNumber.Normalize(raw));

    [Fact]
    public void Only_letters_and_digits_are_valid()
    {
        Assert.True(BadgeNumber.IsValid("27"));
        Assert.False(BadgeNumber.IsValid("27;drop"));
    }
}

public class CentralTimeTests
{
    [Fact]
    public void Summer_is_CDT()
    {
        var parts = CentralTime.From(new DateTimeOffset(2026, 10, 8, 22, 17, 0, TimeSpan.Zero));

        Assert.Equal("Th10/08/2026|17:17", parts.Stamp);
        Assert.Equal("CDT", parts.ZoneAbbreviation);
        Assert.Equal(new DateOnly(2026, 10, 8), parts.Date);
        Assert.Equal(new DateTime(2026, 10, 8, 17, 17, 0), parts.Wall);
    }

    [Fact]
    public void Winter_is_CST_and_the_date_can_roll_back()
    {
        var parts = CentralTime.From(new DateTimeOffset(2026, 1, 15, 0, 30, 0, TimeSpan.Zero));

        Assert.Equal("We01/14/2026|18:30", parts.Stamp);
        Assert.Equal("CST", parts.ZoneAbbreviation);
        Assert.Equal(new DateOnly(2026, 1, 14), parts.Date);
    }

    [Fact]
    public void A_stamp_round_trips()
    {
        var instant = new DateTimeOffset(2026, 10, 8, 22, 17, 0, TimeSpan.Zero);
        Assert.True(CentralTime.TryParseStamp(CentralTime.From(instant).Stamp, out var parsed));
        Assert.Equal(instant, parsed);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("Th10/08/2026")]
    [InlineData("")]
    public void A_bad_stamp_is_refused(string stamp) => Assert.False(CentralTime.TryParseStamp(stamp, out _));
}

public class CsvTests
{
    [Fact]
    public void Quotes_commas_and_line_endings()
    {
        var rows = Csv.Parse("name,team,badge\r\n\"D., Jane\",7196,\"27\"\n\"Say \"\"hi\"\"\",,\n");

        Assert.Equal(3, rows.Count);
        Assert.Equal(["name", "team", "badge"], rows[0]);
        Assert.Equal(["D., Jane", "7196", "27"], rows[1]);
        Assert.Equal(["Say \"hi\"", "", ""], rows[2]);
    }

    [Fact]
    public void Fields_that_need_quotes_get_them()
    {
        Assert.Equal("a,\"b,c\",\"say \"\"hi\"\"\",", Csv.Line("a", "b,c", "say \"hi\"", null));
    }
}
