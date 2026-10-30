using AssignHub.Domain.Experts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssignHub.Infrastructure.Persistence.Configurations;

internal sealed class ExpertSpecialtyConfiguration : IEntityTypeConfiguration<ExpertSpecialty>
{
    public void Configure(EntityTypeBuilder<ExpertSpecialty> builder)
    {
        builder.ToTable("ExpertSpecialties");
        builder.HasKey(s => new { s.ExpertId, s.Subject });
    }
}
