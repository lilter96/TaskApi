using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskApi.Messaging;

namespace TaskApi.Persistence;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.RoutingKey)
            .HasMaxLength(255);

        builder.Property(m => m.Payload)
            .HasColumnType("json");

        builder.Property(m => m.TraceParent)
            .HasMaxLength(128);

        builder.Property(m => m.LastError)
            .HasMaxLength(OutboxMessage.MaxErrorLength);

        builder.HasIndex(m => m.NextAttemptAt);
    }
}
