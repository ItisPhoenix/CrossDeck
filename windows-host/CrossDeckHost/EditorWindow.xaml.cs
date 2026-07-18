using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CrossDeckHost.ProfileStore;
using Microsoft.Win32;
using System.Windows.Input;
using System.Text.Json;

namespace CrossDeckHost;

public partial class EditorWindow : Window
{
    private readonly ProfileStoreService _profileStore;
    private readonly Server.WebSocketServer? _server;
    private string? _currentFolderId = null;
    private readonly System.Collections.Generic.Stack<(string Id, string Label)> _folderHistory = new();

    // Undo: snapshot of the state before the last button edit
    private ButtonModel? _lastUndoSnapshot;
    private bool _isUndoDelete; // true if the last op was a delete (undo = re-add)
    private System.Windows.Threading.DispatcherTimer? _undoTimer;

    // True while a cross-fade is happening — blocks re-entrant RefreshGrid calls
    private bool _isFading;

    private readonly Server.PairingManager? _pairing;

    public EditorWindow(ProfileStoreService profileStore, Server.WebSocketServer? server, Server.PairingManager? pairing = null)
    {
        InitializeComponent();
        _profileStore = profileStore;
        _server = server;
        _pairing = pairing;

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
            // Restore saved window position/size
            WindowSettings.Restore(this);

            ThemeManager.AccentColor = _profileStore.Set.AccentColor;
            ThemeManager.ApplyTheme(this);
            RefreshProfileSelector();
            RefreshProfileTabStrip();
            RefreshGrid();
            UpdateConnectionStatusCard();
            SyncGridSizeCombo();

            // Set auto-run checkbox initial state without triggering selection handler
            RunOnBootCheck.Checked -= RunOnBootCheck_Changed;
            RunOnBootCheck.Unchecked -= RunOnBootCheck_Changed;
            RunOnBootCheck.IsChecked = _profileStore.Set.RunOnBoot;
            RunOnBootCheck.Checked += RunOnBootCheck_Changed;
            RunOnBootCheck.Unchecked += RunOnBootCheck_Changed;
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

        // Persist window geometry on every move/resize
        LocationChanged += (s, e) => WindowSettings.Save(this);
        SizeChanged     += (s, e) => WindowSettings.Save(this);
    }

    private void OnProfileChangedOnThread(Profile profile)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshProfileSelector();
            RefreshProfileTabStrip();
            RefreshGrid();
        }));
    }

    private void OnProfileSetChangedOnThread(ProfileSet set)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshProfileSelector();
            RefreshProfileTabStrip();
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
                Background = ThemeManager.Brush(isSelected ? "Brush.Panel" : "Brush.Void"),
                BorderBrush = isSelected ? ThemeManager.Brush("Brush.Accent") : ThemeManager.Brush("Brush.Hairline"),
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
                Foreground = ThemeManager.Brush("Brush.Paper"),
                FontWeight = System.Windows.FontWeights.SemiBold,
                FontSize = 12.5,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            Grid.SetColumn(nameTxt, 0);
            grid.Children.Add(nameTxt);

            int buttonCount = profile.Buttons?.Count ?? 0;
            int totalSlots = profile.Rows * profile.Columns;
            var capTxt = new TextBlock
            {
                Text = $"{buttonCount}/{totalSlots}",
                Foreground = ThemeManager.Brush("Brush.Mist"),
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
                    border.Background = ThemeManager.Brush("Brush.Panel");
                    border.BorderBrush = ThemeManager.Brush("Brush.Hairline");
                }
            };
            border.MouseLeave += (s, e) =>
            {
                if (profile.ProfileId != _profileStore.Set.ActiveProfileId)
                {
                    border.Background = ThemeManager.Brush("Brush.Void");
                    border.BorderBrush = ThemeManager.Brush("Brush.Hairline");
                }
            };
            border.MouseLeftButtonDown += (s, e) =>
            {
                SwitchToProfile(profile.ProfileId);
            };

            // Right-click context menu: Export / Import profile
            var ctx = new ContextMenu();

            var exportItem = new MenuItem { Header = "📤 Export Profile…" };
            exportItem.Click += (s, e) => ExportProfile(profile);

            var importItem = new MenuItem { Header = "📥 Import Profile…" };
            importItem.Click += (s, e) => ImportProfile();

            ctx.Items.Add(exportItem);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(importItem);
            border.ContextMenu = ctx;

            ProfileListContainer.Children.Add(border);
        }
    }

    private void RefreshProfileTabStrip()
    {
        if (ProfileTabStrip == null) return;
        ProfileTabStrip.Children.Clear();
        var activeProfileId = _profileStore.Set.ActiveProfileId;

        foreach (var profile in _profileStore.Set.Profiles)
        {
            var isSelected = profile.ProfileId == activeProfileId;
            var tab = new Border
            {
                Background = ThemeManager.Brush(isSelected ? "Brush.Panel" : "Brush.Void"),
                BorderBrush = isSelected ? ThemeManager.Brush("Brush.Accent") : ThemeManager.Brush("Brush.Hairline"),
                BorderThickness = new Thickness(isSelected ? 1.5 : 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = new TextBlock
                {
                    Text = profile.Name,
                    Foreground = ThemeManager.Brush(isSelected ? "Brush.Paper" : "Brush.Mist"),
                    FontWeight = isSelected ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal,
                    FontSize = 12.5
                }
            };
            tab.MouseLeftButtonDown += (s, e) => SwitchToProfile(profile.ProfileId);

            var ctx = new ContextMenu();
            var exportItem = new MenuItem { Header = "📤 Export Profile…" };
            exportItem.Click += (s, e) => ExportProfile(profile);
            var importItem = new MenuItem { Header = "📥 Import Profile…" };
            importItem.Click += (s, e) => ImportProfile();
            ctx.Items.Add(exportItem);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(importItem);
            tab.ContextMenu = ctx;

            ProfileTabStrip.Children.Add(tab);
        }
    }

    private void SwitchToProfile(string profileId)
    {
        _currentFolderId = null;
        _folderHistory.Clear();
        _profileStore.SwitchProfile(profileId);
        RefreshProfileSelector();
        RefreshProfileTabStrip();
        RefreshGridWithFade();
        SyncGridSizeCombo();
    }

    /// <summary>Syncs the GridSizeCombo selection to match the current profile's rows/columns.</summary>
    private void SyncGridSizeCombo()
    {
        if (GridSizeCombo == null) return;
        var rows = _profileStore.Current.Rows;
        var cols = _profileStore.Current.Columns;
        var tag = $"{rows},{cols}";
        foreach (ComboBoxItem item in GridSizeCombo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                GridSizeCombo.SelectionChanged -= GridSizeCombo_SelectionChanged;
                GridSizeCombo.SelectedItem = item;
                GridSizeCombo.SelectionChanged += GridSizeCombo_SelectionChanged;
                break;
            }
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
        RebuildGrid();
    }

    /// <summary>Cross-fades the button grid: fade out → rebuild → fade in.</summary>
    private void RefreshGridWithFade()
    {
        if (_isFading) { RebuildGrid(); return; }
        _isFading = true;

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(140));
        fadeOut.Completed += (s, e) =>
        {
            RebuildGrid();
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
            fadeIn.Completed += (si, ei) => _isFading = false;
            ButtonGrid.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };
        ButtonGrid.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    /// <summary>Core logic that populates ButtonGrid and rebuilds the breadcrumb panel.</summary>
    private void RebuildGrid()
    {
        ButtonGrid.Children.Clear();

        // --- Breadcrumb panel ---
        BreadcrumbPanel.Visibility = _currentFolderId != null ? Visibility.Visible : Visibility.Collapsed;
        ProfileTabScroller.Visibility = _currentFolderId != null ? Visibility.Collapsed : Visibility.Visible;
        BreadcrumbPanel.Children.Clear();
        if (_currentFolderId == null)
        {
            BackButton.Visibility = Visibility.Collapsed;
            // Root segment (non-clickable)
            BreadcrumbPanel.Children.Add(MakeBreadcrumbSegment("Root", isLast: true, onClick: null));
        }
        else
        {
            BackButton.Visibility = Visibility.Visible;
            // Clickable "Root" segment that navigates all the way to root
            BreadcrumbPanel.Children.Add(MakeBreadcrumbSegment("Root", isLast: false, onClick: () =>
            {
                _folderHistory.Clear();
                _currentFolderId = null;
                RebuildGrid();
            }));

            var historyList = _folderHistory.ToList();
            historyList.Reverse();
            for (int i = 0; i < historyList.Count; i++)
            {
                int capturedI = i;
                bool isLast = (i == historyList.Count - 1);
                var (folderId, folderLabel) = historyList[i];
                string capturedFolderId = folderId;

                // Separator
                BreadcrumbPanel.Children.Add(new TextBlock
                {
                    Text = " ➤ ",
                    Foreground = ThemeManager.Brush("Brush.Mist"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });

                Action? clickAction = isLast ? null : () =>
                {
                    // Pop the history stack back to this folder
                    while (_folderHistory.Count > 0 && _folderHistory.Peek().Id != capturedFolderId)
                        _folderHistory.Pop();
                    _currentFolderId = capturedFolderId;
                    RebuildGrid();
                };
                BreadcrumbPanel.Children.Add(MakeBreadcrumbSegment(folderLabel, isLast, clickAction));
            }
        }

        // --- Dynamic grid dimensions ---
        int rows = _profileStore.Current.Rows;
        int cols = _profileStore.Current.Columns;
        ButtonGrid.Rows = rows;
        ButtonGrid.Columns = cols;

        // Group by cell so that any duplicate (row,col) in stored data can't
        // crash the dictionary build — first button at a cell wins.
        var buttons = _profileStore.Current.Buttons
            .Where(b => b.ParentFolderId == _currentFolderId)
            .GroupBy(b => (b.Position.Row, b.Position.Col))
            .ToDictionary(g => g.Key, g => g.First());

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int row = r;
                int col = c;
                bool hasButton = buttons.TryGetValue((row, col), out var buttonModel);

                // DeckButtonStyle mirrors Android's DeckButton; accent border is drag-over-only feedback.
                var btn = new System.Windows.Controls.Button
                {
                    Style = System.Windows.Application.Current.Resources["DeckButtonStyle"] as Style,
                    Margin = new Thickness(6),
                    Background = ThemeManager.Brush(hasButton ? "Brush.Panel" : "Brush.Void"),
                    BorderBrush = ThemeManager.Brush("Brush.Hairline"),
                    BorderThickness = new Thickness(hasButton ? 1.5 : 1),
                    Tag = Tuple.Create(r, c)
                };

                var stack = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };

                if (hasButton && buttonModel != null)
                {
                    bool iconLoaded = false;

                    if (buttonModel.Action.Type == "multi_action" && buttonModel.Action.Actions?.Count > 0)
                    {
                        stack.Children.Add(BuildMultiActionGlyphGrid(buttonModel.Action.Actions));
                        iconLoaded = true;
                    }
                    else
                    {
                        var iconPath = ProfileStoreService.ResolveIconFilePath(buttonModel.Icon);
                        if (iconPath != null)
                        {
                            try
                            {
                                var img = new System.Windows.Controls.Image
                                {
                                    Width = 52,
                                    Height = 52,
                                    Stretch = Stretch.Uniform,
                                    Source = new BitmapImage(new Uri(iconPath))
                                };
                                stack.Children.Add(img);
                                iconLoaded = true;
                            }
                            catch { }
                        }
                    }

                    // Icon-dominant tiles: the icon alone reads as the button once loaded.
                    // Label only shows when there's no icon to anchor on (keeps text-only actions legible).
                    if (!iconLoaded)
                    {
                        var tbLabel = new TextBlock
                        {
                            Text = buttonModel.Label,
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                            TextAlignment = System.Windows.TextAlignment.Center,
                            Foreground = ThemeManager.Brush("Brush.Paper"),
                            FontSize = 11,
                            FontWeight = System.Windows.FontWeights.SemiBold,
                            TextWrapping = TextWrapping.Wrap
                        };
                        stack.Children.Add(tbLabel);
                    }
                }
                else
                {
                    // Animated pulse ring + centered "+" for empty cells.
                    // A Grid with both children set to Center alignment centers on actual
                    // rendered size — unlike the old Canvas.Left/Top version, which hand-computed
                    // offsets from assumed glyph metrics and always landed the "+" off-center.
                    var canvas = new Grid { Width = 48, Height = 48 };

                    var ring = new Ellipse
                    {
                        Width = 36,
                        Height = 36,
                        Stroke = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ThemeManager.AccentColor)),
                        StrokeThickness = 1.2,
                        Fill = System.Windows.Media.Brushes.Transparent,
                        Opacity = 0.18,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    };

                    // Pulse animation: 0.12 → 0.55 → 0.12, looping every 2.4s
                    var pulse = new DoubleAnimation(0.12, 0.55, TimeSpan.FromSeconds(1.2))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    };
                    ring.BeginAnimation(UIElement.OpacityProperty, pulse);
                    canvas.Children.Add(ring);

                    var tbPlus = new TextBlock
                    {
                        Text = "+",
                        Foreground = ThemeManager.Brush("Brush.Mist"),
                        FontSize = 20,
                        FontWeight = System.Windows.FontWeights.Bold,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        Margin = new Thickness(0, 2, 0, 0),
                        LineHeight = 20,
                        LineStackingStrategy = LineStackingStrategy.BlockLineHeight
                    };
                    canvas.Children.Add(tbPlus);

                    stack.Children.Add(canvas);
                }

                if (hasButton && buttonModel != null && buttonModel.LongPressAction != null)
                {
                    // Small badge showing the long-press action's OWN icon — so between the
                    // button's main icon (tap) and this badge (hold), both configured actions are
                    // visible at a glance instead of just "something happens on hold".
                    var cellContent = new Grid();
                    cellContent.Children.Add(stack);

                    var badge = new Border
                    {
                        Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ThemeManager.AccentColor)) { Opacity = 0.92 },
                        CornerRadius = new CornerRadius(9),
                        Width = 20,
                        Height = 20,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                        VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 4, 4),
                        Child = new TextBlock
                        {
                            Text = GetActionGlyph(buttonModel.LongPressAction),
                            FontSize = 11,
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                            VerticalAlignment = System.Windows.VerticalAlignment.Center,
                            Foreground = ThemeManager.Brush("Brush.Void")
                        }
                    };
                    cellContent.Children.Add(badge);

                    btn.Content = cellContent;
                    btn.ToolTip = "Long-press: " + DescribeActionForTooltip(buttonModel.LongPressAction);
                }
                else
                {
                    btn.Content = stack;
                }

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

    /// <summary>Small glyph representing an action's type — used on the long-press corner badge
    /// so both a button's actions (tap icon + this) are visible at a glance.</summary>
    private static System.Windows.Controls.Primitives.UniformGrid BuildMultiActionGlyphGrid(List<ActionModel> actions)
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Width = 44, Height = 44 };
        int extra = Math.Max(0, actions.Count - 4);
        var glyphs = extra > 0
            ? actions.Take(3).Select(GetActionGlyph).Append($"+{extra}").ToList()
            : actions.Select(GetActionGlyph).ToList();

        foreach (var g in glyphs)
        {
            grid.Children.Add(new TextBlock
            {
                Text = g,
                FontSize = 14,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = ThemeManager.Brush("Brush.Paper")
            });
        }
        return grid;
    }

    private static string GetActionGlyph(ActionModel action) => action.Type switch
    {
        "hotkey" => "⌨",
        "launch_app" => "🚀",
        "media_control" => action.MediaCommand switch
        {
            "PlayPause" => "⏯",
            "NextTrack" => "⏭",
            "PrevTrack" => "⏮",
            "VolumeUp" => "🔊",
            "VolumeDown" => "🔉",
            "VolumeMute" => "🔇",
            _ => "🎵"
        },
        "open_url" => "🌐",
        "run_command" => "💻",
        "text_snippet" => "📋",
        "open_folder" => "📁",
        "multi_action" => "🔗",
        "dial" => "🎚",
        "mouse_click" => "🖱",
        _ => "•"
    };

    /// <summary>Short human-readable summary for a long-press action's tooltip.</summary>
    private static string DescribeActionForTooltip(ActionModel action) => action.Type switch
    {
        "hotkey" => $"Keyboard Shortcut ({(action.Keys != null ? string.Join("+", action.Keys) : "")})",
        "launch_app" => "Launch App",
        "media_control" => $"Media Control ({action.MediaCommand})",
        "open_url" => "Open Website",
        "run_command" => "Run Command",
        "text_snippet" => "Text Snippet",
        "open_folder" => "Open Folder",
        "multi_action" => $"Multiple Actions ({action.Actions?.Count ?? 0} steps)",
        "dial" => $"Dial ({action.DialTarget})",
        "mouse_click" => "Mouse Click",
        _ => action.Type
    };

    /// <summary>Creates a styled breadcrumb text segment, optionally clickable.</summary>
    private TextBlock MakeBreadcrumbSegment(string label, bool isLast, Action? onClick)
    {
        var tb = new TextBlock
        {
            Text = label,
            FontSize = isLast ? 14 : 12,
            FontWeight = isLast ? FontWeights.Bold : FontWeights.Normal,
            Foreground = isLast ? ThemeManager.Brush("Brush.Paper") : ThemeManager.Brush("Brush.Accent"),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = onClick != null ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow
        };
        if (onClick != null)
        {
            tb.TextDecorations = TextDecorations.Underline;
            tb.MouseLeftButtonDown += (s, e) => onClick();
        }
        return tb;
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
                    // Snapshot for undo before deleting
                    SnapshotForUndo(buttonModel, isDelete: true);
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
                    // Snapshot for undo before overwriting
                    SnapshotForUndo(buttonModel, isDelete: false);
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
            RefreshProfileTabStrip();
        }
    }

    // --------------- UNDO LOGIC ---------------

    private void SnapshotForUndo(ButtonModel btn, bool isDelete)
    {
        // Deep-copy via JSON round-trip to avoid aliasing
        _lastUndoSnapshot = JsonSerializer.Deserialize<ButtonModel>(JsonSerializer.Serialize(btn));
        _isUndoDelete = isDelete;
        ShowUndoToast(isDelete ? $"Deleted \"{btn.Label}\"" : $"Edited \"{btn.Label}\"");
    }

    private void ShowUndoToast(string label)
    {
        if (UndoToastCard == null) return;
        UndoToastLabel.Text = label;
        UndoToastCard.Visibility = Visibility.Visible;

        _undoTimer?.Stop();
        _undoTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _undoTimer.Tick += (s, e) =>
        {
            UndoToastCard.Visibility = Visibility.Collapsed;
            _undoTimer.Stop();
        };
        _undoTimer.Start();
    }

    private void UndoBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_lastUndoSnapshot == null) return;

        if (_isUndoDelete)
        {
            // Re-add the deleted button
            _profileStore.Current.Buttons.Add(_lastUndoSnapshot);
        }
        else
        {
            // Restore previous state
            _profileStore.UpdateButton(_profileStore.Set.ActiveProfileId, _lastUndoSnapshot);
        }

        _profileStore.Save();
        _profileStore.NotifyChanged();
        RefreshGrid();
        RefreshProfileSelector();
        RefreshProfileTabStrip();

        _lastUndoSnapshot = null;
        UndoToastCard.Visibility = Visibility.Collapsed;
        _undoTimer?.Stop();
    }

    // --------------- GRID SIZE PICKER ---------------

    private void GridSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridSizeCombo?.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Tag?.ToString();
        if (string.IsNullOrEmpty(tag)) return;

        // Guard: SelectionChanged can fire during InitializeComponent() (initial
        // SelectedIndex) before _profileStore is assigned — ignore until ready.
        if (_profileStore?.Current == null) return;

        var parts = tag.Split(',');
        if (parts.Length != 2) return;
        if (!int.TryParse(parts[0], out int rows) || !int.TryParse(parts[1], out int cols)) return;

        int capacity = rows * cols;
        var buttons = _profileStore.Current.Buttons ?? new List<ButtonModel>();

        // Each folder scope (root = null, plus every folder) shares one grid, so no scope may hold
        // more buttons than the new grid fits. If one does, block the resize instead of silently
        // orphaning buttons that fall outside the new bounds.
        var scopes = buttons.GroupBy(b => b.ParentFolderId);
        var tooFull = scopes.FirstOrDefault(g => g.Count() > capacity);
        if (tooFull != null)
        {
            ShowToast($"Can't shrink to {rows}×{cols}: a page has {tooFull.Count()} buttons.", isSuccess: false);
            SyncGridSizeCombo(); // revert the combo to the profile's real size
            return;
        }

        // Reflow any button now out of bounds into the first free cell within its own scope.
        int moved = 0;
        foreach (var scope in scopes)
        {
            var occupied = new HashSet<(int, int)>(
                scope.Where(b => b.Position.Row < rows && b.Position.Col < cols)
                     .Select(b => (b.Position.Row, b.Position.Col)));
            foreach (var b in scope.Where(b => b.Position.Row >= rows || b.Position.Col >= cols))
            {
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        if (occupied.Add((r, c)))
                        {
                            b.Position.Row = r;
                            b.Position.Col = c;
                            moved++;
                            goto placed;
                        }
                    }
                }
                placed: ;
            }
        }

        _profileStore.Current.Rows = rows;
        _profileStore.Current.Columns = cols;
        _profileStore.Save();
        _profileStore.NotifyChanged();
        RefreshGrid();
        RefreshProfileSelector();
        RefreshProfileTabStrip();
        if (moved > 0)
            ShowToast($"Moved {moved} button{(moved == 1 ? "" : "s")} to fit the new grid.", isSuccess: true);
    }

    // --------------- PROFILE EXPORT / IMPORT ---------------

    private void ExportProfile(Profile profile)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{profile.Name}.crossdeck.json",
            DefaultExt = ".json",
            Filter = "CrossDeck Profile (*.crossdeck.json)|*.crossdeck.json|JSON Files (*.json)|*.json"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            ShowToast($"Exported \"{profile.Name}\" successfully", isSuccess: true);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportProfile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CrossDeck Profile (*.crossdeck.json)|*.crossdeck.json|JSON Files (*.json)|*.json"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var profile = JsonSerializer.Deserialize<Profile>(json);
            if (profile == null) throw new InvalidDataException("Invalid profile file.");

            // Ensure unique ID to avoid conflicts
            profile.ProfileId = $"p_{Guid.NewGuid().ToString().Substring(0, 8)}";

            // Resolve duplicate names
            var name = profile.Name;
            int idx = 1;
            while (_profileStore.Set.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                name = $"{profile.Name} ({idx++})";
            profile.Name = name;

            _profileStore.Set.Profiles.Add(profile);
            _profileStore.Save();
            _profileStore.NotifyChanged();
            ShowToast($"Imported \"{profile.Name}\" successfully", isSuccess: true);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (sender is System.Windows.Controls.Button btn)
            {
                btn.BorderBrush = ThemeManager.Brush("Brush.Accent");
                btn.BorderThickness = new Thickness(2);
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
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Tuple<int, int> pos)
        {
            var buttons = _profileStore.Current.Buttons
                .Where(b => b.ParentFolderId == _currentFolderId)
                .ToDictionary(b => (b.Position.Row, b.Position.Col));

            bool hasButton = buttons.ContainsKey((pos.Item1, pos.Item2));
            btn.BorderBrush = ThemeManager.Brush("Brush.Hairline");
            btn.BorderThickness = new Thickness(hasButton ? 1.5 : 1);
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
                    RefreshProfileTabStrip();
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
        ConnectionDot.Fill = ThemeManager.Brush(isConnected ? "Brush.Go" : "Brush.Alarm");

        // Inline pairing details replace the old separate PairingWindow — shown only while
        // no phone is connected.
        bool showPairing = !isConnected && _pairing != null && _server != null;
        PairingPanel.Visibility = showPairing ? Visibility.Visible : Visibility.Collapsed;
        if (showPairing)
        {
            PairingAddressText.Text = $"{_server!.LocalIpAddress}:{_server.Port}";
            PairingPinText.Text = _pairing!.CurrentPin;
            GeneratePairingQr(_server.LocalIpAddress, _server.Port, _pairing.CurrentPin);
        }
    }

    private string? _lastQrContent;

    private void GeneratePairingQr(string ip, int port, string pin)
    {
        string content = $"{ip},{port},{pin}";
        if (content == _lastQrContent) return;
        try
        {
            using var qrGenerator = new QRCoder.QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(content, QRCoder.QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new QRCoder.PngByteQRCode(qrCodeData);
            byte[] qrCodeBytes = qrCode.GetGraphic(20);
            using var ms = new MemoryStream(qrCodeBytes);
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            QrImage.Source = bitmap;
            _lastQrContent = content;
        }
        catch { }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private System.Windows.Threading.DispatcherTimer? _toastTimer;

    private void ShowToast(string message, bool isSuccess)
    {
        if (ToastNotificationCard == null) return;

        ToastMessageText.Text = message;
        ToastStateDot.Fill = ThemeManager.Brush(isSuccess ? "Brush.Go" : "Brush.Alarm");
        ToastNotificationCard.BorderBrush = ThemeManager.Brush("Brush.Accent");
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
        System.Windows.MessageBox.Show("CrossDeck Host v0.1.0-beta\nMade by ItisPhoenix — github.com/ItisPhoenix\nPersonal project — not licensed for redistribution.", "About CrossDeck", MessageBoxButton.OK, MessageBoxImage.Information);
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
            RebuildGrid();
        }
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
                RefreshProfileTabStrip();
            }
        }
    }

    private void RunOnBootCheck_Changed(object sender, RoutedEventArgs e)
    {
        bool runOnBoot = RunOnBootCheck.IsChecked == true;
        _profileStore.Set.RunOnBoot = runOnBoot;
        _profileStore.Save();

        const string runKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string appName = "CrossDeckHost";

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
            if (key != null)
            {
                if (runOnBoot)
                {
                    // Add registry run value targeting current executable
                    string execPath = System.Windows.Forms.Application.ExecutablePath;
                    key.SetValue(appName, $"\"{execPath}\" --background");
                }
                else
                {
                    // Remove registry run value if exists
                    key.DeleteValue(appName, throwOnMissingValue: false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to modify startup registry key: {ex.Message}", "Startup Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
