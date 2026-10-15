namespace CedroModernDock.Tests;

using CedroModernDock.Infrastructure.Windows.Adapters;

public class PackagedAppIdTest
{
    private const string SettingsId = "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";

    private static readonly (string AppId, string InstallPath)[] Catalog =
    [
        ("Microsoft.Windows.Explorer", ""),
        (SettingsId, @"C:\Windows\ImmersiveControlPanel"),
        ("5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App",
            @"C:\Program Files\WindowsApps\5319275A.WhatsAppDesktop_2.2639.100.0_x64__cv1g1gvanyjgm"),
    ];

    [Fact]
    public void SettingsResolvesToItsPackagedApp()
    {
        var appIds = AppUserModelIdResolver.AppIdsInstalledAt(
            @"C:\Windows\ImmersiveControlPanel\SystemSettings.exe", Catalog);

        Assert.Equal([SettingsId], appIds);
    }

    [Fact]
    public void AnExeInAPackageSubfolderBelongsToThePackage()
    {
        var appIds = AppUserModelIdResolver.AppIdsInstalledAt(
            @"c:\program files\windowsapps\5319275A.WhatsAppDesktop_2.2639.100.0_x64__cv1g1gvanyjgm\app\WhatsApp.exe",
            Catalog);

        Assert.Equal(["5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App"], appIds);
    }

    [Theory]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData(@"C:\Windows\ImmersiveControlPanelExtra\Tool.exe")]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WindowsApps\wt.exe")]
    public void DesktopProgramsAndLookalikeFoldersAreNotPackaged(string executablePath)
    {
        Assert.Empty(AppUserModelIdResolver.AppIdsInstalledAt(executablePath, Catalog));
    }

    [Fact]
    public void TheDeepestInstallFolderComesFirst()
    {
        (string, string)[] nested =
        [
            ("Outer!App", @"C:\Apps\Outer"),
            ("Inner!App", @"C:\Apps\Outer\Inner\"),
        ];

        Assert.Equal(["Inner!App", "Outer!App"],
            AppUserModelIdResolver.AppIdsInstalledAt(@"C:\Apps\Outer\Inner\app.exe", nested));
    }
}
