using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hamyar;

// Settings page + Help page
public partial class MainWindow
{
    StackPanel NewCard(Panel host, string titleKey, string glyph)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 14),
            Margin = new Thickness(0, 0, 0, 10),
            BorderThickness = new Thickness(1)
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");

        var body = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var t = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(t, TextBlock.TextProperty, titleKey);
        head.Children.Add(t);
        body.Children.Add(head);
        card.Child = body;
        host.Children.Add(card);
        return body;
    }

    void SubHeader(Panel host, string key)
    {
        var t = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4), TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(t, TextBlock.TextProperty, key);
        host.Children.Add(t);
    }

    void Note(Panel host, string key)
    {
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(4, 4, 0, 0) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(note, TextBlock.TextProperty, key);
        host.Children.Add(note);
    }

    /// <summary>One "label + HH:mm box" pair used by the sleep/wake/school hour settings.</summary>
    StackPanel HourField(string labelKey, string initial, Action<string> save)
    {
        var col = new StackPanel { Margin = new Thickness(0, 4, 24, 10) };
        col.Children.Add(UI.Lbl(labelKey));
        var box = new TextBox
        {
            Text = initial,
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Center,
            FlowDirection = System.Windows.FlowDirection.LeftToRight,
            Margin = new Thickness(0, 3, 0, 0)
        };
        box.LostFocus += (s, e) =>
        {
            if (Cfg.TryParseTimeOfDay(box.Text, out var ts)) save(ts.ToString(@"hh\:mm"));
            // re-read the canonical saved value so an invalid entry snaps back to the last good time
            box.Text = labelKey switch
            {
                "wake_time_label" => Cfg.WakeTimeText,
                "sleep_time_label" => Cfg.SleepTimeText,
                "school_start_label" => Cfg.SchoolStartText,
                "school_end_label" => Cfg.SchoolEndText,
                _ => box.Text
            };
        };
        col.Children.Add(box);
        return col;
    }

    void BuildSettings()
    {
        var h = SettingsHost;
        h.Children.Clear();

        var ap = NewCard(h, "sec_appearance", "\uE771");
        SubHeader(ap, "theme");
        AddChips(ap, "theme", new[] { ("0", "dark"), ("1", "light") }, Cfg.Theme.ToString(), false);
        SubHeader(ap, "accent");
        AddChips(ap, "accent", new[] { ("0", "blue"), ("1", "red"), ("2", "green"), ("3", "yellow") }, Cfg.Accent.ToString(), true);
        SubHeader(ap, "backdrop");
        AddChips(ap, "backdrop", new[] { ("3", "acrylic"), ("2", "mica"), ("1", "none") }, Cfg.Backdrop.ToString(), false);

        var lg = NewCard(h, "sec_language", "\uE774");
        SubHeader(lg, "language");
        AddChips(lg, "language", new[] { ("fa", "lang_fa"), ("en", "lang_en") }, Cfg.Lang, false);

        // working days (alarm repeat "Working days" + the timetable columns) and the "next school day" hour
        var wk = NewCard(h, "sec_week", "\uE787");
        SubHeader(wk, "workdays");
        var wd = Cfg.WorkDaySet();
        var boxes = new List<CheckBox>();
        var wrap = new WrapPanel();
        foreach (var dow in DayOrder)
        {
            var cb = new CheckBox { IsChecked = wd.Contains(dow), Tag = dow, Margin = new Thickness(0, 4, 20, 4) };
            UI.Bind(cb, ContentControl.ContentProperty, "d_" + dow);
            boxes.Add(cb);
            wrap.Children.Add(cb);
        }
        foreach (var box in boxes)
        {
            box.Click += (s, e) =>
            {
                var selected = boxes.Where(b => b.IsChecked == true).Select(b => (int)b.Tag).ToList();
                if (selected.Count == 0) { ((CheckBox)s).IsChecked = true; return; }   // at least one working day
                Cfg.SetWorkDays(selected);
                Safe(BuildTimetable);
            };
        }
        wk.Children.Add(wrap);

        SubHeader(wk, "nextday");
        var timeBox = new TextBox
        {
            Text = Cfg.NextDayTimeText,
            Width = 110,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Center,
            FlowDirection = System.Windows.FlowDirection.LeftToRight
        };
        timeBox.LostFocus += (s, e) =>
        {
            if (Cfg.TryParseTimeOfDay(timeBox.Text, out var ts)) Cfg.NextDayTimeText = ts.ToString(@"hh\:mm");
            timeBox.Text = Cfg.NextDayTimeText;           // invalid text goes back to the saved time
            Safe(RefreshNextDay);
        };
        wk.Children.Add(timeBox);
        Note(wk, "nextday_note");

        // sleep / wake / school hours used to keep study and flashcard reviews out of sleep and school time
        var hrs = NewCard(h, "settings_hours_title", "\uE823");
        var hoursGrid = new WrapPanel();
        hoursGrid.Children.Add(HourField("wake_time_label", Cfg.WakeTimeText, v => Cfg.WakeTimeText = v));
        hoursGrid.Children.Add(HourField("sleep_time_label", Cfg.SleepTimeText, v => Cfg.SleepTimeText = v));
        hoursGrid.Children.Add(HourField("school_start_label", Cfg.SchoolStartText, v => Cfg.SchoolStartText = v));
        hoursGrid.Children.Add(HourField("school_end_label", Cfg.SchoolEndText, v => Cfg.SchoolEndText = v));
        hrs.Children.Add(hoursGrid);
        Note(hrs, "settings_hours_note");

        var sm = NewCard(h, "study_mode", "\uE7BE");
        AddChips(sm, "studymode", new[] { ("1", "study_mode_compact"), ("0", "study_mode_normal") }, Cfg.StudyCompact ? "1" : "0", false);
        Note(sm, "study_mode_note");

        var pn = NewCard(h, "sec_planner", "\uE787");
        AddCheck(pn, "opt_pl_limit", Cfg.PlanLimit, v => Cfg.PlanLimit = v);
        AddCheck(pn, "opt_pl_autofill", Cfg.PlanAutoFill, v => Cfg.PlanAutoFill = v);
        AddCheck(pn, "opt_pl_chain", Cfg.PlanChain, v => Cfg.PlanChain = v);

        var gn = NewCard(h, "sec_general", "\uE713");
        AddCheck(gn, "opt_autostart", Cfg.Autostart, v => { Cfg.Autostart = v; Cfg.ApplyAutostart(v); });
        AddCheck(gn, "opt_keep", Cfg.KeepRunning, v => Cfg.KeepRunning = v);
        AddCheck(gn, "show_help", Cfg.ShowHelp, v => { Cfg.ShowHelp = v; ApplyTabs(); });
        AddCheck(gn, "opt_anim", Cfg.Animations, v => Cfg.Animations = v);
        AddCheck(gn, "safe_mode", Cfg.SafeMode, v => Cfg.SafeMode = v);

        Safe(() => BuildHotkeyCard(h));

        BuildBackupCard(h);

        var fl = NewCard(h, "sec_files", "\uE8B7");
        AddFileLinks(fl);
    }

    static void AddFileLinks(Panel host)
    {
        host.Children.Add(UI.Link("\uE8B7", "link_data", () => UI.OpenFolder(Db.Dir)));
        host.Children.Add(UI.Link("\uE74E", "link_backups", () => UI.OpenFolder(Backups.Dir)));
        host.Children.Add(UI.Link("\uE8A5", "link_log", () => UI.OpenFile(Log.FilePath)));
        host.Children.Add(UI.Link("\uE8B7", "link_app", () => UI.OpenFolder(AppContext.BaseDirectory)));
    }

    void AddChips(Panel host, string group, (string tag, string key)[] opts, string current, bool colorDot)
    {
        var wp = new WrapPanel();
        foreach (var (tag, key) in opts)
        {
            var rb = new RadioButton
            {
                GroupName = "set_" + group,
                Tag = tag,
                IsChecked = tag == current,
                Style = UI.S(colorDot ? "ColorChip" : "Chip")
            };
            if (colorDot)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Fill = new SolidColorBrush(ThemeManager.Accents[int.Parse(tag)]),
                    StrokeThickness = 1.5
                };
                dot.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "FgBrush");   // outline keeps it visible on any color
                sp.Children.Add(dot);
                var tb = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                UI.Bind(tb, TextBlock.TextProperty, key);
                sp.Children.Add(tb);
                rb.Content = sp;
            }
            else
            {
                UI.Bind(rb, ContentControl.ContentProperty, key);
            }
            rb.Checked += (s, e) => SettingChanged(group, (string)((RadioButton)s).Tag);
            wp.Children.Add(rb);
        }
        host.Children.Add(wp);
    }

    void AddCheck(Panel host, string key, bool value, Action<bool> set)
    {
        var cb = new CheckBox { IsChecked = value, Margin = new Thickness(0, 6, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
        UI.Bind(cb, ContentControl.ContentProperty, key);
        cb.Click += (s, e) => set(cb.IsChecked == true);
        host.Children.Add(cb);
    }

    void SettingChanged(string group, string value)
    {
        switch (group)
        {
            case "studymode": Cfg.StudyCompact = value == "1"; break;
            case "theme": Cfg.Theme = int.Parse(value); ReTheme(); break;
            case "accent": Cfg.Accent = int.Parse(value); ReTheme(); break;
            case "backdrop": Cfg.Backdrop = int.Parse(value); ReTheme(); break;
            case "language": Cfg.Lang = value; Loc.I.SetLang(value); break;
        }
    }

    void ReTheme()
    {
        ThemeManager.Apply(Cfg.Theme, Cfg.Accent, Cfg.Backdrop);
        Glass.Apply(this, Cfg.Theme == 0, Cfg.Backdrop);
    }

    // ------------------------------------------------------------------ help page
    void BuildHelp()
    {
        var h = HelpHost;
        h.Children.Clear();

        var start = NewCard(h, "help_start_title", "\uE897");
        foreach (var k in new[] { "help_1", "help_2", "help_3", "help_4", "help_5" })
            start.Children.Add(HelpText(Loc.Get(k)));

        var study = NewCard(h, "help_study_title", "\uE7BE");
        foreach (var k in new[] { "help_study_1", "help_study_2", "help_study_3", "help_study_4", "help_study_5" })
            study.Children.Add(HelpText(Loc.Get(k)));

        var cards = NewCard(h, "help_cards_title", "\uE8F1");
        foreach (var k in new[] { "help_cards_1", "help_cards_2", "help_cards_3", "help_cards_4", "help_cards_5" })
            cards.Children.Add(HelpText(Loc.Get(k)));

        var exam = NewCard(h, "help_exam_title", "\uE70B");
        foreach (var k in new[] { "help_exam_1", "help_exam_2" })
            exam.Children.Add(HelpText(Loc.Get(k)));

        var tt = NewCard(h, "help_tt_title", "\uE787");
        foreach (var k in new[] { "help_tt_1", "help_tt_2" })
            tt.Children.Add(HelpText(Loc.Get(k)));

        var pr = NewCard(h, "help_pr_title", "\uE708");
        pr.Children.Add(HelpText(Loc.Get("help_pr_1")));

        Safe(() => BuildHotkeyHelp(h));
        Safe(() => BuildTourCard(h));

        var backup = NewCard(h, "help_backup_title", "\uE74E");
        foreach (var k in new[] { "help_backup_1", "help_backup_2" })
            backup.Children.Add(HelpText(Loc.Get(k)));

        var files = NewCard(h, "help_files_title", "\uE8B7");
        Safe(() => AddFileLinks(files));
    }

    static TextBlock HelpText(string text)
    {
        return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 23, Margin = new Thickness(0, 5, 0, 5) };
    }
}
