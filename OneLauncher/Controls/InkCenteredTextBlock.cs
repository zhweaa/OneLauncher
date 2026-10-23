using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace OneLauncher.Views.Controls;

/// <summary>
/// 按字形实际笔画（墨迹）范围垂直居中的 TextBlock。
/// 普通 TextBlock 按字体行框居中，大号粗体文字的笔画会明显偏下。
/// </summary>
public sealed class InkCenteredTextBlock : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <summary>当前为居中笔画施加的垂直偏移。</summary>
    public double InkOffsetY { get; private set; }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        UpdateInkOffset();
        return size;
    }

    private void UpdateInkOffset()
    {
        var layout = TextLayout;
        var offset = 0d;
        if (layout.TextLines.Count > 0 && layout.Extent > 0)
        {
            // OverhangAfter 为墨迹底部相对最后一行底边的距离，向下为正。
            var inkBottom = layout.Height + layout.OverhangAfter;
            var inkCenter = inkBottom - layout.Extent / 2;
            offset = Math.Round(layout.Height / 2 - inkCenter, 2);
        }
        if (Math.Abs(offset - InkOffsetY) < 0.01 && RenderTransform != null)
            return;
        InkOffsetY = offset;
        RenderTransform = new TranslateTransform(0, offset);
    }
}
