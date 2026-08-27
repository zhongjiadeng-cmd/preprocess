using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class ProcessCancellationTests
{
    [TestMethod]
    public async Task CancellationRequestsCooperativeTerminationBeforeForceKill()
    {
        var marker = Path.Combine(
            Path.GetTempPath(),
            $"grayscale-layers-term-{Guid.NewGuid():N}");
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "python3",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(
                "import pathlib,signal,sys,time; " +
                "marker=pathlib.Path(sys.argv[1]); " +
                "signal.signal(signal.SIGTERM, lambda *_: (marker.write_text('term'), sys.exit(0))); " +
                "print('ready', flush=True); time.sleep(30)");
            info.ArgumentList.Add(marker);
            using var process = Process.Start(info)!;
            Assert.AreEqual("ready", await process.StandardOutput.ReadLineAsync());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                ProcessCancellation.WaitForExitOrTerminateAsync(
                    process,
                    cancellation.Token,
                    TimeSpan.FromSeconds(2)));

            Assert.IsTrue(process.HasExited);
            Assert.IsTrue(File.Exists(marker),
                "SIGTERM handler must run before any force-kill fallback.");
        }
        finally
        {
            File.Delete(marker);
        }
    }
}
