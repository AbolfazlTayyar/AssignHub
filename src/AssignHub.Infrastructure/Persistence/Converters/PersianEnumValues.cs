using AssignHub.Domain.Enums;

namespace AssignHub.Infrastructure.Persistence.Converters;

// SPEC §4.1: the only Persian in the codebase. Raw SQL takes these values as parameters, never as literals.
public static class PersianEnumValues
{
    public static readonly IReadOnlyDictionary<Subject, string> Subjects = new Dictionary<Subject, string>
    {
        [Subject.Financial] = "مالی",
        [Subject.Legal] = "حقوقی",
        [Subject.Technical] = "فنی",
    };

    public static readonly IReadOnlyDictionary<Priority, string> Priorities = new Dictionary<Priority, string>
    {
        [Priority.Normal] = "عادی",
        [Priority.Urgent] = "فوری",
    };

    public static readonly IReadOnlyDictionary<RequestStatus, string> Statuses = new Dictionary<RequestStatus, string>
    {
        [RequestStatus.Pending] = "در انتظار",
        [RequestStatus.Assigned] = "واگذار شده",
        [RequestStatus.Closed] = "بسته شده",
    };

    public static IReadOnlyDictionary<TEnum, string> For<TEnum>()
        where TEnum : struct, Enum =>
        typeof(TEnum) switch
        {
            var t when t == typeof(Subject) => (IReadOnlyDictionary<TEnum, string>)Subjects,
            var t when t == typeof(Priority) => (IReadOnlyDictionary<TEnum, string>)Priorities,
            var t when t == typeof(RequestStatus) => (IReadOnlyDictionary<TEnum, string>)Statuses,
            _ => throw new NotSupportedException($"No Persian mapping for {typeof(TEnum).Name}."),
        };

    public static string ToDb<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        PersianEnumConverter<TEnum>.ToDb(value);
}
