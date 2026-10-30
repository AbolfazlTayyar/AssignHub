using AssignHub.Domain;
using AssignHub.Domain.Enums;
using AssignHub.Domain.Requests;

namespace AssignHub.UnitTests.Requests;

public sealed class RequestTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    private static Request Create(
        string requestKey = "key-1",
        string title = "Tax question",
        string? description = "Details",
        Subject subject = Subject.Financial,
        Priority priority = Priority.Urgent) =>
        Request.Create(requestKey, title, description, subject, priority, Now);

    [Fact]
    public void Create_ValidInput_StartsPendingWithNoExpert()
    {
        var request = Create();

        Assert.Equal("key-1", request.RequestKey);
        Assert.Equal("Tax question", request.Title);
        Assert.Equal("Details", request.Description);
        Assert.Equal(Subject.Financial, request.Subject);
        Assert.Equal(Priority.Urgent, request.Priority);
        Assert.Equal(RequestStatus.Pending, request.Status);
        Assert.Null(request.ExpertId);
        Assert.Equal(Now, request.CreatedAt);
        Assert.Null(request.AssignedAt);
        Assert.Null(request.ClosedAt);
    }

    [Fact]
    public void Create_NullDescription_Succeeds()
    {
        Assert.Null(Create(description: null).Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_EmptyRequestKey_Throws(string? requestKey)
    {
        Assert.Throws<DomainException>(() => Create(requestKey: requestKey!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_EmptyTitle_Throws(string? title)
    {
        Assert.Throws<DomainException>(() => Create(title: title!));
    }

    [Fact]
    public void Create_FieldsTooLong_Throw()
    {
        Assert.Throws<DomainException>(() => Create(requestKey: new string('k', Request.MaxRequestKeyLength + 1)));
        Assert.Throws<DomainException>(() => Create(title: new string('t', Request.MaxTitleLength + 1)));
        Assert.Throws<DomainException>(() => Create(description: new string('d', Request.MaxDescriptionLength + 1)));
    }

    [Fact]
    public void Create_FieldsAtMaxLength_Succeed()
    {
        var request = Create(
            requestKey: new string('k', Request.MaxRequestKeyLength),
            title: new string('t', Request.MaxTitleLength),
            description: new string('d', Request.MaxDescriptionLength));

        Assert.Equal(RequestStatus.Pending, request.Status);
    }

    [Fact]
    public void Create_UnknownEnumValues_Throw()
    {
        Assert.Throws<DomainException>(() => Create(subject: (Subject)99));
        Assert.Throws<DomainException>(() => Create(priority: (Priority)99));
    }

    [Fact]
    public void HasSamePayload_AllFieldsEqual_ReturnsTrue()
    {
        var request = Create();

        Assert.True(request.HasSamePayload("Tax question", "Details", Subject.Financial, Priority.Urgent));
        Assert.True(Create(description: null).HasSamePayload("Tax question", null, Subject.Financial, Priority.Urgent));
    }

    [Theory]
    [InlineData("Other title", "Details", Subject.Financial, Priority.Urgent)]
    [InlineData("Tax question", "Other details", Subject.Financial, Priority.Urgent)]
    [InlineData("Tax question", null, Subject.Financial, Priority.Urgent)]
    [InlineData("Tax question", "Details", Subject.Legal, Priority.Urgent)]
    [InlineData("Tax question", "Details", Subject.Financial, Priority.Normal)]
    public void HasSamePayload_AnyFieldDiffers_ReturnsFalse(
        string title,
        string? description,
        Subject subject,
        Priority priority)
    {
        Assert.False(Create().HasSamePayload(title, description, subject, priority));
    }
}
