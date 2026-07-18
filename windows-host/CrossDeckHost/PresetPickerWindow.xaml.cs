using System.Windows;

namespace CrossDeckHost;

public partial class PresetPickerWindow : Window
{
    public string SelectedPreset { get; private set; } = "Blank";

    public PresetPickerWindow()
    {
        InitializeComponent();
        // ApplyTheme must run after layout (VisualTreeHelper walk is a no-op pre-layout) — Loaded,
        // not the constructor. See EditorWindow's constructor for the same pattern.
        Loaded += (s, e) => ThemeManager.ApplyTheme(this);
    }

    private void DialogClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Productivity_Click(object sender, RoutedEventArgs e)
    {
        SelectedPreset = "Productivity";
        DialogResult = true;
        Close();
    }

    private void Streaming_Click(object sender, RoutedEventArgs e)
    {
        SelectedPreset = "Streaming";
        DialogResult = true;
        Close();
    }

    private void Blank_Click(object sender, RoutedEventArgs e)
    {
        SelectedPreset = "Blank";
        DialogResult = true;
        Close();
    }
}
