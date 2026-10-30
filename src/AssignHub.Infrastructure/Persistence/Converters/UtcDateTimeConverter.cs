using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AssignHub.Infrastructure.Persistence.Converters;

// datetime2 has no offset; every stored time is UTC, so restore Kind on read and the API emits "Z".
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
