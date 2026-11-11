using System.Windows;
using System.Windows.Controls;

namespace Hamyar;

/// <summary>Name + difficulty of a flashcard subject (lesson / chapter).</summary>
public class SubjectDialog : Window
{
    readonly TextBox _name = new();
    readonly List<RadioButton> _diffChips = new();
    public string SubjectName => _name.Text.Trim();
    readonly TextBox _date = new();
    DateTime _start;
    public DateTime StartDate => _start;
    public int Difficulty => int.Parse((string)_diffChips.First(c => c.IsChecked == true).Tag);

    public SubjectDialog(Window owner, string name, ExamDifficulty difficulty, DateTime startDate)
    {
        _start = startDate.Date;
        UI.Setup(this, owner, true);
        Title = Loc.Get("subj_dialog");
        Width = 460;
        Height = 470;

        var root = new StackPanel { Margin = new Thickness(20) };
        UI.BindFlow(root);
        root.Children.Add(UI.Lbl("subj_name"));
        _name.Text = name;
        root.Children.Add(_name);

        root.Children.Add(UI.Lbl("topic_difficulty"));
        var dw = new WrapPanel();
        foreach (ExamDifficulty d in Enum.GetValues(typeof(ExamDifficulty)))
        {
            var rb = UI.Chip("sdiff", ((int)d).ToString(), "exam_diff_" + (int)d, true);
            rb.IsChecked = d == difficulty;
            _diffChips.Add(rb);
            dw.Children.Add(rb);
        }
        root.Children.Add(dw);

        // start date of the whole subject (cards no longer have their own date)
        root.Children.Add(UI.Lbl("subj_start"));
        _date.Text = Jal.Date(startDate);
        _date.Width = 150;
        _date.FlowDirection = System.Windows.FlowDirection.LeftToRight;
        _date.HorizontalAlignment = HorizontalAlignment.Left;
        _date.Margin = new Thickness(0, 3, 8, 3);
        var dr = new StackPanel { Orientation = Orientation.Horizontal };
        dr.Children.Add(_date);
        dr.Children.Add(UI.Btn("today", (s, e) => _date.Text = Jal.Date(DateTime.Today), true));
        dr.Children.Add(UI.Btn("yesterday", (s, e) => _date.Text = Jal.Date(DateTime.Today.AddDays(-1)), true));
        root.Children.Add(dr);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(hint, TextBlock.TextProperty, "subj_start_note");
        root.Children.Add(hint);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        footer.Children.Add(UI.Btn("save", (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text)) return;
            if (!Jal.TryParse(_date.Text, out var d)) { Dialogs.Info(this, Loc.Get("invalid_dt")); return; }
            _start = d.Date;
            DialogResult = true;
        }));
        footer.Children.Add(UI.Btn("cancel", (s, e) => DialogResult = false, true));
        root.Children.Add(footer);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
    }
}
