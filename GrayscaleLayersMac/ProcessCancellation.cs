using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GrayscaleLayersMac;

public static class ProcessCancellation
{
    private const int SigTerm = 15;
    private static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(2);

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int processId, int signal);

    public static Task WaitForExitOrTerminateAsync(
        Process process,
        CancellationToken cancellationToken) =>
        WaitForExitOrTerminateAsync(process, cancellationToken, DefaultGracePeriod);

    public static async Task WaitForExitOrTerminateAsync(
        Process process,
        CancellationToken cancellationToken,
        TimeSpan gracePeriod)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RequestCooperativeTermination(process);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(gracePeriod);
            }
            catch (TimeoutException)
            {
                ForceTerminate(process);
                await ReapBestEffortAsync(process);
            }
            catch
            {
                ForceTerminate(process);
                await ReapBestEffortAsync(process);
            }
            throw;
        }
    }

    private static void RequestCooperativeTermination(Process process)
    {
        try
        {
            if (process.HasExited)
                return;
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                _ = kill(process.Id, SigTerm);
            else
                _ = process.CloseMainWindow();
        }
        catch
        {
            // The process may have exited between the state check and signal.
        }
    }

    private static void ForceTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The process may already have exited.
        }
    }

    private static async Task ReapBestEffortAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve cancellation even if final process reaping fails.
        }
    }
}
