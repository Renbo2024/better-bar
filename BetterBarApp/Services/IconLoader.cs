using System.Collections.Concurrent;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BetterBarApp.Services;

/// <summary>
/// Serializes shell-icon extraction onto a single long-lived STA worker thread.
///
/// Why this exists: each launcher icon used to load on its own freshly-spawned STA thread, so at
/// startup dozens of threads hammered the shell's COM image factory at the most contended moment
/// (appbar handshake + staggered control build + tray registration). Under that contention
/// <see cref="ShellIconService.GetIcon"/> intermittently returned null, and the icon was left
/// blank — a "black spot" (the dark bar showing through an empty Image) that only fixed itself when
/// the bar was rebuilt later, once the shell was warm. Funnelling every request through one queue
/// removes the thread storm, and a short retry rides out any remaining transient failure.
/// </summary>
public static class IconLoader
{
    private sealed record Job(string Path, int SizePixels, Dispatcher Dispatcher, Action<BitmapSource> OnLoaded);

    private static readonly BlockingCollection<Job> _queue = new();
    private static readonly Lazy<Thread> _worker = new(StartWorker);

    /// <summary>
    /// Queues an icon load. <paramref name="onLoaded"/> runs on <paramref name="dispatcher"/> once a
    /// (frozen) icon is available; it is never invoked if extraction ultimately fails.
    /// </summary>
    public static void Queue(string path, int sizePixels, Dispatcher dispatcher, Action<BitmapSource> onLoaded)
    {
        _ = _worker.Value;   // ensure the worker is running
        _queue.Add(new Job(path, sizePixels, dispatcher, onLoaded));
    }

    private static Thread StartWorker()
    {
        var t = new Thread(Run) { IsBackground = true, Name = "IconLoader" };
        t.SetApartmentState(ApartmentState.STA);   // shell icon APIs require STA
        t.Start();
        return t;
    }

    private static void Run()
    {
        foreach (var job in _queue.GetConsumingEnumerable())
        {
            var icon = LoadWithRetry(job.Path, job.SizePixels);
            if (icon != null)
                job.Dispatcher.BeginInvoke(() => job.OnLoaded(icon));
        }
    }

    // The shell can briefly fail to produce an icon while it's busy at startup; retry a few times
    // with a short backoff before giving up. Frozen results are safe to hand to the UI thread.
    private static BitmapSource? LoadWithRetry(string path, int sizePixels)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var icon = ShellIconService.GetIcon(path, sizePixels);
                if (icon != null) return icon;
            }
            catch { }
            Thread.Sleep(40 * (attempt + 1));
        }
        return null;
    }
}
