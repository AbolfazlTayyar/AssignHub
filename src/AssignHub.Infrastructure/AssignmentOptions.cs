namespace AssignHub.Infrastructure;

public sealed class AssignmentOptions
{
    public const string SectionName = "Assignment";

    public int MaxCandidateRefreshes { get; set; } = 3;
}
