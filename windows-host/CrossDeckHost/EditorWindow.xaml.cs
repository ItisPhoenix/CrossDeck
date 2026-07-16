using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CrossDeckHost.ProfileStore;
using Microsoft.Win32;
using System.Windows.Input;

namespace CrossDeckHost;

public partial class EditorWindow : Window
{
    private readonly ProfileStoreService _profileStore;
    private readonly Server.WebSocketServer? _server;
    private string? _currentFolderId = null;
    private readonly System.Collections.Generic.Stack<(string Id, string Label)> _folderHistory = new();

    public EditorWindow(ProfileStoreService profileStore, Server.WebSocketServer? server)
    {
        InitializeComponent();
        _profileStore = profileStore;
        _server = server;

        // Listen for changes from phone or other sources to keep grid in sync
        _profileStore.ProfileChanged += OnProfileChangedOnThread;
        _profileStore.ProfileSetChanged += OnProfileSetChangedOnThread;

        if (_server != null)
        {
            _server.ClientAuthenticated += OnClientConnectionStatusChanged;
            _server.ClientDisconnected += OnClientConnectionStatusChanged;
        }

        Loaded += (s, e) =>
        {
            ThemeManager.AccentColor = _profileStore.Set.AccentColor;
            ThemeManager.ApplyTheme(this);
            RefreshProfileSelector();
            RefreshGrid();
            UpdateConnectionStatusCard();
        };
        Closed += (s, e) =>
        {
            _profileStore.ProfileChanged -= OnProfileChangedOnThread;
            _profileStore.ProfileSetChanged -= OnProfileSetChangedOnThread;
            if (_server != null)
            {
                _server.ClientAuthenticated -= OnClientConnectionStatusChanged;
                _server.ClientDisconnected -= OnClientConnectionStatusChanged;
            }
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
        if (ProfileListContainer == null) return;
        ProfileListContainer.Children.Clear();
        var activeProfileId = _profileStore.Set.ActiveProfileId;

        // Associated process textbox
        TriggerProcessTxt.Text = _profileStore.Current.TriggerProcess ?? "";

        foreach (var profile in _profileStore.Set.Profiles)
        {
            var isSelected = profile.ProfileId == activeProfileId;

            // Card container
            var border = new Border
            {
                Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isSelected ? "#1C1C24" : "#121216")),
                BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isSelected ? ThemeManager.AccentColor : "#1F1F24")),
                BorderThickness = new Thickness(isSelected ? 1.5 : 1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var mainStack = new StackPanel();

            // Name & Capacity header
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameTxt = new TextBlock
            {
                Text = profile.Name,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = System.Windows.FontWeights.SemiBold,
                FontSize = 12.5,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            Grid.SetColumn(nameTxt, 0);
            grid.Children.Add(nameTxt);

            int buttonCount = profile.Buttons?.Count ?? 0;
            var capTxt = new TextBlock
            {
                Text = $"{buttonCount}/15",
                Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8A8A93")),
                FontSize = 10.5,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            Grid.SetColumn(capTxt, 1);
            grid.Children.Add(capTxt);

            mainStack.Children.Add(grid);

            // Mini previews row (First 4 buttons)
            var miniStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            if (profile.Buttons != null)
            {
                var buttonsWithIcons = profile.Buttons.Where(b => !string.IsNullOrEmpty(b.Icon)).Take(4).ToList();
                foreach (var btn in buttonsWithIcons)
                {
                    try
                    {
                        var resolvedPath = ProfileStoreService.ResolveIconFilePath(btn.Icon);
                        if (File.Exists(resolvedPath))
                        {
                            var img = new System.Windows.Controls.Image
                            {
                                Width = 16,
                                Height = 16,
                                Margin = new Thickness(0, 0, 4, 0),
                                Source = new BitmapImage(new Uri(resolvedPath))
                            };
                            miniStack.Children.Add(img);
                        }
                    }
                    catch { /* skip */ }
                }
            }
            mainStack.Children.Add(miniStack);

            border.Child = mainStack;

            // Interactive hovers
            border.MouseEnter += (s, e) =>
            {
                if (profile.ProfileId != _profileStore.Set.ActiveProfileId)
                {
                    border.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#16161C"));
                    border.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#444452"));
                }
            };
            border.MouseLeave += (s, e) =>
            {
                if (profile.ProfileId != _profileStore.Set.ActiveProfileId)
                {
                    border.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#121216"));
                    border.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1F1F24"));
                }
            };
            border.MouseLeftButtonDown += (s, e) =>
            {
                SwitchToProfile(profile.ProfileId);
            };

            ProfileListContainer.Children.Add(border);
        }
    }

    private void SwitchToProfile(string profileId)
    {
        _currentFolderId = null;
        _folderHistory.Clear();
        _profileStore.SwitchProfile(profileId);
        RefreshProfileSelector();
        RefreshGrid();
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
            ResizeMode = ResizeMode.NoResize
        };
        dialog.Loaded += (s, e) => ThemeManager.ApplyTheme(dialog);

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
            ResizeMode = ResizeMode.NoResize
        };
        dialog.Loaded += (s, e) => ThemeManager.ApplyTheme(dialog);

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
        }
    }

    private void RefreshGrid()
    {
        ButtonGrid.Children.Clear();

        if (_currentFolderId == null)
        {
            BreadcrumbText.Text = "Folder: Root";
            BackButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            var folderHierarchyNames = _folderHistory.Select(h => h.Label).Reverse().ToList();
            string folderPath = string.Join(" ➔ ", folderHierarchyNames);
            BreadcrumbText.Text = $"Folder: Root ➔ {folderPath}";
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
                    Margin = new Thickness(6),
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Tag = Tuple.Create(r, c)
                };

                int row = r;
                int col = c;
                bool hasButton = buttons.TryGetValue((row, col), out var buttonModel);

                var border = new Border
                {
                    CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hasButton ? "#181822" : "#0C0C0E")),
                    BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hasButton ? ThemeManager.AccentColor : "#25252E")),
                    BorderThickness = new Thickness(hasButton ? 1.5 : 1),
                    Padding = new Thickness(8)
                };

                var stack = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };

                if (hasButton && buttonModel != null)
                {
                    var iconPath = ProfileStoreService.ResolveIconFilePath(buttonModel.Icon);
                    if (iconPath != null)
                    {
                        try
                        {
                            var img = new System.Windows.Controls.Image
                            {
                                Width = 32,
                                Height = 32,
                                Margin = new Thickness(0, 0, 0, 4),
                                Source = new BitmapImage(new Uri(iconPath))
                            };
                            stack.Children.Add(img);
                        }
                        catch { }
                    }

                    var tbLabel = new TextBlock 
                    { 
                        Text = buttonModel.Label, 
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center, 
                        TextAlignment = System.Windows.TextAlignment.Center,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 11,
                        FontWeight = System.Windows.FontWeights.SemiBold
                    };
                    stack.Children.Add(tbLabel);
                }
                else
                {
                    var tbPlus = new TextBlock
                    {
                        Text = "+",
                        Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#44444F")),
                        FontSize = 18,
                        FontWeight = System.Windows.FontWeights.Bold,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    };
                    stack.Children.Add(tbPlus);
                }

                border.Child = stack;
                btn.Content = border;

                // Setup Click to edit cell
                btn.Click += (s, e) => SelectCell(row, col);

                // Setup Drag and Drop events
                btn.PreviewMouseLeftButtonDown += Cell_PreviewMouseLeftButtonDown;
                btn.MouseMove += Cell_MouseMove;
                btn.AllowDrop = true;
                btn.DragOver += Cell_DragOver;
                btn.DragLeave += Cell_DragLeave;
                btn.Drop += Cell_Drop;

                ButtonGrid.Children.Add(btn);
            }
        }
    }

    private void SelectCell(int row, int col)
    {
        var buttonModel = _profileStore.Current.Buttons
            .FirstOrDefault(b => b.Position.Row == row && b.Position.Col == col && b.ParentFolderId == _currentFolderId);

        bool isNew = false;
        if (buttonModel == null)
        {
            isNew = true;
            buttonModel = new ButtonModel
            {
                ButtonId = $"b_{Guid.NewGuid().ToString().Substring(0, 8)}",
                Position = new Position { Row = row, Col = col },
                ParentFolderId = _currentFolderId,
                Action = new ActionModel { Type = "hotkey" }
            };
        }

        // Open ButtonEditorWindow modally
        var editorDlg = new ButtonEditorWindow(buttonModel);
        editorDlg.Owner = this;
        
        // Hide delete button if it's a new cell
        if (isNew)
        {
            editorDlg.DeleteBtn.Visibility = Visibility.Collapsed;
        }

        if (editorDlg.ShowDialog() == true)
        {
            if (editorDlg.IsDeleted)
            {
                if (!isNew)
                {
                    _profileStore.DeleteButton(_profileStore.Set.ActiveProfileId, buttonModel.ButtonId);
                }
            }
            else
            {
                if (isNew)
                {
                    _profileStore.Current.Buttons.Add(editorDlg.Button);
                }
                else
                {
                    _profileStore.UpdateButton(_profileStore.Set.ActiveProfileId, editorDlg.Button);
                }

                // If user clicked "Enter Folder", automatically descend into it
                if (editorDlg.EnterFolderRequested && editorDlg.Button.Action.Type == "open_folder" && !string.IsNullOrEmpty(editorDlg.Button.Action.TargetFolderId))
                {
                    _currentFolderId = editorDlg.Button.Action.TargetFolderId;
                    _folderHistory.Push((editorDlg.Button.Action.TargetFolderId, editorDlg.Button.Label));
                }
            }

            _profileStore.Save();
            _profileStore.NotifyChanged();
            RefreshGrid();
            RefreshProfileSelector();
        }
    }

    // Drag-and-drop reordering code-behind logic
    private System.Windows.Point _dragStartPoint;

    private void Cell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Tuple<int, int> pos)
        {
            _dragStartPoint = e.GetPosition(null);
        }
    }

    private void Cell_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var diff = e.GetPosition(null) - _dragStartPoint;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is Tuple<int, int> sourcePos)
            {
                var buttons = _profileStore.Current.Buttons
                    .Where(b => b.ParentFolderId == _currentFolderId)
                    .ToDictionary(b => (b.Position.Row, b.Position.Col));

                if (buttons.TryGetValue((sourcePos.Item1, sourcePos.Item2), out var sourceBtn))
                {
                    var data = new System.Windows.DataObject("CrossDeckButton", sourcePos);
                    System.Windows.DragDrop.DoDragDrop(btn, data, System.Windows.DragDropEffects.Move);
                }
            }
        }
    }

    private void Cell_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent("CrossDeckButton"))
        {
            e.Effects = System.Windows.DragDropEffects.Move;
            if (sender is System.Windows.Controls.Button btn && btn.Content is Border border)
            {
                border.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ThemeManager.AccentColor));
                border.BorderThickness = new Thickness(2);
            }
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Cell_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Content is Border border && btn.Tag is Tuple<int, int> pos)
        {
            var buttons = _profileStore.Current.Buttons
                .Where(b => b.ParentFolderId == _currentFolderId)
                .ToDictionary(b => (b.Position.Row, b.Position.Col));

            bool hasButton = buttons.ContainsKey((pos.Item1, pos.Item2));
            border.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hasButton ? ThemeManager.AccentColor : "#25252E"));
            border.BorderThickness = new Thickness(hasButton ? 1.5 : 1);
        }
    }

    private void Cell_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent("CrossDeckButton"))
        {
            var sourcePos = e.Data.GetData("CrossDeckButton") as Tuple<int, int>;
            if (sender is System.Windows.Controls.Button targetBtn && targetBtn.Tag is Tuple<int, int> targetPos && sourcePos != null)
            {
                if (sourcePos.Item1 == targetPos.Item1 && sourcePos.Item2 == targetPos.Item2) return;

                var activeProfile = _profileStore.Current;
                var sourceButton = activeProfile.Buttons.FirstOrDefault(b => b.ParentFolderId == _currentFolderId && b.Position.Row == sourcePos.Item1 && b.Position.Col == sourcePos.Item2);
                var targetButton = activeProfile.Buttons.FirstOrDefault(b => b.ParentFolderId == _currentFolderId && b.Position.Row == targetPos.Item1 && b.Position.Col == targetPos.Item2);

                if (sourceButton != null)
                {
                    sourceButton.Position = new Position { Row = targetPos.Item1, Col = targetPos.Item2 };
                    if (targetButton != null)
                    {
                        targetButton.Position = new Position { Row = sourcePos.Item1, Col = sourcePos.Item2 };
                    }
                    
                    _profileStore.Save();
                    _profileStore.NotifyChanged();
                    RefreshGrid();
                    RefreshProfileSelector();
                }
            }
        }
    }

    // Connection events
    private void OnClientConnectionStatusChanged()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateConnectionStatusCard();
            bool isConnected = _server != null && _server.IsClientConnected;
            string msg = isConnected 
                ? $"Connected: {_server!.ConnectedDeviceName}" 
                : "Device Disconnected";
            ShowToast(msg, isConnected);
        }));
    }

    private void UpdateConnectionStatusCard()
    {
        if (DeviceNameText == null) return;

        bool isConnected = _server != null && _server.IsClientConnected;
        DeviceNameText.Text = isConnected ? (_server!.ConnectedDeviceName ?? "Android Client") : "Offline";
        ConnectionDetailsText.Text = isConnected ? $"IP: {_server!.LocalIpAddress}:{_server!.Port}" : "Waiting for client...";
        ConnectionDot.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isConnected ? "#27C93F" : "#FF5555"));
    }

    private System.Windows.Threading.DispatcherTimer? _toastTimer;

    private void ShowToast(string message, bool isSuccess)
    {
        if (ToastNotificationCard == null) return;

        ToastMessageText.Text = message;
        ToastStateDot.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isSuccess ? "#27C93F" : "#FF5555"));
        ToastNotificationCard.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ThemeManager.AccentColor));
        ToastNotificationCard.Visibility = Visibility.Visible;

        if (_toastTimer != null)
        {
            _toastTimer.Stop();
        }
        else
        {
            _toastTimer = new System.Windows.Threading.DispatcherTimer();
            _toastTimer.Interval = TimeSpan.FromSeconds(3);
            _toastTimer.Tick += (s, e) =>
            {
                ToastNotificationCard.Visibility = Visibility.Collapsed;
                _toastTimer.Stop();
            };
        }
        _toastTimer.Start();
    }

    // Footer links clicks
    private void AboutLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        System.Windows.MessageBox.Show("CrossDeck Host v1.0\n\nInspired by macOS & iOS layout designs.\nFeaturing custom cyber outlined control rings, Left sidebar profile cards, and real-time device connection state syncing.\n\nAdvanced Agentic Coding Pair Programmed.", "About CrossDeck", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void HelpLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com") { UseShellExecute = true });
        }
        catch { }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_folderHistory.Count > 0)
        {
            _folderHistory.Pop();
            _currentFolderId = _folderHistory.Count > 0 ? _folderHistory.Peek().Id : null;
            RefreshGrid();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            this.DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void AccentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox combo && combo.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            var newColor = item.Tag?.ToString();
            if (!string.IsNullOrEmpty(newColor))
            {
                _profileStore.Set.AccentColor = newColor;
                _profileStore.Save();
                ThemeManager.AccentColor = newColor;
                foreach (Window win in System.Windows.Application.Current.Windows)
                {
                    ThemeManager.ApplyTheme(win);
                }
                
                // Refresh grid and sidebar to paint active accent color
                RefreshGrid();
                RefreshProfileSelector();
            }
        }
    }
}

