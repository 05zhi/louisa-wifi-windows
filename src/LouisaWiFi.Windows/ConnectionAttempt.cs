namespace LouisaWiFi.Windows;

internal sealed class ConnectionAttempt
{
    internal TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool SawStart { get; private set; }
    internal bool ExternalChange { get; private set; }
    internal uint? FailureReason { get; private set; }

    internal void Observe(uint code, bool matches, uint reason)
    {
        if ((code == 9 && !matches) || (code == 10 && !matches && reason == 0))
        {
            ExternalChange = true;
            Completion.TrySetCanceled();
            Settled.TrySetResult();
            return;
        }
        if (!matches) return;
        if (code == 9) { SawStart = true; return; }
        if (!SawStart) return;
        if (code == 10)
        {
            if (!Completion.Task.IsCompleted)
            {
                FailureReason = reason == 0 ? null : reason;
                Completion.TrySetResult(reason == 0);
            }
            Settled.TrySetResult();
        }
        if (code == 21)
        {
            // A failed completion followed by disconnect is a failed password, not a user action.
            if (!Completion.Task.IsCompleted)
            {
                ExternalChange = true;
                Completion.TrySetCanceled();
            }
            Settled.TrySetResult();
        }
    }
}
