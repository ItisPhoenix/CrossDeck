using System;
using System.Windows;
using System.Windows.Input;

namespace CrossDeckHost;

/// <summary>Always-on-top Stop/Cancel control shown while a macro is being recorded, so the user
/// doesn't have to alt-tab back to the CrossDeck editor mid-recording. Purely a relay — the caller
/// owns what Stop/Cancel actually do (keep steps vs. discard, confirmation prompts, etc.).</summary>
public partial class MacroRecordingOverlayWindow : Window
{
    public event Action? StopRequested;
    public event Action? CancelRequested;

    public MacroRecordingOverlayWindow()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 16;
            Top = area.Top + 16;
        };
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
}
