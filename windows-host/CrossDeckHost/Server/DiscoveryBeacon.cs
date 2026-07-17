using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CrossDeckHost.Server;

public class DiscoveryBeacon
{
    private readonly int _webSocketPort;
    private readonly string _localIp;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;

    public DiscoveryBeacon(string localIp, int webSocketPort)
    {
        _localIp = localIp;
        _webSocketPort = webSocketPort;
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
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 7891));

            while (!ct.IsCancellationRequested)
            {
                var result = await _udpClient.ReceiveAsync(ct);
                var requestText = Encoding.UTF8.GetString(result.Buffer);

                if (requestText == "CROSSDECK_DISCOVER")
                {
                    var responseJson = JsonSerializer.Serialize(new
                    {
                        ip = _localIp,
                        port = _webSocketPort,
                        hostName = Environment.MachineName
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
