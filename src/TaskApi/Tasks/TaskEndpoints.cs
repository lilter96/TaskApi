using System.Linq.Expressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TaskApi.Common;
using TaskApi.Idempotency;
using TaskApi.Persistence;

namespace TaskApi.Tasks;

public static class TaskEndpoints
{
    private const string GetTaskRouteName = "GetTask";

    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var tasks = app.MapGroup("/tasks").WithTags("Tasks");

        tasks.MapPost("/", CreateAsync)
            .WithValidation<CreateTaskRequest>()
            .WithIdempotency()
            .WithSummary("Creates a task.");

        tasks.MapGet("/", GetListAsync)
            .WithValidation<TaskListQuery>()
            .WithSummary("Returns tasks, newest first unless sort is given.");

        tasks.MapGet("/{id}", GetByIdAsync)
            .WithName(GetTaskRouteName)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Returns a task.");

        tasks.MapPut("/{id}/complete", CompleteAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Completes a task and publishes task.completed.");

        tasks.MapDelete("/{id}", DeleteAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Deletes a task permanently.");

        return app;
    }

    private static async Task<CreatedAtRoute<TaskResponse>> CreateAsync(
        CreateTaskRequest request,
        TaskDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var task = TaskItem.Create(request.Title!, request.Priority ?? Priority.Medium, timeProvider.GetUtcNow());

        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.CreatedAtRoute(TaskResponse.From(task), GetTaskRouteName, new { id = task.Id });
    }

    private static async Task<Ok<List<TaskResponse>>> GetListAsync(
        [AsParameters] TaskListQuery query,
        TaskDbContext db,
        CancellationToken cancellationToken)
    {
        var tasks = db.Tasks.AsNoTracking();

        if (query.IsCompleted is { } isCompleted)
        {
            tasks = tasks.Where(t => t.IsCompleted == isCompleted);
        }

        if (query.ParsePriority() is { } priority)
        {
            tasks = tasks.Where(t => t.Priority == priority);
        }

        tasks = Sort(tasks, query.ParseSort() ?? TaskSort.Default);

        if (query.Offset is { } offset)
        {
            tasks = tasks.Skip(offset);
        }

        if (query.Limit is { } limit)
        {
            tasks = tasks.Take(limit);
        }

        var result = await tasks.ToListAsync(cancellationToken);

        return TypedResults.Ok(result.ConvertAll(TaskResponse.From));
    }

    private static async Task<Results<Ok<TaskResponse>, NotFound>> GetByIdAsync(
        Guid id,
        TaskDbContext db,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        return task is null ? TypedResults.NotFound() : TypedResults.Ok(TaskResponse.From(task));
    }

    private static async Task<Results<Ok<TaskResponse>, NotFound, ProblemHttpResult>> CompleteAsync(
        Guid id,
        TaskDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks.FindAsync([id], cancellationToken);
        if (task is null)
        {
            return TypedResults.NotFound();
        }

        if (task.IsCompleted)
        {
            return Conflict($"Task {id} is already completed.");
        }

        task.Complete(timeProvider.GetUtcNow());

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict($"Task {id} was modified concurrently.");
        }

        return TypedResults.Ok(TaskResponse.From(task));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id,
        TaskDbContext db,
        CancellationToken cancellationToken)
    {
        var deleted = await db.Tasks.Where(t => t.Id == id).ExecuteDeleteAsync(cancellationToken);

        return deleted > 0 ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static IQueryable<TaskItem> Sort(IQueryable<TaskItem> tasks, TaskSort sort)
    {
        var sorted = sort.Field switch
        {
            TaskSortField.Priority => OrderBy(
                tasks,
                t => t.Priority == Priority.High ? 2 : t.Priority == Priority.Medium ? 1 : 0,
                sort.Descending),
            TaskSortField.Title => OrderBy(tasks, t => t.Title, sort.Descending),
            _ => OrderBy(tasks, t => t.CreatedAt, sort.Descending),
        };

        return sorted.ThenBy(t => t.Id);
    }

    private static IOrderedQueryable<TaskItem> OrderBy<TKey>(
        IQueryable<TaskItem> tasks,
        Expression<Func<TaskItem, TKey>> key,
        bool descending) =>
        descending ? tasks.OrderByDescending(key) : tasks.OrderBy(key);

    private static ProblemHttpResult Conflict(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict);
}
