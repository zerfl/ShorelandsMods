namespace ShorelandsMods.Core
{
    internal static class CueRules
    {
        // A cue stays a restyle: its drawn size after scaling, in art pixels (32 per world unit, like the game's own).
        public const int MaxArtPixels = 64;

        // What the game draws at each moment (FishingLineFX.OnFishingEvent and UpdateMark), for _names.txt.
        public static string GameDraws(string slot)
        {
            switch (slot)
            {
                case "fishing_nibble":
                    return "a fish nibbles: a ring on the water at the bait (0.5 s)";
                case "fishing_bite":
                    return "a fish bites: the \"!\" over your character (5x12 pixels with its black outline, the pixel "
                        + "size of a scale=\"1\" cue) for as long as the fish is on, and a splash and ring at the bait";
                case "fishing_tug":
                    return "the fish tugs: a splash and a ring at the bait (0.7 s)";
                case "fishing_spook":
                    return "you reeled in too early: a small splash at the bait (0.6 s), the fish flees";
                default:
                    return "the fish got away with the bait: a splash and a ring at the bait (0.6 s)";
            }
        }

        // Only the bite has a mark the game draws; the others are rings and splashes.
        public static bool HasNativeMark(string slot) => slot == "fishing_bite";

        // How long a cue shows without seconds=: the bite as long as the game's mark, the others as long as the
        // game's ring for that event (FishingLineFX.OnFishingEvent; spook makes no ring, so its splash time).
        public static double DefaultSeconds(string slot)
        {
            switch (slot)
            {
                case "fishing_nibble":
                    return 0.5;
                case "fishing_tug":
                    return 0.7;
                default:
                    return 0.6;
            }
        }

        // An author's preview repeats the cue for about PreviewLoop seconds (at least twice, once for a cue that long),
        // each time for the cue's own time with a gap, so it is seen and its real length shows. The game's bite lasts as
        // long as the fish is on; a preview gives it a short bite's time.
        public const double PreviewBite = 1.0,
            PreviewGap = 0.4,
            PreviewLoop = 4.0;

        public static double PreviewSeconds(string slot, double? seconds) =>
            seconds ?? (HasNativeMark(slot) ? PreviewBite : DefaultSeconds(slot));

        public static int PreviewCycles(double seconds) =>
            seconds >= PreviewLoop
                ? 1
                : System.Math.Max(2, (int)System.Math.Round(PreviewLoop / (seconds + PreviewGap)));

        public static double PreviewTotal(double seconds, int cycles) => cycles * (seconds + PreviewGap) - PreviewGap;

        // At t seconds into a preview: which showing it is in (-1 once it is over) and whether the image is drawn (not
        // in a gap); at is the time into that showing, for the frame.
        public static int PreviewAt(double t, double seconds, int cycles, out bool drawn, out double at)
        {
            int k = (int)System.Math.Floor(t / (seconds + PreviewGap));
            at = t - k * (seconds + PreviewGap);
            drawn = k < cycles && at < seconds;
            return k < cycles ? k : -1;
        }

        // Why a strip of this size cannot be used with these options, or null. frameWidth is set when it fits.
        public static string Problem(int width, int height, int frames, int scale, out int frameWidth)
        {
            frameWidth = 0;
            if (width % frames != 0)
                return "it is " + width + " pixels wide, which does not split into " + frames + " equal frames";
            frameWidth = width / frames;
            int w = frameWidth * scale,
                h = height * scale;
            if (w > MaxArtPixels || h > MaxArtPixels)
                return "one frame drawn at scale "
                    + scale
                    + " is "
                    + w
                    + "x"
                    + h
                    + " art pixels; a cue may be at most "
                    + MaxArtPixels
                    + "x"
                    + MaxArtPixels;
            return null;
        }
    }
}
