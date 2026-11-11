using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Hamyar;

public class PlanRow
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Start { get; set; } = "";     // HH:mm
    public int Minutes { get; set; }
    public string Notes { get; set; } = "";
    public long SetId { get; set; }
    /// <summary>0 = the plan's own day, 1 = the next day (the chain passed midnight), ...</summary>
    public int DayOffset { get; set; }
    /// <summary>Muted rows stay in the plan but do not ring.</summary>
    public bool Muted { get; set; }
    /// <summary>Colour group of the row (0 = none, the default).</summary>
    public long GroupId { get; set; }
    /// <summary>Place of the row in its plan (rows can be inserted in the middle).</summary>
    public long Pos { get; set; }
    /// <summary>An extra row: it keeps its own time and neither pushes nor is pushed by the chain of the other rows.</summary>
    public bool Free { get; set; }
}

/// <summary>A saved plan: a name ("برنامه‌ریزی شنبه 16 آبان"), its date and its rows. Daily = repeats every day.</summary>
public class PlanSet
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Today;
    public bool Daily { get; set; }
    public long GroupId { get; set; }      // kept for the database column (unused)
}

// Planner tab: an editable table - title, start time, duration, notes. Every edit is saved as soon as the cell loses focus.
// Options (Settings, all on by default): limit adding rows, auto-fill start time, chain rows.
// Every row rings an alarm (no snooze) when its start time arrives.
public partial class MainWindow
{
    bool _plSortOnce;
    long _plHighlight;
    DispatcherTimer _planTimer;
    readonly HashSet<string> _planFired = new();
    DateTime _planFiredDay = DateTime.Today;

    static bool PlanValid(PlanRow r) => r != null && Cfg.TryParseTimeOfDay(r.Start, out _) && r.Minutes > 0;

    static string PlanEnd(PlanRow r)
    {
        Cfg.TryParseTimeOfDay(r.Start, out var t);
        var total = ((int)t.TotalMinutes + r.Minutes) % 1440;
        return TimeSpan.FromMinutes(total).ToString(@"hh\:mm");
    }

    // ------------------------------------------------------------------ alarms for plan rows
    void StartPlanAlarms()
    {
        _planTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _planTimer.Tick += (s, e) => Safe(CheckPlanAlarms);
        _planTimer.Start();
    }

    void CheckPlanAlarms()
    {
        var now = DateTime.Now;
        if (_planFiredDay != now.Date) { _planFired.Clear(); _planFiredDay = now.Date; }
        CheckPrayAlarms(now);
        var sets = Db.GetPlanSets().ToDictionary(x => x.Id);
        foreach (var p in Db.GetPlans())
        {
            if (p.Muted || !Cfg.TryParseTimeOfDay(p.Start, out var t) || !sets.TryGetValue(p.SetId, out var set)) continue;
            // a dated plan rings on its own day (rows after midnight on the next day); a daily plan rings every day
            // a plan rings only on its own day (rows after midnight on the next one) - never on later days; "daily" plans start on their date
            if (now.Date < set.Date.Date) continue;
            var due = set.Daily ? now.Date + t : set.Date.Date.AddDays(p.DayOffset) + t;
            if (due.Date < now.Date) continue;
            var diff = now - due;
            if (diff < TimeSpan.Zero || diff >= TimeSpan.FromSeconds(60)) continue;   // only inside its own minute
            var key = p.Id + "|" + due.ToString("yyyyMMddHHmm");
            if (!_planFired.Add(key)) continue;
            var it = new Item
            {
                Title = string.IsNullOrWhiteSpace(p.Title) ? Loc.Get("nav_planner") : p.Title,
                Notes = p.Notes ?? "",
                AlarmAt = due,
                Sound = ""
            };
            AudioService.Play(it.Sound, true);
            new AlarmPopup(it, null, true).Show();     // no snooze for planner alarms
        }
    }

    /// <summary>Rings at the prayer times the user ticked (Prayer tab).</summary>
    void CheckPrayAlarms(DateTime now)
    {
        var day = Db.GetPrayDay(now.Date);
        if (day == null) return;
        for (int i = 0; i < 6; i++)
        {
            if (!Cfg.PrayAlarm(i)) continue;
            DateTime w;
            try { w = PrayTimes.At(now.Date, day, i); } catch { continue; }
            var diff = now - w;
            if (diff < TimeSpan.Zero || diff >= TimeSpan.FromSeconds(60)) continue;
            if (!_planFired.Add("pray|" + now.ToString("yyyyMMdd") + "|" + i)) continue;
            var it = new Item { Title = Loc.Get(PrayTimes.Keys[i]), Notes = w.ToString("HH:mm", CultureInfo.InvariantCulture), AlarmAt = w, Sound = "" };
            AudioService.Play(it.Sound, true);
            new AlarmPopup(it, null, true).Show();
        }
    }

    // ------------------------------------------------------------------ saved plans (one per day)
    long _planSetId;

    string PlanSetName(DateTime d) => Loc.Get("pl_set_prefix") + " " + Loc.Get("d_" + (int)d.DayOfWeek) + " " + Jal.DayMonth(d);

    PlanSet CurrentPlanSet(List<PlanSet> sets)
    {
        var cur = sets.FirstOrDefault(x => x.Id == _planSetId);
        if (cur != null) return cur;
        // default: today's plan, else the nearest coming one, else the latest
        cur = sets.FirstOrDefault(x => x.Date.Date == DateTime.Today) ?? sets.Where(x => x.Date.Date > DateTime.Today).OrderBy(x => x.Date).FirstOrDefault() ?? sets.Last();
        _planSetId = cur.Id;
        return cur;
    }

    /// <summary>When the chain of rows passes midnight (a later row starts at an earlier clock time), that row and those after it belong to the next day.</summary>
    static bool RecomputeOffsets(List<PlanRow> creationOrder)
    {
        bool changed = false;
        int offset = 0;
        TimeSpan? prev = null; int prevMin = 0;
        foreach (var r in creationOrder)
        {
            if (r.Free) { if (r.DayOffset != offset) { r.DayOffset = offset; changed = true; if (r.Id != 0) Db.SavePlan(r); } continue; }
            if (Cfg.TryParseTimeOfDay(r.Start, out var t))
            {
                if (prev.HasValue && t < prev.Value && prev.Value.TotalMinutes + prevMin >= 1440) offset++;
                prev = t; prevMin = r.Minutes;
            }
            if (r.DayOffset != offset) { r.DayOffset = offset; changed = true; if (r.Id != 0) Db.SavePlan(r); }
        }
        return changed;
    }

    // ------------------------------------------------------------------ table
    void BuildPlanner()
    {
        if (PlannerHost == null) return;
        var h = PlannerHost;
        h.Children.Clear();
        bool sorted = _plSortOnce;
        _plSortOnce = false;

        var sets = Db.GetPlanSets();
        if (sets.Count == 0)
        {
            var first = new PlanSet { Date = DateTime.Today };
            first.Name = PlanSetName(first.Date);
            Db.SavePlanSet(first);
            Db.AdoptOrphanPlans(first.Id);
            sets = Db.GetPlanSets();
        }
        var set = CurrentPlanSet(sets);
        BuildPlanHeader(h, sets, set);

        var rows = Db.GetPlans(set.Id);
        RecomputeOffsets(rows);
        if (sorted)
            rows = rows.OrderBy(r => r.DayOffset).ThenBy(r => Cfg.TryParseTimeOfDay(r.Start, out var t) ? t : TimeSpan.MaxValue).ThenBy(r => r.Id).ToList();

        var table = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(1) };
        table.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        table.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        var body = new StackPanel();
        table.Child = body;

        Grid NewRow()
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 120 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 140 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(262) });   // fixed, so every row lines up
            return g;
        }

        var head = NewRow();
        string[] keys = { "pl_title", "pl_start", "pl_duration_short", "pl_notes" };
        for (int i = 0; i < 4; i++)
        {
            var t = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 8, 4) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
            UI.Bind(t, TextBlock.TextProperty, keys[i]);
            Grid.SetColumn(t, i);
            head.Children.Add(t);
        }
        body.Children.Add(head);

        // start/duration boxes of every row, in display order, so a change can ripple down the table
        var cells = new List<(PlanRow row, TextBox start, TextBox dur)>();
        var dayLabels = new List<(PlanRow row, TextBlock label)>();
        string DayText(PlanRow r) => r.DayOffset <= 0 ? "" : set.Daily ? Loc.Get("tt_tomorrow") : Jal.DayMonth(set.Date.AddDays(r.DayOffset));
        void RefreshDayLabels()
        {
            foreach (var (row, label) in dayLabels) { label.Text = DayText(row); label.Visibility = row.DayOffset > 0 ? Visibility.Visible : Visibility.Collapsed; }
        }

        void Cascade(int from)
        {
            if (!Cfg.PlanChain || sorted) return;       // in the "sorted by time" view the order is not the chain order
            if (cells[from].row.Free) return;          // an extra row never moves the others
            var prev = cells[from].row;
            for (int i = from + 1; i < cells.Count; i++)
            {
                var cur = cells[i];
                if (cur.row.Free) continue;             // extra rows are skipped by the chain
                if (!PlanValid(prev)) break;
                var ns = PlanEnd(prev);
                prev = cur.row;
                if (cur.row.Start == ns) continue;
                cur.row.Start = ns;
                cur.start.Text = ns;
                Db.SavePlan(cur.row);
                Anim.Pulse(cur.start);
            }
        }

        int index = 0;
        foreach (var r in rows)
        {
            var row = r;
            int myIndex = index++;
            var g = NewRow();
            TextBox Cell(string text, int col, bool ltr, TextAlignment align = TextAlignment.Left)
            {
                var tb = new TextBox { Text = text, Margin = new Thickness(2, 1, 2, 1), Padding = new Thickness(8, 5, 8, 5), TextAlignment = align, VerticalContentAlignment = VerticalAlignment.Center };
                if (ltr) tb.FlowDirection = System.Windows.FlowDirection.LeftToRight;
                Grid.SetColumn(tb, col);
                g.Children.Add(tb);
                return tb;
            }
            var title = Cell(row.Title, 0, false);
            var start = Cell(row.Start, 1, true, TextAlignment.Center);
            // rows after midnight show their date under the time (so they do not look like the start of the day)
            // time box: the time on the left, and (for rows after midnight) the date at its right end - both INSIDE one box
            var dayLabel = new TextBlock { FontSize = 11, Margin = new Thickness(4, 0, 10, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false,
                                           VerticalAlignment = VerticalAlignment.Center, FlowDirection = Loc.I.Flow };
            dayLabel.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            g.Children.Remove(start);
            start.Margin = new Thickness(0); start.Background = System.Windows.Media.Brushes.Transparent; start.BorderThickness = new Thickness(0);
            start.Padding = new Thickness(8, 5, 4, 5);
            var inner = new Grid { FlowDirection = System.Windows.FlowDirection.LeftToRight };
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(start, 0); Grid.SetColumn(dayLabel, 1);
            inner.Children.Add(start); inner.Children.Add(dayLabel);
            var startBox = new Border { Margin = new Thickness(2, 1, 2, 1), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                                        BorderBrush = System.Windows.Media.Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Child = inner };
            startBox.SetResourceReference(Border.BackgroundProperty, "InputBrush");
            start.GotKeyboardFocus += (s2, e2) => startBox.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            start.LostKeyboardFocus += (s2, e2) => startBox.BorderBrush = System.Windows.Media.Brushes.Transparent;
            Grid.SetColumn(startBox, 1);
            g.Children.Add(startBox);
            dayLabels.Add((row, dayLabel));
            var dur = Cell(row.Minutes > 0 ? row.Minutes.ToString() : "", 2, true, TextAlignment.Center);
            var notes = Cell(row.Notes, 3, false);
            cells.Add((row, start, dur));

            void Save()
            {
                var oldStart = row.Start;
                var oldMin = row.Minutes;
                bool hadStart = Cfg.TryParseTimeOfDay(oldStart, out var oldT);
                row.Title = title.Text.Trim();
                row.Start = Cfg.TryParseTimeOfDay(start.Text, out var ts) ? ts.ToString(@"hh\:mm") : "";
                row.Minutes = int.TryParse(Util.Digits(dur.Text.Trim()), out var mn) && mn > 0 ? mn : 0;
                row.Notes = notes.Text.Trim();
                Db.SavePlan(row);
                start.Text = row.Start;
                dur.Text = row.Minutes > 0 ? row.Minutes.ToString() : "";
                if (row.Start != oldStart || row.Minutes != oldMin)
                {
                    // a changed start time moves the rows ABOVE by the same amount; everything below follows the chain
                    if (Cfg.PlanChain && !sorted && !row.Free && hadStart && row.Start != oldStart && Cfg.TryParseTimeOfDay(row.Start, out var newT))
                    {
                        int delta = (int)Math.Round((newT - oldT).TotalMinutes);
                        delta = ((delta % 1440) + 1440) % 1440; if (delta > 720) delta -= 1440;
                        if (delta != 0)
                            for (int i = 0; i < myIndex; i++)
                            {
                                var up = cells[i];
                                if (up.row.Free) continue;
                                if (!Cfg.TryParseTimeOfDay(up.row.Start, out var ut)) continue;
                                var ns = TimeSpan.FromMinutes((((int)ut.TotalMinutes + delta) % 1440 + 1440) % 1440).ToString(@"hh\:mm");
                                up.row.Start = ns; up.start.Text = ns;
                                Db.SavePlan(up.row);
                                Anim.Pulse(up.start);
                            }
                    }
                    Cascade(myIndex);
                }
                RecomputeOffsets(rows.OrderBy(q => q.Pos).ThenBy(q => q.Id).ToList());
                RefreshDayLabels();
            }
            title.LostFocus += (s, e) => Save();
            start.LostFocus += (s, e) => Save();
            dur.LostFocus += (s, e) => Save();
            notes.LostFocus += (s, e) => Save();

            var del = UI.IconBtn("\uE74D", (s, e) =>
            {
                Db.DeletePlan(row.Id);
                // the rows below the deleted one move up and follow the row above it
                if (Cfg.PlanChain && !sorted)
                {
                    int at = rows.IndexOf(row);
                    rows.Remove(row);
                    PlanRow before = null;
                    for (int i = at - 1; i >= 0; i--) if (!rows[i].Free) { before = rows[i]; break; }
                    if (before != null && !row.Free)
                        for (int i = at; i < rows.Count; i++)
                        {
                            if (rows[i].Free) continue;
                            if (!PlanValid(before)) break;
                            var ns = PlanEnd(before);
                            before = rows[i];
                            if (rows[i].Start == ns) continue;
                            rows[i].Start = ns;
                            Db.SavePlan(rows[i]);
                        }
                    RecomputeOffsets(rows);
                }
                BuildPlanner();
            }, "delete");
            // mute / unmute this row's alarm (the row stays in the plan)
            var mute = UI.IconBtn(row.Muted ? "\uE74F" : "\uE7ED", null);
            void ShowMute()
            {
                mute.Content = row.Muted ? "\uE74F" : "\uE7ED";
                mute.ToolTip = Loc.Get(row.Muted ? "pl_unmute" : "pl_mute");
                mute.Opacity = row.Muted ? 0.55 : 1;
                title.Opacity = row.Muted ? 0.55 : 1;
            }
            mute.Click += (s, e) => { row.Muted = !row.Muted; Db.SavePlan(row); ShowMute(); };
            ShowMute();
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            // colour group of this row (none by default)
            tools.Children.Add(MakeGroupButton(() => row.GroupId, id => { row.GroupId = id; Db.SavePlan(row); },
                () => { _plSortOnce = sorted; BuildPlanner(); }));
            // insert a row right below this one (menu: shift the rest, or an extra row that moves nothing)
            var ins = UI.IconBtn("\uE710", null);
            ins.ToolTip = Loc.Get("pl_insert_tip");
            ins.Click += (s, e) =>
            {
                var menu = new ContextMenu { PlacementTarget = ins, FlowDirection = Loc.I.Flow };
                void Insert(bool free)
                {
                    var np = new PlanRow { SetId = set.Id, Free = free };
                    if (Cfg.PlanAutoFill && PlanValid(row)) np.Start = PlanEnd(row);
                    Db.InsertPlanAfter(np, row.Pos);
                    _plHighlight = np.Id;
                    _plSortOnce = false;
                    BuildPlanner();
                }
                var m1 = new MenuItem { Header = Loc.Get("pl_insert_chain") }; m1.Click += (s2, e2) => Insert(false);
                var m2 = new MenuItem { Header = Loc.Get("pl_insert_free") }; m2.Click += (s2, e2) => Insert(true);
                menu.Items.Add(m1); menu.Items.Add(m2);
                // switch THIS row between "extra" (moves nothing) and a normal row that counts in the chain (e.g. a forgotten row)
                menu.Items.Add(new Separator());
                var m3 = new MenuItem { Header = Loc.Get(row.Free ? "pl_make_chain" : "pl_make_free") };
                m3.Click += (s2, e2) =>
                {
                    int at = rows.IndexOf(row);
                    PlanRow before = null;
                    for (int i = at - 1; i >= 0; i--) if (!rows[i].Free) { before = rows[i]; break; }
                    row.Free = !row.Free;
                    if (!row.Free && before != null && PlanValid(before)) row.Start = PlanEnd(before);   // it joins the chain right after the row before it
                    Db.SavePlan(row);
                    if (Cfg.PlanChain && !sorted)
                    {
                        var prevRow = row.Free ? before : row;
                        for (int i = at + 1; i < rows.Count; i++)
                        {
                            if (rows[i].Free) continue;
                            if (prevRow == null || !PlanValid(prevRow)) break;
                            var ns = PlanEnd(prevRow);
                            prevRow = rows[i];
                            if (rows[i].Start == ns) continue;
                            rows[i].Start = ns;
                            Db.SavePlan(rows[i]);
                        }
                    }
                    RecomputeOffsets(rows.OrderBy(q => q.Pos).ThenBy(q => q.Id).ToList());
                    _plSortOnce = sorted;
                    BuildPlanner();
                };
                menu.Items.Add(m3);
                menu.IsOpen = true;
            };
            tools.Children.Add(ins);
            if (row.Free)
            {
                title.ToolTip = Loc.Get("pl_free_tip");
                var bar3 = new Border { Width = 3, Margin = new Thickness(0, 6, 0, 6), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
                bar3.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                Grid.SetColumn(bar3, 0);
                g.Children.Add(bar3);
            }
            tools.Children.Add(mute); tools.Children.Add(del);
            Grid.SetColumn(tools, 4);
            g.Children.Add(tools);
            body.Children.Add(g);

            RefreshDayLabels();
            if (row.Id == _plHighlight)
            {
                _plHighlight = 0;
                Anim.Enter(g, 14, 0.985, 420, 0);
            }
        }

        if (rows.Count == 0)
        {
            var empty = new TextBlock { Margin = new Thickness(8, 10, 8, 6), TextWrapping = TextWrapping.Wrap };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
            UI.Bind(empty, TextBlock.TextProperty, "pl_empty");
            body.Children.Add(empty);
        }

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        bar.Children.Add(UI.Btn("pl_add", (s, e) => AddPlanRow(rows, set)));
        if (rows.Count > 1)
        {
            var sort = UI.Btn("pl_sort", (s, e) => { _plSortOnce = true; BuildPlanner(); }, true);
            sort.Margin = new Thickness(8, 0, 0, 0);
            bar.Children.Add(sort);
        }
        if (rows.Count > 0)
        {
            var exp = UI.Btn("pl_export_word", (s, e) => ExportPlanWord(rows, set), true);
            exp.Margin = new Thickness(8, 0, 0, 0);
            bar.Children.Add(exp);
        }
        body.Children.Add(bar);
        h.Children.Add(table);
    }

    /// <summary>Planner header: the saved plans as chips, "+ new plan", and the date / repeat / delete of the selected one.</summary>
    void BuildPlanHeader(Panel h, List<PlanSet> sets, PlanSet set)
    {
        var box = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1) };
        box.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        var col = new StackPanel();
        box.Child = col;

        var chips = new WrapPanel();
        foreach (var x in sets)
        {
            var rb = new RadioButton { Content = x.Name, Tag = x.Id, GroupName = "plansets", IsChecked = x.Id == set.Id, Style = (Style)FindResource("Chip") };
            rb.Checked += (s, e) => { if (_planSetId != x.Id) { _planSetId = x.Id; BuildPlanner(); } };
            ChipBar.Attach(chips, rb, (id, name) => { var ps = Db.GetPlanSets().FirstOrDefault(q => q.Id == id); if (ps != null) { ps.Name = name; Db.SavePlanSet(ps); } }, null);
            chips.Children.Add(rb);
        }
        var plus = UI.Btn("pl_new_set", (s, e) => NewPlanSet(), true);
        chips.Children.Add(plus);
        col.Children.Add(chips);

        var line = new WrapPanel { Margin = new Thickness(4, 6, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var lbl = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        lbl.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(lbl, TextBlock.TextProperty, "pl_set_date");
        var date = new TextBox { Text = Jal.Date(set.Date), Width = 120, FlowDirection = System.Windows.FlowDirection.LeftToRight, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        date.LostFocus += (s, e) =>
        {
            if (!Jal.TryParse(date.Text, out var d)) { date.Text = Jal.Date(set.Date); return; }
            if (d.Date == set.Date.Date) return;
            bool autoNamed = set.Name == PlanSetName(set.Date);
            set.Date = d.Date;
            if (autoNamed) set.Name = PlanSetName(d.Date);
            Db.SavePlanSet(set);
            BuildPlanner();
        };
        var daily = new CheckBox { IsChecked = set.Daily, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        UI.Bind(daily, ContentControl.ContentProperty, "pl_daily");
        daily.Click += (s, e) => { set.Daily = daily.IsChecked == true; Db.SavePlanSet(set); BuildPlanner(); };
        var del = UI.IconBtn("\uE74D", (s, e) =>
        {
            if (!Dialogs.Confirm(this, Loc.Get("pl_delete_set_confirm"))) return;
            Db.DeletePlanSet(set.Id);
            _planSetId = 0;
            BuildPlanner();
        }, "pl_delete_set");
        del.Margin = new Thickness(12, 0, 0, 0);
        line.Children.Add(lbl); line.Children.Add(date); line.Children.Add(daily); line.Children.Add(del);
        col.Children.Add(line);
        h.Children.Add(box);
    }

    void NewPlanSet()
    {
        var sets = Db.GetPlanSets();
        var last = sets.Count > 0 ? sets.Max(x => x.Date.Date) : DateTime.Today.AddDays(-1);
        var suggest = last >= DateTime.Today ? last.AddDays(1) : DateTime.Today;
        var text = InputDialog.Ask(this, Loc.Get("pl_new_set_ask"), Jal.Date(suggest));
        if (text == null) return;
        if (!Jal.TryParse(text, out var d)) { Dialogs.Info(this, Loc.Get("invalid_dt")); return; }
        var ps = new PlanSet { Date = d.Date, Name = PlanSetName(d.Date) };
        Db.SavePlanSet(ps);
        _planSetId = ps.Id;
        BuildPlanner();
    }

    /// <summary>Planner table -> Word file (A5, one plain table).</summary>
    void ExportPlanWord(List<PlanRow> rows, PlanSet set)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = set.Name + ".docx",
            DefaultExt = ".docx",
            AddExtension = true,
            Filter = "Word (*.docx)|*.docx"
        };
        if (dlg.ShowDialog(this) != true) return;
        string DayText(PlanRow r) => r.DayOffset <= 0 ? "" : set.Daily ? Loc.Get("tt_tomorrow") : Jal.DayMonth(set.Date.AddDays(r.DayOffset));
        var (count, error) = PlanDocx.Export(dlg.FileName, rows, Loc.I.IsRtl, set.Name, set.Daily ? "" : Jal.Date(set.Date), DayText);
        Dialogs.Info(this, error ?? string.Format(Loc.Get("pl_export_done"), count));
    }

    void AddPlanRow(List<PlanRow> rows, PlanSet set)
    {
        // "last" = the most recently created row (the table is ordered by creation, except in the sorted view)
        var last = rows.OrderBy(r => r.Pos).ThenBy(r => r.Id).LastOrDefault(r => !r.Free) ?? rows.OrderBy(r => r.Pos).ThenBy(r => r.Id).LastOrDefault();
        if (Cfg.PlanLimit && last != null && !PlanValid(last))
        {
            Dialogs.Info(this, Loc.Get("pl_limit_msg"));
            return;
        }
        var np = new PlanRow { SetId = set.Id };
        if (Cfg.PlanAutoFill && PlanValid(last)) np.Start = PlanEnd(last);
        Db.SavePlan(np);
        _plHighlight = np.Id;
        BuildPlanner();
    }
}
