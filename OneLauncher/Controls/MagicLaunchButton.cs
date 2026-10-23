using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using System;

namespace OneLauncher.Views.Controls;

/// <summary>
/// 主页魔法按钮：按下时阴影以点击位置为中心快速向四周扩散，扩散过程中阴影逐渐加深，
/// 覆盖整个按钮后迅速淡出。命令仍由 ViewModel 提供。
/// </summary>
public sealed class MagicLaunchButton : Button
{
    private const double SpreadSeconds = 0.42;
    private const double FadeSeconds = 0.22;
    private const double MinOpacity = 0.18;
    private const double MaxOpacity = 0.72;

    private Canvas? _burstLayer;
    private Ellipse? _shadow;
    private Point _burstOrigin;
    private double _maxDiameter;
    private Point? _pressPoint;
    private TimeSpan? _burstStart;
    private bool _frameRequested;

    protected override Type StyleKeyOverride => typeof(MagicLaunchButton);

    /// <summary>扩散阴影当前的不透明度；没有进行中的效果时为 0。</summary>
    public double BurstShadowOpacity => _shadow?.Opacity ?? 0;

    /// <summary>扩散阴影当前的直径；没有进行中的效果时为 0。</summary>
    public double BurstShadowDiameter => _shadow?.Width ?? 0;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _burstLayer?.Children.Clear();
        _burstLayer = e.NameScope.Find<Canvas>("PART_BurstLayer");
        _shadow = null;
        _burstStart = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _pressPoint = e.GetPosition(_burstLayer ?? (Visual)this);
        base.OnPointerPressed(e);
    }

    protected override void OnClick()
    {
        StartBurst(_pressPoint ?? new Point(Bounds.Width / 2, Bounds.Height / 2));
        _pressPoint = null;
        base.OnClick();
    }

    /// <summary>从按钮内的坐标开始一次扩散；新的点击会替换上一次未完成的效果。</summary>
    public void StartBurst(Point origin)
    {
        if (_burstLayer == null)
            return;

        _burstLayer.Children.Clear();
        _burstOrigin = origin;
        _burstStart = null;

        // 直径取到最远角距离的两倍，保证扩散结束时覆盖整个按钮。
        var size = _burstLayer.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            size = Bounds.Size;
        var farX = Math.Max(origin.X, size.Width - origin.X);
        var farY = Math.Max(origin.Y, size.Height - origin.Y);
        _maxDiameter = Math.Max(40, Math.Sqrt(farX * farX + farY * farY) * 2);

        _shadow = new Ellipse
        {
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xD0, 0, 0, 0), 0),
                    new GradientStop(Color.FromArgb(0xA0, 0, 0, 0), 0.62),
                    new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1)
                }
            }
        };
        _burstLayer.Children.Add(_shadow);
        UpdateBurst(0);
        RequestBurstFrame();
    }

    private void RequestBurstFrame()
    {
        if (_frameRequested || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;
        _frameRequested = true;
        topLevel.RequestAnimationFrame(OnBurstFrame);
    }

    private void OnBurstFrame(TimeSpan time)
    {
        _frameRequested = false;
        if (_shadow == null)
            return;
        _burstStart ??= time;
        var elapsed = (time - _burstStart.Value).TotalSeconds;
        if (elapsed >= SpreadSeconds + FadeSeconds)
        {
            _burstLayer?.Children.Clear();
            _shadow = null;
            _burstStart = null;
            return;
        }
        UpdateBurst(elapsed);
        RequestBurstFrame();
    }

    private void UpdateBurst(double elapsed)
    {
        if (_shadow == null)
            return;

        var spread = Math.Clamp(elapsed / SpreadSeconds, 0, 1);
        var easeOut = 1 - Math.Pow(1 - spread, 3);
        var diameter = 12 + easeOut * (_maxDiameter - 12);
        _shadow.Width = diameter;
        _shadow.Height = diameter;
        Canvas.SetLeft(_shadow, _burstOrigin.X - diameter / 2);
        Canvas.SetTop(_shadow, _burstOrigin.Y - diameter / 2);

        // 扩散阶段阴影随半径加深，结束后快速淡出。
        var opacity = MinOpacity + (MaxOpacity - MinOpacity) * easeOut;
        if (elapsed > SpreadSeconds)
            opacity *= 1 - Math.Clamp((elapsed - SpreadSeconds) / FadeSeconds, 0, 1);
        _shadow.Opacity = opacity;
    }
}
