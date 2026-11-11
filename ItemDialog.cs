using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Hamyar;

/// <summary>Add / edit one item: title, alarm (date, time, repeat), sound, tasks. Compact layout.</summary>
public class ItemDialog : Window
{
    const string AudioFilter =
        "Audio|*.mp3;*.wav;*.wma;*.aac;*.m4a;*.flac;*.ogg;*.aiff;*.aif;*.mp2;*.mp4|All files|*.*";

    readonly Item _it;
    readonly List<TaskAction> _tasks;
    readonly TextBox _title = new(), _notes = new(), _date = new(), _time = new(), _sound = new(), _target = new();
    readonly CheckBox _enabled = new();
    readonly List<RadioButton> _rep = new(), _types = new();
    readonly List<CheckBox> _dayBoxes = new();
    readonly WrapPanel _customPanel = new() { Margin = new Thickness(0, 6, 0, 0) };
    readonly StackPanel _taskPanel = new();

    public ItemDialog(Window owner, Item it)
    {
        _it = it;
        UI.Setup(this, owner, true);
        Title = Loc.Get("item");
        Width = 600;
        Height = 700;
        MinHeight = 480;

        _tasks = it.Tasks.Select(t => new TaskAction { Id = t.Id, Type = t.Type, Target = t.Target, Args = t.Args }).ToList();

        var ltr = System.Windows.FlowDirection.LeftToRight;
        _date.FlowDirection = ltr;
        _time.FlowDirection = ltr;
        _sound.FlowDirection = ltr;
        _target.FlowDirection = ltr;

        var root = new StackPanel { Margin = new Thickness(20) };
        UI.BindFlow(root);

        // ---- title + notes
        _title.Text = it.Title;
        _notes.Text = it.Notes;
        _notes.AcceptsReturn = true;
        _notes.TextWrapping = TextWrapping.Wrap;
        _notes.Height = 92;      // about 4 lines
        _notes.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        root.Children.Add(UI.Lbl("item_title"));
        root.Children.Add(_title);
        root.Children.Add(UI.Lbl("notes"));
        root.Children.Add(_notes);

        // ---- alarm: checkbox, date/time, repeat
        _enabled.IsChecked = it.Id == 0 || it.AlarmAt.HasValue;
        _enabled.Margin = new Thickness(0, 18, 0, 6);
        UI.Bind(_enabled, ContentControl.ContentProperty, "alarm_on");
        root.Children.Add(_enabled);

        var dt = it.AlarmAt ?? DateTime.Now.AddMinutes(10);
        _date.Text = Jal.Date(dt);
        _time.Text = dt.ToString("HH:mm", CultureInfo.InvariantCulture);
        _date.Width = 150;
        _time.Width = 110;
        _date.Margin = new Thickness(0, 3, 8, 3);
        _time.Margin = new Thickness(0, 3, 8, 3);

        var alarmBox = new StackPanel();      // everything about the alarm; disabled when "Set an alarm" is off
        var dateRow = new StackPanel { Orientation = Orientation.Horizontal };
        dateRow.Children.Add(_date);
        dateRow.Children.Add(_time);
        dateRow.Children.Add(UI.Btn("today", (s, e) => _date.Text = Jal.Date(DateTime.Today), true));
        dateRow.Children.Add(UI.Btn("tomorrow", (s, e) => _date.Text = Jal.Date(DateTime.Today.AddDays(1)), true));
        alarmBox.Children.Add(dateRow);

        var hint = new TextBlock { FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(hint, TextBlock.TextProperty, "dt_hint");
        alarmBox.Children.Add(hint);

        var rp = new WrapPanel();
        foreach (var r in Enum.GetValues<RepeatMode>())
        {
            var c = UI.Chip("rp", ((int)r).ToString(), "rp_" + r, true);
            c.IsChecked = r == it.Repeat;
            _rep.Add(c);
            rp.Children.Add(c);
        }
        alarmBox.Children.Add(rp);

        // custom repeat: pick any days of the week (Saturday first)
        var chosen = Scheduler.ParseDays(it.CustomDays);
        foreach (var dow in new[] { 6, 0, 1, 2, 3, 4, 5 })
        {
            var cb = new CheckBox { IsChecked = chosen.Contains(dow), Tag = dow, Margin = new Thickness(0, 2, 16, 2) };
            UI.Bind(cb, ContentControl.ContentProperty, "d_" + dow);
            _dayBoxes.Add(cb);
            _customPanel.Children.Add(cb);
        }
        _customPanel.Visibility = it.Repeat == RepeatMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        foreach (var c in _rep)
            c.Checked += (s, e) => _customPanel.Visibility = (string)((RadioButton)s).Tag == ((int)RepeatMode.Custom).ToString() ? Visibility.Visible : Visibility.Collapsed;
        alarmBox.Children.Add(_customPanel);
        root.Children.Add(alarmBox);

        // ---- sound: one row
        var soundLabel = UI.Lbl("sound");
        root.Children.Add(soundLabel);
        _sound.Text = string.IsNullOrWhiteSpace(it.Sound) && it.Id == 0 ? AudioService.DefaultSound : it.Sound;
        var winBtn = UI.Btn("sound_win", null, true);
        winBtn.Click += (s, e) => ShowWindowsSounds(winBtn);
        var soundRow = new Grid();
        soundRow.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 4; i++) soundRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var browse = UI.IconBtn("\uE712", (s, e) =>
        {
            var dlg = new OpenFileDialog { Filter = AudioFilter };
            if (dlg.ShowDialog(this) == true) _sound.Text = dlg.FileName;
        }, "browse");
        var play = UI.IconBtn("\uE768", (s, e) => AudioService.Play(_sound.Text.Trim(), false), "test");
        var stop = UI.IconBtn("\uE71A", (s, e) => AudioService.Stop(), "stop");
        Grid.SetColumn(winBtn, 1);
        Grid.SetColumn(browse, 2);
        Grid.SetColumn(play, 3);
        Grid.SetColumn(stop, 4);
        foreach (UIElement el in new UIElement[] { _sound, winBtn, browse, play, stop }) soundRow.Children.Add(el);
        root.Children.Add(soundRow);

        // Without an alarm there is nothing to play a sound FOR, so both the alarm's own date/time/repeat
        // section AND the whole sound row (play/stop/browse/Windows-sounds) are disabled together, and
        // playback stops immediately if it was mid-preview when the alarm gets turned off.
        void ApplyAlarmEnabled()
        {
            bool on = _enabled.IsChecked == true;
            alarmBox.IsEnabled = on;
            soundLabel.IsEnabled = on;
            soundRow.IsEnabled = on;
            if (!on) AudioService.Stop();
        }
        ApplyAlarmEnabled();
        _enabled.Click += (s, e) => ApplyAlarmEnabled();

        // ---- tasks: list with ✕ per row + one compact add row
        root.Children.Add(UI.Lbl("tasks"));
        root.Children.Add(_taskPanel);
        RebuildTasks();

        var tt = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var t in Enum.GetValues<TaskType>())
        {
            var c = UI.Chip("tt", ((int)t).ToString(), "tt_" + t, true);
            c.IsChecked = t == TaskType.OpenTarget;
            _types.Add(c);
            tt.Children.Add(c);
        }
        root.Children.Add(tt);

        var addRow = new Grid();
        addRow.ColumnDefinitions.Add(new ColumnDefinition());
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        UI.Bind(_target, FrameworkElement.ToolTipProperty, "target");
        _target.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { AddTask(); e.Handled = true; }
        };
        var browseTarget = UI.IconBtn("\uE712", (s, e) =>
        {
            var dlg = new OpenFileDialog();
            if (dlg.ShowDialog(this) == true) _target.Text = dlg.FileName;
        }, "browse");
        var add = UI.Btn("add_task", (s, e) => AddTask());
        Grid.SetColumn(browseTarget, 1);
        Grid.SetColumn(add, 2);
        addRow.Children.Add(_target);
        addRow.Children.Add(browseTarget);
        addRow.Children.Add(add);
        root.Children.Add(addRow);

        // ---- save / cancel
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        footer.Children.Add(UI.Btn("save", (s, e) => Save()));
        footer.Children.Add(UI.Btn("cancel", (s, e) => DialogResult = false, true));
        root.Children.Add(footer);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        Closed += (s, e) => AudioService.Stop();
    }

    void ShowWindowsSounds(Button anchor)
    {
        var menu = new ContextMenu { MaxHeight = 320, PlacementTarget = anchor };
        menu.SetResourceReference(Control.FontFamilyProperty, "AppFont");
        UI.BindFlow(menu);

        var none = new MenuItem { Header = Loc.Get("sound_none") };
        none.Click += (s, e) => _sound.Text = "";
        menu.Items.Add(none);

        var media = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
        if (Directory.Exists(media))
        {
            foreach (var f in Directory.GetFiles(media, "*.wav").OrderBy(x => x))
            {
                var path = f;
                var mi = new MenuItem { Header = System.IO.Path.GetFileNameWithoutExtension(f) };
                mi.Click += (s, e) => { _sound.Text = path; AudioService.Play(path, false); };
                menu.Items.Add(mi);
            }
        }
        menu.IsOpen = true;
    }

    void RebuildTasks()
    {
        _taskPanel.Children.Clear();
        foreach (var t in _tasks.ToList())
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock
            {
                Text = t.Display,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var del = UI.IconBtn("\uE711", (s, e) => { _tasks.Remove(t); RebuildTasks(); }, "delete");
            Grid.SetColumn(del, 1);
            row.Children.Add(text);
            row.Children.Add(del);

            var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 2, 2, 2), Margin = new Thickness(0, 2, 0, 2), Child = row };
            card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            _taskPanel.Children.Add(card);
        }
    }

    void AddTask()
    {
        if (string.IsNullOrWhiteSpace(_target.Text)) return;
        var type = (TaskType)int.Parse((string)_types.First(c => c.IsChecked == true).Tag);
        _tasks.Add(new TaskAction { Type = type, Target = _target.Text.Trim() });
        _target.Clear();
        RebuildTasks();
        _target.Focus();
    }

    void Save()
    {
        bool ok = Jal.TryParse(_date.Text, out var d)
                  & DateTime.TryParseExact(Util.Digits(_time.Text.Trim()), "H:mm",
                      CultureInfo.InvariantCulture, DateTimeStyles.None, out var t);
        bool alarmOn = _enabled.IsChecked == true;

        if (alarmOn && !ok)
        {
            Dialogs.Info(this, Loc.Get("invalid_dt"));
            return;
        }

        _it.Title = string.IsNullOrWhiteSpace(_title.Text) ? Loc.Get("untitled") : _title.Text.Trim();
        _it.Notes = _notes.Text;
        // no alarm = no date and no time (the item is just a note/task list, never shown as "disabled").
        // An existing alarm keeps its on/off switch state; a new or changed alarm starts switched on.
        bool hadAlarm = _it.Id != 0 && _it.AlarmAt.HasValue;
        _it.Enabled = alarmOn ? (hadAlarm ? _it.Enabled : true) : true;
        _it.AlarmAt = alarmOn ? d.Date + t.TimeOfDay : null;
        _it.Repeat = alarmOn ? (RepeatMode)int.Parse((string)_rep.First(c => c.IsChecked == true).Tag) : RepeatMode.None;
        _it.CustomDays = string.Join(",", _dayBoxes.Where(b => b.IsChecked == true).Select(b => (int)b.Tag));
        if (_it.Repeat == RepeatMode.Custom && _it.CustomDays.Length == 0) _it.Repeat = RepeatMode.None;   // no day picked = just once
        if (_it.AlarmAt.HasValue && (_it.Repeat == RepeatMode.Holidays || _it.Repeat == RepeatMode.Custom))
            _it.AlarmAt = Scheduler.FirstMatch(_it, _it.AlarmAt.Value.Date) + _it.AlarmAt.Value.TimeOfDay;   // first ring on a matching day
        _it.Sound = _sound.Text.Trim();
        _it.Tasks = _tasks.ToList();
        Db.SaveItem(_it);
        DialogResult = true;
    }
}
