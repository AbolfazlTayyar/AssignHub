using AssignHub.Domain.Enums;
using AssignHub.Infrastructure.Persistence.Converters;

namespace AssignHub.UnitTests.Persistence;

public sealed class PersianEnumConverterTests
{
    [Theory]
    [InlineData(Subject.Financial, "مالی")]
    [InlineData(Subject.Legal, "حقوقی")]
    [InlineData(Subject.Technical, "فنی")]
    public void Subject_RoundTrips(Subject member, string stored) => AssertRoundTrip(member, stored);

    [Theory]
    [InlineData(Priority.Normal, "عادی")]
    [InlineData(Priority.Urgent, "فوری")]
    public void Priority_RoundTrips(Priority member, string stored) => AssertRoundTrip(member, stored);

    [Theory]
    [InlineData(RequestStatus.Pending, "در انتظار")]
    [InlineData(RequestStatus.Assigned, "واگذار شده")]
    [InlineData(RequestStatus.Closed, "بسته شده")]
    public void RequestStatus_RoundTrips(RequestStatus member, string stored) => AssertRoundTrip(member, stored);

    [Fact]
    public void EveryEnumMember_HasAStoredValue()
    {
        Assert.Equal(Enum.GetValues<Subject>(), PersianEnumValues.Subjects.Keys.Order());
        Assert.Equal(Enum.GetValues<Priority>(), PersianEnumValues.Priorities.Keys.Order());
        Assert.Equal(Enum.GetValues<RequestStatus>(), PersianEnumValues.Statuses.Keys.Order());
    }

    [Fact]
    public void UnknownDbValue_Throws()
    {
        var converter = new PersianEnumConverter<Subject>();

        Assert.Throws<InvalidOperationException>(() => converter.ConvertFromProvider("Financial"));
        Assert.Throws<InvalidOperationException>(() => converter.ConvertFromProvider("مالي")); // Arabic yeh, not Persian
    }

    [Fact]
    public void UndefinedMember_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new PersianEnumConverter<Priority>().ConvertToProvider((Priority)99));
    }

    private static void AssertRoundTrip<TEnum>(TEnum member, string stored)
        where TEnum : struct, Enum
    {
        var converter = new PersianEnumConverter<TEnum>();

        Assert.Equal(stored, converter.ConvertToProvider(member));
        Assert.Equal(member, converter.ConvertFromProvider(stored));
    }
}
