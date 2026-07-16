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
    private PairingWindow? _pairingWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _profileStore = new ProfileStoreService();

        _profileStore.LoadOrCreateDefault("Blank");

        var actionExecutor = new ActionExecutor();

        _pairing = new PairingManager();
        _pairing.GenerateNewPin();

        _server = new WebSocketServer(port: 7890, pairing: _pairing, profileStore: _profileStore, actionExecutor: actionExecutor);
        _server.ClientAuthenticated += OnClientAuthenticated;
        _server.Start();

        _discoveryBeacon = new DiscoveryBeacon(_server.LocalIpAddress, _server.Port);
        _discoveryBeacon.Start();

        _profileWatcher = new AutoProfileWatcher(_profileStore);
        _profileWatcher.Start();

        _tray = new TrayIconManager(
            profileStore: _profileStore,
            onShowPairingInfo: ShowPairingWindow,
            onShowEditor: ShowEditorWindow,
            onExit: () =>
            {
                _profileWatcher?.Stop();
                _discoveryBeacon?.Stop();
                _server?.Stop();
                Shutdown();
            });
        _tray.Initialize();

        // Show pairing info immediately on first launch so it's not hidden behind a tray click.
        ShowPairingWindow();
    }

    private void ShowPairingWindow()
    {
        if (_pairing is null || _server is null) return;

        if (_pairingWindow != null)
        {
            _pairingWindow.Activate();
            return;
        }

        _pairingWindow = new PairingWindow(_pairing.CurrentPin, _server.LocalIpAddress, _server.Port);
        _pairingWindow.Closed += (s, e) => _pairingWindow = null;
        _pairingWindow.Show();
        _pairingWindow.Activate();
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

        var window = new EditorWindow(_profileStore);
        window.Show();
        window.Activate();
    }

    private void OnClientAuthenticated()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_pairingWindow != null)
            {
                _pairingWindow.Close();
                _pairingWindow = null;
            }

            ShowEditorWindow();

            if (_profileStore != null && !_profileStore.Set.PresetSelected)
            {
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
        _server?.Stop();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
