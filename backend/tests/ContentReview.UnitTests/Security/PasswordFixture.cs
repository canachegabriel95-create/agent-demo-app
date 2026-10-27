using System.Text;
using System.Text.Json;

namespace ContentReview.UnitTests.Security;

/// <summary>Reads docs/fixtures/password-cases.json, the table that the Author Portal tests use too.</summary>
public static class PasswordFixture
{
    public sealed record Case(string Name, string? Password, string[] Violations)
    {
        public override string ToString() => Name;
    }

    public static string FixturePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "fixtures", "password-cases.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("docs/fixtures/password-cases.json was not found above the test output folder.");
    }

    public static IReadOnlyList<Case> Load()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(ToCase).ToList();
    }

    public static TheoryData<Case> Cases()
    {
        var data = new TheoryData<Case>();
        foreach (var item in Load())
        {
            data.Add(item);
        }

        return data;
    }

    private static Case ToCase(JsonElement element)
    {
        string? password = null;
        if (element.TryGetProperty("build", out var build))
        {
            var text = new StringBuilder(build.GetProperty("prefix").GetString());
            var fill = build.GetProperty("fill").GetString()!;
            var length = build.GetProperty("length").GetInt32();
            while (text.Length < length)
            {
                text.Append(fill);
            }

            password = text.ToString(0, length);
        }
        else if (element.TryGetProperty("password", out var value))
        {
            password = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
        }

        return new Case(
            element.GetProperty("name").GetString()!,
            password,
            element.GetProperty("violations").EnumerateArray().Select(v => v.GetString()!).ToArray());
    }
}
