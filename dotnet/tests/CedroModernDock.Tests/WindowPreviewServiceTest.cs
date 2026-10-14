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

    private sealed class FakeWindowQueryGateway(params WindowInfo[] windows) : IWindowQueryGateway
    {
        public string? QueriedPath { get; private set; }
        public List<IntPtr> Closed { get; } = new();

        public List<WindowInfo> FindOpenWindows(string? executablePath)
        {
            QueriedPath = executablePath;
            return windows.ToList();
        }

        public List<RunningWindowInfo> FindTaskbarWindows() => new();
        public void Activate(WindowInfo windowInfo) { }
        public void Close(WindowInfo windowInfo) => Closed.Add(windowInfo.Handle);
    }
}
