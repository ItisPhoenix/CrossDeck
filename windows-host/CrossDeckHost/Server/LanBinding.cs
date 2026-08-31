using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CrossDeckHost.Server;

/// <summary>
/// Selects one operational IPv4 LAN interface and provides the same-subnet policy used by every
/// CrossDeck listener. There is deliberately no IPAddress.Any fallback.
/// </summary>
public sealed class LanBinding
{
    private LanBinding(IPAddress address, IPAddress mask)
    {
        Address = address;
        Mask = mask;
    }

    public IPAddress Address { get; }
    public IPAddress Mask { get; }

    public static LanBinding Detect()
    {
        var candidates = new List<(IPAddress Address, IPAddress Mask, bool HasGateway)>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch
            {
                continue;
            }

            var hasGateway = properties.GatewayAddresses.Any(gateway =>
                gateway.Address.AddressFamily == AddressFamily.InterNetwork);

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                    unicast.IPv4Mask is null ||
                    IPAddress.IsLoopback(unicast.Address) ||
                    unicast.Address.Equals(IPAddress.Any) ||
                    unicast.Address.GetAddressBytes()[0] == 169)
                    continue;

                candidates.Add((unicast.Address, unicast.IPv4Mask, hasGateway));
            }
        }

        var selected = candidates
            .OrderByDescending(candidate => candidate.HasGateway)
            .FirstOrDefault();

        return selected.Address is null
            ? new LanBinding(IPAddress.Loopback, IPAddress.Parse("255.0.0.0"))
            : new LanBinding(selected.Address, selected.Mask);
    }

    public bool IsAllowedPeer(IPAddress peer)
    {
        if (Address.Equals(IPAddress.Loopback))
            return IPAddress.IsLoopback(peer);

        if (peer.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var localBytes = Address.GetAddressBytes();
        var peerBytes = peer.GetAddressBytes();
        var maskBytes = Mask.GetAddressBytes();
        for (var i = 0; i < localBytes.Length; i++)
        {
            if ((localBytes[i] & maskBytes[i]) != (peerBytes[i] & maskBytes[i]))
                return false;
        }

        return true;
    }
}
