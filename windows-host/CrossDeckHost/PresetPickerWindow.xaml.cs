using System.Windows;

namespace CrossDeckHost;

public partial class PresetPickerWindow : Window
{
    public string SelectedPreset { get; private set; } = "Blank";

    public PresetPickerWindow()
    {
        InitializeComponent();
    }

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
