using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskApi.Idempotency;

namespace TaskApi.Persistence;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");

        builder.HasKey(r => r.Key);

        builder.Property(r => r.Key)
            .HasMaxLength(IdempotencyRecord.MaxKeyLength);

        builder.Property(r => r.ContentType)
            .HasMaxLength(256);

        builder.Property(r => r.Location)
            .HasMaxLength(2048);

        builder.HasIndex(r => r.ExpiresAt);
    }
}
