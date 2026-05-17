using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class TaskUtilsTests
{
    [Fact]
    public async Task WaitAsync_AlreadyCompletedTask_CompletesWithoutCreatingTcs()
    {
        await Should.NotThrowAsync(
            () => TaskUtils.WaitAsync(Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task WaitAsync_TaskCompletesNormally_DoesNotThrow()
    {
        var tcs = new TaskCompletionSource<bool>();
        var waitTask = TaskUtils.WaitAsync(tcs.Task, CancellationToken.None);
        tcs.SetResult(true);
        await Should.NotThrowAsync(() => waitTask);
    }

    [Fact]
    public async Task WaitAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var tcs = new TaskCompletionSource<bool>();

        await Should.ThrowAsync<OperationCanceledException>(
            () => TaskUtils.WaitAsync(tcs.Task, cts.Token));
    }

    [Fact]
    public async Task WaitAsync_TokenCancelledDuringWait_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<bool>();

        var waitTask = TaskUtils.WaitAsync(tcs.Task, cts.Token);
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => waitTask);
    }

    [Fact]
    public async Task WaitAsync_FaultedTask_PropagatesException()
    {
        var tcs = new TaskCompletionSource<bool>();
        var waitTask = TaskUtils.WaitAsync(tcs.Task, CancellationToken.None);
        tcs.SetException(new InvalidOperationException("boom"));

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => waitTask);
        ex.Message.ShouldBe("boom");
    }

    [Fact]
    public async Task WaitAsync_FaultedTask_PropagatesMultipleExceptions()
    {
        var tcs = new TaskCompletionSource<bool>();
        var waitTask = TaskUtils.WaitAsync(tcs.Task, CancellationToken.None);
        tcs.SetException(new Exception[]
        {
            new InvalidOperationException("error1"),
            new ArgumentException("error2")
        });

        var ex = await Should.ThrowAsync<Exception>(() => waitTask);
        ex.ShouldNotBeNull();
    }

    [Fact]
    public async Task WaitAsync_CancelledTask_PropagatesCancellation()
    {
        var tcs = new TaskCompletionSource<bool>();
        var waitTask = TaskUtils.WaitAsync(tcs.Task, CancellationToken.None);
        tcs.SetCanceled();

        await Should.ThrowAsync<OperationCanceledException>(() => waitTask);
    }

    [Fact]
    public async Task WaitAsyncT_AlreadyCompletedTask_ReturnsResultDirectly()
    {
        var result = await TaskUtils.WaitAsync(Task.FromResult(42), CancellationToken.None);
        result.ShouldBe(42);
    }

    [Fact]
    public async Task WaitAsyncT_TaskCompletesWithResult_ReturnsCorrectValue()
    {
        var tcs = new TaskCompletionSource<int>();
        var waitTask = TaskUtils.WaitAsync(tcs.Task, CancellationToken.None);
        tcs.SetResult(99);

        var result = await waitTask;
        result.ShouldBe(99);
    }

    [Fact]
    public async Task WaitAsyncT_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var tcs = new TaskCompletionSource<int>();

        await Should.ThrowAsync<OperationCanceledException>(
            () => TaskUtils.WaitAsync(tcs.Task, cts.Token));
    }

    [Fact]
    public async Task WaitAsyncT_TokenCancelledDuringWait_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<int>();

        var waitTask = TaskUtils.WaitAsync(tcs.Task, cts.Token);
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => waitTask);
    }

    [Fact]
    public async Task WaitAsyncT_FaultedTask_PropagatesException()
    {
        var tcs = new TaskCompletionSource<int>();
        var waitTask = TaskUtils.WaitAsync(tcs.Task, CancellationToken.None);
        tcs.SetException(new ArgumentException("bad arg"));

        var ex = await Should.ThrowAsync<ArgumentException>(() => waitTask);
        ex.Message.ShouldBe("bad arg");
    }

    [Fact]
    public async Task WaitAsyncT_CancelledTask_PropagatesCancellation()
    {
        var tcs = new TaskCompletionSource<int>();
        var waitTask = TaskUtils.WaitAsync(tcs.Task, CancellationToken.None);
        tcs.SetCanceled();

        await Should.ThrowAsync<OperationCanceledException>(() => waitTask);
    }

    [Fact]
    public async Task WaitAsyncT_AlreadyCompletedTask_ReturnsReferenceType()
    {
        var expected = "hello";
        var result = await TaskUtils.WaitAsync(Task.FromResult(expected), CancellationToken.None);
        result.ShouldBeSameAs(expected);
    }
}
