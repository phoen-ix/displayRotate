using System.Diagnostics;
using DisplayRotate.Core;
using static DisplayRotate.DocsTests.E2E.Harness;

namespace DisplayRotate.DocsTests.E2E;

/// <summary>
/// DisplayRotate.exe as a separate process (built beside the tests): the command line, and the
/// tray instance's single-instance rule and quit event. Rotations ask for the orientation each
/// display already has, so nothing on screen turns.
/// </summary>
[Trait("Category", "E2E")]
public class CommandLineJourneys
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "DisplayRotate.exe");

    [Fact]
    public void VersionPrintsTheBuildsVersion() =>
        Assert.Equal(ProductInfo.Version, Run("--version").Trim());

    [Fact]
    public void ListShowsEveryDisplayAndMarksThePrimary()
    {
        var output = Run("LIST").Split('\n');

        Assert.Contains(output, l => l.Contains("Device") && l.Contains("Monitor") && l.Contains("Resolution") && l.Contains("Orientation"));
        foreach (var d in DisplayManager.GetDisplays())
        {
            var row = Assert.Single(output, l => l.Contains(d.DeviceName));
            Assert.Contains($"{d.Width}x{d.Height}", row);
            Assert.Equal(d.IsPrimary, row.TrimEnd().EndsWith('*'));
        }
    }

    [Fact]
    public void RotateToTheCurrentAngleSaysSoAndChangesNothing()
    {
        var primary = DisplayManager.GetDisplays().Single(d => d.IsPrimary);
        var first = DisplayManager.GetDisplays()[0];

        Assert.Contains("OK: Display is already at", Run("rotate", Angle(primary.CurrentOrientation)));
        Assert.Contains("OK: Display is already at", Run("rotate", "1", Angle(first.CurrentOrientation)));
        Assert.Equal(primary.CurrentOrientation, DisplayManager.GetDisplays().Single(d => d.IsPrimary).CurrentOrientation);
    }

    [Theory]
    [InlineData("rotate 45", "Invalid angle: 45. Use 0, 90, 180, or 270.")]
    [InlineData("rotate 1 45", "Invalid angle: 45. Use 0, 90, 180, or 270.")]
    [InlineData("rotate x 90", "Invalid display number: x")]
    [InlineData("rotate 99 0", "Display 99 not found. Use 'DisplayRotate list' to see available displays.")]
    [InlineData("rotate", "Usage:")]
    [InlineData("frobnicate", "Usage:")]
    public void BadInputIsExplained(string arguments, string expected) =>
        Assert.Contains(expected, Run(arguments.Split(' ')));

    [Fact]
    public void ASecondCopyLeavesAtOnceAndTheInstallersQuitEventEndsTheFirst()
    {
        NoOtherCopy();
        using var state = new UserState();
        using var first = Process.Start(Exe)!;
        try
        {
            // Main takes the mutex first; the quit event comes later, from TrayApp's constructor.
            Wait(() => Mutex.TryOpenExisting(Names.SingleInstanceMutex, out var m) && Dispose(m!), "the tray instance to take its mutex", 30);
            Wait(() => EventWaitHandle.TryOpenExisting(Names.QuitEvent, out var e) && Dispose(e!), "the tray instance to listen for the quit event", 30);

            using (var second = Process.Start(Exe)!)
            {
                Assert.True(second.WaitForExit(10_000), "a second copy kept running");
                Assert.Equal(0, second.ExitCode);
            }

            Assert.False(first.HasExited, "the second copy took the first one down");

            using var quit = EventWaitHandle.OpenExisting(Names.QuitEvent);
            quit.Set();
            Assert.True(first.WaitForExit(10_000), "the quit event did not end the tray instance");
        }
        finally
        {
            if (!first.HasExited)
            {
                first.Kill();
            }
        }
    }

    private static string Angle(int orientation) => (orientation * 90).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Runs the exe with the arguments and returns what it printed (ASCII is compared only:
    /// the console's code page decides the rest).</summary>
    private static string Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(Exe) { RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        Assert.True(process.WaitForExit(30_000), $"DisplayRotate {string.Join(' ', arguments)} did not finish");
        return output.Result;
    }

    private static bool Dispose(IDisposable disposable)
    {
        disposable.Dispose();
        return true;
    }

    private static void Wait(Func<bool> condition, string what, double seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"waited {seconds} s for {what}");
            Thread.Sleep(100);
        }
    }
}
