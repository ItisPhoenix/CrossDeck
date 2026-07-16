using System.Drawing;
using System.Windows.Forms;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost.Tray;

public sealed class TrayIconManager : IDisposable
{
    private readonly ProfileStoreService _profileStore;
    private readonly Action _onShowPairingInfo;
    private readonly Action _onShowEditor;
    private readonly Action _onRevokeDevice;
    private readonly Action _onExit;
    private NotifyIcon? _notifyIcon;

    public TrayIconManager(ProfileStoreService profileStore, Action onShowPairingInfo, Action onShowEditor, Action onRevokeDevice, Action onExit)
    {
        _profileStore = profileStore;
        _onShowPairingInfo = onShowPairingInfo;
        _onShowEditor = onShowEditor;
        _onRevokeDevice = onRevokeDevice;
        _onExit = onExit;

        _profileStore.ProfileChanged += (p) => RebuildMenu();
        _profileStore.ProfileSetChanged += (set) => RebuildMenu();
    }

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            // ApplicationIcon (CrossDeckHost.csproj) embeds Assets/app.ico into the exe at build
            // time; extract it back out rather than shipping/loading the file a second time.
            Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath) ?? SystemIcons.Application,
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
        menu.Renderer = new ObsidianMenuRenderer();
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
        menu.Items.Add("Revoke Paired Device", null, (_, _) =>
        {
            var confirm = System.Windows.Forms.MessageBox.Show(
                "This disconnects the paired phone and it will need to re-pair with a new PIN. Continue?",
                "Revoke Paired Device", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm == DialogResult.Yes) _onRevokeDevice();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _onExit());
    }

    private sealed class ObsidianMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly System.Drawing.Color _bgColor = System.Drawing.ColorTranslator.FromHtml("#0E0E10");
        private readonly System.Drawing.Color _borderColor = System.Drawing.ColorTranslator.FromHtml("#1F1F23");
        private readonly System.Drawing.Color _textColor = System.Drawing.Color.White;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new System.Drawing.SolidBrush(_bgColor))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // Disable background shading for margins
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                var accentColor = System.Drawing.ColorTranslator.FromHtml(ThemeManager.AccentColor);
                using (var brush = new System.Drawing.SolidBrush(accentColor))
                {
                    e.Graphics.FillRectangle(brush, new System.Drawing.Rectangle(0, 0, e.Item.Width, e.Item.Height));
                }
            }
            else
            {
                using (var brush = new System.Drawing.SolidBrush(_bgColor))
                {
                    e.Graphics.FillRectangle(brush, new System.Drawing.Rectangle(0, 0, e.Item.Width, e.Item.Height));
                }
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? System.Drawing.Color.Black : _textColor;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new System.Drawing.Pen(_borderColor))
            {
                e.Graphics.DrawLine(pen, 0, e.Item.Height / 2, e.Item.Width, e.Item.Height / 2);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new System.Drawing.Pen(_borderColor, 1.5f))
            {
                e.Graphics.DrawRectangle(pen, new System.Drawing.Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1));
            }
        }
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
