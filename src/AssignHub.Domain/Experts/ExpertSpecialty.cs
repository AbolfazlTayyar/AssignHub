using AssignHub.Domain.Enums;

namespace AssignHub.Domain.Experts;

public sealed class ExpertSpecialty
{
    private ExpertSpecialty()
    {
    }

    internal ExpertSpecialty(Subject subject)
    {
        Subject = subject;
    }

    public int ExpertId { get; private set; }

    public Subject Subject { get; private set; }
}
