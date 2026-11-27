using System;
using System.Collections.Generic;

namespace ShorelandsMods.Core.Ui
{
    // A rectangle in whole canvas units, origin at the canvas's top-left corner, y down.
    internal readonly struct URect : IEquatable<URect>
    {
        public readonly int X,
            Y,
            W,
            H;

        public URect(int x, int y, int w, int h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        public int Right => X + W;
        public int Bottom => Y + H;

        public bool Overlaps(URect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

        public bool Inside(int canvasW, int canvasH) => X >= 0 && Y >= 0 && Right <= canvasW && Bottom <= canvasH;

        public URect At(int x, int y) => new URect(x, y, W, H);

        public bool Equals(URect o) => X == o.X && Y == o.Y && W == o.W && H == o.H;

        public override bool Equals(object o) => o is URect r && Equals(r);

        public override int GetHashCode() => ((X * 397 ^ Y) * 397 ^ W) * 397 ^ H;

        public override string ToString() => X + "," + Y + " " + W + "x" + H;
    }

    internal enum Anchor
    {
        TopLeft,
        Top,
        TopRight,
        Left,
        Center,
        Right,
        BottomLeft,
        Bottom,
        BottomRight,
    }

    internal static class Anchors
    {
        static readonly string[] Names =
        {
            "top_left",
            "top",
            "top_right",
            "left",
            "center",
            "right",
            "bottom_left",
            "bottom",
            "bottom_right",
        };

        public static string Name(Anchor a) => Names[(int)a];

        public static string List => string.Join(", ", Names);

        // Forgiving: case, "-", " " and "_" do not matter; "centre", "middle" and "top_center" are accepted.
        public static bool TryParse(string s, out Anchor anchor)
        {
            anchor = Anchor.TopLeft;
            if (s == null)
                return false;
            string k = s.ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
            k = k.Replace("centre", "center").Replace("middle", "center");
            if (k.Length > 6 && k.EndsWith("center") && k != "center")
                k = k.Substring(0, k.Length - 6);
            if (k.StartsWith("center") && k.Length > 6)
                k = k.Substring(6);
            for (int i = 0; i < Names.Length; i++)
                if (Names[i].Replace("_", "") == k)
                {
                    anchor = (Anchor)i;
                    return true;
                }
            return false;
        }

        // -1 left/top, 0 centre, 1 right/bottom.
        public static int Column(Anchor a) => (int)a % 3 - 1;

        public static int Row(Anchor a) => (int)a / 3 - 1;
    }

    // Widths of text in canvas units, from the font in use (TMP in the game, a fake offline).
    internal delegate int Measure(string text);
}
