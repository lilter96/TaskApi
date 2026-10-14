using FluentValidation.TestHelper;
using TaskApi.Tasks;

namespace TaskApi.UnitTests;

public sealed class TaskValidatorsTests
{
    private readonly CreateTaskRequestValidator _createValidator = new();
    private readonly TaskListQueryValidator _listValidator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateTask_WithBlankTitle_IsInvalid(string? title)
    {
        _createValidator.TestValidate(new CreateTaskRequest(title, null))
            .ShouldHaveValidationErrorFor(r => r.Title);
    }

    [Fact]
    public void CreateTask_CountsTitleLengthAfterTrim()
    {
        var title = $"  {new string('a', TaskItem.TitleMaxLength)}  ";

        _createValidator.TestValidate(new CreateTaskRequest(title, null)).ShouldNotHaveAnyValidationErrors();
        _createValidator.TestValidate(new CreateTaskRequest(title + "a", null))
            .ShouldHaveValidationErrorFor(r => r.Title);
    }

    [Fact]
    public void CreateTask_WithUndefinedPriority_IsInvalid()
    {
        _createValidator.TestValidate(new CreateTaskRequest("Buy milk", (Priority)42))
            .ShouldHaveValidationErrorFor(r => r.Priority);
    }

    [Theory]
    [InlineData("high")]
    [InlineData("LOW")]
    [InlineData("Medium")]
    public void ListQuery_AcceptsPriorityInAnyCase(string priority)
    {
        _listValidator.TestValidate(Query(priority: priority)).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("Urgent")]
    [InlineData("42")]
    public void ListQuery_RejectsUnknownPriority(string priority)
    {
        _listValidator.TestValidate(Query(priority: priority)).ShouldHaveValidationErrorFor(q => q.Priority);
    }

    [Fact]
    public void ListQuery_RejectsOutOfRangePaging()
    {
        var result = _listValidator.TestValidate(Query(offset: -1, limit: TaskListQuery.MaxLimit + 1));

        result.ShouldHaveValidationErrorFor(q => q.Offset);
        result.ShouldHaveValidationErrorFor(q => q.Limit);
    }

    [Fact]
    public void ListQuery_RejectsUnknownSort()
    {
        _listValidator.TestValidate(Query(sort: "dueDate")).ShouldHaveValidationErrorFor(q => q.Sort);
    }

    private static TaskListQuery Query(string? priority = null, string? sort = null, int? offset = null, int? limit = null) =>
        new(IsCompleted: null, priority, sort, offset, limit);
}
