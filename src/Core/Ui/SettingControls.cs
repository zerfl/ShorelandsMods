using System;
using System.Globalization;

namespace ShorelandsMods.Core.Ui
{
    internal enum ControlKind
    {
        Toggle,
        Slider,
        Stepper,
        Dropdown,
        TextField,
        KeyButton,
    }

    // The Mods page control for a setting from settings.lua, and how its numbers move and read.
    internal sealed class ControlSpec
    {
        public ControlKind Kind;

        // Numbers: the step a slider snaps to or a stepper moves by, and the decimals shown.
        public double Step;
        public int Decimals;

        public static ControlSpec For(SettingDef d)
        {
            switch (d.Type)
            {
                case SettingType.Boolean:
                    return new ControlSpec { Kind = ControlKind.Toggle };
                case SettingType.Choice:
                    return new ControlSpec { Kind = ControlKind.Dropdown };
                case SettingType.Text:
                    return new ControlSpec { Kind = ControlKind.TextField };
                case SettingType.Key:
                    return new ControlSpec { Kind = ControlKind.KeyButton };
            }
            double dflt = d.Default is double v ? v : d.Min;
            bool whole = IsWhole(d.Min) && IsWhole(d.Max) && IsWhole(dflt);
            // A few whole numbers are counted out with a stepper; anything else slides.
            if (whole && d.Max - d.Min <= 10)
                return new ControlSpec
                {
                    Kind = ControlKind.Stepper,
                    Step = 1,
                    Decimals = 0,
                };
            double step = whole ? 1 : NiceStep((d.Max - d.Min) / 100);
            if (!whole && !IsMultiple(dflt - d.Min, step))
                step = Math.Min(step, NiceStep(Math.Abs(dflt - d.Min) > 0 ? Math.Abs(dflt - d.Min) / 10 : step));
            return new ControlSpec
            {
                Kind = ControlKind.Slider,
                Step = step,
                Decimals = whole ? 0 : DecimalsOf(step),
            };
        }

        // Snapped to a whole number of steps from the minimum, inside the range.
        public double Snap(SettingDef d, double value)
        {
            if (double.IsNaN(value))
                return d.Min;
            double v = Step > 0 ? d.Min + Math.Round((value - d.Min) / Step) * Step : value;
            v = Math.Round(v, Decimals);
            return Math.Max(d.Min, Math.Min(d.Max, v));
        }

        public string Show(double value) => value.ToString("F" + Decimals, CultureInfo.InvariantCulture);

        static bool IsWhole(double v) => Math.Abs(v - Math.Round(v)) < 1e-9;

        static bool IsMultiple(double v, double step) => Math.Abs(v / step - Math.Round(v / step)) < 1e-6;

        // 1, 2 or 5 times a power of ten, at most `max`.
        static double NiceStep(double max)
        {
            if (max <= 0)
                return 0.01;
            double p = Math.Pow(10, Math.Floor(Math.Log10(max)));
            foreach (var m in new[] { 5.0, 2.0, 1.0 })
                if (m * p <= max + 1e-12)
                    return m * p;
            return p;
        }

        static int DecimalsOf(double step)
        {
            int d = 0;
            while (d < 6 && Math.Abs(step * Math.Pow(10, d) - Math.Round(step * Math.Pow(10, d))) > 1e-9)
                d++;
            return d;
        }
    }
}
