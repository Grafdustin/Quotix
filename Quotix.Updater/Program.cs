using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Quotix.Updater
{
    class Program
    {
        static string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quotix", "Updater.log");

        static void Main(string[] args)
        {
            try
            {
                Log("Quotix Updater v1.0.11 started");

                if (args.Length < 2)
                {
                    Log("Error: Insufficient arguments");
                    Log("Usage: Quotix.Updater.exe <installer-path> <main-app-path>");
                    return;
                }

                string installerPath = args[0];
                string mainAppPath = args[1];

                Log($"Installer: {installerPath}");
                Log($"Main App: {mainAppPath}");

                // Step 1: Wait for main app to exit
                Log("Waiting for main app to exit...");
                Thread.Sleep(5000);
                Log("");

                // Step 2: Run installer silently
                Log("Running installer...");
                try
                {
                    var process = Process.Start(installerPath, "/SILENT /NORESTART");
                    if (process != null)
                    {
                        process.WaitForExit();
                        Log("Installation completed.");
                    }
                    else
                    {
                        Log("Error: Failed to start installer.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error: {ex.Message}");
                    return;
                }
                Log("");

                // Step 3: Delete installer
                Log("Deleting installer...");
                try
                {
                    if (File.Exists(installerPath))
                    {
                        File.Delete(installerPath);
                        Log("Installer deleted.");
                    }
                    else
                    {
                        Log("Installer not found (may have been deleted by installer).");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Warning: Failed to delete installer: {ex.Message}");
                }
                Log("");

                // Step 4: Restart main app
                Log("Starting main app...");
                try
                {
                    if (File.Exists(mainAppPath))
                    {
                        Process.Start(mainAppPath);
                        Log("Main app started.");
                    }
                    else
                    {
                        Log($"Error: Main app not found: {mainAppPath}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error: Failed to start main app: {ex.Message}");
                }
                Log("");

                Log("Update completed successfully!");
                Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                Log($"Unhandled exception: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
            }
        }

        static void Log(string message)
        {
            try
            {
                var logDir = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch
            {
                // Ignore logging errors
            }
        }
    }
}
