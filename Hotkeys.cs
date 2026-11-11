using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hamyar;

/// <summary>Keyboard shortcuts: built-in ones that can be changed, plus the user's own "open this file / app / link" shortcuts.
/// A shortcut is stored as text like "Ctrl+Shift+K" in the settings table.</summary>
public static class Hotkeys
{
    public record Def(string Id, string LocKey, string Default);

    public static readonly Def[] Actions =
    {
        new("home", "nav_home", "Ctrl+1"),
        new("planner", "nav_planner", "Ctrl+2"),
        new("tables", "nav_tables", "Ctrl+3"),
        new("study", "nav_study", "Ctrl+4"),
        new("cards", "nav_cards", "Ctrl+5"),
        new("timetable", "nav_timetable", "Ctrl+6"),
        new("prayer", "nav_prayer", "Ctrl+7"),
        new("exam", "nav_exam", "Ctrl+8"),
        new("settings", "nav_settings", "Ctrl+9"),
        new("help", "nav_help", "F1"),
        new("session", "hk_session", "Ctrl+Shift+S"),
        new("tour", "hk_tour", "Ctrl+Shift+H"),
    };

    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        { "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+Y", "Ctrl+A", "Alt+F4", "Ctrl+Tab", "Alt+Tab" };

    public static string Get(string id)
    {
        var d = Actions.First(a => a.Id == id).Default;
        return Db.GetSetting("hk_" + id, d);
    }

    public static void Set(string id, string gesture) => Db.SetSetting("hk_" + id, gesture ?? "");

    public static void ResetAll()
    {
        foreach (var a in Actions) Db.SetSetting("hk_" + a.Id, a.Default);
        SaveCustom(new List<(string, string)>());
    }

    // ---- custom shortcuts (open a file / app / link)
    public static List<(string gesture, string target)> Custom()
    {
        var l = new List<(string, string)>();
        foreach (var line in Db.GetSetting("hk_custom", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('\t');
            l.Add((p[0], p.Length > 1 ? p[1] : ""));
        }
        return l;
    }

    public static void SaveCustom(List<(string gesture, string target)> l) =>
        Db.SetSetting("hk_custom", string.Join("\n", l.Select(x => x.gesture + "\t" + x.target.Replace("\n", " ").Replace("\t", " "))));

    /// <summary>"Ctrl+Shift+K" from a key press; null for a lone modifier or a key with no modifier (except F-keys).</summary>
    public static string Format(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None or Key.DeadCharProcessed) return null;
        var m = Keyboard.Modifiers;
        bool fkey = key >= Key.F1 && key <= Key.F12;
        if (!fkey && (m & (ModifierKeys.Control | ModifierKeys.Alt)) == 0) return null;   // plain letters must keep typing
        var parts = new List<string>();
        if ((m & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((m & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((m & ModifierKeys.Shift) != 0) parts.Add("Shift");
        string k = key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)key - (int)Key.NumPad0).ToString(),
            Key.OemComma => ",", Key.OemPeriod => ".", Key.OemPlus => "+", Key.OemMinus => "-",
            _ => key.ToString()
        };
        parts.Add(k);
        return string.Join("+", parts);
    }

    public static bool IsReserved(string g) => Reserved.Contains(g ?? "");

    /// <summary>The action id (or "custom:index") bound to this gesture.</summary>
    public static string Find(string gesture)
    {
        foreach (var a in Actions) if (string.Equals(Get(a.Id), gesture, StringComparison.OrdinalIgnoreCase)) return a.Id;
        var c = Custom();
        for (int i = 0; i < c.Count; i++) if (string.Equals(c[i].gesture, gesture, StringComparison.OrdinalIgnoreCase)) return "custom:" + i;
        return null;
    }

    public static void OpenTarget(string target)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            Process.Start(new ProcessStartInfo(target.Trim().Trim('"')) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Write("Hotkey open: " + ex.Message); }
    }
}

public partial class MainWindow
{
    /// <summary>Runs the shortcut that matches this key press. True when it was used.</summary>
    bool TryHotkey(KeyEventArgs e)
    {
        if (_tourActive) return false;
        var g = Hotkeys.Format(e);
        if (g == null) return false;
        var id = Hotkeys.Find(g);
        if (id == null) return false;
        if (id.StartsWith("custom:"))
        {
            var c = Hotkeys.Custom();
            int i = int.Parse(id.Substring(7));
            if (i < c.Count) Hotkeys.OpenTarget(c[i].target);
        }
        else if (id == "session") RunSession(null);
        else if (id == "tour") StartTour();
        else GoTo(id);
        e.Handled = true;
        return true;
    }

    void GoTo(string tag)
    {
        var rb = NavPanel.Children.OfType<RadioButton>().FirstOrDefault(r => (r.Tag as string) == tag);
        if (rb != null && rb.Visibility == Visibility.Visible) rb.IsChecked = true;
    }

    // ---------------------------------------------------------------- settings card
    void BuildHotkeyCard(Panel host)
    {
        var card = NewCard(host, "sec_hotkeys", "");
        Note(card, "hk_note");

        TextBox Capture(string current, Action<string> assign)
        {
            var tb = new TextBox { Text = current, IsReadOnly = true, Width = 170, TextAlignment = TextAlignment.Center, FlowDirection = FlowDirection.LeftToRight, Cursor = Cursors.Hand };
            tb.PreviewKeyDown += (s, e) =>
            {
                e.Handled = true;
                var k = e.Key == Key.System ? e.SystemKey : e.Key;
                if ((k == Key.Back || k == Key.Delete) && Keyboard.Modifiers == ModifierKeys.None) { tb.Text = ""; assign(""); return; }
                var g = Hotkeys.Format(e);
                if (g == null || Hotkeys.IsReserved(g)) return;
                var owner = Hotkeys.Find(g);
                if (owner != null && !string.Equals(g, tb.Text, StringComparison.OrdinalIgnoreCase))
                {
                    Dialogs.Info(this, string.Format(Loc.Get("hk_conflict"), g));
                    return;
                }
                tb.Text = g; assign(g);
            };
            return tb;
        }

        foreach (var a in Hotkeys.Actions)
        {
            var def = a;
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lbl = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            UI.Bind(lbl, TextBlock.TextProperty, def.LocKey);
            row.Children.Add(lbl);
            var box = Capture(Hotkeys.Get(def.Id), g => Hotkeys.Set(def.Id, g));
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            card.Children.Add(row);
        }

        SubHeader(card, "hk_custom_title");
        Note(card, "hk_custom_note");
        var custom = Hotkeys.Custom();
        void Save() => Hotkeys.SaveCustom(custom);
        for (int i = 0; i < custom.Count; i++)
        {
            int idx = i;
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var key = Capture(custom[idx].gesture, g => { custom[idx] = (g, custom[idx].target); Save(); });
            var target = new TextBox { Text = custom[idx].target, Margin = new Thickness(8, 0, 8, 0), FlowDirection = FlowDirection.LeftToRight };
            target.LostFocus += (s, e) => { custom[idx] = (custom[idx].gesture, target.Text.Trim()); Save(); };
            var del = UI.IconBtn("", (s, e) => { custom.RemoveAt(idx); Save(); BuildSettings(); });
            Grid.SetColumn(target, 1); Grid.SetColumn(del, 2);
            row.Children.Add(key); row.Children.Add(target); row.Children.Add(del);
            card.Children.Add(row);
        }
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        bar.Children.Add(UI.Btn("hk_add", (s, e) => { custom.Add(("", "")); Save(); BuildSettings(); }, true));
        bar.Children.Add(UI.Btn("hk_reset", (s, e) => { Hotkeys.ResetAll(); BuildSettings(); }, true));
        card.Children.Add(bar);
    }

    /// <summary>Help page card: the shortcuts as they are right now.</summary>
    void BuildHotkeyHelp(Panel host)
    {
        var card = NewCard(host, "help_hk_title", "");
        card.Children.Add(HelpText(Loc.Get("help_hk_1")));
        var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        void Add(string label, string g)
        {
            if (string.IsNullOrEmpty(g)) return;
            var chip = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1) };
            chip.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var k = new TextBlock { Text = g, FontWeight = FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 0, 8, 0) };
            k.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            sp.Children.Add(k);
            sp.Children.Add(new TextBlock { Text = label });
            chip.Child = sp;
            wrap.Children.Add(chip);
        }
        foreach (var a in Hotkeys.Actions) Add(Loc.Get(a.LocKey), Hotkeys.Get(a.Id));
        foreach (var (g, t) in Hotkeys.Custom()) Add(System.IO.Path.GetFileName(t.Trim().TrimEnd('/', '\\')) is { Length: > 0 } n ? n : t, g);
        card.Children.Add(wrap);
        card.Children.Add(HelpText(Loc.Get("help_hk_2")));
    }
}
