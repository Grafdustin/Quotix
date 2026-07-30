using System.IO;
using System.Runtime.Serialization.Json;
using System.Windows;

namespace Quotix.Updater;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var requestPath = GetRequestPath(e.Args);
            UpdateRequest request;
            using (var stream = File.OpenRead(requestPath))
            {
                request = new DataContractJsonSerializer(typeof(UpdateRequest))
                    .ReadObject(stream) as UpdateRequest
                    ?? throw new InvalidDataException("更新请求无效");
            }

            var window = new MainWindow(request);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Quotix 更新", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string GetRequestPath(string[] args)
    {
        var index = Array.FindIndex(args, value =>
            string.Equals(value, "--request", StringComparison.OrdinalIgnoreCase));

        if (index < 0 || index + 1 >= args.Length || !File.Exists(args[index + 1]))
            throw new ArgumentException("缺少有效的更新请求");

        return Path.GetFullPath(args[index + 1]);
    }
}
