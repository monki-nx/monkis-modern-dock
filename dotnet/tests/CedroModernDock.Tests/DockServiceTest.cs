namespace CedroModernDock.Tests;

using CedroModernDock.Core.Application;
using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;

/// <summary>Direct port of DockServiceTest.java</summary>
public class DockServiceTest
{
    [Fact]
    public void SnapsSavedDockCoordinatesToWholePixels()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);

        service.SetDockPosition(718.5, 28.2);

        DockModel savedDock = repository.SavedModel!;
        Assert.Equal(719.0, savedDock.DockPositionX);
        Assert.Equal(28.0, savedDock.DockPositionY);
    }

    [Fact]
    public void MoveItemMovesAnItemForwardIntoTheGivenGap()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockSettingsItemModel());                     // [0]
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe")); // [1]
        service.AddItem(new DockProgramItemModel("B", @"C:\tools\b.exe")); // [2]
        service.AddItem(new DockProgramItemModel("C", @"C:\tools\c.exe")); // [3]

        // Move index 0 into the gap after item "B" (gap index 3).
        service.MoveItem(0, 3);

        var labels = service.GetItems().Select(i => i.Label).ToList();
        Assert.Equal(new[] { "A", "B", "Settings", "C" }, labels);
        Assert.Same(repository.SavedModel, service.GetDock());
    }

    [Fact]
    public void MoveItemMovesAnItemBackwardIntoTheGivenGap()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe")); // [0]
        service.AddItem(new DockProgramItemModel("B", @"C:\tools\b.exe")); // [1]
        service.AddItem(new DockProgramItemModel("C", @"C:\tools\c.exe")); // [2]
        service.AddItem(new DockSettingsItemModel());                     // [3]

        // Move index 3 into the gap before item "B" (gap index 1).
        service.MoveItem(3, 1);

        var labels = service.GetItems().Select(i => i.Label).ToList();
        Assert.Equal(new[] { "A", "Settings", "B", "C" }, labels);
        Assert.Same(repository.SavedModel, service.GetDock());
    }

    [Fact]
    public void MoveItemToTheSameGapLeavesTheOrderUntouched()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe"));
        service.AddItem(new DockProgramItemModel("B", @"C:\tools\b.exe"));

        service.MoveItem(1, 1);

        var labels = service.GetItems().Select(i => i.Label).ToList();
        Assert.Equal(new[] { "A", "B" }, labels);
    }

    [Fact]
    public void MoveItemClampsAnOutOfRangeGapToTheListBounds()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe")); // [0]
        service.AddItem(new DockProgramItemModel("B", @"C:\tools\b.exe")); // [1]
        service.AddItem(new DockProgramItemModel("C", @"C:\tools\c.exe")); // [2]

        // Gap index way past the end behaves like the last gap (after "C").
        service.MoveItem(0, 99);

        var labels = service.GetItems().Select(i => i.Label).ToList();
        Assert.Equal(new[] { "B", "C", "A" }, labels);
    }

    [Fact]
    public void UnpinItemRemovesThatItemAndSaves()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        var a = new DockProgramItemModel("A", @"C:\tools\a.exe");
        var b = new DockProgramItemModel("B", @"C:\tools\b.exe");
        service.AddItem(a);
        service.AddItem(b);
        service.AddItem(new DockSettingsItemModel());

        bool removed = service.UnpinItem(a);

        Assert.True(removed);
        Assert.Equal(new[] { "B", "Settings" }, service.GetItems().Select(i => i.Label).ToList());
        Assert.Same(repository.SavedModel, service.GetDock());
    }

    [Fact]
    public void UnpinItemNeverRemovesTheSettingsItem()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        var settings = new DockSettingsItemModel();
        service.AddItem(settings);

        Assert.False(service.UnpinItem(settings));
        Assert.Single(service.GetItems());
    }

    [Fact]
    public void UnpinItemIgnoresItemsThatAreNotInTheDock()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe"));

        // Same values, different instance: only the pinned instance is removed.
        Assert.False(service.UnpinItem(new DockProgramItemModel("A", @"C:\tools\a.exe")));
        Assert.Single(service.GetItems());
    }

    [Fact]
    public void PinProgramInsertsBeforeATrailingSettingsItem()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe"));
        service.AddItem(new DockSettingsItemModel());

        bool pinned = service.PinProgram("B", @"C:\tools\b.exe");

        Assert.True(pinned);
        Assert.Equal(new[] { "A", "B", "Settings" }, service.GetItems().Select(i => i.Label).ToList());
        Assert.Same(repository.SavedModel, service.GetDock());
    }

    [Fact]
    public void PinProgramAppendsWhenTheSettingsItemIsNotLast()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockSettingsItemModel());
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe"));

        service.PinProgram("B", @"C:\tools\b.exe");

        Assert.Equal(new[] { "Settings", "A", "B" }, service.GetItems().Select(i => i.Label).ToList());
    }

    [Fact]
    public void PinProgramSkipsAnAlreadyPinnedExecutable()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe"));

        bool pinned = service.PinProgram("A again", @"C:\TOOLS\A.EXE");

        Assert.False(pinned);
        Assert.Single(service.GetItems());
    }

    private sealed class InMemoryDockRepository : IDockRepository
    {
        private readonly DockModel _model = new();
        public DockModel? SavedModel { get; private set; }

        public DockModel Load()
        {
            SavedModel ??= _model;
            return _model;
        }

        public void Save(DockModel model) => SavedModel = model;
    }
}
