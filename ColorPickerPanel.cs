using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Hamyar;

/// <summary>A small colour selector: a saturation/brightness square plus a hue bar (no #RRGGBB typing needed).</summary>
public class ColorPickerPanel : Grid
{
    const double W = 240, H = 150, HueH = 18;
    readonly Border _sv = new() { Width = W, Height = H, CornerRadius = new CornerRadius(6), ClipToBounds = true, Cursor = Cursors.Cross };
    readonly Canvas _svCanvas = new() { Width = W, Height = H };
    readonly Ellipse _svThumb = new() { Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
    readonly Border _hue = new() { Width = W, Height = HueH, CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand, Margin = new Thickness(0, 10, 0, 0) };
    readonly Canvas _hueCanvas = new() { Width = W, Height = HueH, IsHitTestVisible = false };
    readonly Ellipse _hueThumb = new() { Width = HueH, Height = HueH, Stroke = Brushes.White, StrokeThickness = 2.5, IsHitTestVisible = false };
    readonly Border _hueBack = new() { Width = W, Height = H };
    double _h, _s = 1, _v = 1;

    public event Action<Color> ColorChanged;

    public ColorPickerPanel()
    {
        Width = W;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        FlowDirection = FlowDirection.LeftToRight;

        _hueBack.Background = new SolidColorBrush(Colors.Red);
        var white = new Border { Width = W, Height = H, Background = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0) };
        var black = new Border { Width = W, Height = H, Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90) };
        var layers = new Grid();
        layers.Children.Add(_hueBack); layers.Children.Add(white); layers.Children.Add(black);
        _svCanvas.Children.Add(_svThumb);
        layers.Children.Add(_svCanvas);
        _sv.Child = layers;
        Children.Add(_sv);

        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++) rainbow.GradientStops.Add(new GradientStop(HsvToColor(i * 60, 1, 1), i / 6.0));
        _hue.Background = rainbow;
        var hg = new Grid(); hg.Children.Add(_hueCanvas); _hueCanvas.Children.Add(_hueThumb);
        _hue.Child = hg;
        SetRow(_hue, 1);
        Children.Add(_hue);

        Drag(_sv, p => { _s = Math.Clamp(p.X / W, 0, 1); _v = 1 - Math.Clamp(p.Y / H, 0, 1); Update(true); });
        Drag(_hue, p => { _h = Math.Clamp(p.X / W, 0, 1) * 360; if (_h >= 360) _h = 359.9; Update(true); });
        Update(false);
    }

    static void Drag(Border el, Action<Point> at)
    {
        el.MouseLeftButtonDown += (s, e) => { el.CaptureMouse(); at(e.GetPosition(el)); };
        el.MouseMove += (s, e) => { if (el.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) at(e.GetPosition(el)); };
        el.MouseLeftButtonUp += (s, e) => el.ReleaseMouseCapture();
    }

    public Color Color
    {
        get => HsvToColor(_h, _s, _v);
        set
        {
            RgbToHsv(value, out _h, out _s, out _v);
            Update(false);
        }
    }

    void Update(bool notify)
    {
        _hueBack.Background = new SolidColorBrush(HsvToColor(_h, 1, 1));
        Canvas.SetLeft(_svThumb, _s * W - 8);
        Canvas.SetTop(_svThumb, (1 - _v) * H - 8);
        Canvas.SetLeft(_hueThumb, _h / 360 * W - HueH / 2);
        var c = Color;
        _svThumb.Fill = new SolidColorBrush(c);
        _hueThumb.Fill = new SolidColorBrush(HsvToColor(_h, 1, 1));
        if (notify) ColorChanged?.Invoke(c);
    }

    public static Color HsvToColor(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c, r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static void RgbToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        v = max; s = max == 0 ? 0 : d / max;
        if (d == 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
