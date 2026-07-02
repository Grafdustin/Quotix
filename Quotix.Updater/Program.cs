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

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Log("========================================");
                Log($"Quotix Updater v1.0.12 started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Log($"Arguments count: {args.Length}");

                if (args.Length < 2)
                {
                    Log("Error: Insufficient arguments");
                    Log("Usage: Quotix.Updater.exe <installer-path> <main-app-path>");
                    Log($"Received args: {string.Join(" | ", args)}");
                    Thread.Sleep(3000);
                    return;
                }

                string installerPath = args[0];
                string mainAppPath = args[1];

                Log($"Installer path: {installerPath}");
                Log($"Main app path: {mainAppPath}");

                // 验证文件是否存在
                if (!File.Exists(installerPath))
                {
                    Log($"Error: Installer not found: {installerPath}");
                    Thread.Sleep(3000);
                    return;
                }

                if (!File.Exists(mainAppPath))
                {
                    Log($"Warning: Main app not found: {mainAppPath}");
                    // 不返回，继续尝试
                }

                // Step 1: Wait for main app to exit
                Log("Waiting for main app to exit...");
                Thread.Sleep(3000); // 等待 3 秒
                Log("");

                // Step 2: Run installer
                Log("Running installer...");
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = installerPath,
                        Arguments = "/SILENT /NORESTART",
                        UseShellExecute = true,
                        CreateNoWindow = false
                    };

                    Log($"Starting installer: {startInfo.FileName} {startInfo.Arguments}");

                    var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        Log($"Installer started (PID: {process.Id}), waiting for exit...");
                        process.WaitForExit();
                        Log($"Installation completed (Exit code: {process.ExitCode})");
                    }
                    else
                    {
                        Log("Error: Failed to start installer (process is null).");
                        Thread.Sleep(3000);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error starting installer: {ex.Message}");
                    Log($"Stack trace: {ex.StackTrace}");
                    Thread.Sleep(3000);
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
                        // Get the directory of main app (for WorkingDirectory)
                        var mainAppDir = Path.GetDirectoryName(mainAppPath);
                        
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = mainAppPath,
                            UseShellExecute = true,
                            WorkingDirectory = mainAppDir  // Set working directory to Launcher\
                        };

                        Process.Start(startInfo);
                        Log($"Main app started (WorkingDirectory: {mainAppDir}).");
                    }
                    else
                    {
                        Log($"Error: Main app not found: {mainAppPath}");
                        Log("Trying default path...");

                        // 尝试默认路径
                        var defaultPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Programs", "Quotix", "Launcher", "Quotix.exe");

                        if (File.Exists(defaultPath))
                        {
                            var defaultDir = Path.GetDirectoryName(defaultPath);
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = defaultPath,
                                UseShellExecute = true,
                                WorkingDirectory = defaultDir  // Also set working directory
                            });
                            Log($"Main app started (default path): {defaultPath}");
                        }
                        else
                        {
                            Log($"Error: Main app not found at default path either: {defaultPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error: Failed to start main app: {ex.Message}");
                    Log($"Stack trace: {ex.StackTrace}");
                }
                Log("");

                Log("Update completed successfully!");
                Log("========================================");
                Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                Log($"Unhandled exception: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                Thread.Sleep(3000);
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
