namespace CedroModernDock.Tests;

using CedroModernDock.Core.Application;
using CedroModernDock.Core.Models;

public class JumpListServiceTest
{
    private static JumpListEntry Folder(string name) =>
        new(name, @"C:\code\code.exe", $"--folder-uri \"file:///c%3A/{name}\"");

    private static JumpListEntry Document(string name) =>
        new(name, $@"C:\docs\{name}.md", IsLink: false);

    private static JumpListCategory Known(JumpListCategoryKind kind) =>
        new(kind, "", Array.Empty<JumpListEntry>());

    [Fact]
    public void ArrangesLikeTheTaskbarWithTasksLast()
    {
        // VS Code stores Tasks first, then "Recent Folders", then the known Recent category.
        var source = new JumpListSource(true,
            new[]
            {
                new JumpListCategory(JumpListCategoryKind.Tasks, "", new[] { Folder("new-window") }),
                new JumpListCategory(JumpListCategoryKind.Custom, "Recent Folders", new[] { Folder("buk"), Folder("cv") }),
                Known(JumpListCategoryKind.Recent)
            },
            Recent: new[] { Document("a"), Document("b") },
            Frequent: Array.Empty<JumpListEntry>());

        var arranged = JumpListService.Arrange(source, maxItems: 10);

        Assert.Equal(
            new[] { JumpListCategoryKind.Custom, JumpListCategoryKind.Recent, JumpListCategoryKind.Tasks },
            arranged.Select(c => c.Kind));
        Assert.Equal("Recent Folders", arranged[0].Title);
        Assert.Equal(new[] { "buk", "cv" }, arranged[0].Entries.Select(e => e.Title));
    }

    [Fact]
    public void KnownCategoriesFillTheSlotsLeftByCustomCategories()
    {
        var source = new JumpListSource(true,
            new[]
            {
                new JumpListCategory(JumpListCategoryKind.Custom, "Recent Folders",
                    new[] { Folder("a"), Folder("b"), Folder("c") }),
                Known(JumpListCategoryKind.Recent)
            },
            Recent: new[] { Document("1"), Document("2"), Document("3"), Document("4") },
            Frequent: Array.Empty<JumpListEntry>());

        var arranged = JumpListService.Arrange(source, maxItems: 5);

        Assert.Equal(new[] { "1", "2" }, arranged[1].Entries.Select(e => e.Title));
    }

    [Fact]
    public void AKnownCategoryShowsAtLeastOneDestinationWhenCustomCategoriesUseEverySlot()
    {
        // Matches the taskbar: 10 recent folders plus a single Recent document.
        var folders = Enumerable.Range(0, 10).Select(i => Folder($"f{i}")).ToArray();
        var source = new JumpListSource(true,
            new[] { new JumpListCategory(JumpListCategoryKind.Custom, "Recent Folders", folders), Known(JumpListCategoryKind.Recent) },
            Recent: new[] { Document("only"), Document("hidden") },
            Frequent: Array.Empty<JumpListEntry>());

        var arranged = JumpListService.Arrange(source, maxItems: 10);

        Assert.Equal(new[] { "only" }, arranged[1].Entries.Select(e => e.Title));
    }

    [Fact]
    public void KnownCategoriesSkipDestinationsAlreadyShown()
    {
        var shared = Document("shared");
        var source = new JumpListSource(true,
            new[]
            {
                new JumpListCategory(JumpListCategoryKind.Custom, "Pinned docs", new[] { shared }),
                Known(JumpListCategoryKind.Recent)
            },
            Recent: new[] { shared with { Title = "same file, other title" }, Document("other") },
            Frequent: Array.Empty<JumpListEntry>());

        var arranged = JumpListService.Arrange(source, maxItems: 10);

        Assert.Equal(new[] { "other" }, arranged[1].Entries.Select(e => e.Title));
    }

    [Fact]
    public void AppsWithoutACustomListGetTheShellRecentList()
    {
        var source = new JumpListSource(false, Array.Empty<JumpListCategory>(),
            Recent: new[] { Document("1"), Document("2") },
            Frequent: new[] { Document("f") });

        var arranged = JumpListService.Arrange(source, maxItems: 10);

        var only = Assert.Single(arranged);
        Assert.Equal(JumpListCategoryKind.Recent, only.Kind);
        Assert.Equal(new[] { "1", "2" }, only.Entries.Select(e => e.Title));
    }

    [Fact]
    public void EmptyCategoriesAreDropped()
    {
        var source = new JumpListSource(true,
            new[]
            {
                new JumpListCategory(JumpListCategoryKind.Custom, "Nothing", Array.Empty<JumpListEntry>()),
                Known(JumpListCategoryKind.Frequent),
                new JumpListCategory(JumpListCategoryKind.Tasks, "", new[] { new JumpListEntry("", "", IsSeparator: true) })
            },
            Recent: Array.Empty<JumpListEntry>(),
            Frequent: Array.Empty<JumpListEntry>());

        Assert.Empty(JumpListService.Arrange(source, maxItems: 10));
        Assert.Empty(JumpListService.Arrange(JumpListSource.Empty, maxItems: 10));
    }
}
