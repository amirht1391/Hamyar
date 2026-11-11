using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Hamyar;

/// <summary>A named colour group for planner rows (like tab groups in a browser). Rows have no group by default.</summary>
public class PlanGroup
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3B82F6";

    /// <summary>A small calm palette (works with white text).</summary>
    public static readonly string[] Palette =
    {
        "#6B7280", "#3B82F6", "#EF4444", "#D97706", "#16A34A", "#DB2777", "#7C3AED", "#0891B2", "#EA580C"
    };

    /// <summary>40 colours: 10 hues x 4 shades (light to deep) for the picker dialog.</summary>
    public static readonly string[] BigPalette = BuildBig();
    static string[] BuildBig()
    {
        var l = new List<string>();
        double[] hues = { 0, 20, 40, 60, 130, 170, 200, 225, 270, 320 };
        (double s, double v)[] shades = { (0.35, 1.0), (0.65, 0.95), (0.85, 0.80), (0.9, 0.55) };
        foreach (var (s, v) in shades)
            foreach (var h in hues)
                l.Add(ColorPickerPanel.ToHex(ColorPickerPanel.HsvToColor(h, s, v)));
        return l.ToArray();
    }

    public static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return (Color)ColorConverter.ConvertFromString(Palette[1]); }
    }

    public static bool IsHex(string s) =>
        !string.IsNullOrWhiteSpace(s) && System.Text.RegularExpressions.Regex.IsMatch(s.Trim(), "^#[0-9a-fA-F]{6}$");
}

/// <summary>Name + colour of a group: a big palette, plus a real colour selector for your own colour.</summary>
public class GroupDialog : Window
{
    readonly TextBox _name = new();
    readonly List<(Border ring, string color)> _swatches = new();
    readonly Border _preview = new() { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(1) };
    readonly TextBlock _hexText = new() { VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(10, 0, 0, 0) };
    ColorPickerPanel _picker;
    string _color;
    public string GroupName => _name.Text.Trim();
    public string ColorHex => _color;
    public bool Deleted { get; private set; }

    public GroupDialog(Window owner, string name, string color, bool canDelete)
    {
        _color = PlanGroup.IsHex(color) ? color.ToUpperInvariant() : PlanGroup.Palette[1];
        UI.Setup(this, owner, true);
        Title = Loc.Get("grp_title");
        Width = 420;
        Height = 560;

        var root = new StackPanel { Margin = new Thickness(20) };
        UI.BindFlow(root);
        root.Children.Add(UI.Lbl("grp_name"));
        _name.Text = name;
        root.Children.Add(_name);

        root.Children.Add(UI.Lbl("grp_color"));
        var grid = new UniformGrid { Columns = 10, Margin = new Thickness(0, 4, 0, 4), FlowDirection = FlowDirection.LeftToRight };
        foreach (var c in PlanGroup.BigPalette)
        {
            var dot = new Ellipse { Width = 20, Height = 20, Fill = new SolidColorBrush(PlanGroup.ParseColor(c)) };
            var ring = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(1), BorderThickness = new Thickness(2),
                                    BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = dot };
            string col = c;
            ring.MouseLeftButtonUp += (s, e) => Pick(col, true);
            _swatches.Add((ring, c));
            grid.Children.Add(ring);
        }
        root.Children.Add(grid);

        // own colour: preview + hex label + the selector (hidden until asked for)
        var own = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        _preview.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        own.Children.Add(_preview);
        own.Children.Add(_hexText);
        root.Children.Add(own);

        var more = UI.Btn("grp_custom", null, true);
        more.HorizontalAlignment = HorizontalAlignment.Left;
        more.Margin = new Thickness(0, 8, 0, 0);
        _picker = new ColorPickerPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        _picker.Color = PlanGroup.ParseColor(_color);
        _picker.ColorChanged += c => Pick(ColorPickerPanel.ToHex(c), false);
        more.Click += (s, e) => _picker.Visibility = _picker.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        root.Children.Add(more);
        root.Children.Add(_picker);
        Pick(_color, true);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        footer.Children.Add(UI.Btn("save", (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text)) return;
            DialogResult = true;
        }));
        footer.Children.Add(UI.Btn("cancel", (s, e) => DialogResult = false, true));
        if (canDelete) footer.Children.Add(UI.Btn("delete", (s, e) => { Deleted = true; DialogResult = true; }, true));
        root.Children.Add(footer);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        Loaded += (s, e) => { _name.Focus(); _name.SelectAll(); };
    }

    void Pick(string c, bool syncPicker)
    {
        _color = c.ToUpperInvariant();
        if (syncPicker) _picker.Color = PlanGroup.ParseColor(_color);
        _preview.Background = new SolidColorBrush(PlanGroup.ParseColor(_color));
        _hexText.Text = _color;
        foreach (var (ring, color) in _swatches)
        {
            bool on = string.Equals(color, _color, StringComparison.OrdinalIgnoreCase);
            if (on) ring.SetResourceReference(Border.BorderBrushProperty, "FgBrush"); else ring.BorderBrush = Brushes.Transparent;
        }
    }
}

public partial class MainWindow
{
    static FrameworkElement Dot(string hex, double size = 12) =>
        new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(PlanGroup.ParseColor(hex)) };

    /// <summary>The group menu: one line per group (colour circle + name), "no group", "new group..." and "manage groups...".</summary>
    ContextMenu BuildGroupMenu(UIElement target, Func<long> get, Action<long> set, Action refresh)
    {
        var menu = new ContextMenu { PlacementTarget = target, FlowDirection = Loc.I.Flow };
        var groups = Db.GetPlanGroups();
        long cur = get();
        foreach (var g in groups)
        {
            var gg = g;
            var mi = new MenuItem { Header = g.Name, Icon = Dot(g.Color), FontWeight = g.Id == cur ? FontWeights.ExtraBold : FontWeights.Normal };
            mi.Click += (s2, e2) => { set(gg.Id); refresh(); };
            menu.Items.Add(mi);
        }
        if (cur != 0)
        {
            var none = new MenuItem { Header = Loc.Get("grp_remove"), Icon = new Ellipse { Width = 12, Height = 12, StrokeThickness = 1.5, Stroke = Brushes.Gray } };
            none.Click += (s2, e2) => { set(0); refresh(); };
            menu.Items.Add(none);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator { Style = UI.S("MenuSep") });
        var add = new MenuItem { Header = Loc.Get("grp_menu_new") };
        add.Click += (s2, e2) =>
        {
            var dlg = new GroupDialog(this, "", PlanGroup.Palette[(groups.Count + 1) % PlanGroup.Palette.Length], false);
            if (dlg.ShowDialog() != true) return;
            var ng = new PlanGroup { Name = dlg.GroupName, Color = dlg.ColorHex };
            Db.SavePlanGroup(ng);
            set(ng.Id); refresh();
        };
        menu.Items.Add(add);
        if (groups.Count > 0)
        {
            var manage = new MenuItem { Header = Loc.Get("grp_manage") };
            manage.Click += (s2, e2) => { new GroupsDialog(this).ShowDialog(); refresh(); };
            menu.Items.Add(manage);
        }
        return menu;
    }

    /// <summary>Button of fixed size (so all rows line up): a colour chip with the group's circle and name, or a faint tag icon.</summary>
    Button MakeGroupButton(Func<long> get, Action<long> set, Action refresh)
    {
        var btn = new Button { Style = UI.S("GhostButton"), Padding = new Thickness(6, 4, 6, 4), Width = 112, VerticalAlignment = VerticalAlignment.Center };
        var g = Db.GetPlanGroups().FirstOrDefault(x => x.Id == get());
        if (g == null)
        {
            var ic = new TextBlock { Text = "\uE8EC", FontSize = 15, Opacity = 0.55, HorizontalAlignment = HorizontalAlignment.Center };
            ic.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            btn.Content = ic;
        }
        else
        {
            var col = PlanGroup.ParseColor(g.Color);
            var chip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 3, 8, 3), Background = new SolidColorBrush(Color.FromArgb(46, col.R, col.G, col.B)) };
            var line = new DockPanel { LastChildFill = true };
            var dot = Dot(g.Color, 10);
            dot.Margin = new Thickness(0, 0, 7, 0); dot.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(dot, Dock.Left);
            line.Children.Add(dot);
            line.Children.Add(new TextBlock { Text = g.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            chip.Child = line;
            btn.Content = chip;
        }
        btn.ToolTip = Loc.Get("grp_menu_tip");
        btn.Click += (s, e) => BuildGroupMenu(btn, get, set, refresh).IsOpen = true;
        return btn;
    }
}

/// <summary>All groups in one tidy list: click a row to change its name / colour, the bin deletes it (a quick yes / no appears right in the row).</summary>
public class GroupsDialog : Window
{
    readonly StackPanel _list = new();

    public GroupsDialog(Window owner)
    {
        UI.Setup(this, owner, true);
        Title = Loc.Get("grp_manage");
        Width = 440;
        Height = 520;
        MinHeight = 360;
        ResizeMode = ResizeMode.NoResize;

        var grid = new Grid { Margin = new Thickness(20, 14, 20, 18) };
        UI.BindFlow(grid);
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };
        Grid.SetRow(scroll, 0);
        grid.Children.Add(scroll);

        var foot = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        var add = UI.Btn("grp_menu_new", (s, e) =>
        {
            var dlg = new GroupDialog(this, "", PlanGroup.Palette[(Db.GetPlanGroups().Count + 1) % PlanGroup.Palette.Length], false);
            if (dlg.ShowDialog() != true) return;
            Db.SavePlanGroup(new PlanGroup { Name = dlg.GroupName, Color = dlg.ColorHex });
            Fill();
        }, true);
        DockPanel.SetDock(add, Dock.Left);
        var ok = UI.Btn("ok", (s, e) => Close());
        DockPanel.SetDock(ok, Dock.Right);
        foot.Children.Add(add);
        foot.Children.Add(ok);
        Grid.SetRow(foot, 1);
        grid.Children.Add(foot);

        Content = grid;
        Fill();
    }

    void Fill(long confirmId = 0)
    {
        _list.Children.Clear();
        foreach (var g in Db.GetPlanGroups())
        {
            var gg = g;
            var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 4, 6, 4), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1), MinHeight = 46 };
            card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            var row = new DockPanel { LastChildFill = true };

            if (confirmId == g.Id)
            {
                // inline question: "Delete this group?  [Delete] [No]"
                var yes = new Button { MinWidth = 70, Margin = new Thickness(0, 0, 6, 0), Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), Foreground = Brushes.White };
                UI.Bind(yes, ContentControl.ContentProperty, "delete");
                yes.Click += (s, e) => { Db.DeletePlanGroup(gg.Id); Fill(); };
                var no = UI.Btn("cancel", (s, e) => Fill(), true);
                DockPanel.SetDock(no, Dock.Right);
                DockPanel.SetDock(yes, Dock.Right);
                row.Children.Add(no);
                row.Children.Add(yes);
                var q = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                UI.Bind(q, TextBlock.TextProperty, "grp_sure");
                row.Children.Add(q);
            }
            else
            {
                var del = UI.IconBtn("\uE74D", (s, e) => Fill(gg.Id), "delete");
                DockPanel.SetDock(del, Dock.Right);
                row.Children.Add(del);
                var dot = new Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(PlanGroup.ParseColor(g.Color)), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text = g.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                // the whole row (not the bin) opens the editor
                card.Cursor = Cursors.Hand;
                card.Background = Brushes.Transparent;
                card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
                card.MouseLeftButtonUp += (s, e) =>
                {
                    if (e.OriginalSource is DependencyObject d && FindButton(d) != null) return;
                    var dlg = new GroupDialog(this, gg.Name, gg.Color, false);
                    if (dlg.ShowDialog() != true) return;
                    gg.Name = dlg.GroupName; gg.Color = dlg.ColorHex;
                    Db.SavePlanGroup(gg);
                    Fill();
                };
                card.ToolTip = Loc.Get("grp_edit");
            }
            card.Child = row;
            _list.Children.Add(card);
        }
        if (_list.Children.Count == 0)
        {
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 6, 2, 6) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
            UI.Bind(t, TextBlock.TextProperty, "grp_none");
            _list.Children.Add(t);
        }
    }

    static Button FindButton(DependencyObject d)
    {
        while (d != null)
        {
            if (d is Button b) return b;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}
