using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CrossDeckHost.ProfileStore;
using Microsoft.Win32;
using System.Windows.Input;

namespace CrossDeckHost;

public partial class EditorWindow : Window
{
    private readonly ProfileStoreService _profileStore;
    private int _selectedRow = -1;
    private int _selectedCol = -1;
    private string? _currentFolderId = null;
    private readonly System.Collections.Generic.Stack<(string Id, string Label)> _folderHistory = new();

    private bool _updatingSelector = false;

    public EditorWindow(ProfileStoreService profileStore)
    {
        InitializeComponent();
        _profileStore = profileStore;

        // Listen for changes from phone or other sources to keep grid in sync
        _profileStore.ProfileChanged += OnProfileChangedOnThread;
        _profileStore.ProfileSetChanged += OnProfileSetChangedOnThread;

        Loaded += (s, e) =>
        {
            RefreshProfileSelector();
            RefreshGrid();
        };
        Closed += (s, e) =>
        {
            _profileStore.ProfileChanged -= OnProfileChangedOnThread;
            _profileStore.ProfileSetChanged -= OnProfileSetChangedOnThread;
        };
    }

    private void OnProfileChangedOnThread(Profile profile)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshProfileSelector();
            RefreshGrid();
        }));
    }

    private void OnProfileSetChangedOnThread(ProfileSet set)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshProfileSelector();
            RefreshGrid();
        }));
    }

    private void RefreshProfileSelector()
    {
        _updatingSelector = true;
        try
        {
            ProfileSelectorCombo.ItemsSource = null;
            ProfileSelectorCombo.ItemsSource = _profileStore.Set.Profiles;
            ProfileSelectorCombo.SelectedValue = _profileStore.Set.ActiveProfileId;

            // Update associated process textbox
            TriggerProcessTxt.Text = _profileStore.Current.TriggerProcess ?? "";

            // Cannot delete if it is the only profile left
            DeleteProfileButton.IsEnabled = _profileStore.Set.Profiles.Count > 1;
        }
        finally
        {
            _updatingSelector = false;
        }
    }

    private void ProfileSelectorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelector) return;
        var selectedId = ProfileSelectorCombo.SelectedValue as string;
        if (selectedId != null)
        {
            _currentFolderId = null;
            _folderHistory.Clear();
            _profileStore.SwitchProfile(selectedId);
            EditPanel.Visibility = Visibility.Collapsed;
            PlaceholderText.Visibility = Visibility.Visible;
            _selectedRow = -1;
            _selectedCol = -1;
        }
    }

    private void SaveTriggerProcess()
    {
        var activeProfile = _profileStore.Current;
        var text = TriggerProcessTxt.Text.Trim();
        activeProfile.TriggerProcess = string.IsNullOrEmpty(text) ? null : text;
        _profileStore.Save();
        _profileStore.NotifyChanged();
    }

    private void TriggerProcessTxt_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveTriggerProcess();
    }

    private void TriggerProcessTxt_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveTriggerProcess();
            Keyboard.ClearFocus();
        }
    }

    private void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "New Profile",
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.LightGray
        };

        var stack = new StackPanel { Margin = new Thickness(12) };
        stack.Children.Add(new TextBlock { Text = "Profile Name:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        var input = new System.Windows.Controls.TextBox { Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 12) };
        stack.Children.Add(input);

        var btnStack = new StackPanel 
        { 
            Orientation = System.Windows.Controls.Orientation.Horizontal, 
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right 
        };
        var okBtn = new System.Windows.Controls.Button { Content = "Create", Width = 70, Padding = new Thickness(4), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel", Width = 70, Padding = new Thickness(4), IsCancel = true };
        btnStack.Children.Add(okBtn);
        btnStack.Children.Add(cancelBtn);
        stack.Children.Add(btnStack);

        dialog.Content = stack;

        okBtn.Click += (s, ev) =>
        {
            var name = input.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                System.Windows.MessageBox.Show("Profile name cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _currentFolderId = null;
            _folderHistory.Clear();
            _profileStore.CreateProfile(name);
            dialog.DialogResult = true;
            dialog.Close();
        };

        dialog.ShowDialog();
    }

    private void RenameProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var activeId = _profileStore.Set.ActiveProfileId;
        var dialog = new Window
        {
            Title = "Rename Profile",
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.LightGray
        };

        var stack = new StackPanel { Margin = new Thickness(12) };
        stack.Children.Add(new TextBlock { Text = "New Profile Name:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        var input = new System.Windows.Controls.TextBox { Text = _profileStore.Current.Name, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 12) };
        stack.Children.Add(input);

        var btnStack = new StackPanel 
        { 
            Orientation = System.Windows.Controls.Orientation.Horizontal, 
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right 
        };
        var okBtn = new System.Windows.Controls.Button { Content = "Rename", Width = 70, Padding = new Thickness(4), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel", Width = 70, Padding = new Thickness(4), IsCancel = true };
        btnStack.Children.Add(okBtn);
        btnStack.Children.Add(cancelBtn);
        stack.Children.Add(btnStack);

        dialog.Content = stack;

        okBtn.Click += (s, ev) =>
        {
            var name = input.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                System.Windows.MessageBox.Show("Profile name cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _profileStore.RenameProfile(activeId, name);
            dialog.DialogResult = true;
            dialog.Close();
        };

        dialog.ShowDialog();
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var activeId = _profileStore.Set.ActiveProfileId;
        if (_profileStore.Set.Profiles.Count <= 1) return;

        var result = System.Windows.MessageBox.Show(
            $"Are you sure you want to delete the profile '{_profileStore.Current.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _currentFolderId = null;
            _folderHistory.Clear();
            _profileStore.DeleteProfile(activeId);
            EditPanel.Visibility = Visibility.Collapsed;
            PlaceholderText.Visibility = Visibility.Visible;
            _selectedRow = -1;
            _selectedCol = -1;
        }
    }

    private void AddPresetProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new PresetPickerWindow();
        picker.Owner = this;
        if (picker.ShowDialog() == true)
        {
            var preset = picker.SelectedPreset;
            var name = $"{preset} Preset";
            
            // Resolve duplicate names by adding index if needed
            int index = 1;
            while (_profileStore.Set.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{preset} Preset {index++}";
            }

            _currentFolderId = null;
            _folderHistory.Clear();
            _profileStore.CreateProfileFromPreset(name, preset);
            EditPanel.Visibility = Visibility.Collapsed;
            PlaceholderText.Visibility = Visibility.Visible;
            _selectedRow = -1;
            _selectedCol = -1;
        }
    }

    private void RefreshGrid()
    {
        ButtonGrid.Children.Clear();

        if (_currentFolderId == null)
        {
            BreadcrumbText.Text = "Location: Root";
            BackButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            var folderHierarchyNames = _folderHistory.Select(h => h.Label).Reverse().ToList();
            string folderPath = string.Join(" ➔ ", folderHierarchyNames);
            BreadcrumbText.Text = $"Location: Root ➔ {folderPath}";
            BackButton.Visibility = Visibility.Visible;
        }

        var buttons = _profileStore.Current.Buttons
            .Where(b => b.ParentFolderId == _currentFolderId)
            .ToDictionary(b => (b.Position.Row, b.Position.Col));

        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                var btn = new System.Windows.Controls.Button
                {
                    Margin = new Thickness(4),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold
                };

                int row = r;
                int col = c;

                if (buttons.TryGetValue((row, col), out var buttonModel))
                {
                    btn.Content = $"{buttonModel.Label}\n({buttonModel.Action.Type})";
                    btn.Background = System.Windows.Media.Brushes.LightBlue;
                }
                else
                {
                    btn.Content = "+";
                    btn.Background = System.Windows.Media.Brushes.White;
                }

                btn.Click += (s, e) => SelectCell(row, col);
                ButtonGrid.Children.Add(btn);
            }
        }
    }

    private void SelectCell(int row, int col)
    {
        _selectedRow = row;
        _selectedCol = col;

        PositionText.Text = $"Row: {row}, Col: {col}";
        PlaceholderText.Visibility = Visibility.Collapsed;
        EditPanel.Visibility = Visibility.Visible;

        var buttonModel = _profileStore.Current.Buttons
            .FirstOrDefault(b => b.Position.Row == row && b.Position.Col == col && b.ParentFolderId == _currentFolderId);

        if (buttonModel != null)
        {
            LabelInput.Text = buttonModel.Label;
            string type = buttonModel.Action.Type;
            int typeIdx = type switch
            {
                "hotkey" => 0,
                "launch_app" => 1,
                "media_control" => 2,
                "open_url" => 3,
                "run_command" => 4,
                "text_snippet" => 5,
                "open_folder" => 6,
                "multi_action" => 7,
                "dial" => 8,
                _ => 0
            };
            ActionTypeCombo.SelectedIndex = typeIdx;
            HotkeyInput.Text = buttonModel.Action.Keys != null ? string.Join(",", buttonModel.Action.Keys) : "";
            PathInput.Text = buttonModel.Action.Path ?? "";
            
            string mediaCmd = buttonModel.Action.MediaCommand ?? "PlayPause";
            foreach (ComboBoxItem item in MediaCommandCombo.Items)
            {
                if (item.Content.ToString() == mediaCmd)
                {
                    MediaCommandCombo.SelectedItem = item;
                    break;
                }
            }
            UrlInput.Text = buttonModel.Action.Url ?? "";
            CommandInput.Text = buttonModel.Action.Command ?? "";
            SnippetTextInput.Text = buttonModel.Action.Text ?? "";
            TargetFolderIdInput.Text = buttonModel.Action.TargetFolderId ?? "";
            MultiActionInput.Text = FormatMultiAction(buttonModel.Action.Actions, buttonModel.Action.Delays);
            
            string dialTgt = buttonModel.Action.DialTarget ?? "volume";
            foreach (ComboBoxItem item in DialTargetCombo.Items)
            {
                if (item.Content.ToString() == dialTgt)
                {
                    DialTargetCombo.SelectedItem = item;
                    break;
                }
            }
        }
        else
        {
            LabelInput.Text = "";
            ActionTypeCombo.SelectedIndex = 0;
            HotkeyInput.Text = "";
            PathInput.Text = "";
            MediaCommandCombo.SelectedIndex = 0;
            UrlInput.Text = "";
            CommandInput.Text = "";
            SnippetTextInput.Text = "";
            TargetFolderIdInput.Text = "";
            MultiActionInput.Text = "";
            DialTargetCombo.SelectedIndex = 0;
        }
    }

    private void ActionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HotkeyPanel == null || LaunchAppPanel == null || MediaControlPanel == null || OpenUrlPanel == null || RunCommandPanel == null || TextSnippetPanel == null || FolderPanel == null || MultiActionPanel == null || DialPanel == null) return;

        HotkeyPanel.Visibility = Visibility.Collapsed;
        LaunchAppPanel.Visibility = Visibility.Collapsed;
        MediaControlPanel.Visibility = Visibility.Collapsed;
        OpenUrlPanel.Visibility = Visibility.Collapsed;
        RunCommandPanel.Visibility = Visibility.Collapsed;
        TextSnippetPanel.Visibility = Visibility.Collapsed;
        FolderPanel.Visibility = Visibility.Collapsed;
        MultiActionPanel.Visibility = Visibility.Collapsed;
        DialPanel.Visibility = Visibility.Collapsed;

        switch (ActionTypeCombo.SelectedIndex)
        {
            case 0: HotkeyPanel.Visibility = Visibility.Visible; break;
            case 1: LaunchAppPanel.Visibility = Visibility.Visible; break;
            case 2: MediaControlPanel.Visibility = Visibility.Visible; break;
            case 3: OpenUrlPanel.Visibility = Visibility.Visible; break;
            case 4: RunCommandPanel.Visibility = Visibility.Visible; break;
            case 5: TextSnippetPanel.Visibility = Visibility.Visible; break;
            case 6: FolderPanel.Visibility = Visibility.Visible; break;
            case 7: MultiActionPanel.Visibility = Visibility.Visible; break;
            case 8: DialPanel.Visibility = Visibility.Visible; break;
        }
    }

    private void BrowseAppButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            PathInput.Text = dialog.FileName;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow == -1 || _selectedCol == -1) return;

        var label = LabelInput.Text.Trim();
        if (string.IsNullOrEmpty(label))
        {
            System.Windows.MessageBox.Show("Please enter a label for the button.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var buttonModel = _profileStore.Current.Buttons
            .FirstOrDefault(b => b.Position.Row == _selectedRow && b.Position.Col == _selectedCol && b.ParentFolderId == _currentFolderId);

        var actionType = (ActionTypeCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "hotkey";

        var action = new ActionModel { Type = actionType };

        switch (actionType)
        {
            case "hotkey":
                var keysStr = HotkeyInput.Text.Trim();
                if (string.IsNullOrEmpty(keysStr))
                {
                    System.Windows.MessageBox.Show("Please enter at least one key name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                action.Keys = keysStr.Split(',').Select(k => k.Trim()).ToList();
                break;
            case "launch_app":
                var path = PathInput.Text.Trim();
                if (string.IsNullOrEmpty(path))
                {
                    System.Windows.MessageBox.Show("Please enter or browse a path.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                action.Path = path;
                break;
            case "media_control":
                action.MediaCommand = (MediaCommandCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "PlayPause";
                break;
            case "open_url":
                var url = UrlInput.Text.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    System.Windows.MessageBox.Show("Please enter a URL.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                action.Url = url;
                break;
            case "run_command":
                var cmd = CommandInput.Text.Trim();
                if (string.IsNullOrEmpty(cmd))
                {
                    System.Windows.MessageBox.Show("Please enter a command.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                action.Command = cmd;
                break;
            case "text_snippet":
                var text = SnippetTextInput.Text;
                if (string.IsNullOrEmpty(text))
                {
                    System.Windows.MessageBox.Show("Please enter a text snippet.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                action.Text = text;
                break;
            case "open_folder":
                var fId = TargetFolderIdInput.Text.Trim();
                if (string.IsNullOrEmpty(fId))
                {
                    fId = $"f_{Guid.NewGuid().ToString().Substring(0, 8)}";
                }
                action.TargetFolderId = fId;
                break;
            case "multi_action":
                var sequenceText = MultiActionInput.Text;
                var (subActions, delays, errorMsg) = ParseMultiAction(sequenceText);
                if (errorMsg != null)
                {
                    System.Windows.MessageBox.Show(errorMsg, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                action.Actions = subActions;
                action.Delays = delays;
                break;
            case "dial":
                action.DialTarget = (DialTargetCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "volume";
                break;
        }

        var newButton = new ButtonModel
        {
            ButtonId = buttonModel?.ButtonId ?? $"b_{Guid.NewGuid().ToString().Substring(0, 8)}",
            Position = new Position { Row = _selectedRow, Col = _selectedCol },
            Label = label,
            Action = action,
            ParentFolderId = _currentFolderId
        };

        try
        {
            _profileStore.UpdateButton(_profileStore.Set.ActiveProfileId, newButton);
            RefreshGrid();
            ShowStatus("✓ Saved — syncing to phone…", success: true);
        }
        catch (Exception ex)
        {
            ShowStatus($"✗ Save failed: {ex.Message}", success: false);
        }
    }

    /// <summary>
    /// Flashes a success (green) or error (red) message in SaveStatusText for 2 seconds.
    /// </summary>
    private void ShowStatus(string message, bool success)
    {
        SaveStatusText.Text = message;
        SaveStatusText.Foreground = success
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(27, 128, 62))   // dark green
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(198, 40, 40));  // dark red
        SaveStatusText.Visibility = Visibility.Visible;

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (_, _) =>
        {
            SaveStatusText.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow == -1 || _selectedCol == -1) return;

        var buttonModel = _profileStore.Current.Buttons
            .FirstOrDefault(b => b.Position.Row == _selectedRow && b.Position.Col == _selectedCol && b.ParentFolderId == _currentFolderId);

        if (buttonModel != null)
        {
            try
            {
                _profileStore.DeleteButton(_profileStore.Set.ActiveProfileId, buttonModel.ButtonId);
                RefreshGrid();
                ShowStatus("✓ Button deleted.", success: true);
            }
            catch (Exception ex)
            {
                ShowStatus($"✗ Delete failed: {ex.Message}", success: false);
                return;
            }
        }

        EditPanel.Visibility = Visibility.Collapsed;
        PlaceholderText.Visibility = Visibility.Visible;
        _selectedRow = -1;
        _selectedCol = -1;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_folderHistory.Count > 0)
        {
            _folderHistory.Pop();
            _currentFolderId = _folderHistory.Count > 0 ? _folderHistory.Peek().Id : null;
            EditPanel.Visibility = Visibility.Collapsed;
            PlaceholderText.Visibility = Visibility.Visible;
            _selectedRow = -1;
            _selectedCol = -1;
            RefreshGrid();
        }
    }

    private void EnterFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow == -1 || _selectedCol == -1) return;

        var buttonModel = _profileStore.Current.Buttons
            .FirstOrDefault(b => b.Position.Row == _selectedRow && b.Position.Col == _selectedCol && b.ParentFolderId == _currentFolderId);

        if (buttonModel != null && buttonModel.Action.Type == "open_folder" && !string.IsNullOrEmpty(buttonModel.Action.TargetFolderId))
        {
            _currentFolderId = buttonModel.Action.TargetFolderId;
            _folderHistory.Push((buttonModel.Action.TargetFolderId, buttonModel.Label));
            EditPanel.Visibility = Visibility.Collapsed;
            PlaceholderText.Visibility = Visibility.Visible;
            _selectedRow = -1;
            _selectedCol = -1;
            RefreshGrid();
        }
    }

    private (System.Collections.Generic.List<ActionModel>? Actions, System.Collections.Generic.List<int>? Delays, string? Error) ParseMultiAction(string text)
    {
        var actions = new System.Collections.Generic.List<ActionModel>();
        var delays = new System.Collections.Generic.List<int>();
        var lines = text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            
            int colonIdx = trimmed.IndexOf(':');
            if (colonIdx <= 0)
                return (null, null, $"Invalid line format (missing ':'): '{trimmed}'");
                
            var key = trimmed.Substring(0, colonIdx).Trim().ToLower();
            var val = trimmed.Substring(colonIdx + 1).Trim();
            
            if (key == "delay")
            {
                if (!int.TryParse(val, out var ms) || ms < 0)
                    return (null, null, $"Invalid delay value '{val}' (must be a positive integer)");
                
                if (actions.Count > 0)
                {
                    while (delays.Count < actions.Count)
                    {
                        delays.Add(0);
                    }
                    delays[actions.Count - 1] = ms;
                }
                else
                {
                    return (null, null, "Delay must follow an action");
                }
            }
            else
            {
                var sub = new ActionModel();
                switch (key)
                {
                    case "hotkey":
                        sub.Type = "hotkey";
                        sub.Keys = val.Split(',').Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k)).ToList();
                        break;
                    case "launch_app":
                        sub.Type = "launch_app";
                        sub.Path = val;
                        break;
                    case "media_control":
                        sub.Type = "media_control";
                        sub.MediaCommand = val;
                        break;
                    case "open_url":
                        sub.Type = "open_url";
                        sub.Url = val;
                        break;
                    case "run_command":
                        sub.Type = "run_command";
                        sub.Command = val;
                        break;
                    case "text_snippet":
                        sub.Type = "text_snippet";
                        sub.Text = val;
                        break;
                    default:
                        return (null, null, $"Unknown action type '{key}' in multi-action");
                }
                actions.Add(sub);
            }
        }
        
        while (delays.Count < actions.Count)
        {
            delays.Add(0);
        }
        
        return (actions, delays, null);
    }

    private string FormatMultiAction(System.Collections.Generic.List<ActionModel>? actions, System.Collections.Generic.List<int>? delays)
    {
        if (actions == null) return "";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < actions.Count; i++)
        {
            var act = actions[i];
            switch (act.Type)
            {
                case "hotkey":
                    sb.AppendLine($"hotkey: {string.Join(",", act.Keys ?? new())}");
                    break;
                case "launch_app":
                    sb.AppendLine($"launch_app: {act.Path}");
                    break;
                case "media_control":
                    sb.AppendLine($"media_control: {act.MediaCommand}");
                    break;
                case "open_url":
                    sb.AppendLine($"open_url: {act.Url}");
                    break;
                case "run_command":
                    sb.AppendLine($"run_command: {act.Command}");
                    break;
                case "text_snippet":
                    sb.AppendLine($"text_snippet: {act.Text}");
                    break;
            }
            if (delays != null && i < delays.Count && delays[i] > 0)
            {
                sb.AppendLine($"delay: {delays[i]}");
            }
        }
        return sb.ToString();
    }
}
