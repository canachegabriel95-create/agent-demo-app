using ContentReview.Infrastructure.Sessions;

namespace ContentReview.UnitTests.Security;

public class SessionTokenHashTests
{
    [Fact]
    public void The_hash_is_lowercase_sha256_hex()
    {
        // SHA-256("abc")
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            SessionService.HashToken("abc"));
    }

    [Fact]
    public void Different_tokens_have_different_hashes_and_the_hash_fits_the_column()
    {
        var first = SessionService.HashToken("token-one");
        var second = SessionService.HashToken("token-two");

        Assert.NotEqual(first, second);
        Assert.Equal(64, first.Length);
        Assert.True(first.Length <= ContentReview.Domain.Entities.UserSession.TokenHashMaxLength);
    }
}
