using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Quotix.Services;

/// <summary>
/// 将表格原始文本渲染为适合复制到剪贴板的图片。
/// </summary>
public static class TableCaptureService
{
    private const double MinColumnWidth = 96;
    private const double MaxColumnWidth = 360;
    private const double HorizontalPadding = 12;
    private const double VerticalPadding = 8;
    private const double MinRowHeight = 36;
    private const int MaxBitmapDimension = 8192;
    private const double MaxBitmapPixels = 32_000_000;

    public static BitmapSource Render(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool includeHeaders,
        double pixelsPerDip)
    {
        if (headers.Count == 0 || rows.Count == 0)
            throw new ArgumentException("截图范围不能为空。");

        var regularTypeface = new Typeface(
            new FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);
        var headerTypeface = new Typeface(
            new FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal,
            FontWeights.SemiBold,
            FontStretches.Normal);

        var columnWidths = CalculateColumnWidths(
            headers,
            rows,
            regularTypeface,
            headerTypeface,
            pixelsPerDip);
        var headerHeight = includeHeaders
            ? CalculateRowHeight(headers, columnWidths, headerTypeface, 12, pixelsPerDip)
            : 0;
        var rowHeights = rows
            .Select(row => CalculateRowHeight(row, columnWidths, regularTypeface, 13, pixelsPerDip))
            .ToArray();

        var totalWidth = columnWidths.Sum();
        var totalHeight = headerHeight + rowHeights.Sum();
        var drawing = new DrawingVisual();

        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, totalWidth, totalHeight));

            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(218, 221, 226)), 1);
            gridPen.Freeze();
            var headerBackground = new SolidColorBrush(Color.FromRgb(241, 243, 245));
            var alternateBackground = new SolidColorBrush(Color.FromRgb(249, 250, 251));
            var textBrush = new SolidColorBrush(Color.FromRgb(45, 47, 51));
            headerBackground.Freeze();
            alternateBackground.Freeze();
            textBrush.Freeze();

            var y = 0d;
            if (includeHeaders)
            {
                DrawRow(
                    context,
                    headers,
                    columnWidths,
                    y,
                    headerHeight,
                    headerTypeface,
                    12,
                    headerBackground,
                    textBrush,
                    gridPen,
                    pixelsPerDip);
                y += headerHeight;
            }

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var background = rowIndex % 2 == 1 ? alternateBackground : Brushes.White;
                DrawRow(
                    context,
                    rows[rowIndex],
                    columnWidths,
                    y,
                    rowHeights[rowIndex],
                    regularTypeface,
                    13,
                    background,
                    textBrush,
                    gridPen,
                    pixelsPerDip);
                y += rowHeights[rowIndex];
            }
        }

        var scale = CalculateBitmapScale(totalWidth, totalHeight);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(totalWidth * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(totalHeight * scale));
        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }

    private static double[] CalculateColumnWidths(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        Typeface regularTypeface,
        Typeface headerTypeface,
        double pixelsPerDip)
    {
        var widths = new double[headers.Count];
        for (var columnIndex = 0; columnIndex < headers.Count; columnIndex++)
        {
            var width = MeasureUnwrappedWidth(
                headers[columnIndex],
                headerTypeface,
                12,
                pixelsPerDip);

            foreach (var row in rows)
            {
                var value = columnIndex < row.Count ? row[columnIndex] : "";
                width = Math.Max(
                    width,
                    MeasureUnwrappedWidth(value, regularTypeface, 13, pixelsPerDip));
            }

            widths[columnIndex] = Math.Clamp(
                width + (HorizontalPadding * 2),
                MinColumnWidth,
                MaxColumnWidth);
        }

        return widths;
    }

    private static double CalculateRowHeight(
        IReadOnlyList<string> values,
        IReadOnlyList<double> columnWidths,
        Typeface typeface,
        double fontSize,
        double pixelsPerDip)
    {
        var height = MinRowHeight;
        for (var columnIndex = 0; columnIndex < columnWidths.Count; columnIndex++)
        {
            var value = columnIndex < values.Count ? values[columnIndex] : "";
            var text = CreateText(value, typeface, fontSize, pixelsPerDip);
            text.MaxTextWidth = Math.Max(1, columnWidths[columnIndex] - (HorizontalPadding * 2));
            height = Math.Max(height, text.Height + (VerticalPadding * 2));
        }

        return Math.Ceiling(height);
    }

    private static void DrawRow(
        DrawingContext context,
        IReadOnlyList<string> values,
        IReadOnlyList<double> columnWidths,
        double y,
        double height,
        Typeface typeface,
        double fontSize,
        Brush background,
        Brush textBrush,
        Pen gridPen,
        double pixelsPerDip)
    {
        var x = 0d;
        for (var columnIndex = 0; columnIndex < columnWidths.Count; columnIndex++)
        {
            var width = columnWidths[columnIndex];
            var bounds = new Rect(x, y, width, height);
            context.DrawRectangle(background, gridPen, bounds);

            var value = columnIndex < values.Count ? values[columnIndex] : "";
            var text = CreateText(value, typeface, fontSize, pixelsPerDip);
            text.MaxTextWidth = Math.Max(1, width - (HorizontalPadding * 2));
            text.SetForegroundBrush(textBrush);
            context.DrawText(text, new Point(x + HorizontalPadding, y + VerticalPadding));
            x += width;
        }
    }

    private static FormattedText CreateText(
        string? value,
        Typeface typeface,
        double fontSize,
        double pixelsPerDip)
        => new(
            value ?? "",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            pixelsPerDip);

    private static double MeasureUnwrappedWidth(
        string? value,
        Typeface typeface,
        double fontSize,
        double pixelsPerDip)
    {
        var text = CreateText(value, typeface, fontSize, pixelsPerDip);
        return text.WidthIncludingTrailingWhitespace;
    }

    private static double CalculateBitmapScale(double width, double height)
    {
        var dimensionScale = Math.Min(
            1,
            Math.Min(MaxBitmapDimension / width, MaxBitmapDimension / height));
        var pixelScale = Math.Min(1, Math.Sqrt(MaxBitmapPixels / (width * height)));
        return Math.Min(dimensionScale, pixelScale);
    }
}
