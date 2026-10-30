using AssignHub.Domain.Experts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssignHub.Infrastructure.Persistence.Configurations;

internal sealed class ExpertConfiguration : IEntityTypeConfiguration<Expert>
{
    public void Configure(EntityTypeBuilder<Expert> builder)
    {
        builder.ToTable("Experts", t =>
        {
            t.HasCheckConstraint(
                "CK_Experts_Capacity",
                $"[Capacity] BETWEEN {Expert.MinCapacity} AND {Expert.MaxCapacity}");
            t.HasCheckConstraint("CK_Experts_OpenCount", "[OpenCount] >= 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(Expert.MaxNameLength).IsRequired();

        builder.HasMany(e => e.Specialties)
            .WithOne()
            .HasForeignKey(s => s.ExpertId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Specialties)
            .HasField("_specialties")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
