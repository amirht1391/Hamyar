using System.Windows;
using System.Windows.Media;

namespace Hamyar;

/// <summary>2 themes (dark/light) x 4 accents. Builds the gradient + brushes and puts them into Application.Resources.</summary>
public static class ThemeManager
{
    public static readonly Color[] Accents = { Hex("#3B82F6"), Hex("#EF4444"), Hex("#22C55E"), Hex("#EAB308") };

    static Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);
    static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    static SolidColorBrush Br(Color c) => new SolidColorBrush(c);

    static LinearGradientBrush Grad(Color c1, Color c2, byte a1, byte a2)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        g.GradientStops.Add(new GradientStop(A(c1, a1), 0));
        g.GradientStops.Add(new GradientStop(A(c2, a2), 1));
        return g;
    }

    public static void Apply(int theme, int accent, int backdrop)
    {
        bool dark = theme == 0;
        var acc = Accents[accent];
        var res = Application.Current.Resources;

        var baseC = dark ? Hex("#0E1118") : Hex("#F4F6FB");
        var endC = Mix(baseC, acc, dark ? 0.05 : 0.12);

        // Very glassy: low alpha when Windows can draw a backdrop behind us, opaque otherwise
        bool glass = Glass.Supported && backdrop != 1 && !Cfg.SafeMode;
        // (previous values 0x55/0x80 and 0x78/0xA0 minus ~5%)
        byte a1 = glass ? (byte)(dark ? 0x48 : 0x6B) : (byte)0xFF;
        byte a2 = glass ? (byte)(dark ? 0x58 : 0x93) : (byte)0xFF;

        res["BgBrush"] = Grad(baseC, endC, a1, a2);
        res["MenuBrush"] = Br(dark ? Hex("#171B25") : Hex("#FFFFFF"));
        res["CardBrush"] = Br(dark ? Color.FromArgb(0x14, 255, 255, 255) : Color.FromArgb(0xB4, 255, 255, 255));
        res["InputBrush"] = Br(dark ? Color.FromArgb(0x16, 255, 255, 255) : Color.FromArgb(0xC8, 255, 255, 255));
        res["LineBrush"] = Br(dark ? Color.FromArgb(0x5A, 255, 255, 255) : Color.FromArgb(0x48, 0, 0, 0));
        res["FgBrush"] = Br(dark ? Hex("#F2F4F8") : Hex("#111827"));
        res["SubFgBrush"] = Br(dark ? Hex("#B4BCCB") : Hex("#4B5563"));
        res["ThumbBrush"] = Br(dark ? Color.FromArgb(0x55, 255, 255, 255) : Color.FromArgb(0x55, 0, 0, 0));
        res["OnFgBrush"] = Br(dark ? Hex("#0E1118") : Hex("#FFFFFF"));
        res["AccentBrush"] = Br(acc);
        res["OnAccentBrush"] = Br(accent == 3 ? Colors.Black : Colors.White);
    }
}
