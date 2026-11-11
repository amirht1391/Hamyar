using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Hamyar;

/// <summary>The guided first-run tour: a cursor flies to each side-bar tab, "clicks" it, the page opens and a card explains it.
/// It is shown automatically the first time, and again from Help or with the tour shortcut.</summary>
public partial class MainWindow
{
    record TourStep(string Tag, string Glyph, string Key, string Mock);

    static readonly TourStep[] TourSteps =
    {
        new(null,        "", "welcome",   "rows"),
        new("home",      "", "home",      "rows"),
        new("planner",   "", "planner",   "rows"),
        new("tables",    "", "tables",    "toggles"),
        new("study",     "", "study",     "dots"),
        new("cards",     "", "cards",     "card"),
        new("timetable", "", "timetable", "grid"),
        new("prayer",    "", "prayer",    "dots"),
        new("exam",      "", "exam",      "grid"),
        new("settings",  "", "settings",  "toggles"),
        new("settings",  "", "hotkeys",   "keys"),
        new("help",      "", "help",      "rows"),
        new(null,        "", "done",      "rows"),
    };

    bool _tourActive;
    int _tourIdx;
    Grid _tourLayer;
    System.Windows.Shapes.Path _tourDim;
    Border _tourRing, _tourCard;
    Canvas _tourCanvas;
    FrameworkElement _tourTarget;
    Polygon _tourCursor;
    Ellipse _tourRipple;

    void BuildTourCard(Panel host)
    {
        var card = NewCard(host, "help_tour_title", "");
        card.Children.Add(HelpText(Loc.Get("help_tour_1")));
        var b = UI.Btn("tour_btn", (s, e) => StartTour());
        b.HorizontalAlignment = HorizontalAlignment.Left;
        card.Children.Add(b);
    }

    void StartTour()
    {
        if (_tourActive) return;
        _tourActive = true;
        _tourIdx = 0;
        GoTo("home");

        _tourLayer = new Grid { FlowDirection = FlowDirection.LeftToRight, Focusable = true, Background = Brushes.Transparent, ClipToBounds = true };
        Grid.SetColumnSpan(_tourLayer, 2);
        System.Windows.Controls.Panel.SetZIndex(_tourLayer, 1000);

        _tourDim = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), IsHitTestVisible = false };
        _tourLayer.Children.Add(_tourDim);

        _tourCanvas = new Canvas { IsHitTestVisible = false };
        _tourRing = new Border { BorderThickness = new Thickness(2.5), CornerRadius = new CornerRadius(14), Opacity = 0, IsHitTestVisible = false };
        _tourRing.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        _tourCanvas.Children.Add(_tourRing);
        _tourRipple = new Ellipse { Width = 20, Height = 20, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1), StrokeThickness = 3 };
        _tourRipple.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        _tourCanvas.Children.Add(_tourRipple);
        _tourCursor = new Polygon
        {
            Points = new PointCollection { new(0, 0), new(0, 22), new(6, 17), new(10, 26), new(14, 24), new(10, 16), new(18, 16) },
            Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1.4, Opacity = 0, IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.5 }
        };
        _tourCanvas.Children.Add(_tourCursor);
        _tourLayer.Children.Add(_tourCanvas);

        _tourLayer.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) { EndTour(); e.Handled = true; }
            else if (e.Key == Key.Right || e.Key == Key.Enter || e.Key == Key.Space) { TourNext(); e.Handled = true; }
            else if (e.Key == Key.Left) { TourGo(_tourIdx - 1); e.Handled = true; }
        };
        _tourLayer.SizeChanged += (s, e) => { Spot(_tourTarget, false); if (_tourCard != null && _tourIdx < TourSteps.Length) PlaceTourCard(_tourCard, TourSteps[_tourIdx]); };
        RootGrid.Children.Add(_tourLayer);
        _tourLayer.Opacity = 0;
        _tourLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
        _tourLayer.Focus();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => TourGo(0)));
    }

    void EndTour()
    {
        if (!_tourActive) return;
        _tourActive = false;
        Db.SetSetting("tour_done", "1");
        var layer = _tourLayer;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (s, e) => RootGrid.Children.Remove(layer);
        layer.BeginAnimation(OpacityProperty, fade);
        GoTo("home");
    }

    void TourNext()
    {
        if (_tourIdx >= TourSteps.Length - 1) EndTour(); else TourGo(_tourIdx + 1);
    }

    /// <summary>The on-screen rectangle of an element inside the tour layer. (TranslatePoint is wrong for elements in a right-to-left
    /// tree - their local x starts at the right edge - so the box is transformed instead.)</summary>
    Rect BoundsIn(FrameworkElement e) =>
        e.TransformToVisual(_tourLayer).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    FrameworkElement NavChip(string tag) =>
        NavPanel.Children.OfType<RadioButton>().FirstOrDefault(r => (r.Tag as string) == tag && r.Visibility == Visibility.Visible);

    void TourGo(int i)
    {
        if (!_tourActive || i < 0 || i >= TourSteps.Length) return;
        _tourIdx = i;
        var step = TourSteps[i];
        BuildTourCard(step);
        _tourLayer.UpdateLayout();

        var chip = step.Tag == null ? null : NavChip(step.Tag);
        bool sameTab = step.Tag != null && step.Tag == (i > 0 ? TourSteps[i - 1].Tag : null);
        _tourRing.Opacity = 0;
        if (chip == null || sameTab)
        {
            // nothing to click: just light up the chip (or nothing for the welcome / finish steps)
            if (step.Tag != null && step.Tag != "settings") GoTo(step.Tag);
            Spot(chip, true);
            return;
        }

        // cursor flies from the card to the tab, clicks it, the page opens
        var cb = BoundsIn(_tourCard);
        var tb = BoundsIn(chip);
        var from = new Point(cb.X + cb.Width / 2, cb.Y + cb.Height / 2);
        var to = new Point(tb.X + tb.Width * 0.5, tb.Y + tb.Height * 0.55);
        _tourCursor.Opacity = 1;
        var move = new Duration(TimeSpan.FromMilliseconds(650));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        Canvas.SetLeft(_tourCursor, from.X); Canvas.SetTop(_tourCursor, from.Y);
        var ax = new DoubleAnimation(from.X, to.X, move) { EasingFunction = ease };
        var ay = new DoubleAnimation(from.Y, to.Y, move) { EasingFunction = ease };
        ax.Completed += (s, e) =>
        {
            if (!_tourActive || _tourIdx != i) return;
            // click: ripple + press, then open the page
            Canvas.SetLeft(_tourRipple, to.X - 10); Canvas.SetTop(_tourRipple, to.Y - 10);
            var sc = (ScaleTransform)_tourRipple.RenderTransform;
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 4, TimeSpan.FromMilliseconds(450)));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 4, TimeSpan.FromMilliseconds(450)));
            _tourRipple.BeginAnimation(OpacityProperty, new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(450)));
            GoTo(step.Tag);
            _tourCursor.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500)) { BeginTime = TimeSpan.FromMilliseconds(250) });
            Spot(chip, true);
        };
        _tourCursor.BeginAnimation(Canvas.LeftProperty, ax);
        _tourCursor.BeginAnimation(Canvas.TopProperty, ay);
    }

    /// <summary>Dims everything except the chip and draws the pulsing ring around it.</summary>
    void Spot(FrameworkElement target, bool animate)
    {
        _tourTarget = target;
        double w = _tourLayer.ActualWidth, h = _tourLayer.ActualHeight;
        var full = new RectangleGeometry(new Rect(0, 0, w, h));
        if (target == null || !target.IsLoaded)
        {
            _tourDim.Data = full;
            _tourRing.Opacity = 0;
            return;
        }
        var tbx = BoundsIn(target);
        var r = new Rect(tbx.X - 5, tbx.Y - 3, tbx.Width + 10, tbx.Height + 6);
        _tourDim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(r, 14, 14));
        Canvas.SetLeft(_tourRing, r.X); Canvas.SetTop(_tourRing, r.Y);
        _tourRing.Width = r.Width; _tourRing.Height = r.Height;
        if (animate)
        {
            var pulse = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            _tourRing.BeginAnimation(OpacityProperty, pulse);
        }
        else _tourRing.Opacity = 1;
    }

    // ------------------------------------------------------------ the explanation card
    void BuildTourCard(TourStep step)
    {
        if (_tourCard != null) _tourLayer.Children.Remove(_tourCard);
        var card = new Border { Width = 410, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(18),
                                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, FlowDirection = Loc.I.Flow, RenderTransformOrigin = new Point(0.5, 0.5),
                                Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 8, Opacity = 0.45, Direction = 270 } };
        card.SetResourceReference(Border.BackgroundProperty, "BgBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");

        var col = new StackPanel();
        col.Children.Add(TourHero(step));

        var title = new TextBlock { Text = Loc.Get("tour_" + step.Key + "_t"), FontSize = 19, FontWeight = FontWeights.ExtraBold, Margin = new Thickness(2, 16, 2, 6), TextWrapping = TextWrapping.Wrap };
        col.Children.Add(title);
        var body = new TextBlock { Text = Loc.Get("tour_" + step.Key + "_b"), TextWrapping = TextWrapping.Wrap, LineHeight = 23, Margin = new Thickness(2, 0, 2, 14) };
        body.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        col.Children.Add(body);

        // progress dots
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14), FlowDirection = FlowDirection.LeftToRight };
        for (int i = 0; i < TourSteps.Length; i++)
        {
            var d = new Border { Height = 6, Width = i == _tourIdx ? 20 : 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(2, 0, 2, 0), Opacity = i <= _tourIdx ? 1 : 0.3 };
            d.SetResourceReference(Border.BackgroundProperty, i == _tourIdx ? "AccentBrush" : "FgBrush");
            dots.Children.Add(d);
        }
        col.Children.Add(dots);

        var bar = new DockPanel { LastChildFill = false };
        bool last = _tourIdx == TourSteps.Length - 1;
        var next = UI.Btn(last ? "tour_finish" : "tour_next", (s, e) => TourNext());
        DockPanel.SetDock(next, Dock.Right);
        bar.Children.Add(next);
        if (_tourIdx > 0)
        {
            var back = UI.Btn("tour_back", (s, e) => TourGo(_tourIdx - 1), true);
            DockPanel.SetDock(back, Dock.Right);
            bar.Children.Add(back);
        }
        if (!last)
        {
            var skip = UI.Btn("tour_skip", (s, e) => EndTour(), true);
            DockPanel.SetDock(skip, Dock.Left);
            bar.Children.Add(skip);
        }
        col.Children.Add(bar);
        card.Child = col;

        _tourCard = card;
        PlaceTourCard(card, step);
        _tourLayer.Children.Add(card);
        card.Opacity = 0;
        card.RenderTransform = new TranslateTransform(0, 14);
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(320)));
        ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _tourLayer.Focus();
    }

    /// <summary>Puts the card right next to the highlighted side-bar tab (on the side away from the bar), level with it and kept inside
    /// the window. The welcome / finish steps have no tab, so the card is centred.</summary>
    void PlaceTourCard(Border card, TourStep step)
    {
        double W = _tourLayer.ActualWidth, H = _tourLayer.ActualHeight;
        if (W < 10 || H < 10) { W = RootGrid.ActualWidth; H = RootGrid.ActualHeight; }
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double cw = card.Width, ch = card.DesiredSize.Height;
        const double gap = 26, edge = 16;
        double x = (W - cw) / 2, y = (H - ch) / 2;
        var chip = step.Tag == null ? null : NavChip(step.Tag);
        if (chip != null && chip.IsLoaded && chip.ActualWidth > 0)
        {
            var cbx = BoundsIn(chip);
            bool navLeft = cbx.X + cbx.Width / 2 < W / 2;
            x = navLeft ? cbx.Right + gap : cbx.X - gap - cw;
            y = cbx.Y + cbx.Height / 2 - ch / 2;
        }
        x = Math.Max(edge, Math.Min(x, W - cw - edge));
        y = Math.Max(edge, Math.Min(y, H - ch - edge));
        card.Margin = new Thickness(x, y, 0, 0);
    }

    /// <summary>The picture at the top of a tour card: accent gradient, big icon and a little mock-up of the page.</summary>
    FrameworkElement TourHero(TourStep step)
    {
        var hero = new Border { Height = 118, CornerRadius = new CornerRadius(14), ClipToBounds = true, FlowDirection = FlowDirection.LeftToRight };
        hero.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var g = new Grid();
        hero.Child = g;

        // soft decorative circles
        void Blob(double size, double x, double y, double op)
        {
            var b = new Ellipse { Width = size, Height = size, Opacity = op, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(x, y, 0, 0) };
            b.SetResourceReference(Shape.FillProperty, "OnAccentBrush");
            g.Children.Add(b);
        }
        Blob(150, -40, -50, 0.10); Blob(90, 300, 50, 0.10);

        var icon = new Border { Width = 66, Height = 66, CornerRadius = new CornerRadius(20), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 0, 0), Opacity = 0.95 };
        icon.SetResourceReference(Border.BackgroundProperty, "OnAccentBrush");
        var glyph = new TextBlock { Text = step.Glyph, FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        icon.Child = glyph;
        g.Children.Add(icon);

        var mock = new Canvas { Width = 200, Height = 90, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        Shape Shp(Shape s, double op)
        {
            s.Opacity = op; s.SetResourceReference(Shape.FillProperty, "OnAccentBrush"); mock.Children.Add(s); return s;
        }
        Rectangle Bar(double x, double y, double w, double h, double op) { var r = (Rectangle)Shp(new Rectangle { Width = w, Height = h, RadiusX = h / 2, RadiusY = h / 2 }, op); Canvas.SetLeft(r, x); Canvas.SetTop(r, y); return r; }
        Ellipse Dot(double x, double y, double d, double op) { var e = (Ellipse)Shp(new Ellipse { Width = d, Height = d }, op); Canvas.SetLeft(e, x); Canvas.SetTop(e, y); return e; }
        switch (step.Mock)
        {
            case "rows":
                for (int r = 0; r < 3; r++) { Dot(0, 6 + r * 30, 18, r == 0 ? 0.95 : 0.55); Bar(28, 9 + r * 30, 90 + (r * 37) % 70, 12, 0.85 - r * 0.2); Bar(28, 25 + r * 30 - 4, 50, 5, 0.4); }
                break;
            case "grid":
                for (int r = 0; r < 3; r++) for (int c = 0; c < 5; c++) { var q = (Rectangle)Shp(new Rectangle { Width = 34, Height = 24, RadiusX = 7, RadiusY = 7 }, (r == 1 && c == 2) ? 0.95 : 0.4); Canvas.SetLeft(q, c * 40); Canvas.SetTop(q, r * 31); }
                break;
            case "card":
                var cd = (Rectangle)Shp(new Rectangle { Width = 120, Height = 78, RadiusX = 14, RadiusY = 14 }, 0.9); Canvas.SetLeft(cd, 40); Canvas.SetTop(cd, 6);
                var back = (Rectangle)Shp(new Rectangle { Width = 120, Height = 78, RadiusX = 14, RadiusY = 14 }, 0.3); Canvas.SetLeft(back, 56); Canvas.SetTop(back, 0);
                Panel.SetZIndex(cd, 5);
                var q2 = new TextBlock { Text = "?", FontSize = 38, FontWeight = FontWeights.Bold }; q2.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                Canvas.SetLeft(q2, 88); Canvas.SetTop(q2, 12); Panel.SetZIndex(q2, 6); mock.Children.Add(q2);
                break;
            case "dots":
                Bar(8, 41, 184, 4, 0.4);
                for (int c = 0; c < 5; c++) Dot(c * 42, 33, 20, c < 2 ? 0.95 : 0.45);
                break;
            case "toggles":
                for (int r = 0; r < 3; r++)
                {
                    Bar(0, 6 + r * 30, 110, 12, 0.7);
                    var pill = Bar(140, 2 + r * 30, 50, 22, r == 1 ? 0.35 : 0.9);
                    Dot(r == 1 ? 143 : 167, 5 + r * 30, 16, 0.5);
                }
                break;
            case "keys":
                string[] ks = { "Ctrl", "+", "1" };
                double x = 10;
                foreach (var k in ks)
                {
                    bool plus = k == "+";
                    double w = plus ? 22 : (k.Length > 1 ? 70 : 46);
                    if (!plus)
                    {
                        var kb = (Rectangle)Shp(new Rectangle { Width = w, Height = 46, RadiusX = 10, RadiusY = 10 }, 0.95);
                        Canvas.SetLeft(kb, x); Canvas.SetTop(kb, 22);
                    }
                    var t = new TextBlock { Text = k, FontSize = plus ? 22 : 17, FontWeight = FontWeights.Bold, Width = w, TextAlignment = TextAlignment.Center };
                    t.SetResourceReference(TextBlock.ForegroundProperty, plus ? "OnAccentBrush" : "AccentBrush");
                    Canvas.SetLeft(t, x); Canvas.SetTop(t, plus ? 28 : 33); Panel.SetZIndex(t, 6);
                    mock.Children.Add(t);
                    x += w + 8;
                }
                break;
        }
        g.Children.Add(mock);
        return hero;
    }
}
