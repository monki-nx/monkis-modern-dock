namespace CedroModernDock.Core.Application;

using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;

/// <summary>Direct port of DockItemActionService.</summary>
public class DockItemActionService
{
    private readonly IFolderLauncher _folderLauncher;
    private readonly IProgramLauncher _programLauncher;
    private readonly IWindowsModuleLauncher _windowsModuleLauncher;

    public DockItemActionService(
        IProgramLauncher programLauncher,
        IFolderLauncher folderLauncher,
        IWindowsModuleLauncher windowsModuleLauncher)
    {
        _programLauncher = programLauncher;
        _folderLauncher = folderLauncher;
        _windowsModuleLauncher = windowsModuleLauncher;
    }

    /// <summary>
    /// Executes the dock item action. Returns true when the item was handled
    /// (or launched), false when a program item could not be launched.
    /// </summary>
    public bool Execute(DockItem item, Action openSettingsAction)
    {
        if (item is DockProgramItemModel programItem)
        {
            return _programLauncher.Launch(programItem.ExecutablePath, programItem.Label);
        }

        if (item is DockFolderItemModel folderItem)
        {
            _folderLauncher.Launch(folderItem.FolderPath, folderItem.Label);
            return true;
        }

        if (item is DockWindowsModuleItemModel windowsModuleItem)
        {
            _windowsModuleLauncher.Launch(windowsModuleItem.Module, windowsModuleItem.Label);
            return true;
        }

        if (item is DockSettingsItemModel)
        {
            openSettingsAction();
        }

        return true;
    }

    /// <summary>
    /// Launches a program item elevated. Only program items can be elevated;
    /// any other item type returns false.
    /// </summary>
    public bool RunAsAdministrator(DockItem item)
    {
        if (item is DockProgramItemModel programItem)
            return _programLauncher.LaunchAsAdministrator(programItem.ExecutablePath, programItem.Label);
        return false;
    }

    /// <summary>
    /// Reveals the item's target in Explorer: the executable for program
    /// items, the folder itself for folder items. Other item types have no
    /// file location and return false.
    /// </summary>
    public bool OpenFileLocation(DockItem item)
    {
        return item switch
        {
            DockProgramItemModel programItem => _programLauncher.OpenFileLocation(programItem.ExecutablePath),
            DockFolderItemModel folderItem => _programLauncher.OpenFileLocation(folderItem.FolderPath),
            _ => false
        };
    }

    /// <summary>
    /// True for program items of packaged apps (Windows Settings, Store apps):
    /// like the taskbar, their menu offers neither elevation nor file location.
    /// </summary>
    public bool IsPackagedApp(DockItem item) =>
        item is DockProgramItemModel programItem && _programLauncher.IsPackagedApp(programItem.ExecutablePath);

    /// <summary>True when the item has a file location that Explorer can reveal.</summary>
    public static bool HasFileLocation(DockItem item) =>
        item is DockProgramItemModel or DockFolderItemModel;
}
