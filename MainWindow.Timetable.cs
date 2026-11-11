using System.Windows;
using System.Windows.Controls;

namespace Hamyar;

// Weekly timetable: 7 periods per day, days start from Saturday
public partial class MainWindow
{
    /// <summary>Saturday first (Iranian week). Values are (int)DayOfWeek.</summary>
    static readonly int[] DayOrder = { 6, 0, 1, 2, 3, 4, 5 };

    Dictionary<(int, int), string> _tt = new();
    StackPanel _ttNextBody;

    /// <summary>
    /// The day to show in the top card: the current day until the fixed time of day (Settings, default 15:00),
    /// then the next school day until midnight. After midnight it is the current day again.
    /// A day off is skipped: the next working day is shown.
    /// </summary>
    static DateTime TargetDay(DateTime now)
    {
        var wd = Cfg.WorkDaySet();
        var start = now.TimeOfDay >= Cfg.NextDayTime ? now.Date.AddDays(1) : now.Date;
        for (int i = 0; i < 7; i++)
        {
            var day = start.AddDays(i);
            if (wd.Contains((int)day.DayOfWeek)) return day;
        }
        return start;
    }

    static Border NumberBadge(string text)
    {
        var b = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, "InputBrush");
        b.Child = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FlowDirection = System.Windows.FlowDirection.LeftToRight
        };
        return b;
    }

    void BuildTimetable()
    {
        if (TimetableHost == null) return;
        var h = TimetableHost;
        h.Children.Clear();
        _tt = Db.GetTimetable();

        // ---- card 1: the next school day
        _ttNextBody = NewCard(h, "tt_next_title", "\uE823");
        RefreshNextDay();

        // ---- card 2: the whole week
        var week = NewCard(h, "tt_week", "\uE787");
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 10) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(hint, TextBlock.TextProperty, "tt_hint");
        week.Children.Add(hint);

        var wd = Cfg.WorkDaySet();
        var days = DayOrder.Where(x => wd.Contains(x)).ToList();
        int targetDow = (int)TargetDay(DateTime.Now).DayOfWeek;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // period numbers only
        foreach (var dayCol in days)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 110 });
        for (int r = 0; r <= 7; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // header row: day names (the day shown in the top card is highlighted)
        for (int i = 0; i < days.Count; i++)
        {
            int dow = days[i];
            var name = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold };
            UI.Bind(name, TextBlock.TextProperty, "d_" + dow);

            FrameworkElement header = name;
            if (dow == targetDow)
            {
                name.SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
                var pill = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(4, 0, 4, 8), HorizontalAlignment = HorizontalAlignment.Center, Child = name };
                pill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                header = pill;
            }
            else
            {
                name.Margin = new Thickness(4, 4, 4, 12);
            }
            Grid.SetRow(header, 0);
            Grid.SetColumn(header, i + 1);
            grid.Children.Add(header);
        }

        // 7 periods
        for (int slot = 1; slot <= 7; slot++)
        {
            int sl = slot;

            var number = NumberBadge(sl.ToString());
            number.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetRow(number, sl);
            Grid.SetColumn(number, 0);
            grid.Children.Add(number);

            for (int i = 0; i < days.Count; i++)
            {
                int dow = days[i];
                _tt.TryGetValue((dow, sl), out var existing);
                var cell = new TextBox
                {
                    Text = existing ?? "",
                    Margin = new Thickness(3),
                    TextAlignment = TextAlignment.Center,
                    FontSize = 13,
                    MinWidth = 100
                };
                cell.LostFocus += (s, e) =>
                {
                    var text = cell.Text.Trim();
                    Db.SetCell(dow, sl, text);
                    _tt[(dow, sl)] = text;
                    RefreshNextDay();
                };
                Grid.SetRow(cell, sl);
                Grid.SetColumn(cell, i + 1);
                grid.Children.Add(cell);
            }
        }
        week.Children.Add(grid);
    }

    /// <summary>Rebuilds only the top card (keeps the typing focus in the grid below).</summary>
    void RefreshNextDay()
    {
        if (_ttNextBody == null) return;
        while (_ttNextBody.Children.Count > 1) _ttNextBody.Children.RemoveAt(1);

        var target = TargetDay(DateTime.Now);
        string when = target.Date == DateTime.Today ? Loc.Get("tt_today")
                    : target.Date == DateTime.Today.AddDays(1) ? Loc.Get("tt_tomorrow") : "";
        string name = Loc.Get("d_" + (int)target.DayOfWeek);
        string label = (when.Length > 0 ? when + " — " : "") + name;

        _ttNextBody.Children.Add(new TextBlock
        {
            Text = string.Format(Loc.Get("tt_for"), label) + "   (" + ReviewEngine.Iso(target) + ")",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        bool any = false;
        for (int slot = 1; slot <= 7; slot++)
        {
            if (!_tt.TryGetValue(((int)target.DayOfWeek, slot), out var subject) || string.IsNullOrWhiteSpace(subject)) continue;
            any = true;

            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());

            var title = new TextBlock { Text = subject, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(title, 1);
            row.Children.Add(NumberBadge(slot.ToString()));
            row.Children.Add(title);
            _ttNextBody.Children.Add(row);
        }

        if (!any)
        {
            var empty = new TextBlock { Text = Loc.Get("tt_empty_day"), TextWrapping = TextWrapping.Wrap };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
            _ttNextBody.Children.Add(empty);
        }
    }
}
