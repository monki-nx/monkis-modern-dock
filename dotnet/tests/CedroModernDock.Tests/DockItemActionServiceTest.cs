namespace CedroModernDock.Tests;

using CedroModernDock.Core.Application;
using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;

/// <summary>Direct port of DockItemActionServiceTest.java</summary>
public class DockItemActionServiceTest
{
    [Fact]
    public void ExecutesProgramItemsThroughProgramLauncher()
    {
        var capture = new InvocationCapture();
        var service = new DockItemActionService(
            LambdaProgramLauncher(capture),
            LambdaFolderLauncher(capture),
            LambdaWindowsModuleLauncher(capture)
        );

        service.Execute(new DockProgramItemModel("Editor", @"C:\tools\editor.exe"), capture.OpenSettings);

        Assert.Equal("program", capture.Kind);
        Assert.Equal(@"C:\tools\editor.exe", capture.Value);
        Assert.Equal("Editor", capture.Label);
    }

    [Fact]
    public void ExecutesFolderItemsThroughFolderLauncher()
    {
        var capture = new InvocationCapture();
        var service = new DockItemActionService(
            LambdaProgramLauncher(capture),
            LambdaFolderLauncher(capture),
            LambdaWindowsModuleLauncher(capture)
        );

        service.Execute(new DockFolderItemModel("Projects", @"C:\Users\Arthur Rodrigues\Projects"), capture.OpenSettings);

        Assert.Equal("folder", capture.Kind);
        Assert.Equal(@"C:\Users\Arthur Rodrigues\Projects", capture.Value);
        Assert.Equal("Projects", capture.Label);
    }

    [Fact]
    public void ExecutesWindowsModuleItemsThroughWindowsModuleLauncher()
    {
        var capture = new InvocationCapture();
        var service = new DockItemActionService(
            LambdaProgramLauncher(capture),
            LambdaFolderLauncher(capture),
            LambdaWindowsModuleLauncher(capture)
        );

        service.Execute(new DockWindowsModuleItemModel("Control Panel", "ctrlpnl"), capture.OpenSettings);

        Assert.Equal("module", capture.Kind);
        Assert.Equal("ctrlpnl", capture.Value);
        Assert.Equal("Control Panel", capture.Label);
    }

    [Fact]
    public void ExecutesStartMenuModuleThroughWindowsModuleLauncher()
    {
        var capture = new InvocationCapture();
        var service = new DockItemActionService(
            LambdaProgramLauncher(capture),
            LambdaFolderLauncher(capture),
            LambdaWindowsModuleLauncher(capture)
        );

        service.Execute(new DockWindowsModuleItemModel("Start Menu", "start"), capture.OpenSettings);

        Assert.Equal("module", capture.Kind);
        Assert.Equal("start", capture.Value);
        Assert.Equal("Start Menu", capture.Label);
    }

    [Fact]
    public void ExecutesSettingsItemsThroughSettingsAction()
    {
        var capture = new InvocationCapture();
        var service = new DockItemActionService(
            LambdaProgramLauncher(capture),
            LambdaFolderLauncher(capture),
            LambdaWindowsModuleLauncher(capture)
        );

        service.Execute(new DockSettingsItemModel(), capture.OpenSettings);

        Assert.Equal("settings", capture.Kind);
    }

    [Fact]
    public void RunsProgramItemsAsAdministratorThroughProgramLauncher()
    {
        var capture = new InvocationCapture();
        var service = new DockItemActionService(
            LambdaProgramLauncher(capture),
            LambdaFolderLauncher(capture),
            LambdaWindowsModuleLauncher(capture)
        );

        bool handled = service.RunAsAdministrator(new DockProgramItemModel("Editor", @"C:\tools\editor.exe"));

        Assert.True(handled);
        Assert.Equal("elevated", capture.Kind);
        Assert.Equal(@"C:\tools\editor.exe", capture.Value);
        Assert.Equal("Editor", capture.Label);
    }

    [Fact]
    public void DoesNotRunNonProgramItemsAsAdministrator()
    {
        var capture = new InvocationCapture();
        var service = new DockItemActionService(
            LambdaProgramLauncher(capture),
            LambdaFolderLauncher(capture),
            LambdaWindowsModuleLauncher(capture)
        );

        bool handled = service.RunAsAdministrator(new DockWindowsModuleItemModel("Control Panel", "ctrlpnl"));

        Assert.False(handled);
        Assert.Equal("", capture.Kind);
    }

    [Fact]
    public void OpensFileLocationOfProgramAndFolderItems()
    {
        var capture = new InvocationCapture();
        var service = new DockItemActionService(
            LambdaProgramLauncher(capture),
            LambdaFolderLauncher(capture),
            LambdaWindowsModuleLauncher(capture)
        );

        service.OpenFileLocation(new DockProgramItemModel("Editor", @"C:\tools\editor.exe"));
        Assert.Equal("location", capture.Kind);
        Assert.Equal(@"C:\tools\editor.exe", capture.Value);

        service.OpenFileLocation(new DockFolderItemModel("Projects", @"C:\Projects"));
        Assert.Equal("location", capture.Kind);
        Assert.Equal(@"C:\Projects", capture.Value);
    }

    [Fact]
    public void OnlyProgramAndFolderItemsHaveAFileLocation()
    {
        Assert.True(DockItemActionService.HasFileLocation(new DockProgramItemModel("Editor", @"C:\tools\editor.exe")));
        Assert.True(DockItemActionService.HasFileLocation(new DockFolderItemModel("Projects", @"C:\Projects")));
        Assert.False(DockItemActionService.HasFileLocation(new DockWindowsModuleItemModel("Control Panel", "ctrlpnl")));
        Assert.False(DockItemActionService.HasFileLocation(new DockSettingsItemModel()));
    }

    // Small adapter wrappers so lambdas can be passed as interface parameters
    // (C# interfaces are not delegates, unlike Java functional interfaces).
    private static IProgramLauncher LambdaProgramLauncher(InvocationCapture c) =>
        new ProgramLauncherImpl(c.Record);
    private static IFolderLauncher LambdaFolderLauncher(InvocationCapture c) =>
        new FolderLauncherImpl((path, label) => c.Record("folder", path, label));
    private static IWindowsModuleLauncher LambdaWindowsModuleLauncher(InvocationCapture c) =>
        new WindowsModuleLauncherImpl((module, label) => c.Record("module", module, label));

    private sealed class ProgramLauncherImpl(Action<string, string, string> record) : IProgramLauncher
    {
        public bool Launch(string executablePath, string label)
        {
            record("program", executablePath, label);
            return true;
        }

        public bool LaunchAsAdministrator(string executablePath, string label)
        {
            record("elevated", executablePath, label);
            return true;
        }

        public bool OpenFileLocation(string path)
        {
            record("location", path, "");
            return true;
        }
    }
    private sealed class FolderLauncherImpl(Action<string, string> fn) : IFolderLauncher
    {
        public void Launch(string folderPath, string label) => fn(folderPath, label);
    }
    private sealed class WindowsModuleLauncherImpl(Action<string, string> fn) : IWindowsModuleLauncher
    {
        public void Launch(string module, string label) => fn(module, label);
    }

    private sealed class InvocationCapture
    {
        public string Kind { get; private set; } = "";
        public string Value { get; private set; } = "";
        public string Label { get; private set; } = "";

        public void Record(string kind, string value, string label)
        {
            Kind = kind;
            Value = value;
            Label = label;
        }

        public void OpenSettings() => Kind = "settings";
    }
}
