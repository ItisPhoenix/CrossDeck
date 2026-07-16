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
        IpText.Text = ipAddress;
        PortText.Text = port.ToString();
        PinText.Text = pin;

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
}
