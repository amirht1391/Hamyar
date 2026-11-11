using System.Text;

namespace Hamyar;

/// <summary>
/// Imports flashcards from a CSV file into one existing course. Format (documented in the Help tab too):
///
///   front,back,subject
///   "What is the capital of France?","Paris","Europe"
///   "2 + 2 = ?",4,"Math"
///   "Plain card with no subject column",answer
///
/// Rules:
///  - Columns per row: front (the question), back (the answer), and an OPTIONAL third column: the subject
///    (lesson / chapter) of the course. A subject that does not exist yet in the course is created automatically
///    (matched by name, ignoring upper/lower case). When the third column is empty or missing, the card goes to the
///    subject currently selected on the Flashcards page (or to no subject when "All ..." is selected).
///  - A comma or a line break inside a field must be wrapped in double quotes; a literal double quote
///    inside a quoted field is written as two double quotes ("").
///  - The first row may be a header ("front,back" or similar words) - it is detected and skipped
///    automatically; if the first row looks like real card data instead, it is imported as a card.
///  - Blank rows are skipped. A row with an empty front AND an empty back is skipped.
///  - Every imported card is added to the destination course, starting today.
/// </summary>
public static class CsvImport
{
    /// <summary>Parses one CSV file's rows (RFC 4180-style quoting). Each returned row is the raw field list.</summary>
    public static List<string[]> ParseRows(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        int i = 0;
        int n = text.Length;

        void EndField() { fields.Add(field.ToString()); field.Clear(); }
        void EndRow()
        {
            EndField();
            // skip fully-blank rows (all fields empty, including a trailing blank line at EOF)
            if (!(fields.Count == 1 && fields[0].Length == 0)) rows.Add(fields.ToArray());
            fields = new List<string>();
        }

        while (i < n)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < n && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field.Append(ch); i++; continue;
            }
            switch (ch)
            {
                case '"': inQuotes = true; i++; break;
                case ',': EndField(); i++; break;
                case '\r':
                    i++;
                    if (i < n && text[i] == '\n') i++;
                    EndRow();
                    break;
                case '\n': i++; EndRow(); break;
                default: field.Append(ch); i++; break;
            }
        }
        // last row (file may or may not end with a newline)
        if (field.Length > 0 || fields.Count > 0) EndRow();
        return rows;
    }

    static bool LooksLikeHeader(string[] row)
    {
        if (row.Length < 2) return false;
        string a = row[0].Trim().ToLowerInvariant(), b = row[1].Trim().ToLowerInvariant();
        string[] frontWords = { "front", "question", "q", "term", "سوال", "جلو", "روبرو" };
        string[] backWords = { "back", "answer", "a", "definition", "جواب", "پاسخ", "پشت" };   // a 3rd header cell (subject / مبحث ...) is ignored
        return frontWords.Contains(a) && backWords.Contains(b);
    }

    /// <summary>Reads the file, adds every valid row as a card in <paramref name="courseId"/> under that
    /// course's current method, and returns (cards added, rows skipped, subjects created, error message or null).
    /// <paramref name="defaultSubjectId"/> is used for rows that have no subject column value (0 = no subject).</summary>
    public static (int added, int skipped, int newSubjects, string error) ImportCards(string filePath, long courseId, long defaultSubjectId = 0)
    {
        string text;
        try { text = File.ReadAllText(filePath, Encoding.UTF8); }
        catch (Exception ex) { return (0, 0, 0, string.Format(Loc.Get("card_import_read_error"), ex.Message)); }

        var rows = ParseRows(text);
        if (rows.Count == 0) return (0, 0, 0, Loc.Get("card_import_empty"));

        var course = Db.GetCourses(1).FirstOrDefault(c => c.Id == courseId);
        if (course == null) return (0, 0, 0, Loc.Get("card_import_no_course"));
        var courseExam = course.ExamId.HasValue ? Db.GetExams().FirstOrDefault(e => e.Id == course.ExamId.Value) : null;

        int start = 0;
        if (LooksLikeHeader(rows[0])) start = 1;

        // the course's existing subjects by name, so a name in the file reuses one instead of making a duplicate
        var subjects = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var subjectStarts = new Dictionary<long, DateTime>();
        foreach (var sj in Db.GetSubjects(courseId)) { subjects[sj.Name.Trim()] = sj.Id; subjectStarts[sj.Id] = sj.StartDate.Date; }
        if (defaultSubjectId != 0 && !subjects.Values.Contains(defaultSubjectId)) defaultSubjectId = 0;

        int added = 0, skipped = 0, newSubjects = 0;
        for (int i = start; i < rows.Count; i++)
        {
            var row = rows[i];
            string front = row.Length > 0 ? row[0].Trim() : "";
            string back = row.Length > 1 ? row[1].Trim() : "";
            if (front.Length == 0 && back.Length == 0) { skipped++; continue; }

            string subjectName = row.Length > 2 ? row[2].Trim() : "";
            long subjectId = defaultSubjectId;
            if (subjectName.Length > 0)
            {
                if (!subjects.TryGetValue(subjectName, out subjectId))
                {
                    subjectId = Db.AddSubject(courseId, subjectName);
                    subjectStarts[subjectId] = DateTime.Today;
                    subjects[subjectName] = subjectId;
                    newSubjects++;
                }
            }

            var card = new Card
            {
                CourseId = courseId,
                SubjectId = subjectId,
                Front = front,
                Back = back,
                StartDate = subjectStarts.TryGetValue(subjectId, out var sd0) ? sd0 : DateTime.Today,
                Method = course.Method,
                CustomIntervalsCsv = course.CustomIntervalsCsv
            };
            ReviewEngine.Restart(card);
            ReviewEngine.Reschedule(card, courseExam, DateTime.Now);
            Db.SaveCard(card);
            added++;
        }

        return (added, skipped, newSubjects, null);
    }
}
