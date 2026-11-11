using System.Globalization;
using System.Text;

namespace Hamyar;

/// <summary>One study session: how long, on what, the notes / checklist / board drawn during it.</summary>
public class StudySession
{
    public long Id { get; set; }
    public long? TopicId { get; set; }
    public long? CourseId { get; set; }
    public string Title { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public int Seconds { get; set; }              // time really spent focusing (breaks and pauses are not counted)
    public int PlannedSeconds { get; set; }       // 0 = open-ended
    public int Mode { get; set; }                 // 0 countdown, 1 pomodoro, 2 open
    public bool Locked { get; set; }
    public int Rating { get; set; }               // 0 = not rated, 1..5
    public string Notes { get; set; } = "";
    public string Checklist { get; set; } = "";   // one line per task: "0|text" (open) or "1|text" (done)
    public int Cards { get; set; }                // flashcards created during the session
    public byte[] Ink { get; set; }               // the drawing board (ISF), only loaded on request
    public bool HasInk { get; set; }

    public string DateText => Jal.Stamp(StartedAt);
    public string DurationText => FocusUtil.Dur(Seconds);

    public (int done, int total) TaskCounts()
    {
        int d = 0, t = 0;
        foreach (var line in (Checklist ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            t++;
            if (line.StartsWith("1|")) d++;
        }
        return (d, t);
    }
}

public static class FocusUtil
{
    /// <summary>"1 h 05 min" / "25 min" (localized units).</summary>
    public static string Dur(int seconds)
    {
        int m = (int)Math.Round(seconds / 60.0);
        int h = m / 60;
        m %= 60;
        return h > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0} {1} {2:00} {3}", h, Loc.Get("fs_h"), m, Loc.Get("fs_m"))
            : string.Format(CultureInfo.InvariantCulture, "{0} {1}", m, Loc.Get("fs_m"));
    }

    /// <summary>Clock text: 24:59 or 1:05:00.</summary>
    public static string Clock(double seconds)
    {
        if (seconds < 0) seconds = 0;
        int s = (int)Math.Floor(seconds);
        int h = s / 3600, m = s % 3600 / 60, sec = s % 60;
        return h > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", h, m, sec)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", m, sec);
    }

    public static int Num(string text, int def, int min, int max)
    {
        if (!int.TryParse(Util.Digits(text ?? "").Trim(), out var v)) return def;
        return Math.Clamp(v, min, max);
    }
}

/// <summary>A small scientific calculator: + - * / ^ % ( ), sqrt abs sin cos tan asin acos atan ln log exp floor ceil round, pi, e.</summary>
public static class MiniCalc
{
    public static bool TryEval(string text, bool degrees, out double value)
    {
        value = 0;
        var s = Util.Digits(text ?? "")
            .Replace('×', '*').Replace('÷', '/').Replace('−', '-').Replace('٫', '.').Replace(',', '.')
            .Replace(" ", "");
        if (s.Length == 0) return false;
        try
        {
            var p = new Parser(s, degrees);
            value = p.ParseAll();
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
        catch { return false; }
    }

    public static string Format(double v)
    {
        if (Math.Abs(v) >= 1e15 || (Math.Abs(v) < 1e-9 && v != 0)) return v.ToString("0.#####E+0", CultureInfo.InvariantCulture);
        return Math.Round(v, 10).ToString("0.##########", CultureInfo.InvariantCulture);
    }

    sealed class Parser
    {
        readonly string _s;
        readonly bool _deg;
        int _i;

        public Parser(string s, bool deg) { _s = s; _deg = deg; }

        public double ParseAll()
        {
            var v = Expr();
            if (_i != _s.Length) throw new FormatException();
            return v;
        }

        bool Peek(char c) => _i < _s.Length && _s[_i] == c;

        double Expr()
        {
            var v = Term();
            while (true)
            {
                if (Peek('+')) { _i++; v += Term(); }
                else if (Peek('-')) { _i++; v -= Term(); }
                else return v;
            }
        }

        double Term()
        {
            var v = Power();
            while (true)
            {
                if (Peek('*')) { _i++; v *= Power(); }
                else if (Peek('/')) { _i++; v /= Power(); }
                else if (_i < _s.Length && (_s[_i] == '(' || char.IsLetter(_s[_i]))) v *= Power();   // 2(3+4), 2pi
                else return v;
            }
        }

        double Power()
        {
            var v = Unary();
            if (Peek('^')) { _i++; return Math.Pow(v, Power()); }      // right-associative
            return v;
        }

        double Unary()
        {
            if (Peek('-')) { _i++; return -Unary(); }
            if (Peek('+')) { _i++; return Unary(); }
            var v = Primary();
            while (Peek('%')) { _i++; v /= 100.0; }
            return v;
        }

        double Primary()
        {
            if (Peek('('))
            {
                _i++;
                var v = Expr();
                if (!Peek(')')) throw new FormatException();
                _i++;
                return v;
            }
            if (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
            {
                int st = _i;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                return double.Parse(_s.Substring(st, _i - st), CultureInfo.InvariantCulture);
            }
            if (_i < _s.Length && char.IsLetter(_s[_i]))
            {
                int st = _i;
                while (_i < _s.Length && char.IsLetter(_s[_i])) _i++;
                var name = _s.Substring(st, _i - st).ToLowerInvariant();
                if (name == "pi") return Math.PI;
                if (name == "e") return Math.E;
                if (!Peek('(')) throw new FormatException();
                _i++;
                var a = Expr();
                if (!Peek(')')) throw new FormatException();
                _i++;
                double rad = _deg ? a * Math.PI / 180.0 : a;
                double ToUnit(double r) => _deg ? r * 180.0 / Math.PI : r;
                return name switch
                {
                    "sqrt" => Math.Sqrt(a),
                    "abs" => Math.Abs(a),
                    "sin" => Math.Sin(rad),
                    "cos" => Math.Cos(rad),
                    "tan" => Math.Tan(rad),
                    "asin" => ToUnit(Math.Asin(a)),
                    "acos" => ToUnit(Math.Acos(a)),
                    "atan" => ToUnit(Math.Atan(a)),
                    "ln" => Math.Log(a),
                    "log" => Math.Log10(a),
                    "exp" => Math.Exp(a),
                    "floor" => Math.Floor(a),
                    "ceil" => Math.Ceiling(a),
                    "round" => Math.Round(a, MidpointRounding.AwayFromZero),
                    _ => throw new FormatException()
                };
            }
            throw new FormatException();
        }
    }
}
