using ContentReview.Domain.Security;
using ContentReview.Infrastructure.Security;

namespace ContentReview.UnitTests.Security;

public class PasswordHashingServiceTests
{
    private const string Password = "Str0ng!Passw0rd";

    private readonly IPasswordHashingService _service = new PasswordHashingService();

    [Fact]
    public void Hash_verifies_with_the_same_password()
    {
        var hash = _service.Hash(Password);

        Assert.True(_service.Verify(hash, Password));
    }

    [Fact]
    public void Verify_rejects_a_different_password()
    {
        var hash = _service.Hash(Password);

        Assert.False(_service.Verify(hash, Password + "x"));
    }

    [Fact]
    public void Hash_does_not_contain_the_password_and_is_salted()
    {
        var first = _service.Hash(Password);
        var second = _service.Hash(Password);

        Assert.DoesNotContain(Password, first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Hash_uses_the_identity_v3_format_with_hmac_sha512()
    {
        var bytes = Convert.FromBase64String(_service.Hash(Password));

        Assert.Equal(0x01, bytes[0]); // format marker: V3
        Assert.Equal(2u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1, 4))); // PRF: HMACSHA512
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("!!!not base64!!!")]
    public void Verify_returns_false_for_invalid_hashes(string hash)
    {
        Assert.False(_service.Verify(hash, Password));
    }

    [Fact]
    public void Hash_requires_a_password()
    {
        Assert.Throws<ArgumentException>(() => _service.Hash(string.Empty));
        Assert.Throws<ArgumentNullException>(() => _service.Hash(null!));
    }
}
