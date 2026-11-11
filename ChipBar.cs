using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Hamyar;

/// <summary>Makes a row of RadioButton chips interactive: double-click a chip to rename it in place, drag a chip to change the order.
/// Chips whose Tag is a long &gt; 0 are real items; any other chip ("All ...") is fixed (it can only be renamed through onRenameFixed).</summary>
public static class ChipBar
{
    sealed class Ghost : Adorner
    {
        readonly VisualBrush _brush; readonly Size _size;
        public Point At;
        public Ghost(UIElement host, FrameworkElement chip) : base(host)
        {
            IsHitTestVisible = false;
            _size = new Size(chip.ActualWidth, chip.ActualHeight);
            _brush = new VisualBrush(chip) { Stretch = Stretch.Fill };
        }
        protected override void OnRender(DrawingContext dc)
        {
            var r = new Rect(At.X, At.Y, _size.Width, _size.Height);
            for (int i = 4; i >= 1; i--)
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(12 + (4 - i) * 4), 0, 0, 0)), null,
                    new Rect(r.X - i + 1, r.Y - i + 4, r.Width + 2 * i - 2, r.Height + 2 * i - 2), 14, 14);
            dc.PushOpacity(0.95);
            dc.DrawRectangle(_brush, null, r);
            dc.Pop();
        }
    }

    static bool IsItem(UIElement e) => e is RadioButton rb && (rb.Tag is long id && id > 0 || rb.Tag is string);

    /// <summary>Makes the main side-bar tabs draggable (vertical list); the new order of the tags is handed to onReorder.</summary>
    public static void AttachTabs(Panel host, RadioButton tab, Action<List<string>> onReorder) =>
        Attach(host, tab, null, null, null, onReorder);

    public static void Attach(Panel host, RadioButton chip, Action<long, string> onRename, Action<List<long>> onReorder, Action onRenameFixed = null, Action<List<string>> onReorderTags = null)
    {
        Point down = default; bool pressed = false, dragging = false; Ghost ghost = null; AdornerLayer layer = null; Vector grab = default;

        chip.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2 && chip.Tag is string) { down = e.GetPosition(host); pressed = true; return; }   // side-bar tab: no rename
            if (e.ClickCount == 2)
            {
                e.Handled = true;
                if (IsItem(chip)) BeginRename(host, chip, onRename);
                else onRenameFixed?.Invoke();
                return;
            }
            down = e.GetPosition(host); pressed = IsItem(chip) && (onReorder != null || onReorderTags != null);
        };

        chip.PreviewMouseMove += (s, e) =>
        {
            if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(host);
            if (!dragging)
            {
                if (Math.Abs(p.X - down.X) < 6 && Math.Abs(p.Y - down.Y) < 6) return;
                layer = AdornerLayer.GetAdornerLayer(host);
                if (layer == null) { pressed = false; return; }
                dragging = true;
                var origin = chip.TranslatePoint(new Point(0, 0), host);
                grab = new Vector(down.X - origin.X, down.Y - origin.Y);
                ghost = new Ghost(host, chip);
                layer.Add(ghost);
                chip.Opacity = 0.3;
                chip.CaptureMouse();
            }
            ghost.At = new Point(p.X - grab.X, p.Y - grab.Y);
            ghost.InvalidateVisual();

            // swap with the chip under the mouse (the others slide to their new place)
            foreach (UIElement other in host.Children)
            {
                if (other == chip || !IsItem(other)) continue;
                var fe = (FrameworkElement)other;
                var tl = fe.TranslatePoint(new Point(0, 0), host);
                if (new Rect(tl.X, tl.Y, fe.ActualWidth, fe.ActualHeight).Contains(p))
                {
                    int from = host.Children.IndexOf(chip), to = host.Children.IndexOf(other);
                    Move(host, chip, to);
                    break;
                }
            }
        };

        void Finish()
        {
            if (!dragging) { pressed = false; return; }
            dragging = false; pressed = false;
            layer?.Remove(ghost); ghost = null;
            chip.Opacity = 1;
            onReorder?.Invoke(host.Children.OfType<RadioButton>().Where(IsItem).Select(r => r.Tag is long l ? l : 0L).ToList());
            onReorderTags?.Invoke(host.Children.OfType<RadioButton>().Where(IsItem).Select(r => r.Tag.ToString()).ToList());
        }
        chip.PreviewMouseLeftButtonUp += (s, e) => { if (dragging) { chip.ReleaseMouseCapture(); e.Handled = true; } pressed = false; };
        chip.LostMouseCapture += (s, e) => Finish();
    }

    /// <summary>Moves a chip to a new index; every other chip glides from its old position to the new one.</summary>
    static void Move(Panel host, UIElement chip, int to)
    {
        var before = new Dictionary<UIElement, Point>();
        foreach (UIElement c in host.Children) before[c] = ((FrameworkElement)c).TranslatePoint(new Point(0, 0), host);
        host.Children.Remove(chip);
        host.Children.Insert(to, chip);
        host.UpdateLayout();
        foreach (UIElement c in host.Children)
        {
            if (c == chip) continue;
            var now = ((FrameworkElement)c).TranslatePoint(new Point(0, 0), host);
            // TranslatePoint includes a running animation's offset, so measure the layout spot without it
            var t = (c.RenderTransform as TranslateTransform);
            double cx = t?.X ?? 0, cy = t?.Y ?? 0;
            double dx = before[c].X - (now.X - cx), dy = before[c].Y - (now.Y - cy);
            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) continue;
            var tt = new TranslateTransform(dx, dy);
            c.RenderTransform = tt;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        }
    }

    static void BeginRename(Panel host, RadioButton chip, Action<long, string> onRename)
    {
        int idx = host.Children.IndexOf(chip);
        if (idx < 0) return;
        string old = chip.Content as string ?? "";
        var box = new TextBox
        {
            Text = old, MinWidth = Math.Max(90, chip.ActualWidth), Margin = chip.Margin, Padding = new Thickness(10, 5, 10, 5),
            FontSize = chip.FontSize, VerticalAlignment = VerticalAlignment.Center
        };
        bool done = false;
        void Commit(bool ok)
        {
            if (done) return; done = true;
            var name = box.Text.Trim();
            int i = host.Children.IndexOf(box);
            if (i >= 0) { host.Children.RemoveAt(i); host.Children.Insert(i, chip); }
            if (ok && name.Length > 0 && name != old) { chip.Content = name; onRename((long)chip.Tag, name); }
        }
        box.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { Commit(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { Commit(false); e.Handled = true; }
        };
        box.LostKeyboardFocus += (s, e) => Commit(true);
        host.Children.RemoveAt(idx);
        host.Children.Insert(idx, box);
        box.Focus();
        box.SelectAll();
    }
}
