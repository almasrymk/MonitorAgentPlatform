using System.Collections.Concurrent;
using System.Reflection;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Common;

/// <summary>Creates a failed <c>Result</c> or <c>Result&lt;T&gt;</c> when the response type is only known generically.</summary>
public static class ResultFactory
{
    private static readonly ConcurrentDictionary<Type, Func<Error, Result>> Factories = new();

    public static TResult Failure<TResult>(Error error)
        where TResult : Result =>
        (TResult)Factories.GetOrAdd(typeof(TResult), Create)(error);

    private static Func<Error, Result> Create(Type type)
    {
        if (type == typeof(Result))
            return Result.Failure;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var method = type.GetMethod(nameof(Result.Failure), BindingFlags.Public | BindingFlags.Static, [typeof(Error)])!;
            return error => (Result)method.Invoke(null, [error])!;
        }

        throw new InvalidOperationException($"{type} is not a Result type.");
    }
}
