using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BetterBarApp.Pages;

/// <summary>
/// Shared behaviour for a settings page's scroll area: remembering the scroll position per page so
/// navigating back lands where it was left, and making the scroller keyboard-focusable / key-aware so
/// PageUp / PageDown scroll it without a mouse wheel.
/// </summary>
internal static class PageScrolling
{
    private static readonly Dictionary<string, double> _offsets = new();

    public static void Attach(ScrollViewer sv, string key)
    {
        sv.Focusable        = true;
        sv.FocusVisualStyle = null;   // don't draw a focus rectangle around the whole page

        // Scroll when PageUp/PageDown reach this scroller (it's focused, or a focused child bubbled the
        // key up to it). Bails if the window-level handler already did it.
        sv.PreviewKeyDown += (_, e) =>
        {
            if (e.Handled || e.Key is not (Key.PageUp or Key.PageDown) || sv.ScrollableHeight <= 0) return;
            if (e.Key == Key.PageDown) sv.PageDown(); else sv.PageUp();
            e.Handled = true;
        };

        sv.Loaded += (_, _) =>
        {
            // Give the scroller keyboard focus so the keys route here. Deferred to Input priority so we
            // win the focus after the NavigationView's own post-navigation focus handling — otherwise
            // the page can end up with no focused element and key events never route at all.
            sv.Dispatcher.BeginInvoke(new Action(() => { if (sv.IsVisible) sv.Focus(); }),
                                      DispatcherPriority.Input);

            if (_offsets.TryGetValue(key, out var o))
                sv.Dispatcher.BeginInvoke(new Action(() => sv.ScrollToVerticalOffset(o)),
                                          DispatcherPriority.Background);
        };
        sv.Unloaded += (_, _) => _offsets[key] = sv.VerticalOffset;
    }
}
