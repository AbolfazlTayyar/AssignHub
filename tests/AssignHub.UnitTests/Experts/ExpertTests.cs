using AssignHub.Domain;
using AssignHub.Domain.Enums;
using AssignHub.Domain.Experts;

namespace AssignHub.UnitTests.Experts;

public sealed class ExpertTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    private static Expert Create(
        string name = "Sara",
        int capacity = 5,
        Subject[]? specialties = null,
        bool isActive = true) =>
        Expert.Create(name, capacity, specialties ?? [Subject.Legal], isActive, Now);

    [Fact]
    public void Create_ValidInput_StartsWithNoLoad()
    {
        var expert = Create(specialties: [Subject.Legal, Subject.Technical]);

        Assert.Equal("Sara", expert.Name);
        Assert.Equal(5, expert.Capacity);
        Assert.Equal(0, expert.OpenCount);
        Assert.Null(expert.LastAssignedAt);
        Assert.True(expert.IsActive);
        Assert.Equal(Now, expert.CreatedAt);
        Assert.Equal([Subject.Legal, Subject.Technical], expert.Specialties.Select(s => s.Subject));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    [InlineData(-1)]
    public void Create_CapacityOutOfRange_Throws(int capacity)
    {
        var ex = Assert.Throws<DomainException>(() => Create(capacity: capacity));
        Assert.Equal("Capacity must be between 1 and 1000.", ex.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public void Create_CapacityAtBounds_Succeeds(int capacity)
    {
        Assert.Equal(capacity, Create(capacity: capacity).Capacity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyName_Throws(string? name)
    {
        Assert.Throws<DomainException>(() => Create(name: name!));
    }

    [Fact]
    public void Create_NameTooLong_Throws()
    {
        Assert.Throws<DomainException>(() => Create(name: new string('a', Expert.MaxNameLength + 1)));
    }

    [Fact]
    public void Create_NameAtMaxLength_Succeeds()
    {
        Assert.Equal(Expert.MaxNameLength, Create(name: new string('a', Expert.MaxNameLength)).Name.Length);
    }

    [Fact]
    public void Create_NoSpecialties_Throws()
    {
        Assert.Throws<DomainException>(() => Create(specialties: []));
        Assert.Throws<DomainException>(() => Expert.Create("Sara", 5, null!, true, Now));
    }

    [Fact]
    public void Create_DuplicateSpecialties_Throws()
    {
        Assert.Throws<DomainException>(() => Create(specialties: [Subject.Legal, Subject.Legal]));
    }

    [Fact]
    public void Create_UnknownSpecialty_Throws()
    {
        Assert.Throws<DomainException>(() => Create(specialties: [(Subject)99]));
    }

    [Fact]
    public void Activate_IsIdempotent()
    {
        var expert = Create(isActive: false);

        expert.Activate();
        expert.Activate();

        Assert.True(expert.IsActive);
    }

    [Fact]
    public void Deactivate_IsIdempotent()
    {
        var expert = Create(isActive: true);

        expert.Deactivate();
        expert.Deactivate();

        Assert.False(expert.IsActive);
    }
}
