namespace AssignHub.Application.Queue;

public sealed class QueueOptions
{
    public const string SectionName = "Queue";

    public TimeSpan AgingThreshold { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);

    public int BatchSize { get; set; } = 100;
}
