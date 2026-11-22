using System.Net;

namespace AdChecker.Core.Guardrails;

/// <summary>Knows which IP addresses are safe to fetch from (public unicast only).</summary>
public static class IpAddressRules
{
    // Private, loopback, link-local, shared, documentation, multicast, reserved and transition ranges.
    // Transition ranges (NAT64, 6to4, Teredo) are blocked as a whole because they can embed private IPv4 addresses.
    private static readonly IPNetwork[] BlockedRanges =
    [
        // IPv4
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"), // includes the cloud metadata address 169.254.169.254
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.88.99.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"), // includes 255.255.255.255

        // IPv6 (IPv4-mapped addresses are converted to IPv4 before the check)
        IPNetwork.Parse("::/96"), // unspecified, loopback and deprecated IPv4-compatible
        IPNetwork.Parse("64:ff9b::/96"),
        IPNetwork.Parse("64:ff9b:1::/48"),
        IPNetwork.Parse("100::/64"),
        IPNetwork.Parse("2001::/32"),
        IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("2002::/16"),
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("fec0::/10"),
        IPNetwork.Parse("ff00::/8"),
    ];

    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.ScopeId != 0 && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // Scoped addresses only make sense on a local link.
            return false;
        }

        foreach (var range in BlockedRanges)
        {
            if (range.BaseAddress.AddressFamily == address.AddressFamily && range.Contains(address))
            {
                return false;
            }
        }

        return true;
    }
}
