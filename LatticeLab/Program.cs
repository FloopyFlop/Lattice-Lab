using Avalonia;
using LatticeLab.Logic;

namespace LatticeLab;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--validate")
        {
            ValidateCircuitFile(args[1]);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }

    private static void ValidateCircuitFile(string path)
    {
        try
        {
            var project = LegacyCircuitLoader.LoadProject(path);
            int totalGates = project.Circuits.Sum(c => c.Gates.Count);
            int totalWires = project.Circuits.Sum(c => c.Wires.Count);
            Console.WriteLine(
                $"Loaded '{path}' successfully. Circuits={project.Circuits.Count}, Gates={totalGates}, Wires={totalWires}.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Validation failed: " + ex.Message);
            Environment.ExitCode = 1;
        }
    }
}
