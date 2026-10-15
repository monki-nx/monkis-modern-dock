namespace CedroModernDock.Tests;

using System.Text;
using CedroModernDock.Infrastructure.Windows.Adapters;
using static CedroModernDock.Infrastructure.Windows.Adapters.NotificationBadgeReader;

public class NotificationBadgeReaderTest
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static byte[] Payload(string xml) => Encoding.UTF8.GetBytes(xml);

    [Fact]
    public void TheLatestBadgeOfEachAppWins()
    {
        var badges = ParseBadges(
        [
            new BadgeRow("Mail!App", Payload("<badge value=\"2\"/>"), 0),
            new BadgeRow("Chat!App", Payload("<badge value=\"newMessage\"/>"), 0),
            new BadgeRow("Mail!App", Payload("<badge value=\"5\"/>"), 0),
        ], Now);

        Assert.Equal(2, badges.Count);
        Assert.Equal(5, badges["mail!app"].Count);
        Assert.Equal("newMessage", badges["Chat!App"].Glyph);
    }

    [Fact]
    public void AClearedLatestBadgeRemovesTheApp()
    {
        var badges = ParseBadges(
        [
            new BadgeRow("Mail!App", Payload("<badge value=\"2\"/>"), 0),
            new BadgeRow("Mail!App", Payload("<badge value=\"0\"/>"), 0),
            new BadgeRow("Chat!App", Payload("<badge value=\"none\"/>"), 0),
        ], Now);

        Assert.Empty(badges);
    }

    [Fact]
    public void ExpiredBadgesAreIgnored()
    {
        long past = Now.AddMinutes(-1).ToFileTimeUtc();
        long future = Now.AddMinutes(1).ToFileTimeUtc();

        var badges = ParseBadges(
        [
            new BadgeRow("Old!App", Payload("<badge value=\"3\"/>"), past),
            new BadgeRow("Fresh!App", Payload("<badge value=\"4\"/>"), future),
        ], Now);

        Assert.False(badges.ContainsKey("Old!App"));
        Assert.Equal(4, badges["Fresh!App"].Count);
    }

    [Fact]
    public void MalformedPayloadsAndRowsWithoutAppAreIgnored()
    {
        var badges = ParseBadges(
        [
            new BadgeRow("Broken!App", Payload("<badge value="), 0),
            new BadgeRow("Tile!App", Payload("<tile><visual/></tile>"), 0),
            new BadgeRow("", Payload("<badge value=\"1\"/>"), 0),
            new BadgeRow("Empty!App", [], 0),
        ], Now);

        Assert.Empty(badges);
    }

    [Fact]
    public void AMissingDatabaseYieldsNoBadges()
    {
        using var reader = new NotificationBadgeReader(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "wpndatabase.db"));

        Assert.Empty(reader.Current);
    }
}
