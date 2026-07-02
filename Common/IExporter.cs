using Quotix.Models;

namespace Quotix.Common;

/// <summary>
/// 导出器接口。
/// 所有报价单导出实现（Excel / PDF / 打印等）均需实现此接口。
/// </summary>
public interface IExporter
{
    /// <summary>导出报价单到指定目录，返回导出文件的完整路径</summary>
    string Export(Quotation quotation, string outputDir);
}
