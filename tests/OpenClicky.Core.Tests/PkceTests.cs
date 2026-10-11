using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class PkceTests
{
    [Fact]
    public void ChallengeS256_IsStableForVerifier()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        // RFC 7636 appendix B expected challenge for this verifier.
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.ChallengeS256(verifier));
    }

    [Fact]
    public void CreateVerifier_IsUrlSafe()
    {
        var v = Pkce.CreateVerifier();
        Assert.DoesNotContain('+', v);
        Assert.DoesNotContain('/', v);
        Assert.DoesNotContain('=', v);
        Assert.True(v.Length >= 32);
    }
}
