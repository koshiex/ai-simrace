using System.Runtime.InteropServices;

namespace SimCoach.Adapters.ACC;

/// <summary>
/// Raises the Windows multimedia timer resolution to 1 ms for the lifetime of the instance.
/// Without this, short waits in the poll loop (<c>WaitOne(1 ms)</c>) are rounded up to the
/// default scheduler tick (~15.6 ms), which caps the ACC poll — and therefore the emitted
/// frame rate — at ~64 Hz instead of the game's 333 Hz (verified on real hardware, plan B7).
/// Construct around the poll loop and dispose to restore the previous resolution. No-op on
/// non-Windows, so the cross-platform reader can use it unconditionally.
/// </summary>
internal sealed partial class WinTimerResolution : IDisposable
{
    private const uint PeriodMilliseconds = 1;
    private const uint TimerrNoError = 0;

    private readonly bool _raised;

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint uPeriod);

    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static partial uint TimeEndPeriod(uint uPeriod);

    public WinTimerResolution()
    {
        if (OperatingSystem.IsWindows())
        {
            _raised = TimeBeginPeriod(PeriodMilliseconds) == TimerrNoError;
        }
    }

    public void Dispose()
    {
        if (_raised && OperatingSystem.IsWindows())
        {
            TimeEndPeriod(PeriodMilliseconds);
        }
    }
}
