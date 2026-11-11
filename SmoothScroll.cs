using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Hamyar;

/// <summary>
/// Smooth mouse-wheel scrolling for every ScrollViewer of the app: instead of jumping a fixed step per wheel notch, the
/// content glides to the target position (exponential easing, frame-rate independent).
/// Lists use pixel scrolling (ScrollViewer.CanContentScroll=False), so there is no item-by-item jumping.
/// </summary>
public static class SmoothScroll
{
    sealed class State
    {
        public double Target;
        public double Current;
        public long Last;
    }

    static readonly Dictionary<ScrollViewer, State> Active = new();
    static bool _hooked;

    const double PixelsPerNotch = 110;   // one wheel notch (Delta 120) moves the content about this much
    const double Speed = 14;             // higher = snappier glide

    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
    }

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        if (e.Handled || !IsPixelScroll(sv) || Keyboard.Modifiers != ModifierKeys.None) return;
        if (sv.ScrollableHeight <= 0) return;
        if (!CanMove(sv, e.Delta)) return;                                    // at the end already: let the parent scroll
        if (!ReferenceEquals(Innermost(e.OriginalSource as DependencyObject, e.Delta), sv)) return;   // only the ScrollViewer under the mouse

        if (!Active.TryGetValue(sv, out var st))
        {
            st = new State { Target = sv.VerticalOffset, Current = sv.VerticalOffset, Last = Stopwatch.GetTimestamp() };
            Active[sv] = st;
        }
        st.Target = Math.Clamp(st.Target - e.Delta / 120.0 * PixelsPerNotch, 0, sv.ScrollableHeight);
        e.Handled = true;
        Hook();
    }

    /// <summary>True when the offsets are pixels: either plain pixel scrolling, or a virtualized list set to ScrollUnit=Pixel.</summary>
    static bool IsPixelScroll(ScrollViewer sv)
    {
        if (!sv.CanContentScroll) return true;
        return sv.TemplatedParent is ItemsControl ic && VirtualizingPanel.GetScrollUnit(ic) == ScrollUnit.Pixel;
    }

    static bool CanMove(ScrollViewer sv, int delta)
    {
        if (Active.ContainsKey(sv)) return true;
        return delta > 0 ? sv.VerticalOffset > 0.5 : sv.VerticalOffset < sv.ScrollableHeight - 0.5;
    }

    /// <summary>The first ScrollViewer above the element under the mouse that can scroll in the wheel direction.</summary>
    static ScrollViewer Innermost(DependencyObject d, int delta)
    {
        while (d != null)
        {
            if (d is ScrollViewer s && IsPixelScroll(s) && s.ScrollableHeight > 0 && CanMove(s, delta)) return s;
            d = ParentOf(d);
        }
        return null;
    }

    static DependencyObject ParentOf(DependencyObject d)
    {
        if (d is Visual || d is System.Windows.Media.Media3D.Visual3D) return VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        return LogicalTreeHelper.GetParent(d);
    }

    static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        CompositionTarget.Rendering += OnRender;
    }

    static void OnRender(object sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        foreach (var sv in Active.Keys.ToList())
        {
            var st = Active[sv];
            double dt = Math.Min(0.1, (now - st.Last) / (double)Stopwatch.Frequency);
            st.Last = now;

            double diff = st.Target - st.Current;
            if (Math.Abs(diff) < 0.4)
            {
                sv.ScrollToVerticalOffset(st.Target);
                Active.Remove(sv);
                continue;
            }
            st.Current += diff * (1 - Math.Exp(-dt * Speed));
            sv.ScrollToVerticalOffset(st.Current);
        }
        if (Active.Count == 0)
        {
            CompositionTarget.Rendering -= OnRender;
            _hooked = false;
        }
    }
}
