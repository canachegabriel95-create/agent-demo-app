using System.Text.Json;
using System.Text.Json.Nodes;
using ContentReview.Infrastructure.Auditing;

namespace ContentReview.UnitTests;

public class AuditDetailsSanitizerTests
{
    private static JsonNode Node(string json) => JsonNode.Parse(json)!;

    [Theory]
    [InlineData("password")]
    [InlineData("newPassword")]
    [InlineData("passwordHash")]
    [InlineData("hash")]
    [InlineData("token")]
    [InlineData("sessionToken")]
    [InlineData("secret")]
    [InlineData("code")]
    [InlineData("invitationCode")]
    [InlineData("Code")]
    public void A_sensitive_property_name_is_refused(string name)
    {
        var details = Node($"{{\"{name}\":\"x\"}}");

        var thrown = Assert.Throws<SensitiveAuditDetailsException>(() => AuditDetailsSanitizer.Sanitize(details, throwOnSensitive: true));

        Assert.Equal(name, thrown.Path);
    }

    [Theory]
    [InlineData("codeSuffix")]
    [InlineData("email")]
    [InlineData("reason")]
    [InlineData("invitationId")]
    [InlineData("changedFields")]
    public void An_ordinary_property_name_is_kept(string name)
    {
        var details = Node($"{{\"{name}\":\"x\"}}");

        var result = AuditDetailsSanitizer.Sanitize(details, throwOnSensitive: true);

        Assert.Equal("x", result![name]!.GetValue<string>());
    }

    [Theory]
    [InlineData("ABCD-EFGH-JKMN")]
    [InlineData("abcdefghjkmn")]
    [InlineData("0123-4567-89AB")]
    public void A_full_invitation_code_is_refused_whatever_the_property_is_called(string value)
    {
        var details = Node($"{{\"note\":\"{value}\"}}");

        Assert.Throws<SensitiveAuditDetailsException>(() => AuditDetailsSanitizer.Sanitize(details, throwOnSensitive: true));
    }

    [Theory]
    [InlineData("WXYZ")]
    [InlineData("Please add concrete examples.")]
    [InlineData("ann@demo.local")]
    [InlineData("ABCD-EFGH")]
    public void Other_text_is_kept(string value)
    {
        var details = Node($"{{\"note\":\"{value}\"}}");

        var result = AuditDetailsSanitizer.Sanitize(details, throwOnSensitive: true);

        Assert.Equal(value, result!["note"]!.GetValue<string>());
    }

    [Fact]
    public void Nested_objects_and_arrays_are_checked()
    {
        Assert.Throws<SensitiveAuditDetailsException>(() =>
            AuditDetailsSanitizer.Sanitize(Node("{\"user\":{\"profile\":{\"passwordHash\":\"x\"}}}"), true));
        Assert.Throws<SensitiveAuditDetailsException>(() =>
            AuditDetailsSanitizer.Sanitize(Node("{\"items\":[{\"token\":\"x\"}]}"), true));
        Assert.Throws<SensitiveAuditDetailsException>(() =>
            AuditDetailsSanitizer.Sanitize(Node("{\"codes\":[\"ABCD-EFGH-JKMN\"]}"), true));
    }

    [Fact]
    public void In_production_mode_sensitive_values_are_removed_and_the_rest_is_kept()
    {
        var details = Node("{\"email\":\"a@b.c\",\"password\":\"x\",\"codeSuffix\":\"MNPQ\",\"note\":\"ABCD-EFGH-JKMN\",\"nested\":{\"token\":\"t\",\"ok\":1},\"list\":[\"ABCD-EFGH-JKMN\",\"fine\"]}");

        var result = AuditDetailsSanitizer.Sanitize(details, throwOnSensitive: false)!;

        Assert.Equal(
            "{\"email\":\"a@b.c\",\"codeSuffix\":\"MNPQ\",\"nested\":{\"ok\":1},\"list\":[\"fine\"]}",
            result.ToJsonString(new JsonSerializerOptions()));
    }

    [Fact]
    public void Null_and_plain_values_pass_through()
    {
        Assert.Null(AuditDetailsSanitizer.Sanitize(null, true));
        Assert.Equal(5, AuditDetailsSanitizer.Sanitize(JsonValue.Create(5), true)!.GetValue<int>());
    }
}
