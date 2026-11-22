using Azure.Core;

namespace AdChecker.Tests.TestDoubles;

public sealed class FakeTokenCredential : TokenCredential
{
    public int Calls { get; private set; }

    public List<string[]> RequestedScopes { get; } = [];

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        Issue(requestContext);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Issue(requestContext));

    private AccessToken Issue(TokenRequestContext context)
    {
        Calls++;
        RequestedScopes.Add(context.Scopes);
        return new AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1));
    }
}
