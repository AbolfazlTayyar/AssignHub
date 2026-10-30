using AssignHub.Domain.Enums;
using AssignHub.Domain.Experts;
using AssignHub.Domain.Requests;
using AssignHub.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssignHub.Infrastructure.Persistence.Configurations;

internal sealed class RequestConfiguration : IEntityTypeConfiguration<Request>
{
    public void Configure(EntityTypeBuilder<Request> builder)
    {
        var pending = PersianEnumValues.ToDb(RequestStatus.Pending);
        var assigned = PersianEnumValues.ToDb(RequestStatus.Assigned);
        var closed = PersianEnumValues.ToDb(RequestStatus.Closed);

        builder.ToTable("Requests", t => t.HasCheckConstraint(
            "CK_Requests_ExpertWhenAssigned",
            $"[Status] NOT IN (N'{assigned}', N'{closed}') OR [ExpertId] IS NOT NULL"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.RequestKey).HasMaxLength(Request.MaxRequestKeyLength).IsRequired();
        builder.Property(r => r.Title).HasMaxLength(Request.MaxTitleLength).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(Request.MaxDescriptionLength);

        builder.HasOne<Expert>()
            .WithMany()
            .HasForeignKey(r => r.ExpertId)
            .OnDelete(DeleteBehavior.Restrict);

        // SPEC §6.2: the idempotency guarantee across instances.
        builder.HasIndex(r => r.RequestKey)
            .IsUnique()
            .HasDatabaseName("UX_Requests_RequestKey");

        // SPEC §7.4: serves the drain query; the filter only matches the literal N'...' the query uses.
        builder.HasIndex(r => new { r.Subject, r.CreatedAt, r.Id })
            .HasDatabaseName("IX_Requests_Queue")
            .HasFilter($"[Status] = N'{pending}'")
            .IncludeProperties(r => r.Priority);

        builder.HasIndex(r => new { r.ExpertId, r.Status })
            .HasDatabaseName("IX_Requests_Expert_Status");
    }
}
