using System.ComponentModel.DataAnnotations;

namespace TaskApi.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public Uri Uri { get; set; } = null!;
}
