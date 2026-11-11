using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Hamyar;

/// <summary>Smooth tab/page effects: panel slide+zoom+fade, staggered "cascade" of the cards, springy nav chips.
/// Everything is switched off by Settings -> "animations" (and in safe mode).</summary>
public static class Anim
{
    public static bool On => Cfg.Animations && !Cfg.SafeMode;

    static readonly ConditionalWeakTable<UIElement, StrongBox<double>> Orig = new();

    static IEasingFunction Smooth => new QuinticEase { EasingMode = EasingMode.EaseOut };

    static double OrigOpacity(UIElement el)
    {
        if (!Orig.TryGetValue(el, out var box)) { box = new StrongBox<double>(el.Opacity); Orig.Add(el, box); }
        return box.Value;
    }

    /// <summary>Our own Scale+Translate group on the element (null if the element already has another transform).</summary>
    static (ScaleTransform s, TranslateTransform t)? Prepare(UIElement el)
    {
        if (el.RenderTransform is TransformGroup g && g.Children.Count == 2 && g.Children[0] is ScaleTransform sc && g.Children[1] is TranslateTransform tr)
            return (sc, tr);
        if (el.RenderTransform == null || el.RenderTransform == Transform.Identity || el.RenderTransform.Value.IsIdentity)
        {
            var s = new ScaleTransform(1, 1);
            var t = new TranslateTransform(0, 0);
            el.RenderTransform = new TransformGroup { Children = { s, t } };
            return (s, t);
        }
        return null;
    }

    /// <summary>Fade in + slide up + zoom from fromScale, with an optional start delay.</summary>
    public static void Enter(FrameworkElement el, double dy, double fromScale, int ms, int delayMs, Point? origin = null)
    {
        if (!On || el == null) return;
        double orig = OrigOpacity(el);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        el.RenderTransformOrigin = origin ?? new Point(0.5, 0.5);

        el.Opacity = 0;
        var oa = new DoubleAnimation(0, orig, TimeSpan.FromMilliseconds(ms * 0.8)) { BeginTime = begin, EasingFunction = Smooth, FillBehavior = FillBehavior.HoldEnd };
        oa.Completed += (s, e) => { el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = orig; };
        el.BeginAnimation(UIElement.OpacityProperty, oa);

        var p = Prepare(el);
        if (p == null) return;
        var (sc, tr) = p.Value;
        tr.Y = dy; sc.ScaleX = fromScale; sc.ScaleY = fromScale;
        var ty = new DoubleAnimation(dy, 0, TimeSpan.FromMilliseconds(ms)) { BeginTime = begin, EasingFunction = Smooth, FillBehavior = FillBehavior.HoldEnd };
        ty.Completed += (s, e) => { tr.BeginAnimation(TranslateTransform.YProperty, null); tr.Y = 0; };
        tr.BeginAnimation(TranslateTransform.YProperty, ty);
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
        var sx = new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(ms + 120)) { BeginTime = begin, EasingFunction = back, FillBehavior = FillBehavior.HoldEnd };
        sx.Completed += (s, e) => { sc.BeginAnimation(ScaleTransform.ScaleXProperty, null); sc.BeginAnimation(ScaleTransform.ScaleYProperty, null); sc.ScaleX = 1; sc.ScaleY = 1; };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, (DoubleAnimation)sx.Clone());
    }

    /// <summary>The whole page: quick fade, lift and a soft zoom.</summary>
    public static void PanelIn(FrameworkElement panel) => Enter(panel, 22, 0.975, 420, 0, new Point(0.5, 0));

    /// <summary>The cards/rows of a page appear one after the other (cascade).</summary>
    public static void Stagger(FrameworkElement panel, int max = 14)
    {
        if (!On) return;
        Panel host = panel as Panel ?? (panel as ScrollViewer)?.Content as Panel;
        if (host == null) return;
        int i = 0;
        foreach (var c in host.Children.OfType<FrameworkElement>())
        {
            if (c.Visibility != Visibility.Visible) continue;
            if (i >= max) break;
            Enter(c, 26, 0.96, 480, 50 + i * 45);
            i++;
        }
    }

    /// <summary>Springy pop of a nav chip + a little wobble of its icon when it gets selected.</summary>
    public static void NavPop(RadioButton rb)
    {
        if (!On || rb == null) return;
        rb.RenderTransformOrigin = new Point(0.5, 0.5);
        var sc = rb.RenderTransform as ScaleTransform;
        if (sc == null) { sc = new ScaleTransform(1, 1); rb.RenderTransform = sc; }
        var spring = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 5 };
        var a = new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(650)) { EasingFunction = spring };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, (DoubleAnimation)a.Clone());

        if (rb.Content is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock icon)
        {
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            var rot = icon.RenderTransform as RotateTransform;
            if (rot == null) { rot = new RotateTransform(0); icon.RenderTransform = rot; }
            rot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(-35, 0, TimeSpan.FromMilliseconds(700)) { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 3, Springiness = 4 } });
        }
    }

    /// <summary>Page title: slides in from the side while fading in.</summary>
    public static void Title(FrameworkElement title)
    {
        if (!On || title == null) return;
        var tr = title.RenderTransform as TranslateTransform;
        if (tr == null) { tr = new TranslateTransform(); title.RenderTransform = tr; }
        double from = Loc.I.Flow == FlowDirection.RightToLeft ? 30 : -30;
        tr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = Smooth });
        title.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = Smooth });
    }

    /// <summary>Short highlight flash (used for values changed by the chain).</summary>
    public static void Pulse(UIElement el)
    {
        if (!On || el == null) return;
        el.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(650)) { EasingFunction = Smooth });
    }
}
