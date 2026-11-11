using System.IO.Compression;
using System.Security;
using System.Text;

namespace Hamyar;

/// <summary>Exports the planner table to a Word (.docx) file, laid out exactly like the user's reference file: A5 page, centred
/// title + grey subtitle, one "Table Grid" table (#, title, start, minutes, notes), 8 pt Arial, everything centred.
/// Up to 35 rows always fit on ONE page (the row height shrinks); more rows continue on the next page with the header repeated.
/// Written by hand (a .docx is a zip of XML files), so no extra package is needed.</summary>
public static class PlanDocx
{
    const int PageW = 8391, PageH = 11906;
    const int MarginTB = 567;
    static readonly int[] Widths = { 402, 1463, 1196, 652, 3747 };   // #, title, start, minutes, notes   (= 7460 twips)
    const string Font = "Arial";       // on every Windows / Mac, so the file looks the same for everyone
    const string Ink = "000000", Sub = "6B6B6B";
    const int OnePageMaxRows = 35;

    static string E(string s) => SecurityElement.Escape(s ?? "");

    /// <param name="halfPt">font size in half points; 0 = the document default (12 pt)</param>
    static string Run(string text, int halfPt, bool bold, string color)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder("<w:r><w:rPr><w:rFonts w:ascii=\"" + Font + "\" w:hAnsi=\"" + Font + "\" w:cs=\"" + Font + "\"/>");
        if (bold) sb.Append("<w:b/><w:bCs/>");
        sb.Append("<w:color w:val=\"").Append(color).Append("\"/>");
        if (halfPt > 0) sb.Append("<w:sz w:val=\"").Append(halfPt).Append("\"/><w:szCs w:val=\"").Append(halfPt).Append("\"/>");
        sb.Append("<w:rtl/></w:rPr>");
        var parts = text.Replace("\r", "").Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append("<w:br/>");
            sb.Append("<w:t xml:space=\"preserve\">").Append(E(parts[i])).Append("</w:t>");
        }
        return sb.Append("</w:r>").ToString();
    }

    static string Para(string runs, bool rtl, int after, string jc = "center") =>
        "<w:p><w:pPr>" + (rtl ? "<w:bidi/>" : "") + "<w:spacing w:before=\"0\" w:after=\"" + after + "\"/><w:jc w:val=\"" + jc + "\"/></w:pPr>" + runs + "</w:p>";

    static string Cell(int w, string text, string color, bool bold, bool rtl)
    {
        // no paragraph spacing here: the "Table Grid" style sets 0 after / single line, like in the reference file
        return "<w:tc><w:tcPr><w:tcW w:w=\"" + w + "\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr><w:p><w:pPr>" + (rtl ? "<w:bidi/>" : "") +
               "<w:jc w:val=\"center\"/></w:pPr>" + Run(text, 16, bold, color) + "</w:p></w:tc>";
    }

    static int Lines(string s, int perLine) =>
        string.IsNullOrEmpty(s) ? 1 : s.Replace("\r", "").Split('\n').Sum(p => Math.Max(1, (p.Length + perLine - 1) / perLine));

    public static (int count, string error) Export(string path, List<PlanRow> rows, bool rtl, string name, string dateText, Func<PlanRow, string> dayText)
    {
        try
        {
            if (rows.Count == 0) return (0, Loc.Get("pl_export_empty"));
            int tableW = Widths.Sum();
            int marginLR = Math.Max(300, (PageW - tableW) / 2);       // the table is centred on the page
            string[] heads = { "#", Loc.Get("pl_title"), Loc.Get("pl_start"), Loc.Get("pl_duration_short"), Loc.Get("pl_notes") };

            // row height: 310 twips, shrunk so that up to 35 rows (+ header) fit one page; extra wrapped lines are counted in
            int titleCh = 16, noteCh = 46;
            int extraLines = rows.Sum(r => Math.Max(Lines(r.Title, titleCh), Lines(r.Notes, noteCh)) - 1);
            int avail = PageH - 2 * MarginTB - 860;                    // minus title + subtitle block
            int rowCount = rows.Count + 1;
            int height = 310;
            if (rows.Count <= OnePageMaxRows)
            {
                int perRow = (avail - extraLines * 250) / rowCount;
                height = Math.Max(236, Math.Min(310, perRow));
            }

            var b = new StringBuilder();
            b.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");

            // title (12 pt bold) and the grey subtitle
            b.Append(Para(Run(name, 0, true, Ink), rtl, 20));
            int totalMin = rows.Where(r => r.Minutes > 0).Sum(r => r.Minutes);
            string sub = (string.IsNullOrEmpty(dateText) ? "" : dateText + "  ·  ") + string.Format(Loc.Get("pl_export_summary"), rows.Count, totalMin / 60, totalMin % 60);
            b.Append(Para(Run(sub, 16, false, Sub), rtl, 200));

            b.Append("<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/>" + (rtl ? "<w:bidiVisual/>" : "") + "<w:tblW w:w=\"" + tableW + "\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>" +
                     "<w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"1\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/></w:tblPr>");
            b.Append("<w:tblGrid>"); foreach (var x in Widths) b.Append("<w:gridCol w:w=\"" + x + "\"/>"); b.Append("</w:tblGrid>");

            string trPr = "<w:trPr><w:cantSplit/><w:trHeight w:val=\"" + height + "\"/>";
            b.Append("<w:tr>" + trPr + "<w:tblHeader/></w:trPr>");
            for (int i = 0; i < 5; i++) b.Append(Cell(Widths[i], heads[i], Ink, true, rtl));
            b.Append("</w:tr>");

            int n = 0;
            foreach (var r in rows)
            {
                n++;
                bool ok = Cfg.TryParseTimeOfDay(r.Start, out _);
                b.Append("<w:tr>" + trPr + "</w:trPr>");
                b.Append(Cell(Widths[0], n.ToString(), Sub, false, rtl));
                b.Append(Cell(Widths[1], r.Title, Ink, false, rtl));
                b.Append(Cell(Widths[2], ok ? r.Start : "", Ink, false, rtl));
                b.Append(Cell(Widths[3], r.Minutes > 0 ? r.Minutes.ToString() : "-", Ink, false, rtl));
                b.Append(Cell(Widths[4], r.Notes, Sub, false, rtl));
                b.Append("</w:tr>");
            }
            b.Append("</w:tbl>");
            // Word needs a paragraph after a table; this one is tiny so it can never start a new page
            b.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"20\" w:lineRule=\"exact\"/></w:pPr></w:p>");
            b.Append("<w:sectPr><w:pgSz w:w=\"" + PageW + "\" w:h=\"" + PageH + "\"/><w:pgMar w:top=\"" + MarginTB + "\" w:right=\"" + marginLR + "\" w:bottom=\"" + MarginTB + "\" w:left=\"" + marginLR +
                     "\" w:header=\"300\" w:footer=\"300\" w:gutter=\"0\"/><w:cols w:space=\"720\"/>" + (rtl ? "<w:bidi/>" : "") + "</w:sectPr>");
            b.Append("</w:body></w:document>");

            const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";
            string styles = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:styles " + W + "><w:docDefaults><w:rPrDefault><w:rPr>" +
                "<w:rFonts w:ascii=\"" + Font + "\" w:hAnsi=\"" + Font + "\" w:cs=\"" + Font + "\"/><w:sz w:val=\"24\"/><w:szCs w:val=\"24\"/><w:lang w:val=\"en-US\" w:eastAsia=\"en-US\" w:bidi=\"ar-SA\"/></w:rPr></w:rPrDefault>" +
                "<w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"278\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
                "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>" +
                "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/><w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"108\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>" +
                "<w:style w:type=\"table\" w:styleId=\"TableGrid\"><w:name w:val=\"Table Grid\"/><w:basedOn w:val=\"TableNormal\"/><w:uiPriority w:val=\"39\"/><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr>" +
                "<w:tblPr><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:left w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:right w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/></w:tblBorders></w:tblPr></w:style></w:styles>";
            const string ct = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/></Types>";
            const string rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>";
            const string docRels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";

            if (File.Exists(path)) File.Delete(path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                void Add(string entry, string content)
                {
                    var en = zip.CreateEntry(entry);
                    using var sw = new StreamWriter(en.Open(), new UTF8Encoding(false));
                    sw.Write(content);
                }
                Add("[Content_Types].xml", ct);
                Add("_rels/.rels", rels);
                Add("word/document.xml", b.ToString());
                Add("word/styles.xml", styles);
                Add("word/_rels/document.xml.rels", docRels);
            }
            return (rows.Count, null);
        }
        catch (Exception ex)
        {
            return (0, string.Format(Loc.Get("card_export_error"), ex.Message));
        }
    }
}
