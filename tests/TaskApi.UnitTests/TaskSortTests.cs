using TaskApi.Tasks;

namespace TaskApi.UnitTests;

public sealed class TaskSortTests
{
    [Theory]
    [InlineData("createdAt", TaskSortField.CreatedAt, false)]
    [InlineData("-createdAt", TaskSortField.CreatedAt, true)]
    [InlineData("PRIORITY", TaskSortField.Priority, false)]
    [InlineData("-title", TaskSortField.Title, true)]
    public void TryParse_ReadsFieldAndDirection(string value, TaskSortField field, bool descending)
    {
        Assert.True(TaskSort.TryParse(value, out var sort));
        Assert.Equal(new TaskSort(field, descending), sort);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("--title")]
    [InlineData("1")]
    [InlineData("dueDate")]
    public void TryParse_RejectsUnknownValue(string? value)
    {
        Assert.False(TaskSort.TryParse(value, out _));
    }
}
