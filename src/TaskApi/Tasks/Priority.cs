using System.Text.Json.Serialization;

namespace TaskApi.Tasks;

[JsonConverter(typeof(JsonStringEnumConverter<Priority>))]
public enum Priority
{
    Low,
    Medium,
    High,
}
