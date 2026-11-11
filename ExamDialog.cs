using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Hamyar;

/// <summary>Add / edit one exam: title, date+time, difficulty, notes.</summary>
public class ExamDialog : Window
{
    readonly Exam _ex;
    readonly TextBox _title = new(), _date = new(), _time = new(), _notes = new();
    readonly List<RadioButton> _diffChips = new();

    public ExamDialog(Window owner, Exam exam)
    {
        _ex = exam;
        UI.Setup(this, owner, true);
        Title = Loc.Get("exam_dialog");
        Width = 520;
        Height = 560;
        MinHeight = 440;

        var root = new StackPanel { Margin = new Thickness(20) };
        UI.BindFlow(root);

        _title.Text = exam.Title;
        root.Children.Add(UI.Lbl("exam_title"));
        root.Children.Add(_title);

        root.Children.Add(UI.Lbl("exam_when"));
        _date.Text = Jal.Date(exam.When);
        _time.Text = exam.When.ToString("HH:mm", CultureInfo.InvariantCulture);
        _date.Width = 150;
        _time.Width = 90;
        _date.FlowDirection = FlowDirection.LeftToRight;
        _time.FlowDirection = FlowDirection.LeftToRight;
        _date.Margin = new Thickness(0, 3, 8, 3);
        _time.Margin = new Thickness(0, 3, 8, 3);
        var dr = new StackPanel { Orientation = Orientation.Horizontal };
        dr.Children.Add(_date);
        dr.Children.Add(_time);
        root.Children.Add(dr);

        root.Children.Add(UI.Lbl("exam_difficulty"));
        var dw = new WrapPanel();
        foreach (ExamDifficulty d in Enum.GetValues(typeof(ExamDifficulty)))
        {
            var rb = UI.Chip("ediff", ((int)d).ToString(), "exam_diff_" + (int)d, true);
            rb.IsChecked = d == exam.Difficulty;
            _diffChips.Add(rb);
            dw.Children.Add(rb);
        }
        root.Children.Add(dw);

        var diffNote = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0) };
        diffNote.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(diffNote, TextBlock.TextProperty, "exam_difficulty_note");
        root.Children.Add(diffNote);

        root.Children.Add(UI.Lbl("exam_notes"));
        _notes.Text = exam.Notes;
        _notes.AcceptsReturn = true;
        _notes.TextWrapping = TextWrapping.Wrap;
        _notes.Height = 80;
        _notes.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _notes.Margin = new Thickness(0, 3, 0, 0);
        root.Children.Add(_notes);

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
    }

    void Save()
    {
        if (string.IsNullOrWhiteSpace(_title.Text))
        {
            Dialogs.Info(this, Loc.Get("untitled"));
            return;
        }
        if (!Jal.TryParse(_date.Text, out var d))
        {
            Dialogs.Info(this, Loc.Get("invalid_dt"));
            return;
        }
        if (!Cfg.TryParseTimeOfDay(_time.Text, out var t)) t = new TimeSpan(9, 0, 0);

        _ex.Title = _title.Text.Trim();
        _ex.When = d.Date + t;
        _ex.Difficulty = (ExamDifficulty)int.Parse((string)_diffChips.First(c => c.IsChecked == true).Tag);
        _ex.Notes = _notes.Text.Trim();

        DialogResult = true;
    }
}
