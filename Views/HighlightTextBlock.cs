using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Quotix.Views;

/// <summary>
/// 带高亮的文本块：将 SourceText 中匹配 HighlightText 的子串渲染为
/// 黄色背景 + 红色字体，其余部分沿用系统文字色（TextBlock.Foreground）。
/// 用于快捷输入搜索结果中高亮用户输入的内容。
/// </summary>
public class HighlightTextBlock : TextBlock
{
    /// <summary>要显示的完整文本。</summary>
    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.Register(
            nameof(SourceText), typeof(string), typeof(HighlightTextBlock),
            new PropertyMetadata(null, OnPropertyChanged));

    /// <summary>需要高亮（黄色背景 + 红色字体）的子串，大小写不敏感。</summary>
    public static readonly DependencyProperty HighlightTextProperty =
        DependencyProperty.Register(
            nameof(HighlightText), typeof(string), typeof(HighlightTextBlock),
            new PropertyMetadata(null, OnPropertyChanged));

    /// <summary>获取或设置要显示的完整文本。</summary>
    public string? SourceText
    {
        get => (string?)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    /// <summary>获取或设置需要高亮的子串。</summary>
    public string? HighlightText
    {
        get => (string?)GetValue(HighlightTextProperty);
        set => SetValue(HighlightTextProperty, value);
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

    /// <summary>根据 SourceText 与 HighlightText 重建内联内容（高亮匹配部分）。</summary>
    private void Rebuild()
    {
        Inlines.Clear();

        var text = SourceText ?? "";
        var hl = (HighlightText ?? "").Trim();

        // 无高亮词：整段作为普通文本（继承系统文字色）
        if (string.IsNullOrEmpty(hl))
        {
            Inlines.Add(new Run(text));
            return;
        }

        var lowerText = text.ToLowerInvariant();
        var lowerHl = hl.ToLowerInvariant();

        var start = 0;
        int idx;
        while ((idx = lowerText.IndexOf(lowerHl, start, System.StringComparison.Ordinal)) >= 0)
        {
            if (idx > start)
                Inlines.Add(new Run(text.Substring(start, idx - start)));

            Inlines.Add(new Run(text.Substring(idx, hl.Length))
            {
                Foreground = new SolidColorBrush(Colors.Red),
                Background = new SolidColorBrush(Colors.Yellow)
            });

            start = idx + hl.Length;
        }

        if (start < text.Length)
            Inlines.Add(new Run(text.Substring(start)));
    }
}
