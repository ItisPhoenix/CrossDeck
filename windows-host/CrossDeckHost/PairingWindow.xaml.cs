using System.Windows;

namespace CrossDeckHost;

public partial class PairingWindow : Window
{
    public PairingWindow(string pin, string ipAddress, int port)
    {
        InitializeComponent();
        IpText.Text = ipAddress;
        PortText.Text = port.ToString();
        PinText.Text = pin;
    }
}
