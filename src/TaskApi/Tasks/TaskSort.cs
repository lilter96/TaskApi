using System.Collections.Frozen;

namespace TaskApi.Tasks;

public enum TaskSortField
{
    CreatedAt,
    Priority,
    Title,
}

public readonly record struct TaskSort(TaskSortField Field, bool Descending)
{
    public static readonly TaskSort Default = new(TaskSortField.CreatedAt, Descending: true);

    private static readonly FrozenDictionary<string, TaskSortField> Fields = Enum.GetValues<TaskSortField>()
        .ToFrozenDictionary(field => field.ToString(), StringComparer.OrdinalIgnoreCase);

    public static bool TryParse(string? value, out TaskSort sort)
    {
        sort = Default;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var descending = value[0] == '-';
        if (!Fields.TryGetValue(descending ? value[1..] : value, out var field))
        {
            return false;
        }

        sort = new TaskSort(field, descending);
        return true;
    }
}
