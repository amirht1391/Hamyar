using System.Globalization;
using System.Text;

namespace Hamyar;

/// <summary>
/// Exports every flashcard of ONE course (all its subjects, whatever filter is active on the page) to a CSV file.
/// The first three columns are exactly what the CSV import reads: front, back, subject - so an exported file can be
/// imported again into any course. With "progress" four more columns follow (start date, next review, reviews, lapses);
/// the import ignores them. UTF-8 with a BOM, so Excel shows Persian text correctly.
/// </summary>
public static class CsvExport
{
    static string Q(string s)
    {
        s ??= "";
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static (int count, string error) Export(string path, long courseId, bool withProgress)
    {
        try
        {
            var cards = Db.GetCards(courseId, 0)
                          .OrderBy(c => c.SubjectName ?? "", StringComparer.CurrentCultureIgnoreCase)
                          .ThenBy(c => c.Id)
                          .ToList();
            if (cards.Count == 0) return (0, Loc.Get("card_export_empty"));

            var sb = new StringBuilder();
            sb.Append("front,back,subject");
            if (withProgress) sb.Append(",start_date,next_review,reviews,lapses");
            sb.Append("\r\n");
            foreach (var c in cards)
            {
                sb.Append(Q(c.Front)).Append(',').Append(Q(c.Back)).Append(',').Append(Q(c.SubjectName));
                if (withProgress)
                    sb.Append(',').Append(c.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                      .Append(',').Append(c.NextReview.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                      .Append(',').Append(c.Reviews.ToString(CultureInfo.InvariantCulture))
                      .Append(',').Append(c.Lapses.ToString(CultureInfo.InvariantCulture));
                sb.Append("\r\n");
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            return (cards.Count, null);
        }
        catch (Exception ex)
        {
            return (0, string.Format(Loc.Get("card_export_error"), ex.Message));
        }
    }
}
