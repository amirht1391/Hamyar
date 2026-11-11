using System.Windows;
using System.Windows.Controls;

namespace Hamyar;

/// <summary>
/// Sets the exam a flashcard course is being prepared for (optional). Opened from the "course settings" button next to the
/// course chips on the Flashcards page.
/// </summary>
public class CourseSettingsDialog : Window
{
    readonly Course _course;
    readonly List<RadioButton> _examChips = new();

    public CourseSettingsDialog(Window owner, Course course, List<Exam> exams)
    {
        _course = course;
        UI.Setup(this, owner, true);
        Title = course.Name;
        Width = 560;
        Height = 600;
        MinHeight = 460;

        var root = new StackPanel { Margin = new Thickness(20) };
        UI.BindFlow(root);

        var head = new TextBlock { Text = course.Name, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };
        root.Children.Add(head);
        var sub = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 14) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(sub, TextBlock.TextProperty, "course_settings_note");
        root.Children.Add(sub);

        root.Children.Add(UI.Lbl("exam_label"));
        var ew = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var none = UI.Chip("coexam", "0", "exam_none", true);
        none.IsChecked = !course.ExamId.HasValue;
        _examChips.Add(none);
        ew.Children.Add(none);
        foreach (var ex in exams)
        {
            var rb = new RadioButton
            {
                GroupName = "coexam",
                Tag = ex.Id.ToString(),
                Content = ex.Title + "  (" + ex.DateText + ")",
                Style = UI.S("Chip"),
                IsChecked = course.ExamId == ex.Id,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(2)
            };
            _examChips.Add(rb);
            ew.Children.Add(rb);
        }
        root.Children.Add(ew);
        var examNote = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0) };
        examNote.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(examNote, TextBlock.TextProperty, "exam_link_note");
        root.Children.Add(examNote);

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
        var examTag = (string)_examChips.First(c => c.IsChecked == true).Tag;
        long? examId = examTag == "0" ? null : long.Parse(examTag);

        bool changed = _course.ExamId != examId;
        Db.SetCourseExam(_course.Id, examId);
        // another exam (or none): the cards of this course are re-planned against it right away
        if (changed) Recalc.ForCourse(_course.Id, (Owner as MainWindow)?.ExamById(examId));

        DialogResult = true;
    }
}
