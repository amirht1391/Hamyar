using System.Text;

namespace Hamyar;

public static class Util
{
    /// <summary>Persian/Arabic digits to ASCII, so "۱۴:۳۰" can be parsed.</summary>
    public static string Digits(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s ?? "")
        {
            if (ch >= '۰' && ch <= '۹') sb.Append((char)('0' + (ch - '۰')));
            else if (ch >= '٠' && ch <= '٩') sb.Append((char)('0' + (ch - '٠')));
            else sb.Append(ch);
        }
        return sb.ToString();
    }
}
