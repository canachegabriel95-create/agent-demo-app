using System.Text.RegularExpressions;
using ContentReview.Domain.Common;

namespace ContentReview.Domain.Entities;

public partial class Category
{
    public const int NameMaxLength = 100;
    public const int SlugMaxLength = 100;

    private Category()
    {
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public int SortOrder { get; private set; }

    public static Category Create(string name, string slug, int sortOrder)
    {
        var trimmedSlug = Guard.Text(slug, nameof(slug), 1, SlugMaxLength);
        if (!SlugPattern().IsMatch(trimmedSlug))
        {
            throw new ArgumentException("slug must contain only lowercase letters, digits and single hyphens.", nameof(slug));
        }

        return new Category
        {
            Id = Guid.CreateVersion7(),
            Name = Guard.Text(name, nameof(name), 1, NameMaxLength),
            Slug = trimmedSlug,
            SortOrder = sortOrder,
        };
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
