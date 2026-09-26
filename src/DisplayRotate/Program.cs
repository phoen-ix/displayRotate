namespace DisplayRotate;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            RunCli(args);
            return;
        }

        // Hide the console window in tray mode
        var consoleWindow = NativeMethods.GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
            NativeMethods.ShowWindow(consoleWindow, NativeMethods.SW_HIDE);
        NativeMethods.FreeConsole();

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }

    private static void RunCli(string[] args)
    {
        string command = args[0].ToLowerInvariant();

        switch (command)
        {
            case "list":
                ListDisplays();
                break;

            case "rotate" when args.Length >= 2:
                HandleRotate(args);
                break;

            default:
                PrintUsage();
                break;
        }
    }

    private static void ListDisplays()
    {
        var displays = DisplayManager.GetDisplays();
        Console.WriteLine();
        Console.WriteLine($"  {"#",-3} {"Device",-16} {"Monitor",-28} {"Resolution",-14} {"Orientation",-22} {"Primary"}");
        Console.WriteLine($"  {"—",-3} {"——————",-16} {"———————",-28} {"——————————",-14} {"———————————",-22} {"———————"}");

        foreach (var d in displays)
        {
            Console.WriteLine(
                $"  {d.Index,-3} {d.DeviceName,-16} {d.FriendlyName,-28} " +
                $"{d.Width}x{d.Height,-8} {DisplayManager.GetOrientationName(d.CurrentOrientation),-22} " +
                $"{(d.IsPrimary ? "*" : "")}");
        }
        Console.WriteLine();
    }

    private static void HandleRotate(string[] args)
    {
        int? angle;
        string? deviceName;

        if (args.Length == 2)
        {
            // rotate <angle> — rotate primary
            angle = DisplayManager.ParseAngle(args[1]);
            if (angle is null)
            {
                Console.WriteLine($"\n  Invalid angle: {args[1]}. Use 0, 90, 180, or 270.\n");
                return;
            }
            var primary = DisplayManager.GetDisplays().FirstOrDefault(d => d.IsPrimary);
            if (primary is null)
            {
                Console.WriteLine("\n  No primary display found.\n");
                return;
            }
            deviceName = primary.DeviceName;
        }
        else
        {
            // rotate <display#> <angle>
            if (!int.TryParse(args[1], out int displayIndex))
            {
                Console.WriteLine($"\n  Invalid display number: {args[1]}\n");
                return;
            }
            angle = DisplayManager.ParseAngle(args[2]);
            if (angle is null)
            {
                Console.WriteLine($"\n  Invalid angle: {args[2]}. Use 0, 90, 180, or 270.\n");
                return;
            }
            var display = DisplayManager.GetDisplays().FirstOrDefault(d => d.Index == displayIndex);
            if (display is null)
            {
                Console.WriteLine($"\n  Display {displayIndex} not found. Use 'DisplayRotate list' to see available displays.\n");
                return;
            }
            deviceName = display.DeviceName;
        }

        var (success, message) = DisplayManager.Rotate(deviceName, angle.Value);
        Console.WriteLine($"\n  {(success ? "OK" : "FAILED")}: {message}\n");
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"
  DisplayRotate — Screen Rotation Tool

  Usage:
    DisplayRotate                       Start in system tray
    DisplayRotate list                  List connected displays
    DisplayRotate rotate <angle>        Rotate primary display (0, 90, 180, 270)
    DisplayRotate rotate <#> <angle>    Rotate display # to angle

  Hotkeys (when running in tray):
    Ctrl+Alt+Up      Rotate primary to 0° (Landscape)
    Ctrl+Alt+Right   Rotate primary to 90° (Portrait)
    Ctrl+Alt+Down    Rotate primary to 180° (Landscape flipped)
    Ctrl+Alt+Left    Rotate primary to 270° (Portrait flipped)
");
    }
}
