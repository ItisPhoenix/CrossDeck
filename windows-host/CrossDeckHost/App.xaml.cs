using System.Threading;
using System.Windows;
using CrossDeckHost.Actions;
using CrossDeckHost.ProfileStore;
using CrossDeckHost.Server;
using CrossDeckHost.Tray;

namespace CrossDeckHost;

public partial class App : System.Windows.Application
{
    // Held as fields so they aren't garbage collected and so tray menu handlers can reach them.
    private WebSocketServer? _server;
    private TrayIconManager? _tray;
    private PairingManager? _pairing;
    private ProfileStoreService? _profileStore;
    private DiscoveryBeacon? _discoveryBeacon;
    private AutoProfileWatcher? _profileWatcher;
    private LiveStateService? _liveState;

    // Held for the process lifetime — releasing/disposing early would let a second launch through.
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Two instances would both try to bind port 7890/7891 and race over the same profile
        // file on disk — refuse the second launch instead of limping along half-broken.
        _singleInstanceMutex = new Mutex(true, "CrossDeckHost_SingleInstance_9F3E1A2B", out bool isNewInstance);
        _ownsSingleInstanceMutex = isNewInstance;
        if (!isNewInstance)
        {
            System.Windows.MessageBox.Show("CrossDeck Host is already running — check your system tray.", "CrossDeck", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _profileStore = new ProfileStoreService();

        _profileStore.LoadOrCreateDefault("Blank");

        // Must happen before any window is shown, or the first window renders with the default
        // accent for a frame instead of the user's saved custom color.
        ThemeManager.AccentColor = _profileStore.Set.AccentColor;

        var actionExecutor = new ActionExecutor();

        _pairing = new PairingManager();
        _pairing.GenerateNewPin();

        _liveState = new LiveStateService(_profileStore);
        _liveState.Start();

        _server = new WebSocketServer(port: 7890, pairing: _pairing, profileStore: _profileStore, actionExecutor: actionExecutor, liveState: _liveState);
        _server.ClientAuthenticated += OnClientAuthenticated;
        _server.Start();

        _discoveryBeacon = new DiscoveryBeacon(_server.LocalIpAddress, _server.Port);
        _discoveryBeacon.Start();

        _profileWatcher = new AutoProfileWatcher(_profileStore);
        _profileWatcher.Start();

        _tray = new TrayIconManager(
            profileStore: _profileStore,
            onShowPairingInfo: ShowEditorWindow,
            onShowEditor: ShowEditorWindow,
            onRevokeDevice: () =>
            {
                _pairing.RevokeAllTokens();
                _pairing.GenerateNewPin(); // before disconnect, so the editor's pairing card refreshes with the new PIN
                _server?.DisconnectAllClients();
                ShowEditorWindow();
            },
            onExit: () =>
            {
                _profileWatcher?.Stop();
                _liveState?.Stop();
                _discoveryBeacon?.Stop();
                _server?.Stop();
                Shutdown();
            });
        _tray.Initialize();

        // If launched with '--background' (e.g. via startup shortcut or registry key),
        // we stay silently in the system tray without showing the Pairing Window.
        bool startInBackground = false;
        foreach (var arg in e.Args)
        {
            if (arg.Equals("--background", StringComparison.OrdinalIgnoreCase))
            {
                startInBackground = true;
                break;
            }
        }

        if (!startInBackground)
        {
            ShowEditorWindow();
        }
    }

    private void ShowEditorWindow()
    {
        if (_profileStore is null) return;

        foreach (Window win in System.Windows.Application.Current.Windows)
        {
            if (win is EditorWindow)
            {
                win.Activate();
                return;
            }
        }

        var window = new EditorWindow(_profileStore, _server, _pairing, _profileWatcher);
        window.Show();
        window.Activate();
    }

    private void OnClientAuthenticated()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            // Only pop the editor to the foreground for first-time onboarding (no preset chosen
            // yet). Every routine reconnect also fires this event — e.g. the Android app
            // resuming after a background app-switch — and unconditionally calling
            // ShowEditorWindow()/Activate() there yanks focus back to this window each time,
            // which reads as the window "blinking" into the foreground on every reconnect.
            if (_profileStore != null && !_profileStore.Set.PresetSelected)
            {
                ShowEditorWindow();

                var picker = new PresetPickerWindow();
                if (picker.ShowDialog() == true)
                {
                    _profileStore.SetPresetPicked(picker.SelectedPreset);
                }
                else
                {
                    _profileStore.SetPresetPicked("Blank");
                }
            }
        }));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _profileWatcher?.Stop();
        _liveState?.Stop();
        _server?.Stop();
        _tray?.Dispose();
        if (_ownsSingleInstanceMutex) _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
