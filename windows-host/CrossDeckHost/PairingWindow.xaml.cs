using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;

namespace CrossDeckHost;

public partial class PairingWindow : Window
{
    public PairingWindow(string pin, string ipAddress, int port)
    {
        InitializeComponent();
        // ApplyTheme must run after layout (VisualTreeHelper walk is a no-op pre-layout) — Loaded,
        // not the constructor. See EditorWindow's constructor for the same pattern.
        Loaded += (s, e) => ThemeManager.ApplyTheme(this);
        Refresh(pin, ipAddress, port);
    }

    /// <summary>
    /// Re-renders the pairing details and QR code so the window always reflects the
    /// current PIN / IP / port (e.g. after a device revoke or network change) rather
    /// than the values captured when it was first shown.
    /// </summary>
    public void Refresh(string pin, string ipAddress, int port)
    {
        IpText.Text = ipAddress;
        PortText.Text = port.ToString();
        PinText.Text = pin;
        GenerateQr(ipAddress, port, pin);
    }

    private void GenerateQr(string ipAddress, int port, string pin)
    {
        try
        {
            using (var qrGenerator = new QRCodeGenerator())
            using (var qrCodeData = qrGenerator.CreateQrCode($"{ipAddress},{port},{pin}", QRCodeGenerator.ECCLevel.Q))
            using (var qrCode = new PngByteQRCode(qrCodeData))
            {
                byte[] qrCodeBytes = qrCode.GetGraphic(20);
                using (var ms = new MemoryStream(qrCodeBytes))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    QrImage.Source = bitmap;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to generate QR code: {ex.Message}");
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
}

