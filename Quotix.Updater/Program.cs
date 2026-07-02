using System.Diagnostics;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Quotix Updater v1.0.9");
Console.WriteLine("Usage: Quotix.Updater.exe <installer-path> <main-app-path>");
Console.WriteLine("");

if (args.Length < 2)
{
    Console.WriteLine("Error: Missing arguments");
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
    return;
}

var installerPath = args[0];
var mainAppPath = args[1];

Console.WriteLine($"Installer: {installerPath}");
Console.WriteLine($"Main App: {mainAppPath}");
Console.WriteLine("");

// Wait for main app to exit
Console.WriteLine("Waiting for main app to exit...");
await Task.Delay(5000);

// Run installer
Console.WriteLine("Running installer...");
var processStartInfo = new ProcessStartInfo
{
    FileName = installerPath,
    Arguments = "/SILENT /NORESTART",
    UseShellExecute = true
};

try
{
    var process = Process.Start(processStartInfo);
    if (process != null)
    {
        process.WaitForExit();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error running installer: {ex.Message}");
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
    return;
}

// Delete installer
Console.WriteLine("Deleting installer...");
try
{
    if (File.Exists(installerPath))
    {
        File.Delete(installerPath);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Could not delete installer: {ex.Message}");
}

// Restart main app
Console.WriteLine("Starting main app...");
try
{
    var startInfo = new ProcessStartInfo
    {
        FileName = mainAppPath,
        UseShellExecute = true
    };
    Process.Start(startInfo);
}
catch (Exception ex)
{
    Console.WriteLine($"Error starting main app: {ex.Message}");
}

Console.WriteLine("Update complete!");
