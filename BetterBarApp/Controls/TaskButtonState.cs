using System.Windows;

namespace BetterBarApp.Controls;

/// <summary>
/// Attached property used by the themed TaskButton style to drive the
/// "active / focused window" visual state via a trigger.
/// </summary>
public static class TaskButtonState
{
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsActive", typeof(bool), typeof(TaskButtonState),
            new PropertyMetadata(false));

    public static void SetIsActive(DependencyObject d, bool value) => d.SetValue(IsActiveProperty, value);
    public static bool GetIsActive(DependencyObject d) => (bool)d.GetValue(IsActiveProperty);
}
