using System.Net;
using System.Net.Sockets;
using AdChecker.Core.Guardrails;

namespace AdChecker.Tests.TestDoubles;

/// <summary>DNS without the network. Unknown hosts fail like a real lookup.</summary>
public sealed class FakeHostResolver(Dictionary<string, string[]> records) : IHostResolver
{
    public static FakeHostResolver Public(params string[] hosts) =>
        new(hosts.ToDictionary(h => h, _ => new[] { "93.184.215.14" }));

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default) =>
        records.TryGetValue(host, out var addresses)
            ? Task.FromResult(addresses.Select(IPAddress.Parse).ToArray())
            : throw new SocketException((int)SocketError.HostNotFound);
}
