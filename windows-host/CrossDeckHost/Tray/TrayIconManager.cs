using System.Drawing;
using System.Windows.Forms;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost.Tray;

public sealed class TrayIconManager : IDisposable
{
    private readonly ProfileStoreService _profileStore;
    private readonly Action _onShowPairingInfo;
    private readonly Action _onShowEditor;
    private readonly Action _onExit;
    private NotifyIcon? _notifyIcon;

    public TrayIconManager(ProfileStoreService profileStore, Action onShowPairingInfo, Action onShowEditor, Action onExit)
    {
        _profileStore = profileStore;
        _onShowPairingInfo = onShowPairingInfo;
        _onShowEditor = onShowEditor;
        _onExit = onExit;

        _profileStore.ProfileChanged += (p) => RebuildMenu();
        _profileStore.ProfileSetChanged += (set) => RebuildMenu();
    }

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "CrossDeckHost — running",
            ContextMenuStrip = new ContextMenuStrip()
        };

        _notifyIcon.DoubleClick += (_, _) => _onShowPairingInfo();
        RebuildMenu();
    }

    private void RebuildMenu()
    {
        if (_notifyIcon == null) return;

        if (System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            DoRebuildMenu();
        }
        else
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(DoRebuildMenu));
        }
    }

    private void DoRebuildMenu()
    {
        if (_notifyIcon?.ContextMenuStrip == null) return;

        var menu = _notifyIcon.ContextMenuStrip;
        menu.Items.Clear();

        menu.Items.Add("Show Profile Editor", null, (_, _) => _onShowEditor());
        menu.Items.Add("Show Pairing Info", null, (_, _) => _onShowPairingInfo());
        menu.Items.Add(new ToolStripSeparator());

        foreach (var profile in _profileStore.Set.Profiles)
        {
            var isCurrent = profile.ProfileId == _profileStore.Set.ActiveProfileId;
            var prefix = isCurrent ? "✓ " : "   ";
            var item = new ToolStripMenuItem($"{prefix}{profile.Name}", null, (s, e) =>
            {
                _profileStore.SwitchProfile(profile.ProfileId);
            });
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _onExit());
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
