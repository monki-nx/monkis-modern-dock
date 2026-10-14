namespace CedroModernDock.Core.Application;

using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;

/// <summary>
/// Direct port of DockService. Holds the dock model loaded from the repository
/// and persists changes on every mutating operation.
/// </summary>
public class DockService
{
    private readonly IDockRepository _repository;
    private readonly DockModel _dock;

    public DockService(IDockRepository repository)
    {
        _repository = repository;
        _dock = repository.Load();
    }

    public DockModel GetDock() => _dock;

    public List<DockItem> GetItems() => _dock.Items;

    public void AddItem(DockItem item)
    {
        _dock.AddItem(item);
        SaveChanges();
    }

    public void RemoveItem(int index)
    {
        _dock.RemoveItem(index);
        SaveChanges();
    }

    /// <summary>
    /// Removes the given item instance from the dock (context-menu "Unpin").
    /// The settings item is mandatory and is never removed. Returns true
    /// when the item was found and removed.
    /// </summary>
    public bool UnpinItem(DockItem item)
    {
        if (item is DockSettingsItemModel) return false;
        int index = _dock.Items.IndexOf(item);
        if (index < 0) return false;
        RemoveItem(index);
        return true;
    }

    /// <summary>
    /// Pins a running program to the dock (context-menu "Pin"). The new item
    /// goes before a trailing settings item so the settings button stays
    /// last. Returns false when the executable is already pinned.
    /// </summary>
    public bool PinProgram(string label, string executablePath)
    {
        bool alreadyPinned = _dock.Items.OfType<DockProgramItemModel>()
            .Any(p => string.Equals(p.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
        if (alreadyPinned) return false;

        var item = new DockProgramItemModel(label, executablePath);
        int count = _dock.Items.Count;
        if (count > 0 && _dock.Items[count - 1] is DockSettingsItemModel)
            _dock.Items.Insert(count - 1, item);
        else
            _dock.AddItem(item);
        SaveChanges();
        return true;
    }

    public void SwapItems(int firstItemIdx, int secondItemIdx)
    {
        _dock.SwapItems(firstItemIdx, secondItemIdx);
        SaveChanges();
    }

    public void MoveItem(int fromIndex, int toIndex)
    {
        _dock.MoveItem(fromIndex, toIndex);
        SaveChanges();
    }

    public void SetDockPosition(double positionX, double positionY)
    {
        _dock.SetDockPosition(
            DockPositioningService.SnapToPixel(positionX),
            DockPositioningService.SnapToPixel(positionY));
        SaveChanges();
    }

    public void SaveChanges() => _repository.Save(_dock);
}
