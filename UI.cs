using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace Hamyar;

/// <summary>Small helpers to build localized controls in code.</summary>
public static class UI
{
    public static Style S(string key) => (Style)Application.Current.FindResource(key);

    public static void Bind(FrameworkElement el, DependencyProperty dp, string key) =>
        el.SetBinding(dp, new Binding("[" + key + "]") { Source = Loc.I });

    /// <summary>Content follows the app language direction. (The window frame itself always stays LTR so the
    /// caption buttons stay on the right like normal Windows apps.)</summary>
    public static void BindFlow(FrameworkElement el) =>
        el.SetBinding(FrameworkElement.FlowDirectionProperty, new Binding("Flow") { Source = Loc.I });

    public static TextBlock Lbl(string key)
    {
        var t = new TextBlock { Margin = new Thickness(2, 14, 0, 4), FontWeight = FontWeights.SemiBold, FontSize = 13 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        Bind(t, TextBlock.TextProperty, key);
        return t;
    }

    public static Button Btn(string key, RoutedEventHandler click, bool ghost = false)
    {
        var b = new Button();
        if (ghost) b.Style = S("GhostButton");
        Bind(b, ContentControl.ContentProperty, key);
        if (click != null) b.Click += click;      // routed events throw on a null handler
        return b;
    }

    /// <summary>Small glyph button with a localized tooltip. Glyphs in the private-use range use the icon font.</summary>
    public static Button IconBtn(string glyph, RoutedEventHandler click, string tipKey = null)
    {
        var b = new Button { Content = glyph, Style = S("GhostButton"), Padding = new Thickness(10, 6, 10, 6), MinWidth = 38 };
        if (glyph.Length == 1 && glyph[0] >= '\uE000' && glyph[0] <= '\uF8FF')
        {
            b.SetResourceReference(Control.FontFamilyProperty, "IconFont");
            b.FontSize = 16;
        }
        if (tipKey != null) Bind(b, FrameworkElement.ToolTipProperty, tipKey);
        if (click != null) b.Click += click;
        return b;
    }

    public static RadioButton Chip(string group, string tag, string key, bool compact = false)
    {
        var rb = new RadioButton { GroupName = group, Tag = tag, Style = S("Chip") };
        if (compact) { rb.Padding = new Thickness(10, 4, 10, 4); rb.Margin = new Thickness(2); }
        Bind(rb, ContentControl.ContentProperty, key);
        return rb;
    }

    /// <summary>A clickable link line (icon + underlined text) - used instead of printing file paths.</summary>
    public static FrameworkElement Link(string glyph, string key, Action act)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 5, 0, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brushes.Transparent          // so the whole line is clickable
        };
        var ic = new TextBlock { Text = glyph, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        ic.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        ic.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextDecorations = TextDecorations.Underline };
        Bind(text, TextBlock.TextProperty, key);

        row.MouseLeftButtonUp += (s, e) => act();
        row.Children.Add(ic);
        row.Children.Add(text);
        return row;
    }

    /// <summary>Opens a folder in Explorer (creates it first if needed).</summary>
    public static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Write("OpenFolder: " + ex.Message); }
    }

    /// <summary>Opens a file with its default program (e.g. the log in Notepad).</summary>
    public static void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) File.WriteAllText(path, "");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Write("OpenFile: " + ex.Message); }
    }

    /// <summary>Common look for dialogs / popups (glass too).</summary>
    public static void Setup(Window w, Window owner, bool frame = false)
    {
        if (owner != null)
        {
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            w.ShowInTaskbar = false;
        }
        w.SetResourceReference(Window.FontFamilyProperty, "AppFont");
        w.FontSize = 14;
        w.SetResourceReference(Window.BackgroundProperty, "BgBrush");
        w.SetResourceReference(Window.ForegroundProperty, "FgBrush");
        w.SourceInitialized += (s, e) =>
        {
            Glass.Apply(w, Cfg.Theme == 0, Cfg.Backdrop);
            if (frame) AddFrame(w);
        };
    }

    /// <summary>The shared dialog look: a heading with a small accent bar and a fine line, then the dialog's own content (scrolling on its own).</summary>
    static void AddFrame(Window w)
    {
        if (w.Content is not UIElement body || body is Grid { Tag: "dlgframe" }) return;
        w.Content = null;
        var g = new Grid { Tag = "dlgframe" };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new DockPanel { Margin = new Thickness(22, 16, 22, 0), LastChildFill = true };
        BindFlow(head);
        var bar = new Border { Width = 4, Height = 20, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        DockPanel.SetDock(bar, Dock.Left);
        head.Children.Add(bar);
        head.Children.Add(new TextBlock { Text = w.Title, FontSize = 18, FontWeight = FontWeights.ExtraBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var line = new Border { Height = 1, Margin = new Thickness(22, 12, 22, 0), VerticalAlignment = VerticalAlignment.Bottom };
        line.SetResourceReference(Border.BackgroundProperty, "LineBrush");
        var top = new StackPanel();
        top.Children.Add(head);
        top.Children.Add(line);
        Grid.SetRow(top, 0);
        Grid.SetRow(body, 1);
        g.Children.Add(top);
        g.Children.Add(body);
        w.Content = g;
    }
}

/// <summary>Themed replacements for the white Windows message box.</summary>
public static class Dialogs
{
    public static bool Confirm(Window owner, string message, bool danger = true)
    {
        var w = Build(owner, message, out var row);
        bool result = false;
        var yes = new Button { MinWidth = 96 };
        UI.Bind(yes, ContentControl.ContentProperty, danger ? "delete" : "ok");
        if (danger)
        {
            yes.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
            yes.Foreground = Brushes.White;
        }
        yes.Click += (s, e) => { result = true; w.Close(); };
        var no = UI.Btn("cancel", (s, e) => w.Close(), true);
        no.MinWidth = 96;
        row.Children.Add(yes);
        row.Children.Add(no);
        w.ShowDialog();
        return result;
    }

    public static void Info(Window owner, string message)
    {
        var w = Build(owner, message, out var row);
        var ok = UI.Btn("ok", (s, e) => w.Close());
        ok.MinWidth = 96;
        row.Children.Add(ok);
        w.ShowDialog();
    }

    static Window Build(Window owner, string message, out StackPanel row)
    {
        var w = new Window
        {
            Title = Loc.Get("app"),
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        UI.Setup(w, owner);
        if (owner == null)
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.Topmost = true;
        }
        var sp = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        UI.BindFlow(sp);
        sp.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15, Margin = new Thickness(0, 4, 0, 22) });
        row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        sp.Children.Add(row);
        w.Content = sp;
        return w;
    }
}

public static class InputDialog
{
    public static string Ask(Window owner, string title, string initial)
    {
        var w = new Window
        {
            Title = title, Width = 400, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize
        };
        UI.Setup(w, owner);
        var tb = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 16) };
        var ok = UI.Btn("ok", (s, e) => w.DialogResult = true);
        ok.MinWidth = 96;
        var cancel = UI.Btn("cancel", (s, e) => w.DialogResult = false, true);
        cancel.MinWidth = 96;
        tb.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) w.DialogResult = true; };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(ok);
        row.Children.Add(cancel);
        var sp = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        UI.BindFlow(sp);
        sp.Children.Add(tb);
        sp.Children.Add(row);
        w.Content = sp;
        w.Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
        return w.ShowDialog() == true ? tb.Text.Trim() : null;
    }

    /// <summary>Like Ask, but a fixed (non-editable) word is shown in front of the text box - used for "All ..." where
    /// only what follows "All" may change. Returns the trimmed text (empty = reset), or null if cancelled.</summary>
    public static string AskWithPrefix(Window owner, string title, string prefix, string initial)
    {
        var w = new Window
        {
            Title = title, Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize
        };
        UI.Setup(w, owner);
        var tb = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 16) };
        var pre = new TextBlock
        {
            Text = prefix + " ", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 16)
        };
        pre.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        var ok = UI.Btn("ok", (s, e) => w.DialogResult = true);
        ok.MinWidth = 96;
        var cancel = UI.Btn("cancel", (s, e) => w.DialogResult = false, true);
        cancel.MinWidth = 96;
        tb.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) w.DialogResult = true; };
        var field = new DockPanel();
        DockPanel.SetDock(pre, Dock.Left);
        field.Children.Add(pre);
        field.Children.Add(tb);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(ok);
        row.Children.Add(cancel);
        var sp = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        UI.BindFlow(sp);
        sp.Children.Add(field);
        sp.Children.Add(row);
        w.Content = sp;
        w.Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
        return w.ShowDialog() == true ? tb.Text.Trim() : null;
    }
}
