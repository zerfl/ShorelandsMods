using System;
using System.Collections.Generic;
using System.Linq;
using ShorelandsMods.Core.Ui;

// HUD layout, text fitting and the HUD API.
static partial class Program
{
    static readonly Measure Mono5 = s => 5 * (s ?? "").Length;

    // The game's HUD at 640x360 as measured in 0.69i: menu and remote buttons, zone header, offering timer, chat,
    // hotbar, wallet, backpack.
    static List<URect> GameHud(int w, int h) =>
        new List<URect>
        {
            new URect(w - 24, 4, 20, 18),
            new URect(w - 47, 4, 20, 18),
            new URect(w / 2 - 16, 3, 32, 15),
            new URect(w / 2 - 302, h / 2 - 179, 135, 12),
            new URect(4, h - 95, 200, 91),
            new URect(w / 2 - 112, h - 32, 224, 32),
            new URect(w - 93, h - 20, 35, 16),
            new URect(w - 55, h - 44, 51, 40),
        };

    static void HudPlacement()
    {
        foreach (var (cw, ch) in new[] { (640, 360), (800, 450) })
        {
            var hud = GameHud(cw, ch);
            var slots = new List<HudSlot>();
            foreach (Anchor a in Enum.GetValues(typeof(Anchor)))
                for (int i = 0; i < 3; i++)
                    slots.Add(
                        new HudSlot
                        {
                            Anchor = a,
                            W = 40 + 30 * i,
                            H = 10 + 10 * i,
                        }
                    );
            HudLayout.Place(slots, cw, ch, hud);
            bool inside = slots.All(s => s.Placed.Inside(cw, ch));
            bool apart = true,
                clearOfGame = true;
            for (int i = 0; i < slots.Count; i++)
            {
                for (int j = i + 1; j < slots.Count; j++)
                    apart &= !slots[i].Placed.Overlaps(slots[j].Placed);
                clearOfGame &= hud.All(r => !r.Overlaps(slots[i].Placed));
            }
            Check(inside, "27 stacked widgets on the canvas " + cw + "x" + ch);
            Check(apart, "stacked widgets never overlap each other " + cw + "x" + ch);
            Check(
                clearOfGame,
                "stacked widgets keep clear of the game's HUD " + cw + "x" + ch,
                string.Join(
                    " ",
                    slots.Where(s => hud.Any(r => r.Overlaps(s.Placed))).Select(s => s.Anchor + "@" + s.Placed)
                )
            );
            var tl = slots.Where(s => s.Anchor == Anchor.TopLeft).ToList();
            Check(
                tl[0].Placed.X == HudLayout.Margin
                    && tl[1].Placed.Y >= tl[0].Placed.Bottom + HudLayout.Gap
                    && tl[2].Placed.Y >= tl[1].Placed.Bottom + HudLayout.Gap,
                "top_left stacks downwards from the corner",
                string.Join(" ", tl.Select(s => s.Placed))
            );
            var br = slots.Where(s => s.Anchor == Anchor.BottomRight).ToList();
            Check(
                br[0].Placed.Bottom <= ch - 44 - HudLayout.Gap && br[1].Placed.Y < br[0].Placed.Y,
                "bottom_right stacks upwards above the backpack",
                string.Join(" ", br.Select(s => s.Placed))
            );
        }

        foreach (Anchor a in Enum.GetValues(typeof(Anchor)))
        {
            var r = HudLayout.At(a, 50, 12, 7, 9, 800, 450);
            var (dx, dy) = HudLayout.Inward(a, r, 800, 450);
            Check(dx == 7 && dy == 9, "inward distances round trip for " + Anchors.Name(a), dx + "," + dy);
        }
        var pinned = new HudSlot
        {
            Anchor = Anchor.TopRight,
            W = 30,
            H = 10,
            X = 10,
            Y = 40,
        };
        var free = new HudSlot
        {
            Anchor = Anchor.TopRight,
            W = 30,
            H = 10,
        };
        var lone = new List<HudSlot> { free, pinned };
        HudLayout.Place(lone, 640, 360, new List<URect> { new URect(600, 0, 40, 40) });
        Check(
            pinned.Placed.Equals(new URect(600, 40, 30, 10)),
            "a widget with x, y sits exactly there (distance from the right and top edges)",
            pinned.Placed.ToString()
        );
        Check(
            !free.Placed.Overlaps(pinned.Placed) && free.Placed.Y >= 40,
            "a stacked widget flows around a placed one",
            free.Placed.ToString()
        );
        var far = new List<HudSlot>
        {
            new HudSlot
            {
                Anchor = Anchor.TopLeft,
                W = 30,
                H = 10,
                X = 5000,
                Y = -50,
            },
        };
        HudLayout.Place(far, 640, 360, new List<URect>());
        Check(far[0].Placed.Inside(640, 360), "a widget placed off the canvas is kept on it", far[0].Placed.ToString());

        // The left column at 640x360 as three mods fill it, then a 76-high plate: no gap is tall
        // enough between the corner stack, the middle stack and the chat.
        var full = new (Anchor a, int w, int h)[]
        {
            (Anchor.TopLeft, 52, 11),
            (Anchor.Left, 20, 11),
            (Anchor.BottomLeft, 48, 11),
            (Anchor.TopLeft, 24, 24),
            (Anchor.TopLeft, 60, 4),
            (Anchor.TopLeft, 35, 11),
            (Anchor.Left, 20, 11),
            (Anchor.BottomLeft, 48, 11),
            (Anchor.Left, 54, 22),
            (Anchor.TopLeft, 57, 11),
            (Anchor.TopLeft, 81, 76),
        }
            .Select(t => new HudSlot
            {
                Anchor = t.a,
                W = t.w,
                H = t.h,
            })
            .ToList();
        var game640 = GameHud(640, 360);
        HudLayout.Place(full, 640, 360, game640);
        var plate = full.Last().Placed;
        Check(
            plate.Inside(640, 360)
                && full.Take(full.Count - 1).All(s => !s.Placed.Overlaps(plate))
                && game640.All(g => !g.Overlaps(plate)),
            "a widget with no room left in its column goes beside it, overlapping nothing",
            plate.ToString()
        );

        foreach (var name in new[] { "top_left", "Top-Left", "TOPLEFT", "top left" })
            Check(Anchors.TryParse(name, out var p) && p == Anchor.TopLeft, "anchor name " + name);
        Check(Anchors.TryParse("centre", out var c1) && c1 == Anchor.Center, "anchor centre");
        Check(Anchors.TryParse("top_center", out var c2) && c2 == Anchor.Top, "anchor top_center");
        Check(Anchors.TryParse("middle_right", out var c3) && c3 == Anchor.Right, "anchor middle_right");
        Check(!Anchors.TryParse("upper", out _), "unknown anchor refused");
    }

    static void TextFitting()
    {
        var lines = TextFit.Wrap("the quick brown fox jumps over the lazy dog", 50, 10, Mono5);
        Check(lines.All(l => Mono5(l) <= 50), "wrapped lines fit their width", string.Join("|", lines));
        Check(string.Join(" ", lines) == "the quick brown fox jumps over the lazy dog", "wrapping keeps every word");
        var hard = TextFit.Wrap("line one\nline two", 500, 10, Mono5);
        Check(hard.Count == 2 && hard[1] == "line two", "\\n starts a new line");
        var longWord = TextFit.Wrap("abcdefghijklmnopqrstuvwxyz", 50, 10, Mono5);
        Check(
            longWord.All(l => Mono5(l) <= 50) && string.Concat(longWord) == "abcdefghijklmnopqrstuvwxyz",
            "a word wider than the line breaks between characters",
            string.Join("|", longWord)
        );
        var cut = TextFit.Wrap("one two three four five six seven eight nine ten", 40, 2, Mono5);
        Check(
            cut.Count == 2 && cut[1].EndsWith(TextFit.Ellipsis) && cut.All(l => Mono5(l) <= 40),
            "text past the last line ends in an ellipsis inside the width",
            string.Join("|", cut)
        );
        var clip = TextFit.Clip("a long mod name that does not fit", 60, Mono5);
        Check(clip.EndsWith(TextFit.Ellipsis) && Mono5(clip) <= 60, "one line clipped with an ellipsis", clip);
        Check(TextFit.Clip("short", 60, Mono5) == "short", "a line that fits stays as it is");
    }

    static void HudApi()
    {
        var spy = new SpyArea();
        using var rig2 = new Rig(spy);
        rig2.Mod(
            "a",
            @"
HudText('t', 'hello', { anchor = 'Top-Right', size = 2 })
HudText('t', 'hello again')
HudIcon('i', 101, { anchor = 'bottom_left' })
HudBar('b', 1.5, { width = 50 })
HudText('multi', 'one\ntwo<b>')
HudShow('i', false)
Out(HudText('x', 'y', { anchr = 'top', size = 2 }))
Out(HudIcon('t', 101))
Out(HudText('x', 'y', { anchor = 'upper', color = 'blue' }))
Out(HudIcon('e', '../evil.png'))
Out(HudBar('b', 0.5, { width = 500 }))
Out(pcall(HudText, 'x', 'y', { size = 'big' }))
"
        );
        rig2.Start();
        rig2.Run(0.1);
        var w = rig2.Hud.Widgets;
        Check(w.Count == 5, "one widget per id, updated in place", w.Count.ToString());
        var t = w.First(x => x.Id == "t");
        Check(
            t.Lines.Single() == "hello again" && t.Anchor == Anchor.TopRight && t.Scale == 2,
            "update keeps earlier options",
            t.Lines[0]
        );
        var bar = w.First(x => x.Id == "b");
        Check(bar.Fraction == 0.5 && bar.BarW == 200, "bar width clamped to 200", bar.BarW.ToString());
        Check(
            w.First(x => x.Id == "multi").Lines.SequenceEqual(new[] { "one", "two<b>" }),
            "lines kept as typed (labels draw without rich text), breaks kept"
        );
        Check(
            !rig2.Ui.Shown["a/i"].Visible && w.First(x => x.Id == "i").Rect.Equals(default(URect)),
            "hidden widget is not laid out"
        );
        string outs = string.Join("|", spy.Out);
        Check(outs == "false|false|false|false|false|" + spy.Out[5], "refused and clamped options give false", outs);
        var x = w.First(v => v.Id == "x");
        Check(
            x.Scale == 2 && x.Anchor == Anchor.TopLeft && x.Color.R == 1,
            "an unknown option, anchor or colour is left out; the others apply",
            x.Anchor + " " + x.Scale
        );
        Check(w.First(v => v.Id == "t").Kind == HudKind.Text, "an id keeps its kind");
        Check(!w.Any(v => v.Id == "e"), "icon file confined to the mod");
        string chat = rig2.Env.ChatRead;
        Check(
            chat.Contains("the option \"anchr\" is not known (did you mean \"anchor\"?)")
                && chat.Contains("\"t\" is already a HudText")
                && chat.Contains("the anchor \"upper\" is not known")
                && chat.Contains("the option color must be a colour")
                && chat.Contains("inside the mod folder")
                && chat.Contains("the option width must be from 8 to 200; 500 was changed to 200"),
            "each refusal is told once",
            chat
        );

        var tw = w.First(x => x.Id == "t");
        rig2.Hud.Dragged(tw, new URect(100, 200, tw.W, tw.H));
        rig2.Run(0.1);
        Check(tw.Rect.X == 100 && tw.Rect.Y == 200, "a dragged widget stays where it was dropped", tw.Rect.ToString());
        rig2.Host.Reload("a");
        rig2.Run(0.1);
        var again = rig2.Hud.Widgets.First(x => x.Id == "t");
        Check(
            again != tw && again.Rect.X == 100 && again.Rect.Y == 200,
            "the dropped position survives a reload",
            again.Rect.ToString()
        );
        Check(rig2.Ui.Shown.Count == 5, "reload removes the old widgets exactly", string.Join(",", rig2.Ui.Shown.Keys));
        rig2.Hud.ResetPosition(again);
        rig2.Run(0.1);
        Check(again.Rect.Y == HudLayout.Margin, "right click puts it back where the mod put it", again.Rect.ToString());
        rig2.Host.SwitchOff("a", true);
        Check(rig2.Hud.Widgets.Count == 0 && rig2.Ui.Shown.Count == 0, "switching a mod off removes its widgets");
    }

    static void HudStacking()
    {
        using var rig = new Rig();
        rig.Mod("a", "HudText('x', 'from a', { anchor = 'top_left' })");
        rig.Mod("b", "HudText('x', 'from b', { anchor = 'top_left' }) HudText('y', 'b two', { anchor = 'top_left' })");
        rig.Start();
        rig.Run(0.1);
        var w = rig.Hud.Widgets.OrderBy(x => x.Rect.Y).Select(x => x.ModId + x.Id).ToList();
        Check(string.Join(",", w) == "ax,bx,by", "widgets of several mods stack in load order", string.Join(",", w));
        rig.Host.Move("b", -1);
        rig.Run(0.1);
        w = rig.Hud.Widgets.OrderBy(x => x.Rect.Y).Select(x => x.ModId + x.Id).ToList();
        Check(string.Join(",", w) == "bx,by,ax", "moving a mod up moves its widgets up the stack", string.Join(",", w));
        Check(string.Join(",", rig.Env.Order) == "b,a", "the new order is saved", string.Join(",", rig.Env.Order));
    }
}
