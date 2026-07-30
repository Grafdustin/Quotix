namespace Quotix.Updater;

public sealed class UpdateRequest
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int MainProcessId { get; set; }
    public string MainExecutablePath { get; set; } = "";
    public string InstallDirectory { get; set; } = "";
}
