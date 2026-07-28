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
    private readonly Server.AutoProfileWatcher? _profileWatcher;

    // Currently-selected cell being edited in the property panel — null when nothing is selected.
    private ButtonModel? _selectedModel;
    private bool _selectedIsNew;
    private bool _selectedIsDial;
    // Deep clone taken at selection time (existing cells only) — used for the FIRST auto-apply's
    // undo snapshot only, not every keystroke, so Undo restores "before I started editing" instead
    // of "one keystroke ago."
    private ButtonModel? _preEditSnapshot;
    private bool _snapshotShownThisSelection;

    // AppDiscovery.DiscoverApps() walks Start Menu shortcuts via COM (WScript.Shell) plus UWP
    // package enumeration — slow enough to visibly lag every single grid-cell click if run
    // synchronously each time. Scanned once in the background after the window loads instead;
    // refreshed only on explicit re-scan (none needed yet — installed apps rarely change mid-session).
    private System.Collections.Generic.List<DiscoveredApp> _discoveredAppsCache = new();

    public EditorWindow(ProfileStoreService profileStore, Server.WebSocketServer? server, Server.PairingManager? pairing = null, Server.AutoProfileWatcher? profileWatcher = null)
    {
        InitializeComponent();
        _profileStore = profileStore;
        _server = server;
        _pairing = pairing;
        _profileWatcher = profileWatcher;
        _profileWatcher?.SetEditorOpen(true);

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
            RefreshGrid();
            UpdateConnectionStatusCard();

            Task.Run(() => AppDiscovery.DiscoverApps()).ContinueWith(t =>
            {
                _discoveredAppsCache = t.Result;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        };
        Closed += (s, e) =>
        {
            _profileWatcher?.SetEditorOpen(false);
            _profileStore.ProfileChanged -= OnProfileChangedOnThread;
            _profileStore.ProfileSetChanged -= OnProfileSetChangedOnThread;
            if (_server != null)
            {
                _server.ClientAuthenticated -= OnClientConnectionStatusChanged;
                _server.ClientDisconnected -= OnClientConnectionStatusChanged;
            }
            PropertyPanel.Dispose();
        };

        // Persist window geometry on every move/resize, and re-fit the grid's cell size to
        // whatever space a resize just freed up (RebuildGrid no-ops harmlessly before Loaded
        // populates ButtonGridScroller's real size).
        LocationChanged += (s, e) => WindowSettings.Save(this);
        SizeChanged     += (s, e) => { WindowSettings.Save(this); RebuildGrid(); };

        PropertyPanel.Applied += OnPropertyPanelApplied;
        PropertyPanel.DeleteRequested += OnPropertyPanelDeleteRequested;
        PropertyPanel.EnterFolderRequested += OnPropertyPanelEnterFolderRequested;
        PairingPopup.PlacementTarget = ConnectionChip;
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

        TriggerProcessTxt.Text = _profileStore.Current.TriggerProcess ?? "";

        foreach (var profile in _profileStore.Set.Profiles)
        {
            var isSelected = profile.ProfileId == activeProfileId;

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

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameTxt = new TextBlock
            {
                Text = profile.Name,
                Foreground = ThemeManager.Brush("Brush.Paper"),
                FontWeight = System.Windows.FontWeights.SemiBold,
                FontSize = 12.5,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameTxt, 0);
            grid.Children.Add(nameTxt);

            int buttonCount = profile.Buttons?.Count(b => b.ParentFolderId == null) ?? 0;
            int totalSlots = 20;
            var capTxt = new TextBlock
            {
                Text = $"{buttonCount}/{totalSlots}",
                Foreground = ThemeManager.Brush("Brush.Mist"),
                FontSize = 10.5,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(capTxt, 1);
            grid.Children.Add(capTxt);

            mainStack.Children.Add(grid);
            border.Child = mainStack;

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
            border.MouseLeftButtonDown += (s, e) => SwitchToProfile(profile.ProfileId);

            var ctx = new ContextMenu();
            var renameItem = new MenuItem { Header = "✏️ Rename…" };
            renameItem.Click += (s, e) => { SwitchToProfile(profile.ProfileId); RenameProfileButton_Click(s, e); };
            var deleteItem = new MenuItem { Header = "🗑️ Delete Page" };
            deleteItem.Click += (s, e) => { SwitchToProfile(profile.ProfileId); DeleteProfileButton_Click(s, e); };
            var exportItem = new MenuItem { Header = "📤 Export Profile…" };
            exportItem.Click += (s, e) => ExportProfile(profile);
            var importItem = new MenuItem { Header = "📥 Import Profile…" };
            importItem.Click += (s, e) => ImportProfile();
            ctx.Items.Add(renameItem);
            ctx.Items.Add(deleteItem);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(exportItem);
            ctx.Items.Add(importItem);
            border.ContextMenu = ctx;

            ProfileListContainer.Children.Add(border);
        }

        // Trailing "+ New Page" row — replaces the old dedicated sidebar button.
        var addRow = new Border
        {
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(10),
            BorderBrush = ThemeManager.Brush("Brush.Hairline"),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = new TextBlock
            {
                Text = "+ New Page",
                Foreground = ThemeManager.Brush("Brush.Accent"),
                FontSize = 12.5,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            }
        };
        addRow.MouseLeftButtonDown += (s, e) => NewProfileButton_Click(s, e);
        ProfileListContainer.Children.Add(addRow);

        // Secondary "or import a preset" link — replaces the old dedicated sidebar button.
        var presetLink = new TextBlock
        {
            Text = "Import a preset…",
            Foreground = ThemeManager.Brush("Brush.Mist"),
            FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand,
            TextDecorations = TextDecorations.Underline,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
        presetLink.MouseLeftButtonDown += (s, e) => AddPresetProfileButton_Click(s, e);
        ProfileListContainer.Children.Add(presetLink);
    }

    private void SwitchToProfile(string profileId)
    {
        PropertyPanel.Clear();
        _selectedModel = null;
        _currentFolderId = null;
        _folderHistory.Clear();
        _profileStore.SwitchProfile(profileId);
        RefreshProfileSelector();
        RefreshGridWithFade();
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

    /// <summary>Grows the box as you type past its 120px floor — a plain TextBox doesn't size to
    /// its own content in WPF, so this measures the text with the box's own font and widens to
    /// fit (capped so a long path can't push the rest of the header off-screen).</summary>
    private void TriggerProcessTxt_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var formatted = new FormattedText(
            TriggerProcessTxt.Text,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(TriggerProcessTxt.FontFamily, TriggerProcessTxt.FontStyle, TriggerProcessTxt.FontWeight, TriggerProcessTxt.FontStretch),
            TriggerProcessTxt.FontSize,
            System.Windows.Media.Brushes.Black,
            VisualTreeHelper.GetDpi(TriggerProcessTxt).PixelsPerDip);

        TriggerProcessTxt.Width = Math.Clamp(formatted.Width + 24, 120, 320);
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

        var stack = new StackPanel { Margin = new Thickness(10) };
        stack.Children.Add(new TextBlock { Text = "Profile Name:", FontWeight = FontWeights.Bold, Foreground = ThemeManager.Brush("Brush.Paper"), Margin = new Thickness(0, 0, 0, 4) });
        var input = new System.Windows.Controls.TextBox { Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 8) };
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

        var stack = new StackPanel { Margin = new Thickness(10) };
        stack.Children.Add(new TextBlock { Text = "New Profile Name:", FontWeight = FontWeights.Bold, Foreground = ThemeManager.Brush("Brush.Paper"), Margin = new Thickness(0, 0, 0, 4) });
        var input = new System.Windows.Controls.TextBox { Text = _profileStore.Current.Name, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 8) };
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
        RebuildDialRow();
    }

    /// <summary>Cross-fades the button grid: fade out → rebuild → fade in.</summary>
    private void RefreshGridWithFade()
    {
        if (_isFading) { RebuildGrid(); RebuildDialRow(); return; }
        _isFading = true;

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(140));
        fadeOut.Completed += (s, e) =>
        {
            RebuildGrid();
            RebuildDialRow();
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
                RebuildDialRow();
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
                    RebuildDialRow();
                };
                BreadcrumbPanel.Children.Add(MakeBreadcrumbSegment(folderLabel, isLast, clickAction));
            }
        }

        // --- Auto-flow grid: 5 columns fixed, rows grow automatically, capped at 20 buttons
        // per folder scope. A button's position is just its index in this filtered list. ---
        const int columns = 5;
        const int maxButtons = 20;
        var displayedButtons = _profileStore.Current.Buttons
            .Where(b => b.ParentFolderId == _currentFolderId)
            .ToList();
        bool showAddSlot = displayedButtons.Count < maxButtons;
        int totalCellsInGrid = displayedButtons.Count + (showAddSlot ? 1 : 0);
        int rows = Math.Max(1, (int)Math.Ceiling(totalCellsInGrid / (double)columns));
        ButtonGrid.Rows = rows;
        ButtonGrid.Columns = columns;

        // Cells shrink to fit the available viewport like normal, but never below a comfortable
        // floor — past that point ButtonGridScroller scrolls instead, matching the fit-then-scroll
        // rule Android's own grid already uses. An explicit Width/Height is required here because
        // a UniformGrid inside a ScrollViewer is offered infinite space and has nothing else to
        // divide evenly among Rows/Columns.
        const double minCellSize = 96;
        double availableW = ButtonGridScroller.ActualWidth;
        double availableH = ButtonGridScroller.ActualHeight;
        double cellSize = minCellSize;
        if (availableW > 0 && availableH > 0)
        {
            double fitted = Math.Min(availableW / columns, availableH / rows);
            cellSize = Math.Max(fitted, minCellSize);
        }
        ButtonGrid.Width = columns * cellSize;
        ButtonGrid.Height = rows * cellSize;

        for (int index = 0; index < rows * columns; index++)
        {
                bool hasButton = index < displayedButtons.Count;
                var buttonModel = hasButton ? displayedButtons[index] : null;
                bool isAddSlot = index == displayedButtons.Count && showAddSlot;

                // DeckButtonStyle mirrors Android's DeckButton; accent border is drag-over-only feedback.
                var btn = new System.Windows.Controls.Button
                {
                    Style = System.Windows.Application.Current.Resources["DeckButtonStyle"] as Style,
                    Margin = new Thickness(6),
                    Background = ThemeManager.Brush(hasButton ? "Brush.Panel" : "Brush.Void"),
                    BorderBrush = ThemeManager.Brush("Brush.Hairline"),
                    BorderThickness = new Thickness(hasButton ? 1.2 : 1),
                    Tag = index,
                    // Past the last button and the one add-slot: fully blank, not interactive.
                    IsHitTestVisible = hasButton || isAddSlot
                };

                var stack = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };

                if (hasButton && buttonModel != null)
                {
                    bool iconLoaded = false;

                    // Multiple Actions always shows a live step mosaic — each segment its own
                    // step's real icon (falling back to a type glyph per-step if that step has
                    // none) — never a single static button-level icon, so the tile always reflects
                    // what the chain actually does.
                    if ((buttonModel.Action.Type == "multi_action" || buttonModel.Action.Type == "button_group") && buttonModel.Action.Actions?.Count > 0)
                    {
                        bool hasLabel = !string.IsNullOrWhiteSpace(buttonModel.Label);
                        var mosaic = BuildMultiActionMosaic(buttonModel.Action.Actions);
                        btn.SizeChanged += (s, e) => { mosaic.Width = mosaic.Height = btn.ActualWidth * (hasLabel ? 0.65 : 0.85); };
                        stack.Children.Add(mosaic);
                        if (hasLabel)
                        {
                            stack.Children.Add(new TextBlock
                            {
                                Text = buttonModel.Label,
                                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                                TextAlignment = System.Windows.TextAlignment.Center,
                                Foreground = ThemeManager.Brush("Brush.Paper"),
                                FontSize = 10,
                                FontWeight = System.Windows.FontWeights.SemiBold,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(0, 3, 0, 0)
                            });
                        }
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
                                    Stretch = Stretch.Uniform,
                                    Source = new BitmapImage(new Uri(iconPath))
                                };
                                // Icon scales with the actual cell size (set once layout resolves it)
                                // instead of a fixed pixel size, so density settings don't throw off the ratio.
                                btn.SizeChanged += (s, e) => { img.Width = img.Height = btn.ActualWidth * 0.42; };
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
                else if (isAddSlot)
                {
                    // Animated pulse ring + centered "+" for the one trailing add slot.
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
                    // visible at a glance instead of just "something happens on hold". When
                    // long-press is itself a chain of alternatives, the badge shows all of them
                    // (small mosaic) instead of one generic chain-link glyph.
                    bool isChainBadge = buttonModel.LongPressAction.Type == "multi_action" && buttonModel.LongPressAction.Actions is { Count: > 1 };

                    var cellContent = new Grid();
                    cellContent.Children.Add(stack);

                    var badge = new Border
                    {
                        // A chain badge shows several icons at once — accent-tinting the whole
                        // badge behind them competes with each icon's own color, so it stays
                        // neutral dark instead (matches the plain single-glyph badge's accent tint
                        // only when there's just one glyph to tint against).
                        Background = isChainBadge
                            ? ThemeManager.Brush("Brush.Void")
                            : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ThemeManager.AccentColor)) { Opacity = 0.92 },
                        CornerRadius = new CornerRadius(isChainBadge ? 7 : 11),
                        Width = 26,
                        Height = 26,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                        VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                        // Negative margin overhangs the corner (matches Android's badge, which sits
                        // outside the button's clipped content) instead of flush inside the border.
                        Margin = new Thickness(0, 0, -6, -6),
                        Child = BuildBadgeGlyphContent(buttonModel.LongPressAction)
                    };
                    // Badge scales with the button too — same 27%-of-cell ratio as Android.
                    btn.SizeChanged += (s, e) => { badge.Width = badge.Height = btn.ActualWidth * 0.27; };
                    cellContent.Children.Add(badge);

                    btn.Content = cellContent;
                    btn.ToolTip = "Long-press: " + DescribeActionForTooltip(buttonModel.LongPressAction);
                }
                else
                {
                    btn.Content = stack;
                }

                // Setup Click to edit cell — a fresh local per iteration, not the shared `for`-loop
                // variable itself: unlike foreach, a for-loop's index variable is one shared binding
                // across every iteration, so every button's closure would otherwise all read
                // whatever `index` happened to be once the loop finished (one past the last cell),
                // making every click open a blank "new button" dialog instead of that button's own.
                int capturedIndex = index;
                btn.Click += (s, e) => SelectCell(capturedIndex);

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

    /// <summary>Dial row under the grid — same cell chrome as RebuildGrid but a
    /// single fixed row, no folders, capped at 6 dials + one trailing "+" slot. Collapses the
    /// whole strip when the profile has none, so the grid regains that vertical space.</summary>
    private void RebuildDialRow()
    {
        DialRow.Children.Clear();
        const int maxDials = 6;
        var dials = _profileStore.Current.Dials.Where(d => d.ParentFolderId == _currentFolderId).ToList();
        bool showAddSlot = dials.Count < maxDials;
        DialStripBorder.Visibility = (dials.Count > 0 || showAddSlot) ? Visibility.Visible : Visibility.Collapsed;
        int totalCells = dials.Count + (showAddSlot ? 1 : 0);
        DialRow.Columns = Math.Max(1, totalCells);

        for (int index = 0; index < totalCells; index++)
        {
            bool hasDial = index < dials.Count;
            var dialModel = hasDial ? dials[index] : null;
            bool isAddSlot = index == dials.Count && showAddSlot;

            var btn = new System.Windows.Controls.Button
            {
                Style = System.Windows.Application.Current.Resources["DeckButtonStyle"] as Style,
                Width = 84,
                Height = 84,
                Margin = new Thickness(6),
                Background = ThemeManager.Brush(hasDial ? "Brush.Panel" : "Brush.Void"),
                BorderBrush = ThemeManager.Brush("Brush.Hairline"),
                BorderThickness = new Thickness(hasDial ? 1.2 : 1)
            };

            var stack = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };

            if (hasDial && dialModel != null)
            {
                var iconPath = ProfileStoreService.ResolveIconFilePath(dialModel.Icon);
                bool iconLoaded = false;
                if (iconPath != null)
                {
                    try
                    {
                        var img = new System.Windows.Controls.Image { Width = 32, Height = 32, Stretch = Stretch.Uniform, Source = new BitmapImage(new Uri(iconPath)) };
                        stack.Children.Add(img);
                        iconLoaded = true;
                    }
                    catch { }
                }
                var label = string.IsNullOrWhiteSpace(dialModel.Label)
                    ? CrossDeckHost.Controls.ActionConfigControl.SuggestLabel(dialModel.Action) ?? "Dial"
                    : dialModel.Label;
                stack.Children.Add(new TextBlock
                {
                    Text = label,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    TextAlignment = System.Windows.TextAlignment.Center,
                    Foreground = ThemeManager.Brush(iconLoaded ? "Brush.Mist" : "Brush.Paper"),
                    FontSize = 10,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, iconLoaded ? 4 : 0, 0, 0)
                });
            }
            else if (isAddSlot)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "+",
                    Foreground = ThemeManager.Brush("Brush.Mist"),
                    FontSize = 20,
                    FontWeight = System.Windows.FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                });
            }

            btn.Content = stack;
            int capturedIndex = index;
            btn.Click += (s, e) => SelectDialCell(capturedIndex);
            DialRow.Children.Add(btn);
        }
    }

    /// <summary>Same new/edit/delete/save flow as SelectCell, but against Profile.Dials — a
    /// separate list, so it reuses UpdateButton/DeleteButton's "buttons" vs "dials" overload
    /// instead of SelectCell's own hardcoded Buttons calls. Folder-scoped exactly like SelectCell.</summary>
    private void SelectDialCell(int index)
    {
        var dials = _profileStore.Current.Dials.Where(d => d.ParentFolderId == _currentFolderId).ToList();
        var dialModel = index < dials.Count ? dials[index] : null;

        bool isNew = dialModel == null;
        if (isNew)
        {
            dialModel = new ButtonModel
            {
                ButtonId = $"b_{Guid.NewGuid().ToString().Substring(0, 8)}",
                Action = new ActionModel { Type = "dial", DialTarget = "volume" },
                ParentFolderId = _currentFolderId
            };
        }

        _selectedModel = dialModel;
        _selectedIsNew = isNew;
        _selectedIsDial = true;
        _preEditSnapshot = null; // dials never had undo support before this redesign either
        _snapshotShownThisSelection = true; // suppresses the undo-snapshot branch for dials

        var allApps = _discoveredAppsCache;
        PropertyPanel.LoadButton(dialModel, isNew, isDial: true, allApps);
    }

    /// <summary>Closed-grid preview for a multi-action button with no custom icon set — one cell
    /// per step, each showing that step's own real icon (like a folder's app-icon preview),
    /// falling back to a type glyph only for steps with no icon set. Mirrors Android's
    /// MosaicStepGrid. Preview-only: Windows never fires buttons locally, so there's no
    /// per-segment tap here.</summary>
    private static Border BuildMultiActionMosaic(List<ActionModel> actions)
    {
        int overflow = Math.Max(0, actions.Count - 6);
        var cells = overflow > 0
            ? actions.Take(5).Select(a => (Action: (ActionModel?)a, Overflow: (string?)null)).Append((Action: null, Overflow: $"+{overflow + 1}")).ToList()
            : actions.Select(a => (Action: (ActionModel?)a, Overflow: (string?)null)).ToList();

        var uniformGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = cells.Count <= 1 ? 1 : 2 };
        foreach (var (action, overflowLabel) in cells)
        {
            UIElement content;
            if (overflowLabel != null)
            {
                content = new TextBlock { Text = overflowLabel, FontSize = 13, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center, Foreground = ThemeManager.Brush("Brush.Paper") };
            }
            else
            {
                var iconPath = ProfileStoreService.ResolveIconFilePath(action!.Icon);
                if (iconPath != null)
                {
                    try
                    {
                        content = new System.Windows.Controls.Image { Stretch = Stretch.Uniform, Margin = new Thickness(7), Source = new BitmapImage(new Uri(iconPath)) };
                    }
                    catch
                    {
                        content = new TextBlock { Text = GetActionGlyph(action), FontSize = 15, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center, Foreground = ThemeManager.Brush("Brush.Paper") };
                    }
                }
                else
                {
                    content = new TextBlock { Text = GetActionGlyph(action), FontSize = 15, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center, Foreground = ThemeManager.Brush("Brush.Paper") };
                }
            }

            uniformGrid.Children.Add(new Border
            {
                Margin = new Thickness(0.5),
                Background = ThemeManager.Brush("Brush.Panel"),
                Child = content
            });
        }

        return new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = ThemeManager.Brush("Brush.Hairline"),
            ClipToBounds = true,
            Width = 52,
            Height = 52,
            Child = uniformGrid
        };
    }

    internal static string GetActionGlyph(ActionModel action) => action.Type switch
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
        "button_group" => "▦",
        "macro" => "⏺",
        "dial" => "🎚",
        "mouse_click" => "🖱",
        _ => "•"
    };

    /// <summary>The long-press badge's content — a single glyph for a plain action, or a small
    /// mosaic of every alternative's own real icon (falling back to its default builtin, then a
    /// glyph as a last resort) when long-press is itself a chain (multi_action with 2+ entries),
    /// same overflow-into-"+N" rule as the old full-tile mosaic used.</summary>
    private static UIElement BuildBadgeGlyphContent(ActionModel action)
    {
        if (action.Type == "multi_action" && action.Actions is { Count: > 1 } actions)
        {
            int overflow = Math.Max(0, actions.Count - 4);
            var cells = overflow > 0
                ? actions.Take(3).Select(a => (Action: (ActionModel?)a, Overflow: (string?)null)).Append((Action: null, Overflow: $"+{overflow + 1}")).ToList()
                : actions.Select(a => (Action: (ActionModel?)a, Overflow: (string?)null)).ToList();

            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            foreach (var (subAction, overflowLabel) in cells)
            {
                grid.Children.Add(BuildBadgeCell(subAction, overflowLabel));
            }
            return grid;
        }

        return new TextBlock
        {
            Text = GetActionGlyph(action),
            FontSize = 12,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Foreground = ThemeManager.Brush("Brush.Void")
        };
    }

    /// <summary>One cell of a chain badge's mosaic — a real icon (white-on-dark, matching the
    /// badge's own neutral background) if one resolves, else a light-colored type glyph.</summary>
    private static UIElement BuildBadgeCell(ActionModel? action, string? overflowLabel)
    {
        if (overflowLabel != null)
        {
            return new TextBlock { Text = overflowLabel, FontSize = 7, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center, Foreground = ThemeManager.Brush("Brush.Paper") };
        }

        var iconPath = ProfileStoreService.ResolveIconFilePath(action!.Icon ?? ProfileStoreService.DefaultBuiltinIconFor(action));
        if (iconPath != null)
        {
            try
            {
                return new System.Windows.Controls.Image { Stretch = Stretch.Uniform, Margin = new Thickness(3), Source = new BitmapImage(new Uri(iconPath)) };
            }
            catch { /* fall through to glyph */ }
        }

        return new TextBlock { Text = GetActionGlyph(action), FontSize = 8, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center, Foreground = ThemeManager.Brush("Brush.Paper") };
    }

    /// <summary>Short human-readable summary for a long-press action's tooltip.</summary>
    private static string DescribeActionForTooltip(ActionModel action) => !string.IsNullOrWhiteSpace(action.Label) ? action.Label : action.Type switch
    {
        "hotkey" => $"Keyboard Shortcut ({(action.Keys != null ? string.Join("+", action.Keys) : "")})",
        "launch_app" => "Launch App",
        "media_control" => $"Media Control ({action.MediaCommand})",
        "open_url" => "Open Website",
        "run_command" => "Run Command",
        "text_snippet" => "Text Snippet",
        "open_folder" => "Open Folder",
        "multi_action" => $"Multiple Actions ({action.Actions?.Count ?? 0} steps)",
        "button_group" => $"Button Group ({action.Actions?.Count ?? 0} buttons)",
        "macro" => $"Macro ({action.Actions?.Count ?? 0} steps)",
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

    private void SelectCell(int index)
    {
        var scopeButtons = _profileStore.Current.Buttons
            .Where(b => b.ParentFolderId == _currentFolderId)
            .ToList();
        var buttonModel = index < scopeButtons.Count ? scopeButtons[index] : null;

        bool isNew = buttonModel == null;
        if (isNew)
        {
            buttonModel = new ButtonModel
            {
                ButtonId = $"b_{Guid.NewGuid().ToString().Substring(0, 8)}",
                ParentFolderId = _currentFolderId,
                Action = new ActionModel { Type = "hotkey" }
            };
        }

        _selectedModel = buttonModel;
        _selectedIsNew = isNew;
        _selectedIsDial = false;
        _preEditSnapshot = isNew ? null : JsonSerializer.Deserialize<ButtonModel>(JsonSerializer.Serialize(buttonModel));
        _snapshotShownThisSelection = false;

        var allApps = _discoveredAppsCache;
        PropertyPanel.LoadButton(buttonModel, isNew, isDial: false, allApps);
    }

    private void OnPropertyPanelApplied(ButtonModel updated)
    {
        if (_selectedIsDial)
        {
            if (_selectedIsNew)
            {
                _profileStore.Current.Dials.Add(updated);
                _selectedIsNew = false;
            }
            else
            {
                _profileStore.UpdateButton(_profileStore.Set.ActiveProfileId, updated, "dials");
            }
            _profileStore.Save();
            _profileStore.NotifyChanged();
            RebuildDialRow();
            return;
        }

        if (_selectedIsNew)
        {
            _profileStore.Current.Buttons.Add(updated);
            _selectedIsNew = false;
        }
        else
        {
            if (!_snapshotShownThisSelection && _preEditSnapshot != null)
            {
                _lastUndoSnapshot = _preEditSnapshot;
                _isUndoDelete = false;
                ShowUndoToast($"Edited \"{_preEditSnapshot.Label}\"");
                _snapshotShownThisSelection = true;
            }
            _profileStore.UpdateButton(_profileStore.Set.ActiveProfileId, updated);
        }

        _profileStore.Save();
        _profileStore.NotifyChanged();
        RefreshGrid();
        RefreshProfileSelector();
    }

    private void OnPropertyPanelDeleteRequested()
    {
        if (_selectedModel == null || _selectedIsNew) return;

        var result = System.Windows.MessageBox.Show(this,
            $"Delete this {(_selectedIsDial ? "dial" : "button")}? This can't be undone.", "Delete",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        if (_selectedIsDial)
        {
            _profileStore.DeleteButton(_profileStore.Set.ActiveProfileId, _selectedModel.ButtonId, "dials");
            _profileStore.Save();
            _profileStore.NotifyChanged();
            RebuildDialRow();
        }
        else
        {
            SnapshotForUndo(_selectedModel, isDelete: true);
            _profileStore.DeleteButton(_profileStore.Set.ActiveProfileId, _selectedModel.ButtonId);
            _profileStore.Save();
            _profileStore.NotifyChanged();
            RefreshGrid();
            RefreshProfileSelector();
        }

        _selectedModel = null;
        PropertyPanel.Clear();
    }

    private void OnPropertyPanelEnterFolderRequested()
    {
        if (_selectedModel?.Action.Type == "open_folder" && !string.IsNullOrEmpty(_selectedModel.Action.TargetFolderId))
        {
            _currentFolderId = _selectedModel.Action.TargetFolderId;
            _folderHistory.Push((_selectedModel.Action.TargetFolderId, _selectedModel.Label));
            _selectedModel = null;
            PropertyPanel.Clear();
            RefreshGrid();
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

        _lastUndoSnapshot = null;
        UndoToastCard.Visibility = Visibility.Collapsed;
        _undoTimer?.Stop();
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
        if (sender is System.Windows.Controls.Button btn && btn.Tag is int)
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
            if (sender is System.Windows.Controls.Button btn && btn.Tag is int sourceIndex)
            {
                var scopeButtons = _profileStore.Current.Buttons
                    .Where(b => b.ParentFolderId == _currentFolderId)
                    .ToList();

                if (sourceIndex < scopeButtons.Count)
                {
                    var data = new System.Windows.DataObject("CrossDeckButton", sourceIndex);
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
        if (sender is System.Windows.Controls.Button btn && btn.Tag is int index)
        {
            int scopeCount = _profileStore.Current.Buttons.Count(b => b.ParentFolderId == _currentFolderId);
            bool hasButton = index < scopeCount;
            btn.BorderBrush = ThemeManager.Brush("Brush.Hairline");
            btn.BorderThickness = new Thickness(hasButton ? 1.5 : 1);
        }
    }

    private void Cell_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent("CrossDeckButton"))
        {
            var sourceIndexObj = e.Data.GetData("CrossDeckButton");
            if (sender is System.Windows.Controls.Button targetBtn && targetBtn.Tag is int targetIndex && sourceIndexObj is int sourceIndex)
            {
                if (sourceIndex == targetIndex) return;

                var scopeButtons = _profileStore.Current.Buttons
                    .Where(b => b.ParentFolderId == _currentFolderId)
                    .ToList();
                if (sourceIndex >= scopeButtons.Count) return;

                // Reordering, not swapping — the moved button shifts the ones between its old
                // and new spot, same as reordering any list.
                var moved = scopeButtons[sourceIndex];
                scopeButtons.RemoveAt(sourceIndex);
                int insertAt = Math.Min(targetIndex, scopeButtons.Count);
                scopeButtons.Insert(insertAt, moved);

                _profileStore.ReorderButtons(_profileStore.Set.ActiveProfileId, _currentFolderId, scopeButtons.Select(b => b.ButtonId).ToList());
                RefreshGrid();
                RefreshProfileSelector();
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
        ConnectionDot.Fill = ThemeManager.Brush(isConnected ? "Brush.Go" : "Brush.Alarm");

        // Inline pairing details replace the old separate PairingWindow — shown only while
        // no phone is connected.
        bool showPairing = !isConnected && _pairing != null && _server != null;
        PairingPanel.Visibility = showPairing ? Visibility.Visible : Visibility.Collapsed;
        if (!showPairing) PairingPopup.IsOpen = false;
        if (showPairing)
        {
            PairingAddressText.Text = $"{_server!.LocalIpAddress}:{_server.Port}";
            PairingPinText.Text = _pairing!.CurrentPin;
            GeneratePairingQr(_server.LocalIpAddress, _server.Port, _pairing.CurrentPin);
        }
    }

    private void RegeneratePinButton_Click(object sender, RoutedEventArgs e)
    {
        _pairing?.GenerateNewPin();
        UpdateConnectionStatusCard();
    }

    private void ConnectionChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PairingPanel.Children.Count == 0) return; // nothing to show (e.g. already connected)
        PairingPopup.IsOpen = !PairingPopup.IsOpen;
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
        System.Windows.MessageBox.Show("CrossDeck Host v2.0.0\nMade by ItisPhoenix — github.com/ItisPhoenix\nMIT License", "About CrossDeck", MessageBoxButton.OK, MessageBoxImage.Information);
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
        PropertyPanel.Clear();
        _selectedModel = null;
        if (_folderHistory.Count > 0)
        {
            _folderHistory.Pop();
            _currentFolderId = _folderHistory.Count > 0 ? _folderHistory.Peek().Id : null;
            RebuildGrid();
            RebuildDialRow();
        }
    }

    private void SettingsGearButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Settings",
            Width = 320,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };
        dialog.Loaded += (s, e) => ThemeManager.ApplyTheme(dialog);

        var stack = new StackPanel { Margin = new Thickness(12) };

        stack.Children.Add(new TextBlock { Text = "Theme", FontWeight = FontWeights.Bold, Foreground = ThemeManager.Brush("Brush.Paper"), Margin = new Thickness(0, 0, 0, 5) });
        var accentCombo = new System.Windows.Controls.ComboBox { Height = 26, FontSize = 11, Margin = new Thickness(0, 0, 0, 10) };
        var accentOptions = new (string Name, string Hex)[]
        {
            ("Neon Cyan", "#00E5FF"), ("Neon Purple", "#8b5cf6"), ("Cyberpunk Yellow", "#ffb703"),
            ("Toxic Green", "#39FF14"), ("Crimson Red", "#e63946")
        };
        foreach (var (name, hex) in accentOptions)
        {
            var item = new System.Windows.Controls.ComboBoxItem { Content = name, Tag = hex };
            accentCombo.Items.Add(item);
            if (string.Equals(hex, _profileStore.Set.AccentColor, StringComparison.OrdinalIgnoreCase))
                accentCombo.SelectedItem = item;
        }
        accentCombo.SelectionChanged += (s, e) =>
        {
            if (accentCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string hex)
            {
                _profileStore.Set.AccentColor = hex;
                _profileStore.Save();
                ThemeManager.AccentColor = hex;
                foreach (Window win in System.Windows.Application.Current.Windows) ThemeManager.ApplyTheme(win);
                RefreshGrid();
                RefreshProfileSelector();
            }
        };
        stack.Children.Add(accentCombo);

        var runOnBootCheck = new System.Windows.Controls.CheckBox
        {
            Content = "Start CrossDeck on PC startup",
            FontSize = 11,
            IsChecked = _profileStore.Set.RunOnBoot,
            Margin = new Thickness(0, 0, 0, 10)
        };
        runOnBootCheck.Checked += RunOnBootCheck_Changed;
        runOnBootCheck.Unchecked += RunOnBootCheck_Changed;
        stack.Children.Add(runOnBootCheck);

        bool isConnected = _server != null && _server.IsClientConnected;
        if (!isConnected && _pairing != null && _server != null)
        {
            stack.Children.Add(new TextBlock { Text = "Pairing", FontWeight = FontWeights.Bold, Foreground = ThemeManager.Brush("Brush.Paper"), Margin = new Thickness(0, 0, 0, 5) });
            var qrImage = new System.Windows.Controls.Image { Width = 110, Height = 110, Stretch = Stretch.Uniform, Source = QrImage.Source };
            stack.Children.Add(new Border
            {
                Background = System.Windows.Media.Brushes.White, CornerRadius = new CornerRadius(8), Padding = new Thickness(5),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8),
                Child = qrImage
            });
            var addrRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            addrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addrRow.Children.Add(new TextBlock { Text = "ADDRESS", Foreground = ThemeManager.Brush("Brush.Mist"), FontSize = 11, FontWeight = FontWeights.Bold });
            var addrValue = new TextBlock { Text = PairingAddressText.Text, Foreground = ThemeManager.Brush("Brush.Accent"), FontSize = 12, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            Grid.SetColumn(addrValue, 1);
            addrRow.Children.Add(addrValue);
            stack.Children.Add(addrRow);

            var pinRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            pinRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pinRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pinRow.Children.Add(new TextBlock { Text = "PIN", Foreground = ThemeManager.Brush("Brush.Mist"), FontSize = 11, FontWeight = FontWeights.Bold });
            var pinValue = new TextBlock { Text = PairingPinText.Text, Foreground = ThemeManager.Brush("Brush.VoltViolet"), FontSize = 16, FontWeight = FontWeights.Bold, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            Grid.SetColumn(pinValue, 1);
            pinRow.Children.Add(pinValue);
            stack.Children.Add(pinRow);

            stack.Children.Add(new TextBlock
            {
                Text = "Scan the QR in the phone app, or enter the address + PIN. Same WiFi required.",
                Foreground = ThemeManager.Brush("Brush.Mist"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8)
            });
            var regenBtn = new System.Windows.Controls.Button { Content = "🔄 New PIN", Margin = new Thickness(0, 0, 0, 10) };
            regenBtn.Click += (s, e) =>
            {
                _pairing?.GenerateNewPin();
                UpdateConnectionStatusCard();
                qrImage.Source = QrImage.Source;
                addrValue.Text = PairingAddressText.Text;
                pinValue.Text = PairingPinText.Text;
            };
            stack.Children.Add(regenBtn);
        }

        var aboutRow = new Grid();
        aboutRow.Children.Add(new TextBlock
        {
            Text = "About", Foreground = ThemeManager.Brush("Brush.Mist"), FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand, TextDecorations = TextDecorations.Underline,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        });
        ((TextBlock)aboutRow.Children[0]).MouseLeftButtonDown += AboutLink_Click;
        aboutRow.Children.Add(new TextBlock
        {
            Text = "v0.3.5-beta", Foreground = ThemeManager.Brush("Brush.Mist"), FontSize = 11,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        });
        stack.Children.Add(aboutRow);

        dialog.Content = stack;
        dialog.ShowDialog();
    }

    private void RunOnBootCheck_Changed(object sender, RoutedEventArgs e)
    {
        bool runOnBoot = sender is System.Windows.Controls.CheckBox cb && cb.IsChecked == true;
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
