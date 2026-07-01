using Quotix.Models;

namespace Quotix.Common;

/// <summary>
/// 导出器接口 — 插件化支持 Excel / PDF / 打印等格式
/// </summary>
public interface IExporter
{
    /// <summary>导出报价单到指定路径，返回导出文件路径</summary>
    string Export(Quotation quotation, string outputDir);
}
