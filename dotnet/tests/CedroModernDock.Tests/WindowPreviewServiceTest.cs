namespace CedroModernDock.Tests;

using CedroModernDock.Core.Application;
using CedroModernDock.Core.Domain;

public class WindowPreviewServiceTest
{
    [Fact]
    public void CloseAllAsksEveryOpenWindowOfTheExecutableToClose()
    {
        var gateway = new FakeWindowQueryGateway(
            new WindowInfo(new IntPtr(1), "First"),
            new WindowInfo(new IntPtr(2), "Second"));
        var service = new WindowPreviewService(gateway);

        int asked = service.CloseAll(@"C:\tools\editor.exe");

        Assert.Equal(2, asked);
        Assert.Equal(@"C:\tools\editor.exe", gateway.QueriedPath);
        Assert.Equal(new[] { new IntPtr(1), new IntPtr(2) }, gateway.Closed);
    }

    [Fact]
    public void CloseAllWithNoOpenWindowsClosesNothing()
    {
        var gateway = new FakeWindowQueryGateway();
        var service = new WindowPreviewService(gateway);

        Assert.Equal(0, service.CloseAll(@"C:\tools\editor.exe"));
        Assert.Empty(gateway.Closed);
    }

    private static readonly WindowInfo First = new(new IntPtr(1), "First");
    private static readonly WindowInfo Second = new(new IntPtr(2), "Second");
    private static readonly WindowInfo Third = new(new IntPtr(3), "Third");

    [Fact]
    public void ClickWithNoOpenWindowReportsNothingToActivate()
    {
        var gateway = new FakeWindowQueryGateway();
        var service = new WindowPreviewService(gateway);

        Assert.False(service.ActivateOrMinimize(new List<WindowInfo>()));
        Assert.Empty(gateway.Actions);
    }

    [Fact]
    public void ClickBringsASingleBackgroundWindowForward()
    {
        var gateway = new FakeWindowQueryGateway { Foreground = new IntPtr(99) };
        var service = new WindowPreviewService(gateway);

        Assert.True(service.ActivateOrMinimize(new[] { First }));
        Assert.Equal(new[] { "activate 1" }, gateway.Actions);
    }

    [Fact]
    public void ClickMinimizesASingleWindowThatIsAlreadyActive()
    {
        var gateway = new FakeWindowQueryGateway { Foreground = First.Handle };
        var service = new WindowPreviewService(gateway);

        service.ActivateOrMinimize(new[] { First });

        Assert.Equal(new[] { "minimize 1" }, gateway.Actions);
    }

    [Fact]
    public void ClickRestoresAMinimizedWindowEvenIfItStillOwnsTheForeground()
    {
        var gateway = new FakeWindowQueryGateway { Foreground = First.Handle };
        gateway.Minimized.Add(First.Handle);
        var service = new WindowPreviewService(gateway);

        service.ActivateOrMinimize(new[] { First });

        Assert.Equal(new[] { "activate 1" }, gateway.Actions);
    }

    [Fact]
    public void ClickWithSeveralWindowsBringsTheMostRecentForward()
    {
        var gateway = new FakeWindowQueryGateway { Foreground = new IntPtr(99) };
        var service = new WindowPreviewService(gateway);

        service.ActivateOrMinimize(new[] { First, Second, Third });

        Assert.Equal(new[] { "activate 1" }, gateway.Actions);
    }

    [Fact]
    public void ClickWithSeveralWindowsCyclesToTheLeastRecentOne()
    {
        // Windows come topmost first: the active one leads, the least recent closes the list.
        var gateway = new FakeWindowQueryGateway { Foreground = First.Handle };
        var service = new WindowPreviewService(gateway);

        service.ActivateOrMinimize(new[] { First, Second, Third });

        Assert.Equal(new[] { "activate 3" }, gateway.Actions);

        gateway.Foreground = Third.Handle;
        service.ActivateOrMinimize(new[] { First, Second, Third });

        Assert.Equal(new[] { "activate 3", "activate 2" }, gateway.Actions);
    }

    [Fact]
    public void RepeatedClicksVisitEveryWindow()
    {
        // Each activation moves that window to the front of the z-order, as
        // the system does; three clicks must reach all three windows instead
        // of alternating between the two most recent ones.
        var gateway = new FakeWindowQueryGateway { Foreground = First.Handle };
        var service = new WindowPreviewService(gateway);

        service.ActivateOrMinimize(new[] { First, Second, Third });
        gateway.Foreground = Third.Handle;
        service.ActivateOrMinimize(new[] { Third, First, Second });
        gateway.Foreground = Second.Handle;
        service.ActivateOrMinimize(new[] { Second, Third, First });

        Assert.Equal(new[] { "activate 3", "activate 2", "activate 1" }, gateway.Actions);
    }

    private sealed class FakeWindowQueryGateway(params WindowInfo[] windows) : IWindowQueryGateway
    {
        public string? QueriedPath { get; private set; }
        public List<IntPtr> Closed { get; } = new();
        public List<string> Actions { get; } = new();
        public HashSet<IntPtr> Minimized { get; } = new();
        public IntPtr Foreground { get; set; }

        public List<WindowInfo> FindOpenWindows(string? executablePath, string? label = null)
        {
            QueriedPath = executablePath;
            return windows.ToList();
        }

        public List<RunningWindowInfo> FindTaskbarWindows() => new();
        public IReadOnlyCollection<string> AppIdsOf(string executablePath, string? label) => Array.Empty<string>();
        public void Activate(WindowInfo windowInfo) => Actions.Add($"activate {windowInfo.Handle}");
        public void Minimize(WindowInfo windowInfo) => Actions.Add($"minimize {windowInfo.Handle}");
        public bool IsMinimized(WindowInfo windowInfo) => Minimized.Contains(windowInfo.Handle);
        public IntPtr GetForegroundWindow() => Foreground;
        public void Close(WindowInfo windowInfo) => Closed.Add(windowInfo.Handle);
    }
}
