using AssignHub.Domain.Enums;
using AssignHub.Domain.Experts;
using AssignHub.Domain.Requests;
using AssignHub.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace AssignHub.Infrastructure.Persistence;

public sealed class AssignHubDbContext(DbContextOptions<AssignHubDbContext> options) : DbContext(options)
{
    public const int EnumColumnLength = 20;

    public DbSet<Expert> Experts => Set<Expert>();

    public DbSet<Request> Requests => Set<Request>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AssignHubDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Subject>()
            .HaveConversion<PersianEnumConverter<Subject>>()
            .HaveMaxLength(EnumColumnLength)
            .AreUnicode();
        configurationBuilder.Properties<Priority>()
            .HaveConversion<PersianEnumConverter<Priority>>()
            .HaveMaxLength(EnumColumnLength)
            .AreUnicode();
        configurationBuilder.Properties<RequestStatus>()
            .HaveConversion<PersianEnumConverter<RequestStatus>>()
            .HaveMaxLength(EnumColumnLength)
            .AreUnicode();

        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }
}
