using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AssignHub.Infrastructure.Persistence.Converters;

public sealed class PersianEnumConverter<TEnum>() : ValueConverter<TEnum, string>(v => ToDb(v), v => FromDb(v))
    where TEnum : struct, Enum
{
    private static readonly IReadOnlyDictionary<TEnum, string> ToDbValues = PersianEnumValues.For<TEnum>();

    private static readonly IReadOnlyDictionary<string, TEnum> FromDbValues =
        ToDbValues.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

    public static string ToDb(TEnum value) =>
        ToDbValues.TryGetValue(value, out var stored)
            ? stored
            : throw new InvalidOperationException($"{typeof(TEnum).Name} value '{value}' has no stored form.");

    public static TEnum FromDb(string value) =>
        FromDbValues.TryGetValue(value, out var member)
            ? member
            : throw new InvalidOperationException($"Unknown {typeof(TEnum).Name} value '{value}' in the database.");
}
