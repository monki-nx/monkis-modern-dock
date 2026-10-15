namespace CedroModernDock.Tests;

using CedroModernDock.Core.Application;
using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;

public class TaskbarBadgeServiceTest
{
    private static readonly IntPtr Front = new(10);
    private static readonly IntPtr Back = new(20);

    [Fact]
    public void TheOverlayOfTheTopmostWindowThatHasOneWins()
    {
        var front = new TaskbarBadge(OverlayPng: [1], Description: "3 unread");
        var back = new TaskbarBadge(OverlayPng: [2]);
        var gateway = new FakeBadgeGateway();
        gateway.Overlays[Back] = back;
        gateway.Overlays[Front] = front;
        var service = new TaskbarBadgeService(gateway);

        Assert.Same(front, service.BadgeFor([Front, Back], []));
        Assert.Same(back, service.BadgeFor([new IntPtr(99), Back], []));
    }

    [Fact]
    public void AnOverlayTakesPrecedenceOverTheAppBadge()
    {
        var overlay = new TaskbarBadge(OverlayPng: [1]);
        var gateway = new FakeBadgeGateway();
        gateway.Overlays[Front] = overlay;
        gateway.Badges["Contoso.Mail!App"] = new TaskbarBadge(Count: 4);
        var service = new TaskbarBadgeService(gateway);

        Assert.Same(overlay, service.BadgeFor([Front], ["Contoso.Mail!App"]));
    }

    [Fact]
    public void AppBadgesMatchTheAppUserModelIdIgnoringCase()
    {
        var gateway = new FakeBadgeGateway();
        gateway.Badges["Contoso.Mail_8wekyb3d8bbwe!App"] = new TaskbarBadge(Count: 4);
        var service = new TaskbarBadgeService(gateway);

        Assert.Equal(4, service.BadgeFor([], ["contoso.mail_8wekyb3d8bbwe!app"])?.Count);
    }

    [Fact]
    public void APackageFamilyNameMatchesTheBadgeOfItsApps()
    {
        var gateway = new FakeBadgeGateway();
        gateway.Badges["5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App"] = new TaskbarBadge(Glyph: "newMessage");
        var service = new TaskbarBadgeService(gateway);

        Assert.Equal("newMessage",
            service.BadgeFor([], ["5319275A.WhatsAppDesktop_cv1g1gvanyjgm"])?.Glyph);
        // A longer family that merely starts the same is another package.
        Assert.Null(service.BadgeFor([], ["5319275A.WhatsApp"]));
    }

    [Fact]
    public void WithoutOverlaysOrMatchingBadgesThereIsNoBadge()
    {
        var gateway = new FakeBadgeGateway();
        gateway.Badges["Other.App!App"] = new TaskbarBadge(Count: 1);
        var service = new TaskbarBadgeService(gateway);

        Assert.Null(service.BadgeFor([Front], ["Contoso.Mail!App", ""]));
    }

    [Theory]
    [InlineData("7", 7, null)]
    [InlineData(" 120 ", 120, null)]
    [InlineData("newMessage", null, "newMessage")]
    [InlineData("attention", null, "attention")]
    public void BadgeValuesBecomeCountsOrGlyphs(string value, int? count, string? glyph)
    {
        var badge = TaskbarBadge.FromBadgeValue(value);

        Assert.NotNull(badge);
        Assert.Equal(count, badge.Count);
        Assert.Equal(glyph, badge.Glyph);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("None")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData(null)]
    public void ClearedBadgeValuesMeanNoBadge(string? value)
    {
        Assert.Null(TaskbarBadge.FromBadgeValue(value));
    }

    private sealed class FakeBadgeGateway : ITaskbarBadgeGateway
    {
        public Dictionary<IntPtr, TaskbarBadge> Overlays { get; } = new();
        public Dictionary<string, TaskbarBadge> Badges { get; } = new(StringComparer.OrdinalIgnoreCase);

        public event Action? BadgesChanged { add { } remove { } }

        public void Start() { }
        public void Stop() { }
        public IReadOnlyDictionary<IntPtr, TaskbarBadge> WindowOverlays() => Overlays;
        public IReadOnlyDictionary<string, TaskbarBadge> AppBadges() => Badges;
    }
}
