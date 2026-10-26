using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentReview.Infrastructure.Persistence.Configurations;

internal static class EnumColumn
{
    /// <summary>Stores the enum by name in a <c>text</c> column.</summary>
    public static PropertyBuilder<TEnum> AsText<TEnum>(this PropertyBuilder<TEnum> property) where TEnum : struct, Enum =>
        property.HasConversion<string>().HasColumnType("text").IsRequired();

    /// <summary>SQL for a check constraint restricting <paramref name="column"/> to the enum's names.</summary>
    public static string CheckSql<TEnum>(string column) where TEnum : struct, Enum =>
        $"{column} IN ({string.Join(", ", Enum.GetNames<TEnum>().Select(name => $"'{name}'"))})";
}
