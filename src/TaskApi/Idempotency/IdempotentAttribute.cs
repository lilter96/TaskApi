namespace TaskApi.Idempotency;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class IdempotentAttribute : Attribute;
