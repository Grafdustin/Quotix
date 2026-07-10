using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Quotix.Views;

/// <summary>
/// 带高亮的文本块：将 SourceText 中由 HighlightIndices 指定的字符（支持分散匹配的高亮区间）渲染为
/// 黄色背景 + 红色字体，其余部分沿用系统文字色（TextBlock.Foreground）。
/// 用于快捷输入搜索结果中高亮用户输入的内容（黄底红字）。
/// </summary>
public class HighlightTextBlock : TextBlock
{
    /// <summary>要显示的完整文本。</summary>
    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.Register(
            nameof(SourceText), typeof(string), typeof(HighlightTextBlock),
            new PropertyMetadata(null, OnPropertyChanged));

    /// <summary>需要高亮的字符下标集合（针对 SourceText）。为 null 或空时不渲染高亮。</summary>
    public static readonly DependencyProperty HighlightIndicesProperty =
        DependencyProperty.Register(
            nameof(HighlightIndices), typeof(System.Collections.Generic.IList<int>), typeof(HighlightTextBlock),
            new PropertyMetadata(null, OnPropertyChanged));

    /// <summary>获取或设置要显示的完整文本。</summary>
    public string? SourceText
    {
        get => (string?)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    /// <summary>获取或设置需要高亮的字符下标集合（针对 SourceText）。</summary>
    public System.Collections.Generic.IList<int>? HighlightIndices
    {
        get => (System.Collections.Generic.IList<int>?)GetValue(HighlightIndicesProperty);
        set => SetValue(HighlightIndicesProperty, value);
    }

    /// <summary>初始化 HighlightTextBlock 实例。</summary>
    public HighlightTextBlock()
    {
        Initialized += (_, _) => Rebuild();
    }

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((HighlightTextBlock)d).Rebuild();
    }

    /// <summary>根据 SourceText 与 HighlightIndices 重建内联内容（高亮命中的字符）。</summary>
    private void Rebuild()
    {
        Inlines.Clear();

        var text = SourceText ?? "";
        var indices = HighlightIndices;

        // 无高亮区间：整段作为普通文本（继承系统文字色）
        if (indices == null || indices.Count == 0)
        {
            Inlines.Add(new Run(text));
            return;
        }

        // 排序 + 去重，便于按相邻区间合并渲染
        var sorted = indices
            .Where(i => i >= 0 && i < text.Length)
            .Distinct()
            .OrderBy(i => i)
            .ToArray();

        int i = 0;
        while (i < text.Length)
        {
            if (System.Array.BinarySearch(sorted, i) >= 0)
            {
                int start = i;
                while (i < text.Length && System.Array.BinarySearch(sorted, i) >= 0)
                    i++;
                Inlines.Add(new Run(text.Substring(start, i - start))
                {
                    Foreground = new SolidColorBrush(Colors.Red),
                    Background = new SolidColorBrush(Colors.Yellow)
                });
            }
            else
            {
                int start = i;
                while (i < text.Length && System.Array.BinarySearch(sorted, i) < 0)
                    i++;
                Inlines.Add(new Run(text.Substring(start, i - start)));
            }
        }
    }
}
