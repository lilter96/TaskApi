using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskApi.Tasks;

namespace TaskApi.Persistence;

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Ignore(t => t.DomainEvents);

        builder.Property(t => t.Title)
            .HasMaxLength(TaskItem.TitleMaxLength)
            .IsRequired();

        builder.Property(t => t.Priority)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(t => t.RowVersion)
            .IsRowVersion();

        builder.HasIndex(t => t.CreatedAt);
    }
}
