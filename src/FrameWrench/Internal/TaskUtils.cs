namespace FrameWrench;

/// <summary>
/// Cross-target helpers for waiting on <see cref="Task"/> with a
/// <see cref="CancellationToken"/> (no dependency on .NET 6+ APIs).
/// </summary>
internal static class TaskUtils
{
    /// <summary>
    /// Waits for <paramref name="task"/> to complete, honouring <paramref name="ct"/>.
    /// </summary>
    public static async Task WaitAsync(Task task, CancellationToken ct)
    {
        if (task.IsCompleted) 
            return;

        ct.ThrowIfCancellationRequested();

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var reg = ct.Register(
            // Pass tcs as explicit state to avoid capturing it in a closure, which would
            // allocate on every call even when cancellation is never requested.
            state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
            tcs);

        _ = task.ContinueWith(t =>
        {
            if (t.IsFaulted) tcs.TrySetException(t.Exception!.InnerExceptions);
            else if (t.IsCanceled) tcs.TrySetCanceled();
            else tcs.TrySetResult(true);
        }, TaskContinuationOptions.ExecuteSynchronously);

        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Generic variant of <see cref="WaitAsync(Task, CancellationToken)"/>.
    /// </summary>
    public static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken ct)
    {
        if (task.IsCompleted) 
            return task.Result;

        ct.ThrowIfCancellationRequested();

        var tcs = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var reg = ct.Register(
            // Same closure-avoidance pattern as the non-generic overload above.
            state => ((TaskCompletionSource<T>)state!).TrySetCanceled(),
            tcs);

        _ = task.ContinueWith(t =>
        {
            if (t.IsFaulted) tcs.TrySetException(t.Exception!.InnerExceptions);
            else if (t.IsCanceled) tcs.TrySetCanceled();
            else tcs.TrySetResult(t.Result);
        }, TaskContinuationOptions.ExecuteSynchronously);

        return await tcs.Task.ConfigureAwait(false);
    }
}
