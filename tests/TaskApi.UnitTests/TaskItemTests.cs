using TaskApi.Tasks;

namespace TaskApi.UnitTests;

public sealed class TaskItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_TrimsTitleAndLeavesTaskOpen()
    {
        var task = TaskItem.Create("  Buy milk  ", Priority.High, Now);

        Assert.Equal("Buy milk", task.Title);
        Assert.Equal(Priority.High, task.Priority);
        Assert.Equal(Now, task.CreatedAt);
        Assert.False(task.IsCompleted);
        Assert.Null(task.CompletedAt);
        Assert.Empty(task.DomainEvents);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankTitle_Throws(string title)
    {
        Assert.Throws<ArgumentException>(() => TaskItem.Create(title, Priority.Medium, Now));
    }

    [Fact]
    public void Complete_SetsCompletedAtAndRaisesEvent()
    {
        var task = TaskItem.Create("Buy milk", Priority.High, Now);
        var completedAt = Now.AddMinutes(5);

        task.Complete(completedAt);

        Assert.True(task.IsCompleted);
        Assert.Equal(completedAt, task.CompletedAt);
        var raised = Assert.IsType<TaskCompletedEvent>(Assert.Single(task.DomainEvents));
        Assert.Equal(new TaskCompletedEvent(task.Id, "Buy milk", completedAt, Priority.High), raised);
    }

    [Fact]
    public void Complete_WhenAlreadyCompleted_ThrowsWithoutSecondEvent()
    {
        var task = TaskItem.Create("Buy milk", Priority.Low, Now);
        task.Complete(Now);

        Assert.Throws<InvalidOperationException>(() => task.Complete(Now.AddMinutes(1)));
        Assert.Single(task.DomainEvents);
        Assert.Equal(Now, task.CompletedAt);
    }
}
