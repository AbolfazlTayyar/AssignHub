using AssignHub.Domain.Enums;

namespace AssignHub.Domain.Experts;

// SPEC §4.5: OpenCount and LastAssignedAt change only through the conditional SQL
// updates of assign (§6.1) and close (§9.1), so there is deliberately no Assign() here.
public sealed class Expert
{
    public const int MaxNameLength = 200;
    public const int MinCapacity = 1;
    public const int MaxCapacity = 1000;

    private readonly List<ExpertSpecialty> _specialties = [];

    private Expert()
    {
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = null!;

    public int Capacity { get; private set; }

    public int OpenCount { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime? LastAssignedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public IReadOnlyCollection<ExpertSpecialty> Specialties => _specialties.AsReadOnly();

    public static Expert Create(
        string name,
        int capacity,
        IEnumerable<Subject> specialties,
        bool isActive,
        DateTime now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Name is required.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new DomainException($"Name must be at most {MaxNameLength} characters.");
        }

        if (capacity is < MinCapacity or > MaxCapacity)
        {
            throw new DomainException($"Capacity must be between {MinCapacity} and {MaxCapacity}.");
        }

        var subjects = specialties?.ToList() ?? [];

        if (subjects.Count == 0)
        {
            throw new DomainException("At least one specialty is required.");
        }

        if (subjects.Any(s => !Enum.IsDefined(s)))
        {
            throw new DomainException("Specialties contain an unknown subject.");
        }

        if (subjects.Distinct().Count() != subjects.Count)
        {
            throw new DomainException("Specialties must be distinct.");
        }

        var expert = new Expert
        {
            Name = name,
            Capacity = capacity,
            OpenCount = 0,
            IsActive = isActive,
            LastAssignedAt = null,
            CreatedAt = now,
        };
        expert._specialties.AddRange(subjects.Select(s => new ExpertSpecialty(s)));
        return expert;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
