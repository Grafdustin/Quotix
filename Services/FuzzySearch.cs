using System.Globalization;

namespace Quotix.Services;

/// <summary>
/// 模糊搜索算法（移植自前端 JS 实现）。
/// 提供基础模糊匹配（前缀 / 段前缀 / 包含 / 位置 / 数字精确 / 长度加权）与
/// 高级模糊匹配（支持字符分散匹配，如搜索 "1-3" 可匹配 "1-2-3"），并支持生成高亮区间。
/// 全局开关由设置控制：开启时使用高级模糊匹配，关闭时使用基础模糊匹配。
/// </summary>
public static class FuzzySearch
{
    /// <summary>用于拆分“段”的分隔符集合（与 JS 中 /[-_.\s]/ 一致）。</summary>
    private static readonly char[] Separators = { '-', '_', '.', '/', ' ', '\t', '\n', '\r' };

    /// <summary>
    /// 基础模糊匹配（对应 JS 的 fuzzyMatch）：
    /// 前缀 / 段前缀 / 包含 / 匹配位置 / 数字精确 / 长度加权，返回匹配度分数（0 表示无匹配）。
    /// </summary>
    public static double Basic(string text, string search)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(search))
            return 0;

        var textLower = text.ToLowerInvariant();
        var searchLower = search.ToLowerInvariant();

        // 基础匹配要求文本连续包含完整搜索词
        if (!textLower.Contains(searchLower))
            return 0;

        double score = 0;

        // 1. 完全前缀匹配
        if (textLower.StartsWith(searchLower))
            score += 900;

        // 2. 段前缀匹配（在连字符或其他分隔符后的前缀匹配）
        var segments = textLower.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment.StartsWith(searchLower))
            {
                score += 600;
                break;
            }
        }

        // 3. 包含匹配
        score += 400;

        // 4. 匹配位置加分（越靠前越好）
        var position = textLower.IndexOf(searchLower, StringComparison.Ordinal);
        score += (150 - position);

        // 5. 数字精确匹配
        if (double.TryParse(searchLower, NumberStyles.Any, CultureInfo.InvariantCulture, out var searchNum))
        {
            if (double.TryParse(textLower, NumberStyles.Any, CultureInfo.InvariantCulture, out var textNum)
                && textNum == searchNum)
                score += 300;
        }

        // 6. 长度加分（轻微影响）
        score += (50 - text.Length * 0.1);

        return score;
    }

    /// <summary>
    /// 高级模糊匹配（对应 JS 的 advancedFuzzyMatch）：
    /// 支持字符分散匹配（如搜索 "1-3" 能匹配 "1-2-3"）。
    /// </summary>
    public static double Advanced(string text, string search)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(search))
            return 0;

        var textLower = text.ToLowerInvariant();
        var searchLower = search.ToLowerInvariant();

        // 连续包含时直接走基础匹配逻辑
        if (textLower.Contains(searchLower))
            return Basic(text, search);

        int textIndex = 0, searchIndex = 0, matchCount = 0, totalDistance = 0, lastMatchIndex = -1;
        int segmentCount = 1, consecutiveCount = 1, maxConsecutive = 1, dynamicPenalty = 0, crossCount = 0;

        while (textIndex < textLower.Length && searchIndex < searchLower.Length)
        {
            if (textLower[textIndex] == searchLower[searchIndex])
            {
                matchCount++;
                if (lastMatchIndex >= 0)
                {
                    var distance = textIndex - lastMatchIndex;
                    totalDistance += distance;
                    if (distance > 1)
                    {
                        segmentCount++;
                        consecutiveCount = 1;
                        crossCount++;
                        // 判断是否跨分隔符（中间有分隔符则惩罚更重）
                        bool hasSeparator = false;
                        for (int i = lastMatchIndex + 1; i < textIndex; i++)
                        {
                            if (Array.IndexOf(Separators, text[i]) >= 0) { hasSeparator = true; break; }
                        }
                        // 跨分隔符惩罚：-30；无分隔符跨段惩罚：-15
                        dynamicPenalty += hasSeparator ? 30 : 15;
                    }
                    else
                    {
                        consecutiveCount++;
                        maxConsecutive = System.Math.Max(maxConsecutive, consecutiveCount);
                    }
                }
                lastMatchIndex = textIndex;
                searchIndex++;
            }
            textIndex++;
        }

        // 所有搜索字符都匹配上才计分
        if (matchCount == searchLower.Length)
        {
            double score = 100; // 基础分数（明显低于连续匹配的 400）

            // 匹配位置加分（越靠前越好）
            var firstMatchIndex = textLower.IndexOf(searchLower[0]);
            score += System.Math.Max(0, 50 - firstMatchIndex);

            // 字符连续性加分（距离越小分数越高）
            var avgDistance = matchCount > 1 ? (double)totalDistance / (matchCount - 1) : 0;
            score += System.Math.Max(0, 100 - avgDistance * 10);

            // 完全连续加分
            if (totalDistance == searchLower.Length - 1)
                score += 150;

            // 前缀匹配奖励
            if (firstMatchIndex == 0)
                score += 50;

            // 动态惩罚（跨分隔符累加）
            score -= dynamicPenalty;

            // 连续字符奖励（平方关系，更强调长连续）
            score += System.Math.Pow(maxConsecutive, 1.8) * 4;

            // 长度加分
            score += (50 - text.Length * 0.1);

            return score;
        }
        return 0;
    }

    /// <summary>
    /// 根据全局开关选择匹配算法：开启高级模糊（分散匹配），关闭基础模糊。
    /// </summary>
    public static double Match(string text, string search, bool advanced)
        => advanced ? Advanced(text, search) : Basic(text, search);

    /// <summary>
    /// 计算用于高亮的字符下标集合（与前端 highlightFuzzyMatch 一致）：
    /// 连续匹配高亮完整子串；分散匹配按贪心顺序高亮每个命中的非分隔符字符。
    /// 返回针对 text 本身的下标数组（区分大小写下标与显示一致）。
    /// </summary>
    public static int[] GetHighlightIndices(string text, string search)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(search))
            return System.Array.Empty<int>();

        var textLower = text.ToLowerInvariant();
        var searchLower = search.ToLowerInvariant();

        // 连续匹配：高亮完整子串区间
        var idx = textLower.IndexOf(searchLower, System.StringComparison.Ordinal);
        if (idx >= 0)
        {
            var arr = new int[searchLower.Length];
            for (int i = 0; i < searchLower.Length; i++)
                arr[i] = idx + i;
            return arr;
        }

        // 分散匹配：贪心顺序匹配每个搜索字符（分隔符不参与高亮）
        var list = new System.Collections.Generic.List<int>();
        int ti = 0, si = 0;
        while (ti < text.Length && si < searchLower.Length)
        {
            if (textLower[ti] == searchLower[si])
            {
                if (Array.IndexOf(Separators, text[ti]) < 0)
                    list.Add(ti);
                si++;
            }
            ti++;
        }
        return list.ToArray();
    }
}
