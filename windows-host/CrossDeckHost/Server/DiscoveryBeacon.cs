using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CrossDeckHost.Server;

public class DiscoveryBeacon
{
    private readonly int _webSocketPort;
    private readonly IPAddress _localAddress;
    private readonly string _certificateFingerprint;
    private readonly Func<IPAddress, bool> _isAllowedPeer;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;

    public DiscoveryBeacon(
        string localIp,
        int webSocketPort,
        string certificateFingerprint,
        Func<IPAddress, bool> isAllowedPeer)
    {
        _localAddress = IPAddress.Parse(localIp);
        _webSocketPort = webSocketPort;
        _certificateFingerprint = certificateFingerprint;
        _isAllowedPeer = isAllowedPeer;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => ListenLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udpClient?.Close();
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        try
        {
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(_localAddress, 7891));

            while (!ct.IsCancellationRequested)
            {
                var result = await _udpClient.ReceiveAsync(ct);
                var requestText = Encoding.UTF8.GetString(result.Buffer);

                if (requestText == "CROSSDECK_DISCOVER" && _isAllowedPeer(result.RemoteEndPoint.Address))
                {
                    var responseJson = JsonSerializer.Serialize(new
                    {
                        v = 2,
                        ip = _localAddress.ToString(),
                        port = _webSocketPort,
                        hostName = Environment.MachineName,
                        tls = true,
                        fingerprint = _certificateFingerprint
                    });
                    var responseBytes = Encoding.UTF8.GetBytes(responseJson);
                    await _udpClient.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                }
            }
        }
        catch (Exception)
        {
            // Socket closed or error - ignore
        }
    }
}
