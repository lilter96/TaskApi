using FluentValidation;

namespace TaskApi.Tasks;

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(r => r.Title)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Title must not be empty.")
            .Must(title => title!.Trim().Length <= TaskItem.TitleMaxLength)
            .WithMessage($"Title must not be longer than {TaskItem.TitleMaxLength} characters.");

        RuleFor(r => r.Priority)
            .IsInEnum()
            .WithMessage(r => $"Unknown priority '{r.Priority}'.");
    }
}

public sealed class TaskListQueryValidator : AbstractValidator<TaskListQuery>
{
    public TaskListQueryValidator()
    {
        RuleFor(q => q.Priority)
            .Must((query, _) => query.ParsePriority() is not null)
            .When(q => q.Priority is not null)
            .WithMessage(q => $"Unknown priority '{q.Priority}'.");

        RuleFor(q => q.Sort)
            .Must((query, _) => query.ParseSort() is not null)
            .When(q => q.Sort is not null)
            .WithMessage(q => $"Unknown sort '{q.Sort}'. Use one of: createdAt, priority, title, optionally prefixed with '-'.");

        RuleFor(q => q.Offset)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Offset must not be negative.");

        RuleFor(q => q.Limit)
            .InclusiveBetween(1, TaskListQuery.MaxLimit)
            .WithMessage($"Limit must be between 1 and {TaskListQuery.MaxLimit}.");
    }
}
