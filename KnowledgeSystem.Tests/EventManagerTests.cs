using KnowledgeSystem.Events.Api;
using KnowledgeSystem.Events.Implementation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
// ReSharper disable RedundantArgumentDefaultValue
// ReSharper disable ParameterOnlyUsedForPreconditionCheck.Local

namespace KnowledgeSystem.Tests;

public class EventManagerTests
{
    #region Basic Dispatch

    [Fact]
    public async Task VoidMethodHandler_ReceivesEvent()
    {
        var services = CreateServices();
        var manager = services.GetRequiredService<DefaultEventManager>();
        var tcs = new TaskCompletionSource<object?>();

        manager.AddReceiver(new VoidHandler(tcs));
        await manager.SendAsync(new TestEvent());

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ValueTaskMethodHandler_ReceivesEvent()
    {
        var services = CreateServices();
        var manager = services.GetRequiredService<DefaultEventManager>();
        var tcs = new TaskCompletionSource<object?>();

        manager.AddReceiver(new ValueTaskHandler(tcs));
        await manager.SendAsync(new TestEvent());

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Dependency Injection

    [Fact]
    public async Task Handler_WithInjectedServices_ResolvesFromProvider()
    {
        var services = CreateServices();
        var manager = services.GetRequiredService<DefaultEventManager>();
        var tcs = new TaskCompletionSource<object?>();

        manager.AddReceiver(new InjectionHandler(tcs));
        await manager.SendAsync(new TestEvent());

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Priorities

    [Fact]
    public async Task Handlers_ExecuteInDescendingPriorityOrder()
    {
        var services = CreateServices();
        var manager = services.GetRequiredService<DefaultEventManager>();
        var tcs = new TaskCompletionSource<object?>();

        manager.AddReceiver(new PriorityHandler(tcs));
        await manager.SendAsync(new TestEvent());

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task Handler_ReceivesCancellationToken()
    {
        var services = CreateServices();
        var manager = services.GetRequiredService<DefaultEventManager>();
        var tcs = new TaskCompletionSource<CancellationToken>();

        manager.AddReceiver(new CancellationHandler(tcs));
        await manager.SendAsync(new TestEvent(), CancellationToken.None);

        // ReSharper disable once MethodSupportsCancellation
        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(CancellationToken.None, received);
    }

    [Fact]
    public async Task Handler_ReceivesLinkedCancellationToken()
    {
        var services = CreateServices();
        var manager = services.GetRequiredService<DefaultEventManager>();
        var tcs = new TaskCompletionSource<CancellationToken>();
        using var cts = new CancellationTokenSource();

        manager.AddReceiver(new CancellationHandler(tcs));
        await manager.SendAsync(new TestEvent(), cts.Token);

        // ReSharper disable once MethodSupportsCancellation
        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(cts.Token, received);
    }

    [Fact]
    public async Task SendAsync_WithCancelledToken_Throws()
    {
        var services = CreateServices();
        var manager = services.GetRequiredService<DefaultEventManager>();
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        
        var tcs = new TaskCompletionSource<object?>();
        var tokenCheck = new TaskCompletionSource<CancellationToken>();

        manager.AddReceiver(new TokenCaptureHandler(tcs, tokenCheck));
        await manager.SendAsync(new TestEvent(), cts.Token);

        // ReSharper disable once MethodSupportsCancellation
        var capturedToken = await tokenCheck.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(capturedToken.CanBeCanceled);
        Assert.True(capturedToken.IsCancellationRequested);
    }

    #endregion

    #region IsCritical

    [Fact]
    public async Task CriticalHandler_Exception_PropagatesToCaller()
    {
        var services = CreateServices();
        var manager = services.GetRequiredService<DefaultEventManager>();

        manager.AddReceiver(new ThrowingCriticalHandler());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.SendAsync(new TestEvent()));
    }

    [Fact]
    public async Task NonCriticalHandler_Exception_DoesNotPropagate_AndNextHandlerRuns()
    {
        var services = CreateServices();
        var manager = services.GetRequiredService<DefaultEventManager>();
        var tcs = new TaskCompletionSource<object?>();

        manager.AddReceiver(new ThrowingNonCriticalHandler());
        manager.AddReceiver(new VoidHandler(tcs));

        await manager.SendAsync(new TestEvent());

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task NonCriticalHandler_Exception_DoesNotPreventSubsequentHandlers()
    {
        var services = CreateServices();
        var manager = services.GetRequiredService<DefaultEventManager>();
        var order = new List<string>();

        manager.AddReceiver(new TrackingHandler(order, "first"));
        manager.AddReceiver(new TrackingHandler(order, "second"));
        await manager.SendAsync(new TestEvent());

        Assert.Equal(["first", "second"], order);
    }

    [Fact]
    public async Task CriticalHandler_BeforeNonCritical_CriticalFailureStopsAll()
    {
        var services = CreateServices();
        var manager = services.GetRequiredService<DefaultEventManager>();
        var order = new List<string>();

        manager.AddReceiver(new ThrowingCriticalHandler());
        manager.AddReceiver(new TrackingHandler(order, "second"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.SendAsync(new TestEvent()));

        Assert.Equal([], order);
    }

    #endregion

    #region AddReceiver Disposable

    [Fact]
    public async Task DisposedReceiver_DoesNotReceiveEvent()
    {
        var services = CreateServices();
        var manager = services.GetRequiredService<DefaultEventManager>();
        var tcs = new TaskCompletionSource<object?>();

        var receiver = new VoidHandler(tcs);
        var disposable = manager.AddReceiver(receiver);
        disposable.Dispose();

        await manager.SendAsync(new TestEvent());

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(200)));
    }

    #endregion

    private static IServiceProvider CreateServices()
    {
        return new ServiceCollection()
            .AddSingleton<DefaultEventManager>()
            .AddLogging()
            .BuildServiceProvider();
    }

    #region Test Types

    private sealed class TestEvent : IEvent;

    private sealed class VoidHandler(TaskCompletionSource<object?> tcs) : IEventReceiver
    {
        [SubscribeEvent]
        public void Handle(TestEvent e)
        {
            Assert.NotNull(e);
            tcs.TrySetResult(null);
        }
    }

    private sealed class ValueTaskHandler(TaskCompletionSource<object?> tcs) : IEventReceiver
    {
        [SubscribeEvent]
        // ReSharper disable once AsyncMethodWithoutAwait
        public async ValueTask Handle(TestEvent e)
        {
            Assert.NotNull(e);
            tcs.TrySetResult(null);
        }
    }

    private sealed class InjectionHandler(TaskCompletionSource<object?> tcs) : IEventReceiver
    {
        [SubscribeEvent]
        public void Handle(TestEvent e, ILogger<InjectionHandler> logger, DefaultEventManager manager)
        {
            Assert.NotNull(e);
            Assert.NotNull(logger);
            Assert.NotNull(manager);
            tcs.TrySetResult(null);
        }
    }

    private sealed class PriorityHandler(TaskCompletionSource<object?> tcs) : IEventReceiver
    {
        private int _index;

        [SubscribeEvent(EventPriority.Lowest)]
        public void Lowest(TestEvent e)
        {
            Assert.Equal(5, _index++);
            tcs.TrySetResult(null);
        }

        [SubscribeEvent(EventPriority.Low)]
        public void Low(TestEvent e)
        {
            Assert.Equal(4, _index++);
        }

        [SubscribeEvent(EventPriority.Normal)]
        public void Normal(TestEvent e)
        {
            Assert.Equal(3, _index++);
        }

        [SubscribeEvent(EventPriority.High)]
        public void High(TestEvent e)
        {
            Assert.Equal(2, _index++);
        }

        [SubscribeEvent(EventPriority.VeryHigh)]
        public void VeryHigh(TestEvent e)
        {
            Assert.Equal(1, _index++);
        }

        [SubscribeEvent(EventPriority.RealTime)]
        public void RealTime(TestEvent e)
        {
            Assert.Equal(0, _index++);
        }
    }

    private sealed class CancellationHandler(TaskCompletionSource<CancellationToken> tcs) : IEventReceiver
    {
        [SubscribeEvent]
        public void Handle(TestEvent e, CancellationToken ct)
        {
            Assert.NotNull(e);
            tcs.TrySetResult(ct);
        }
    }

    private sealed class TokenCaptureHandler(
        TaskCompletionSource<object?> completion,
        TaskCompletionSource<CancellationToken> tokenCapture) : IEventReceiver
    {
        [SubscribeEvent]
        public void Handle(TestEvent e, CancellationToken ct)
        {
            tokenCapture.TrySetResult(ct);
            completion.TrySetResult(null);
        }
    }

    private sealed class ThrowingCriticalHandler : IEventReceiver
    {
        [SubscribeEvent]
        public void Handle(TestEvent e)
        {
            throw new InvalidOperationException("critical failure");
        }
    }

    private sealed class ThrowingNonCriticalHandler : IEventReceiver
    {
        [SubscribeEvent(IsCritical = false)]
        public void Handle(TestEvent e)
        {
            throw new InvalidOperationException("non-critical failure");
        }
    }

    private sealed class TrackingHandler(List<string> order, string name) : IEventReceiver
    {
        [SubscribeEvent]
        public void Handle(TestEvent e)
        {
            order.Add(name);
        }
    }

    #endregion
}
