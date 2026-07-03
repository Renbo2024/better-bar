using System.ComponentModel;
using ManagedShell.Common.Enums;
using ManagedShell.WindowsTasks;

namespace BetterBarApp.Services;

/// <summary>
/// App-wide singleton that owns ManagedShell's window-tracking infrastructure.
/// All TaskButtons controls share one TasksService/Tasks instance to avoid
/// duplicate window hooks.
/// </summary>
public static class TaskbarService
{
    private static TasksService? _tasksService;
    private static Tasks?        _tasks;

    /// <summary>
    /// The process-wide single TasksService. ManagedShell registers a WPF
    /// DependencyProperty in its constructor, so exactly one may ever be created;
    /// everything that needs one (task buttons, the AppBar FullScreenHelper) shares
    /// this. Created on first access; window tracking only starts in
    /// <see cref="EnsureInitialized"/>.
    /// </summary>
    public static TasksService TasksService => _tasksService ??= new TasksService(IconSize.Small);

    /// <summary>
    /// Live, change-notifying view of running windows (ApplicationWindow items).
    /// Null until <see cref="EnsureInitialized"/> runs. Grouping is performed by
    /// consumers via ApplicationWindow.Category rather than relying on the view's
    /// own group descriptions.
    /// </summary>
    public static ICollectionView? Windows { get; private set; }

    public static void EnsureInitialized()
    {
        if (_tasks != null) return;

        _tasks  = new Tasks(TasksService);   // shared single instance
        _tasks.Initialize(withMultiMonTracking: true);
        Windows = _tasks.GroupedWindows;
    }
}
