namespace FrameWrench;

/// <summary>
/// Cross-target polyfills for <see cref="Task"/> methods not available on
/// .NET Framework 4.x / .NET Standard 2.0.
/// </summary>
internal static class TaskUtils
{
    /// <summary>
    /// Waits for <paramref name="task"/> to complete, honouring <paramref name="ct"/>.
    /// </summary>
    /// <remarks>
    /// On .NET 6+ <c>Task.WaitAsync(CancellationToken)</c> is used natively.
    /// On older targets a <see cref="TaskCompletionSource{T}"/> + registration is used.
    /// </remarks>
    public static async Task WaitAsync(Task task, CancellationToken ct)
    {
#if NET6_0_OR_GREATER
        await task.WaitAsync(ct).ConfigureAwait(false);
#else
        if (task.IsCompleted) return;

        ct.ThrowIfCancellationRequested();

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var reg = ct.Register(
            state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
            tcs);

        _ = task.ContinueWith(t =>
        {
            if      (t.IsFaulted)   tcs.TrySetException(t.Exception!.InnerExceptions);
            else if (t.IsCanceled)  tcs.TrySetCanceled();
            else                    tcs.TrySetResult(true);
        }, TaskContinuationOptions.ExecuteSynchronously);

        await tcs.Task.ConfigureAwait(false);
#endif
    }

    /// <summary>
    /// Generic variant of <see cref="WaitAsync(Task, CancellationToken)"/>.
    /// </summary>
    public static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken ct)
    {
#if NET6_0_OR_GREATER
        return await task.WaitAsync(ct).ConfigureAwait(false);
#else
        if (task.IsCompleted) return task.Result;

        ct.ThrowIfCancellationRequested();

        var tcs = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var reg = ct.Register(
            state => ((TaskCompletionSource<T>)state!).TrySetCanceled(),
            tcs);

        _ = task.ContinueWith(t =>
        {
            if      (t.IsFaulted)   tcs.TrySetException(t.Exception!.InnerExceptions);
            else if (t.IsCanceled)  tcs.TrySetCanceled();
            else                    tcs.TrySetResult(t.Result);
        }, TaskContinuationOptions.ExecuteSynchronously);

        return await tcs.Task.ConfigureAwait(false);
#endif
    }
}
