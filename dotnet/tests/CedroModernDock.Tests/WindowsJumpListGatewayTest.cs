namespace CedroModernDock.Tests;

using System.Text;
using CedroModernDock.Core.Models;
using CedroModernDock.Infrastructure.Windows.Adapters;

public class WindowsJumpListGatewayTest
{
    [Theory]
    // File names observed in %APPDATA%\Microsoft\Windows\Recent for these AppUserModelIDs.
    [InlineData("Microsoft.VisualStudioCode", "1ced32d74a95c7bc")]
    [InlineData("Microsoft.Windows.Explorer", "f01b4d95cf55d32a")]
    [InlineData("microsoft.visualstudiocode", "1ced32d74a95c7bc")]
    public void HashesAppIdsLikeTheShellNamesJumpListFiles(string appId, string expected)
    {
        Assert.Equal(expected, WindowsJumpListGateway.AppIdHash(appId));
    }

    [Fact]
    public void ResolvesBareIconFileNamesLikeTheShell()
    {
        // VS Code's recent folders store "explorer.exe,0" without a directory.
        string? resolved = WindowsJumpListGateway.ResolveIconFile("explorer.exe");

        Assert.NotNull(resolved);
        Assert.True(Path.IsPathRooted(resolved));
        Assert.Null(WindowsJumpListGateway.ResolveIconFile(@"C:\does\not\exist.ico"));
        Assert.Null(WindowsJumpListGateway.ResolveIconFile(""));
    }

    [Fact]
    public void MapsPackagedAppDataLogosToThePackageStateFolders()
    {
        string packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");

        Assert.Equal(
            Path.Combine(packages, "Contoso.Chat_abc123", "LocalState", @"ContactPictures\contact_1.png"),
            WindowsJumpListGateway.PackageLogoPath(
                "ms-appdata:///local/ContactPictures/contact_1.png", "Contoso.Chat_abc123!App"));
        Assert.Equal(
            Path.Combine(packages, "Contoso.Chat_abc123", "RoamingState", "a b.png"),
            WindowsJumpListGateway.PackageLogoPath("ms-appdata:///roaming/a%20b.png", "Contoso.Chat_abc123!App"));
        Assert.Equal("", WindowsJumpListGateway.PackageLogoPath("ms-appx:///Assets/logo.png", "Contoso.Chat_abc123!App"));
        Assert.Equal("", WindowsJumpListGateway.PackageLogoPath("", "Contoso.Chat_abc123!App"));
    }

    [Fact]
    public void ReadsTheCategoryFramingOfACustomDestinationsFile()
    {
        var data = new List<byte>();
        void U32(uint v) => data.AddRange(BitConverter.GetBytes(v));

        U32(2); U32(3); U32(0);                      // version, categories, reserved
        U32(0);                                      // custom category ...
        string name = "Recent Folders";
        data.AddRange(BitConverter.GetBytes((ushort)name.Length));
        data.AddRange(Encoding.Unicode.GetBytes(name));
        U32(0);                                      // ... with no entries
        U32(0xBABFFBAB);
        U32(1); U32(2); U32(0xBABFFBAB);             // known category: Recent
        U32(2); U32(0); U32(0xBABFFBAB);             // tasks with no entries

        var categories = WindowsJumpListGateway.ReadCustomDestinations(data.ToArray());

        Assert.Equal(
            new[] { JumpListCategoryKind.Custom, JumpListCategoryKind.Recent, JumpListCategoryKind.Tasks },
            categories.Select(c => c.Kind));
        Assert.Equal("Recent Folders", categories[0].Title);
    }

    [Fact]
    public void StopsAtAnUnknownVersionOrABrokenFooter()
    {
        Assert.Empty(WindowsJumpListGateway.ReadCustomDestinations(new byte[] { 3, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.Empty(WindowsJumpListGateway.ReadCustomDestinations(Array.Empty<byte>()));

        var data = new List<byte>();
        void U32(uint v) => data.AddRange(BitConverter.GetBytes(v));
        U32(2); U32(2); U32(0);
        U32(1); U32(1); U32(0xDEADBEEF);             // Frequent, then a corrupt footer
        U32(1); U32(2); U32(0xBABFFBAB);

        var categories = WindowsJumpListGateway.ReadCustomDestinations(data.ToArray());

        Assert.Equal(new[] { JumpListCategoryKind.Frequent }, categories.Select(c => c.Kind));
    }
}
