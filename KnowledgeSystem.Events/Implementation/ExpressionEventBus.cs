using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using KnowledgeSystem.Events.Api;
using KnowledgeSystem.Events.Extensions;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace KnowledgeSystem.Events.Implementation;

internal class ExpressionEventBus : IEventBus
{
    private static readonly ConcurrentDictionary<Type, ExpressionEventBus[]> Instances = new();
    private readonly Func<object?, object, IServiceProvider, CancellationToken, ValueTask> _invoker;
    private readonly Type _eventListenerType;

    private ExpressionEventBus(Type eventType, MethodInfo method, SubscribeEvent attribute, Type eventListenerType)
    {
        EventType = eventType;
        Method = method.PrettyPrint();
        Priority = attribute.Priority;
        IsCritical = attribute.IsCritical;
        _eventListenerType = eventListenerType;

        _invoker = CreateInvoker(method);
    }

    public Type EventType { get; }

    public EventPriority Priority { get; }

    public bool IsCritical { get; }

    public string Method { get; }

    public static IReadOnlyList<ExpressionEventBus> FromType(Type type)
    {
        return Instances.GetOrAdd(type, t =>
        {
            return t
                .GetMethods(
                    BindingFlags.Instance | 
                    BindingFlags.Public |
                    BindingFlags.NonPublic)
                .Where(m => 
                    !m.IsStatic 
                    && m.GetCustomAttributes(typeof(SubscribeEvent), false).Any())
                .SelectMany(m => FromMethod(t, m))
                .ToArray();
        });
    }

    public static IEnumerable<ExpressionEventBus> FromMethod(Type listenerType, MethodInfo methodType)
    {
        var returnType = methodType.ReturnType;

        if (returnType != typeof(void) && returnType != typeof(ValueTask))
        {
            throw new Exception($"The method {methodType.PrettyPrint()} doesn't return void or ValueTask.");
        }

        foreach (var attribute in methodType.GetCustomAttributes<SubscribeEvent>(false))
        {
            var eventType = attribute.EventType;

            if (eventType == null)
            {
                if (
                    methodType.GetParameters().Length == 0
                    || !typeof(IEvent).IsAssignableFrom(methodType.GetParameters()[0].ParameterType))
                {
                    throw new Exception($"The first parameter of the method {methodType.PrettyPrint()} should be assignable to {nameof(IEvent)}.");
                }

                eventType = methodType.GetParameters()[0].ParameterType;
            }

            yield return new ExpressionEventBus(eventType, methodType, attribute, listenerType);
        }
    }

    public ValueTask InvokeAsync(object? eventHandler, object @event, IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        return _invoker(eventHandler, @event, provider, cancellationToken);
    }

    private Func<object?, object, IServiceProvider, CancellationToken, ValueTask> CreateInvoker(MethodInfo method)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var eventParameter = Expression.Parameter(typeof(object), "event");
        var provider = Expression.Parameter(typeof(IServiceProvider), "provider");
        var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        var @event = Expression.Convert(eventParameter, EventType);

        var getRequiredService = typeof(ServiceProviderServiceExtensions).GetMethod("GetRequiredService", [typeof(IServiceProvider)]);

        if (getRequiredService == null)
        {
            throw new InvalidOperationException("The method GetRequiredService could not be found.");
        }

        var methodArguments = method.GetParameters();
        var arguments = new Expression[methodArguments.Length];

        for (var i = 0; i < methodArguments.Length; i++)
        {
            var methodArgument = methodArguments[i];

            if (typeof(IEvent).IsAssignableFrom(methodArgument.ParameterType) && methodArgument.ParameterType.IsAssignableFrom(EventType))
            {
                arguments[i] = @event;
            }
            else if (methodArgument.ParameterType == typeof(CancellationToken))
            {
                arguments[i] = cancellationTokenParameter;
            }
            else
            {
                arguments[i] = Expression.Call(getRequiredService.MakeGenericMethod(methodArgument.ParameterType), provider);
            }
        }

        var returnTarget = Expression.Label(typeof(ValueTask));
        Expression invoke = Expression.Call(Expression.Convert(instance, _eventListenerType), method, arguments);

        if (method.ReturnType == typeof(void))
        {
            invoke = Expression.Block(invoke, Expression.Label(returnTarget, Expression.Default(typeof(ValueTask))));
        }
        else if (method.ReturnType != typeof(ValueTask))
        {
            throw new Exception($"The method {method.PrettyPrint()} must return void or ValueTask.");
        }

        return Expression
            .Lambda<Func<object?, object, IServiceProvider, CancellationToken, ValueTask>>(
                invoke,
                instance, 
                eventParameter,
                provider,
                cancellationTokenParameter
            )
            .Compile();
    }
}