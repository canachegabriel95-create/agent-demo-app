namespace ContentReview.Infrastructure.Persistence;

/// <summary>The article categories every installation starts with. The ids never change.</summary>
public static class CategorySeed
{
    public sealed record Entry(Guid Id, string Name, string Slug, int SortOrder);

    public static readonly Entry Technology = new(Guid.Parse("0197a000-0000-7000-8000-000000000001"), "Technology", "technology", 1);
    public static readonly Entry Science = new(Guid.Parse("0197a000-0000-7000-8000-000000000002"), "Science", "science", 2);
    public static readonly Entry Business = new(Guid.Parse("0197a000-0000-7000-8000-000000000003"), "Business", "business", 3);
    public static readonly Entry Health = new(Guid.Parse("0197a000-0000-7000-8000-000000000004"), "Health", "health", 4);
    public static readonly Entry Culture = new(Guid.Parse("0197a000-0000-7000-8000-000000000005"), "Culture", "culture", 5);
    public static readonly Entry Education = new(Guid.Parse("0197a000-0000-7000-8000-000000000006"), "Education", "education", 6);

    /// <summary>In display order.</summary>
    public static IReadOnlyList<Entry> All { get; } = [Technology, Science, Business, Health, Culture, Education];
}
