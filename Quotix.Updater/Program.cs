using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Quotix.Updater
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Quotix Updater v1.0.10");
            Console.WriteLine("");

            if (args.Length < 2)
            {
                Console.WriteLine("Usage: Quotix.Updater.exe <installer-path> <main-app-path>");
                Console.WriteLine("");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            string installerPath = args[0];
            string mainAppPath = args[1];

            Console.WriteLine($"Installer: {installerPath}");
            Console.WriteLine($"Main App: {mainAppPath}");
            Console.WriteLine("");

            // Step 1: Wait for main app to exit
            Console.WriteLine("Waiting for main app to exit...");
            Thread.Sleep(5000); // Wait 5 seconds
            Console.WriteLine("");

            // Step 2: Run installer silently
            Console.WriteLine("Running installer...");
            try
            {
                var process = Process.Start(installerPath, "/SILENT /NORESTART");
                if (process != null)
                {
                    process.WaitForExit();
                    Console.WriteLine("Installation completed.");
                }
                else
                {
                    Console.WriteLine("Error: Failed to start installer.");
                    Console.WriteLine("");
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }
            Console.WriteLine("");

            // Step 3: Delete installer
            Console.WriteLine("Deleting installer...");
            try
            {
                if (File.Exists(installerPath))
                {
                    File.Delete(installerPath);
                    Console.WriteLine("Installer deleted.");
                }
                else
                {
                    Console.WriteLine("Installer not found (may have been deleted by installer).");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to delete installer: {ex.Message}");
            }
            Console.WriteLine("");

            // Step 4: Restart main app
            Console.WriteLine("Starting main app...");
            try
            {
                if (File.Exists(mainAppPath))
                {
                    Process.Start(mainAppPath);
                    Console.WriteLine("Main app started.");
                }
                else
                {
                    Console.WriteLine($"Error: Main app not found: {mainAppPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: Failed to start main app: {ex.Message}");
            }
            Console.WriteLine("");

            Console.WriteLine("Update completed successfully!");
            Thread.Sleep(2000); // Wait 2 seconds before exiting
        }
    }
}
