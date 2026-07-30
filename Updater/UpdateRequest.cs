using System.Runtime.Serialization;

namespace Quotix.Updater;

[DataContract]
public sealed class UpdateRequest
{
    [DataMember]
    public string Version { get; set; } = "";
    [DataMember]
    public string DownloadUrl { get; set; } = "";
    [DataMember]
    public string Sha256 { get; set; } = "";
    [DataMember]
    public int MainProcessId { get; set; }
    [DataMember]
    public string MainExecutablePath { get; set; } = "";
    [DataMember]
    public string InstallDirectory { get; set; } = "";
}
