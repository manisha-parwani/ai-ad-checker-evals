using System.Net;
using AdChecker.Core.Guardrails;

namespace AdChecker.Tests;

public class IpAddressRulesTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.215.14")]
    [InlineData("172.15.255.255")] // just below 172.16.0.0/12
    [InlineData("172.32.0.0")] // just above 172.16.0.0/12
    [InlineData("100.63.255.255")] // just below 100.64.0.0/10
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")] // IPv4-mapped public
    public void Public_addresses_are_allowed(string address) =>
        Assert.True(IpAddressRules.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("169.254.169.254")] // cloud metadata service
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.2.10")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")] // IPv4-mapped loopback
    [InlineData("::ffff:10.1.2.3")] // IPv4-mapped private
    [InlineData("64:ff9b::a00:1")] // NAT64 wrapping 10.0.0.1
    [InlineData("2002:a00:1::1")] // 6to4 wrapping 10.0.0.1
    [InlineData("2001:db8::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    public void Private_and_reserved_addresses_are_blocked(string address) =>
        Assert.False(IpAddressRules.IsPublic(IPAddress.Parse(address)));
}
