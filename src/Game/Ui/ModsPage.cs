using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ReworkedCopium.UI;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;
using ShorelandsMods.Core.Ui;
using ShorelandsMods.Game.Assets;
using ShorelandsMods.Game.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TextFit = ShorelandsMods.Core.Ui.TextFit;

namespace ShorelandsMods.Game.Ui
{
    // The Mods page (ModsPageLayout): the list says at a glance what is on (the switch), what failed (red word) and
    // which mod has unused files; the right side says everything about the selected mod: why it failed, what it is,
    // its switch and load order, its settings (controls made from settings.lua) and what became of its asset files.
    // Changes apply at once. Controls are the game's own where it has them (toggle, slider, dropdown, Back button).
    sealed class ModsPage : MonoBehaviour
    {
        static readonly Color Header = new Color32(0xFF, 0xD9, 0x8C, 0xFF);
        static readonly Color Inset = new Color32(0, 0, 0, 0x33);
        static readonly Color Selected = new Color32(0, 0, 0, 0x59);
        const int KeyW = 72,
            FieldW = 112,
            ControlH = 14,
            Pad = 4,
            CaptureSeconds = 10;

        ModHost host;
        ModsMenu owner;
        MonoBehaviour menu;
        RectTransform backDonor;
        TMP_Text title;
        ModsPageLayout L;
        RectTransform frame,
            content,
            viewport,
            detailTrack,
            detailMark;
        internal VirtualList list;
        TextMeshProUGUI summary,
            folder;
        internal string selected;
        int scroll,
            contentH,
            laidW,
            laidH;
        float drawnAt,
            nextLook;
        internal readonly List<TextMeshProUGUI> detailLabels = new List<TextMeshProUGUI>();

        // What the page drew, to redraw when a mod changes underneath it: the list, the selected mod's setting values
        // (and their definitions: a reload makes new ones) and its asset report.
        readonly ModsSeen seen = new ModsSeen();
        readonly Dictionary<string, object> drawnValues = new Dictionary<string, object>();
        List<SettingDef> drawnDefs;
        string drawnReport,
            drawnCosts;
        int drawnClashes = -1;

        // Preview buttons of the selected mod's sounds and cues (their label says Stop while theirs plays), the last
        // preview started here and the line under its button that says how it goes.
        readonly List<(string name, TextMeshProUGUI label)> playLabels = new List<(string, TextMeshProUGUI)>();
        Previewing preview;
        TextMeshProUGUI previewLine;
        bool previewWasOn;

        sealed class Previewing
        {
            public string Mod,
                Name;
            public float Start;
            public double Sound,
                Each;
            public int Cycles;

            public double Total => Cycles > 0 ? Math.Max(Sound, CueRules.PreviewTotal(Each, Cycles)) : Sound;
            public bool On => Time.unscaledTime - Start < Total;
        }

        // A key setting waiting for a key: which, its button label, until when.
        internal (Mod mod, SettingDef def, TextMeshProUGUI label)? capture;
        float captureUntil;

        sealed class ListRowView
        {
            public Image Selection,
                Check;
            public TextMeshProUGUI Name,
                State;
            public Button Row,
                Toggle;
            public string Id;
        }

        public void Init(ModHost host, ModsMenu owner, MonoBehaviour menu, RectTransform backDonor, TMP_Text title)
        {
            this.host = host;
            this.owner = owner;
            this.menu = menu;
            this.backDonor = backDonor;
            this.title = title;
        }

        Canvas Root => GetComponentInParent<Canvas>(true).rootCanvas;

        int CanvasW => Mathf.RoundToInt(((RectTransform)Root.transform).rect.width);
        int CanvasH => Mathf.RoundToInt(((RectTransform)Root.transform).rect.height);

        public void Open()
        {
            gameObject.SetActive(true);
            title.text = "Mods";
            if (selected == null || host.Find(selected) == null)
                selected = (
                    host.Mods.FirstOrDefault(m => ModsPageModel.State(m) == ModState.Failed)
                    ?? host.Mods.FirstOrDefault()
                )?.Id;
            Build();
            Host?.Area<SoundArea>()?.Event("ui_open");
        }

        ModHost Host => host;

        KeyArea Keys => host.Area<KeyArea>();

        public void Close()
        {
            EndCapture(false);
            host.Area<SoundArea>()?.StopPreview();
            preview = null;
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
                Host?.Area<SoundArea>()?.Event("ui_close");
            }
        }

        void Update()
        {
            if (Root.scaleFactor != drawnAt)
            {
                drawnAt = Root.scaleFactor;
                UiKit.Redraw(transform);
            }
            // A canvas size seen in two frames running is real (not the frame of a resolution change).
            if ((CanvasW != laidW || CanvasH != laidH) && Time.frameCount % 2 == 0)
                Build();
            if (preview != null)
                ShowPreview();
            if (capture.HasValue)
                Capturing();
            else if (Time.unscaledTime >= nextLook)
            {
                nextLook = Time.unscaledTime + 0.25f;
                Look();
            }
        }

        // A mod changed while the page is open (its own ModSettingSet, a failure, a reload, a budget stop). Not while
        // a button is held (a slider drag) or a field has the focus: that would take them away mid-use.
        void Look()
        {
            if (frame == null || InputMute.Active || Mouse.current?.leftButton.isPressed == true)
                return;
            if (seen.Changed(host.Mods) || Keys.ClashVersion != drawnClashes)
            {
                Refresh();
                return;
            }
            var mod = host.Find(selected);
            if (mod != null && DetailStale(mod))
                BuildDetail();
        }

        bool DetailStale(Mod mod)
        {
            var st = mod.Find<SettingsArea.State>();
            if (!ReferenceEquals(st?.Defs, drawnDefs))
                return true;
            if (drawnDefs != null)
                foreach (var def in drawnDefs)
                    if (!drawnValues.TryGetValue(def.Id, out var v) || !Equals(SettingsArea.Value(st, def), v))
                        return true;
            return ReportStamp(mod) != drawnReport || CallCosts.Line(mod) != drawnCosts;
        }

        string ReportStamp(Mod mod) =>
            string.Join("\n", host.AssetReport(mod).Select(l => l.File + " " + l.State + " " + l.Detail));

        // ---- frame ----

        void Build()
        {
            laidW = CanvasW;
            laidH = CanvasH;
            if (frame != null)
                Destroy(frame.gameObject);
            frame = UiKit.Child("Frame", transform);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = frame.offsetMax = Vector2.zero;
            frame.pivot = new Vector2(0f, 1f);
            var backSize = backDonor.sizeDelta;
            int reloadW = UiKit.Measure("Reload mods", 18) + 20;
            string inspect = "Inspect" + (Inspector.KeyName != null ? " (" + Inspector.KeyName + ")" : "");
            int inspectW = UiKit.Measure(inspect, 18) + 20;
            L = ModsPageLayout.Compute(CanvasW, CanvasH, UiKit.LineH, inspectW, reloadW, Mathf.RoundToInt(backSize.x));
            owner.SizeWindow(this, L.W, L.H);

            summary = UiKit.Label("Summary", frame, Color.white, true);
            UiKit.Put(summary.rectTransform, L.Summary);
            folder = UiKit.Label("Folder", frame, UiKit.Hint, true);
            folder.alignment = TextAlignmentOptions.BottomRight;
            UiKit.Put(folder.rectTransform, L.Folder);

            var listBack = UiKit.Child("ListInset", frame);
            UiKit.Put(listBack, L.List.X, L.List.Y, L.ListTrack.Right - L.List.X, L.List.H);
            UiKit.Plate(listBack, UiKit.ChatBg, Inset, true);
            var rows = new URect(L.List.X + 2, L.List.Y + 2, L.List.W - 2, L.ListRows * ModsPageLayout.RowH);
            var track = new URect(L.ListTrack.X, L.List.Y + 2, L.ListTrack.W, rows.H);
            list = VirtualList.Create(frame, rows, track, ModsPageLayout.RowH, MakeListRow, BindListRow);

            var detailBack = UiKit.Child("DetailInset", frame);
            UiKit.Put(detailBack, L.Detail.X, L.Detail.Y, L.DetailTrack.Right - L.Detail.X, L.Detail.H);
            UiKit.Plate(detailBack, UiKit.ChatBg, Inset, true);
            viewport = UiKit.Child("Detail", frame);
            UiKit.Put(viewport, L.Detail);
            viewport.gameObject.AddComponent<RectMask2D>();
            var wheel = viewport.gameObject.AddComponent<Image>();
            wheel.color = Color.clear;
            viewport.gameObject.AddComponent<DetailScroll>().Page = this;
            detailTrack = UiKit.Child("DetailTrack", frame);
            UiKit.Put(detailTrack, L.DetailTrack);
            UiKit.Plate(detailTrack, UiKit.ChatBg, new Color32(0xFF, 0xFF, 0xFF, 0x1F), true);
            detailTrack.gameObject.AddComponent<DetailTrackInput>().Page = this;
            detailMark = UiKit.Child("Mark", detailTrack);
            UiKit.Plate(detailMark, UiKit.ChatBg, new Color32(0xFF, 0xFF, 0xFF, 0x80), false);

            BottomButton("Reload", "Reload mods", L.Reload, ReloadAll);
            // Inspect mode needs the world; it starts once the menu is closed.
            BottomButton("Inspect", inspect, L.Inspect, () =>
            {
                ModsMenu.CloseMenu(menu);
                Inspector.Set(true);
            }).interactable = Inspector.InWorld();

            var back = Instantiate(backDonor.gameObject, frame).GetComponent<RectTransform>();
            back.name = "Back";
            UiKit.Put(back, L.Back);
            var bb = back.GetComponent<Button>();
            bb.onClick = new Button.ButtonClickedEvent();
            bb.onClick.AddListener(() => owner.Back(this));
            back.gameObject.SetActive(true);

            Refresh();
        }

        // The game's plate button with a size 18 label, as its Controls page's Defaults.
        Button BottomButton(string name, string label, URect at, Action click)
        {
            var r = UiKit.Child(name, frame);
            UiKit.Put(r, at);
            var img = UiKit.Plate(r, UiKit.Bg3, Color.white, true);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.colors = UiKit.ButtonColors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var l = UiKit.Label("Label", r, Color.white, false);
            l.fontSize = 18;
            l.alignment = TextAlignmentOptions.Bottom;
            l.text = label;
            UiKit.Put(l.rectTransform, 0, 6, at.W, 20);
            UiKit.AddShadow(l);
            b.onClick.AddListener(() => click());
            return b;
        }

        void Refresh()
        {
            if (frame == null)
                return;
            UiKit.SetText(summary, ModsPageModel.Summary(host.Mods));
            UiKit.SetText(
                folder,
                ClipLeft("ShorelandsMods " + host.Env.HostVersion + ". Mods folder: ", host.Env.ModsDir, L.Folder.W)
            );
            list.SetCount(host.Mods.Count, false);
            int at = host.OrderOf(selected);
            if (at >= 0)
                list.Reveal(at);
            seen.Take(host.Mods);
            drawnClashes = Keys.ClashVersion;
            BuildDetail();
        }

        // The end of a long path is the part that says where; the start gives way.
        static string ClipLeft(string prefix, string path, int width)
        {
            if (UiKit.Measure(prefix + path) <= width)
                return prefix + path;
            for (int i = 1; i < path.Length; i++)
                if (UiKit.Measure(prefix + TextFit.Ellipsis + path.Substring(i)) <= width)
                    return prefix + TextFit.Ellipsis + path.Substring(i);
            return TextFit.Clip(prefix + path, width, UiKit.Width);
        }

        void ReloadAll()
        {
            host.ReloadAll();
            Refresh();
        }

        // ---- list ----

        object MakeListRow(RectTransform slot, int width)
        {
            var v = new ListRowView();
            v.Selection = UiKit.Plate(slot, UiKit.ChatBg, Selected, true);
            v.Row = slot.gameObject.AddComponent<Button>();
            v.Row.transition = Selectable.Transition.None;
            v.Row.navigation = new Navigation { mode = Navigation.Mode.None };
            v.Row.onClick.AddListener(() => Select(v.Id));
            var box = UiKit.Child("Switch", slot);
            UiKit.Put(box, 3, 2, 12, 12);
            var boxImg = UiKit.Plate(box, UiKit.Bg3, Color.white, true);
            v.Toggle = box.gameObject.AddComponent<Button>();
            v.Toggle.targetGraphic = boxImg;
            v.Toggle.colors = UiKit.ButtonColors;
            v.Toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            v.Toggle.onClick.AddListener(() => ToggleMod(v.Id));
            var check = UiKit.Child("Check", box);
            UiKit.Put(check, 3, 3, 6, 6);
            v.Check = UiKit.Plate(check, UiKit.ChatBg, Color.white, false);
            v.Name = UiKit.Label("Name", slot, Color.white, true);
            v.State = UiKit.Label("State", slot, UiKit.Bad, true);
            v.State.alignment = TextAlignmentOptions.BottomRight;
            return v;
        }

        void BindListRow(object o, int index)
        {
            var v = (ListRowView)o;
            var mods = host.Mods;
            bool on = index >= 0 && index < mods.Count;
            v.Row.gameObject.SetActive(on);
            if (!on)
            {
                v.Id = null;
                return;
            }
            var m = mods[index];
            v.Id = m.Id;
            v.Selection.color = m.Id == selected ? Selected : Color.clear;
            var state = ModsPageModel.State(m);
            v.Check.enabled = state != ModState.Off;
            string word =
                m.Status == ModStatus.Failed ? "Failed"
                : m.Status == ModStatus.Skipped ? "Not loaded"
                : state == ModState.On && Keys.HasClash(m) ? "Same key"
                : state == ModState.On && host.AssetReport(m).Any(l => l.State == AssetState.Ignored) ? "Unused files"
                : "";
            v.State.color = m.Status == ModStatus.Loaded ? UiKit.Warn : UiKit.Bad;
            int width = Mathf.RoundToInt(((RectTransform)v.Row.transform).rect.width);
            int ww = word.Length > 0 ? UiKit.Measure(word) : 0;
            UiKit.SetText(v.State, word);
            UiKit.Put(v.State.rectTransform, width - ww - 4, 3, ww + 1, UiKit.LineH);
            int nx = 19;
            int nameW = width - nx - (ww > 0 ? ww + 10 : 4);
            UiKit.SetText(v.Name, TextFit.Clip((index + 1) + ". " + TextEscape.Plain(m.Name), nameW - 1, UiKit.Width));
            v.Name.color =
                m.Id == selected ? Header
                : state == ModState.Off ? UiKit.Hint
                : Color.white;
            UiKit.Put(v.Name.rectTransform, nx, 3, nameW, UiKit.LineH);
        }

        internal void Select(string id)
        {
            if (id == null || host.Find(id) == null)
                return;
            selected = id;
            scroll = 0;
            list.Rebind();
            BuildDetail();
        }

        internal void ToggleMod(string id)
        {
            var m = host.Find(id);
            if (m == null)
                return;
            selected = id;
            host.SwitchOff(id, ModsPageModel.State(m) != ModState.Off);
            Refresh();
        }

        internal void Move(string id, int delta)
        {
            selected = id;
            host.Move(id, delta);
            Refresh();
        }

        // ---- details ----

        void BuildDetail()
        {
            EndCapture(false);
            if (content != null)
                Destroy(content.gameObject);
            detailLabels.Clear();
            drawnValues.Clear();
            playLabels.Clear();
            previewLine = null;
            previewWasOn = false;
            content = UiKit.Child("Content", viewport);
            int w = L.Detail.W - 2 * Pad;
            int y = Pad;
            var mod = host.Find(selected);
            drawnDefs = mod?.Find<SettingsArea.State>()?.Defs;
            drawnReport = mod != null ? ReportStamp(mod) : null;
            if (mod == null)
            {
                y = Paragraph("Put mod folders into the mods folder and press Reload mods.", UiKit.Hint, y, w, 4);
                Finish(y);
                return;
            }
            y = Paragraph(mod.Name, Header, y, w, 2);
            y = Paragraph(ModsPageModel.Byline(mod), UiKit.Hint, y, w, 2);
            y = PathLine("Data: ", mod.DataDir, y, w);
            string export = System.IO.Path.Combine(mod.DataDir, "export");
            if (System.IO.Directory.Exists(export))
                y = PathLine("Files for OBS: ", export, y, w);
            string problem = ModsPageModel.Problem(mod);
            if (problem != null)
                y = Paragraph(problem, UiKit.Bad, y + 2, w, 8);
            else if (mod.Status == ModStatus.Off)
                y = Paragraph("Switched off. Its saved data and settings are kept.", UiKit.Hint, y + 2, w, 2);
            string desc = mod.Manifest?.Description;
            if (!string.IsNullOrWhiteSpace(desc))
                y = Paragraph(desc, Color.white, y + 2, w, 8);
            drawnCosts = CallCosts.Line(mod);
            if (drawnCosts != null)
                y = Paragraph(drawnCosts, UiKit.Hint, y + 2, w, 4);
            y = ModButtons(mod, y + 4, w);
            y = Settings(mod, y + 4, w);
            y = TryLines(mod, y, w);
            y = Files(mod, y + 4, w);
            Finish(y + Pad);
        }

        void Finish(int bottom)
        {
            contentH = bottom;
            UiKit.Put(content, Pad, 0, L.Detail.W - 2 * Pad, contentH);
            ScrollTo(scroll);
        }

        TextMeshProUGUI Text(string s, Color color, int x, int y, int w)
        {
            var t = UiKit.Label("Text", content, color, true);
            t.text = s;
            UiKit.Put(t.rectTransform, x, y, w, UiKit.LineH);
            detailLabels.Add(t);
            return t;
        }

        int Paragraph(string s, Color color, int y, int w, int maxLines)
        {
            foreach (var line in TextFit.Wrap(TextEscape.PlainLines(s), w - 1, maxLines, UiKit.Width))
            {
                Text(line, color, 0, y, w);
                y += UiKit.LineH;
            }
            return y;
        }

        int Indented(string s, Color color, int y, int x, int w, int maxLines)
        {
            foreach (var line in TextFit.Wrap(TextEscape.PlainLines(s), w - 1, maxLines, UiKit.Width))
            {
                Text(line, color, x, y, w);
                y += UiKit.LineH;
            }
            return y;
        }

        int PathLine(string prefix, string path, int y, int w)
        {
            Text(ClipLeft(prefix, path, w - 1), UiKit.Hint, 0, y, w);
            return y + UiKit.LineH;
        }

        int Heading(string s, int y, int w)
        {
            Text(s, Header, 0, y, w);
            return y + UiKit.LineH + 2;
        }

        Button SmallButton(string name, string label, int x, int y, Action click, out int width)
        {
            var b = UiKit.TextButton(name, content, label, out var l);
            width = UiKit.Measure(label) + 12;
            UiKit.PutButton(b, l, new URect(x, y, width, ControlH));
            b.onClick.AddListener(() => click());
            detailLabels.Add(l);
            return b;
        }

        int ModButtons(Mod mod, int y, int w)
        {
            bool off = ModsPageModel.State(mod) == ModState.Off;
            int x = 0;
            SmallButton("Switch", off ? "Switch on" : "Switch off", x, y, () => ToggleMod(mod.Id), out int bw);
            x += bw + 3;
            int at = host.OrderOf(mod.Id);
            var up = SmallButton("LoadEarlier", "Load earlier", x, y, () => Move(mod.Id, -1), out bw);
            up.interactable = at > 0;
            x += bw + 3;
            var down = SmallButton("LoadLater", "Load later", x, y, () => Move(mod.Id, 1), out bw);
            down.interactable = at < host.Mods.Count - 1;
            x += bw + 6;
            string order = "Load order " + (at + 1) + " of " + host.Mods.Count;
            if (x + UiKit.Measure(order) <= w)
                Text(order, UiKit.Hint, x, y + 2, w - x);
            else
            {
                y += ControlH + 2;
                Text(order, UiKit.Hint, 0, y, w);
            }
            return y + ControlH + 2;
        }

        int Settings(Mod mod, int y, int w)
        {
            var st = mod.Find<SettingsArea.State>();
            if (st?.Defs == null || st.Defs.Count == 0)
            {
                if (
                    mod.Status != ModStatus.Loaded
                    && System.IO.File.Exists(System.IO.Path.Combine(mod.Dir, "settings.lua"))
                )
                {
                    y = Heading("Settings", y, w);
                    y = Paragraph("Switch the mod on to change its settings.", UiKit.Hint, y, w, 2);
                }
                return y;
            }
            y = Heading("Settings", y, w);
            foreach (var def in st.Defs)
                y = SettingRow(mod, st, def, y, w);
            return y;
        }

        int SettingRow(Mod mod, SettingsArea.State st, SettingDef def, int y, int w)
        {
            var spec = ControlSpec.For(def);
            int cw =
                spec.Kind == ControlKind.Toggle ? 12
                : spec.Kind == ControlKind.KeyButton ? KeyW
                : FieldW;
            int labelW = w - cw - 8;
            Text(TextFit.Clip(TextEscape.Plain(def.Name), labelW, UiKit.Width), Color.white, 4, y + 3, labelW);
            int cx = w - cw;
            object value = SettingsArea.Value(st, def);
            drawnValues[def.Id] = value;
            switch (spec.Kind)
            {
                case ControlKind.Toggle:
                    MakeToggle(cx, y + 2, (bool)value, on => Set(mod, def, on));
                    break;
                case ControlKind.Stepper:
                    MakeStepper(mod, def, spec, cx, y + 1, cw, (double)value);
                    break;
                case ControlKind.Slider:
                    MakeSlider(mod, def, spec, cx, y + 3, cw, (double)value);
                    break;
                case ControlKind.Dropdown:
                    MakeDropdown(mod, def, cx, y + 1, cw, (string)value);
                    break;
                case ControlKind.TextField:
                    var f = HostField.Create(content, "", def.MaxLength);
                    UiKit.Put((RectTransform)f.transform, cx, y + 1, cw, ControlH);
                    f.SetTextWithoutNotify((string)value);
                    f.onValueChanged.AddListener(v => Set(mod, def, v));
                    break;
                default:
                    var kb = UiKit.TextButton("Key", content, (string)value, out var kl);
                    UiKit.PutButton(kb, kl, new URect(cx, y + 1, cw, ControlH));
                    detailLabels.Add(kl);
                    kb.onClick.AddListener(() => StartCapture(mod, def, kl));
                    break;
            }
            y += ModsPageLayout.RowH;
            if (def.Description.Length > 0)
                y = Indented(def.Description, UiKit.Hint, y - 2, 4, labelW, 2) + 2;
            string clash = spec.Kind == ControlKind.KeyButton ? Keys.ClashOf(mod, def) : null;
            if (clash != null)
                y = Indented(clash, UiKit.Warn, y - 2, 4, w - 4, 3) + 2;
            return y;
        }

        // What /try can call in this mod, as chat commands to copy.
        int TryLines(Mod mod, int y, int w)
        {
            if (mod.Status != ModStatus.Loaded || mod.Lua == null)
                return y;
            var shortcuts = TryCommand.Shortcuts(mod.Lua.HasFunction, host.Area<ToolsArea>().FactsOf(mod));
            if (shortcuts.Count == 0)
                return y;
            y = Heading("Try its functions without waiting for the event", y + 4, w);
            string list = string.Join(
                ", ",
                shortcuts.Select(s => "/try " + mod.Id + " " + TryCommand.Example(s.Words))
            );
            return Paragraph(
                "Type in chat: " + list + ". /try " + mod.Id + " lists every form. Only this mod hears it.",
                UiKit.Hint,
                y,
                w,
                8
            );
        }

        // Applied files take one line; any other state says why on the lines below the file's name. A sound or cue
        // slot has one Play button on its first line, the line under it says how the preview goes, and a cue's image
        // is drawn under its file at the size the game draws it.
        int Files(Mod mod, int y, int w)
        {
            var lines = host.AssetReport(mod);
            if (lines.Count == 0)
                return y;
            y = Heading("Files", y, w);
            y = Paragraph(ModsPageModel.AssetSummary(lines), UiKit.Hint, y, w, 2) + 2;
            var cues = host.Area<CueArea>();
            var playable = new HashSet<string>(host.Area<SoundArea>().PreviewNames(mod).Concat(cues.PreviewNames(mod)));
            int bw = UiKit.Measure("Stop") + 12;
            // With Play buttons every file line is a button's height, so the list keeps one rhythm.
            int rowH = playable.Count > 0 ? ControlH + 2 : UiKit.LineH;
            bool any = playable.Count > 0;
            foreach (var l in lines)
            {
                var (file, what) = ModsPageModel.AssetText(l);
                string name = TextEscape.Plain(file);
                string slot =
                    file.StartsWith("sounds/") || file.StartsWith("cues/") ? ToolsArea.PreviewName(file) : null;
                var cue = file.StartsWith("cues/") && slot != null ? cues.Of(mod, slot) : null;
                int right = w,
                    ty = y + (rowH - UiKit.LineH) / 2;
                bool button = slot != null && playable.Remove(slot);
                if (button)
                {
                    string n = slot;
                    SmallButtonAt("Play:" + n, "Play", w - bw, y, bw, () => PlayPreview(mod, n));
                    playLabels.Add((n, detailLabels[detailLabels.Count - 1]));
                    right = w - bw - 4;
                }
                if (l.State == AssetState.Applied && l.Detail == null)
                {
                    int ww = UiKit.Measure("applied");
                    Text(TextFit.Clip(name, right - ww - 12, UiKit.Width), Color.white, 4, ty, right - ww - 12);
                    Text("applied", UiKit.Good, right - ww - 1, ty, ww + 1);
                    y += rowH;
                }
                else
                {
                    Text(TextFit.Clip(name, right - 4, UiKit.Width), Color.white, 4, ty, right - 4);
                    y += rowH - UiKit.LineH;
                    var color = l.State == AssetState.Ignored ? UiKit.Warn : UiKit.Hint;
                    foreach (var line in TextFit.Wrap(TextEscape.Plain(what), w - 13, 4, UiKit.Width))
                    {
                        y += UiKit.LineH;
                        Text(line, color, 12, y, w - 12);
                    }
                    y += UiKit.LineH;
                }
                if (button && preview != null && preview.Mod == mod.Id && preview.Name == slot)
                    y = PreviewLines(y, w);
                if (cue?.ImageFull != null && file == cue.ImageRel)
                    y = CueFrames(mod, cue, y, w);
                if (!button && cue != null && cue.ImageRel != null && cue.Clip >= 0)
                    y = Indented(
                        "Plays with the Play above: the image and this sound together, as the game plays a cue.",
                        UiKit.Hint,
                        y,
                        12,
                        w - 12,
                        2
                    );
            }
            if (any)
                y = Paragraph(
                    "Play: hear it and see a cue's image now. In chat, /preview "
                        + mod.Id
                        + " <name> does the same and shows a cue at your character.",
                    UiKit.Hint,
                    y + 2,
                    w,
                    3
                );
            return y;
        }

        // ---- previews ----

        void PlayPreview(Mod mod, string name)
        {
            var sound = host.Area<SoundArea>();
            if (preview != null && preview.Mod == mod.Id && preview.Name == name && preview.On)
            {
                sound.StopPreview();
                preview = null;
                BuildDetail();
                return;
            }
            string why = host.Area<ToolsArea>().Preview(mod, name, false);
            if (why != null)
            {
                host.Env.Chat(ModHost.Color.Note, "[" + mod.Id + "] " + why + ".");
                return;
            }
            var cue = host.Area<CueArea>().Of(mod, name);
            preview = new Previewing
            {
                Mod = mod.Id,
                Name = name,
                Start = Time.unscaledTime,
                Sound = cue == null || cue.Clip >= 0 ? sound.PreviewSeconds : 0,
                Each = cue?.ImageFull != null ? CueArea.PreviewSeconds(cue) : 0,
                Cycles = cue?.ImageFull != null ? CueArea.PreviewCycles(cue) : 0,
            };
            BuildDetail();
        }

        // The line under the previewed slot's button, and a warning when the game's volume is 0.
        int PreviewLines(int y, int w)
        {
            previewLine = Text(PreviewText(), UiKit.Gold, 12, y, w - 12);
            y += UiKit.LineH;
            if (preview.Sound > 0 && host.Area<SoundArea>().Muted)
                y = Indented(SoundArea.MutedSentence, UiKit.Warn, y, 12, w - 12, 2);
            return y;
        }

        string PreviewText()
        {
            var p = preview;
            float t = Time.unscaledTime - p.Start;
            string sound = p.Sound > 0 ? Secs(p.Sound) + " sound" : null;
            if (p.Cycles > 0)
            {
                string shows = p.Cycles + " times " + Secs(p.Each) + (sound != null ? ", with its " + sound : "");
                if (!p.On)
                    return "Played: the image " + shows + ".";
                int k = CueRules.PreviewAt(t, p.Each, p.Cycles, out _, out _);
                return "Playing: the image " + (k < 0 ? p.Cycles : k + 1) + " of " + shows;
            }
            return p.On
                ? "Playing: "
                    + Math.Min(t, p.Sound).ToString("0.0", CultureInfo.InvariantCulture)
                    + " of "
                    + Secs(p.Sound)
                : "Played: " + Secs(p.Sound) + " long.";
        }

        static string Secs(double s) => s.ToString(s < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " s";

        // Every frame while a preview is shown: its line and the Play/Stop label follow it.
        void ShowPreview()
        {
            if (previewLine != null)
            {
                string text = PreviewText();
                if (previewLine.text != text)
                    UiKit.SetText(previewLine, text);
            }
            bool on = preview.On;
            if (on == previewWasOn)
                return;
            previewWasOn = on;
            foreach (var (name, label) in playLabels)
                if (label != null)
                    UiKit.SetText(label, on && preview.Mod == selected && preview.Name == name ? "Stop" : "Play");
        }

        // A cue's frames under its image file, at the pixel size the game draws them at: animated at their fps, and
        // while a preview of it runs, shown and hidden as the game would show the cue.
        int CueFrames(Mod mod, CueDef def, int y, int w)
        {
            float perArt = PerArtPixel() * def.Options.Scale;
            int fw = Mathf.Max(1, Mathf.RoundToInt(def.FrameW * perArt)),
                fh = Mathf.Max(1, Mathf.RoundToInt(def.ImageH * perArt));
            var box = UiKit.Child("CuePreview", content);
            UiKit.Put(box, 12, y + 2, fw, fh);
            var img = box.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            var anim = box.gameObject.AddComponent<CueFramesView>();
            if (!anim.Load(def))
            {
                Destroy(box.gameObject);
                return y;
            }
            if (preview != null && preview.Mod == mod.Id && preview.Name == def.Slot && preview.On)
                anim.Play(preview.Start, preview.Each, preview.Cycles);
            Text(
                "as the game draws it, " + def.FrameW + "x" + def.ImageH + " at scale " + def.Options.Scale,
                UiKit.Hint,
                12 + fw + 6,
                y + 2 + Mathf.Max(0, (fh - UiKit.LineH) / 2),
                w - fw - 18
            );
            return y + Mathf.Max(fh, UiKit.LineH) + 4;
        }

        // Canvas units per art pixel in the world (32 art pixels a unit): from the world's camera, or one unit (what
        // the world draws at every supported resolution) where there is none, at the main menu.
        internal float PerArtPixel()
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic || !Inspector.InWorld())
                return 1f;
            return Screen.height / (2f * cam.orthographicSize * 32f) / Root.scaleFactor;
        }

        void Set(Mod mod, SettingDef def, object value)
        {
            if (!host.Area<SettingsArea>().SetFromPage(mod, def, value))
                return;
            drawnValues[def.Id] = value;
            if (def.Type == SettingType.Key)
                Keys.CheckClashes();
            if (host.Find(mod.Id)?.Status != ModStatus.Loaded)
                Refresh();
        }

        void MakeToggle(int x, int y, bool on, Action<bool> changed)
        {
            var donor = menu.GetComponentsInChildren<Toggle>(true).FirstOrDefault(t => t.name.EndsWith("Toggle"));
            if (donor == null)
                return;
            var t = Instantiate(donor.gameObject, content).GetComponent<Toggle>();
            t.name = "Toggle";
            t.onValueChanged = new Toggle.ToggleEvent();
            t.SetIsOnWithoutNotify(on);
            UiKit.Put((RectTransform)t.transform, x, y, 12, 12);
            t.onValueChanged.AddListener(v => changed(v));
            t.gameObject.SetActive(true);
        }

        void MakeStepper(Mod mod, SettingDef def, ControlSpec spec, int x, int y, int w, double value)
        {
            int bw = 14;
            var shown = Text(spec.Show(value), Color.white, x + bw, y + 2, w - 2 * bw);
            shown.alignment = TextAlignmentOptions.Bottom;
            Button minus = null,
                plus = null;
            void Show(double v)
            {
                UiKit.SetText(shown, spec.Show(v));
                minus.interactable = v > def.Min;
                plus.interactable = v < def.Max;
            }
            void Step(int dir)
            {
                double now = (double)SettingsArea.Value(mod.Find<SettingsArea.State>(), def);
                double next = spec.Snap(def, now + dir * spec.Step);
                Set(mod, def, next);
                Show(next);
            }
            minus = SmallButtonAt("Minus:" + def.Id, "-", x, y, bw, () => Step(-1));
            plus = SmallButtonAt("Plus:" + def.Id, "+", x + w - bw, y, bw, () => Step(1));
            Show(value);
        }

        Button SmallButtonAt(string name, string label, int x, int y, int w, Action click)
        {
            var b = UiKit.TextButton(name, content, label, out var l);
            UiKit.PutButton(b, l, new URect(x, y, w, ControlH));
            b.onClick.AddListener(() => click());
            detailLabels.Add(l);
            return b;
        }

        void MakeSlider(Mod mod, SettingDef def, ControlSpec spec, int x, int y, int w, double value)
        {
            var donor = menu.GetComponentsInChildren<Slider>(true).FirstOrDefault();
            if (donor == null)
                return;
            int valueW = Math.Max(UiKit.Measure(spec.Show(def.Min)), UiKit.Measure(spec.Show(def.Max))) + 2;
            var s = Instantiate(donor.gameObject, content).GetComponent<Slider>();
            s.name = "Slider:" + def.Id;
            s.onValueChanged = new Slider.SliderEvent();
            s.wholeNumbers = false;
            s.minValue = (float)def.Min;
            s.maxValue = (float)def.Max;
            s.SetValueWithoutNotify((float)value);
            UiKit.Put((RectTransform)s.transform, x, y, w - valueW - 4, 10);
            // The game's slider takes clicks on its knob only; this makes its whole row a track to click.
            var track = s.gameObject.AddComponent<Image>();
            track.color = Color.clear;
            s.gameObject.SetActive(true);
            OnGrid(s);
            var shown = Text(spec.Show(value), Color.white, x + w - valueW, y, valueW);
            shown.alignment = TextAlignmentOptions.BottomRight;
            // The number follows the knob; the setting changes once, when the pointer lets go (a drag across the range
            // would otherwise save the file and call OnSettingChanged at every step it passes).
            var hold = s.gameObject.AddComponent<SliderHold>();
            hold.Released = () => Set(mod, def, spec.Snap(def, s.value));
            s.onValueChanged.AddListener(v =>
            {
                double snapped = spec.Snap(def, v);
                if (Math.Abs(snapped - v) > 1e-6)
                    s.SetValueWithoutNotify((float)snapped);
                OnGrid(s);
                UiKit.SetText(shown, spec.Show(snapped));
                if (!hold.Held)
                    Set(mod, def, snapped);
            });
        }

        // The slider places its knob and the end of its fill at fractions of a unit; nudged onto whole units.
        static void OnGrid(Slider s)
        {
            float v = s.normalizedValue;
            if (s.handleRect != null)
            {
                float w = ((RectTransform)s.handleRect.parent).rect.width;
                s.handleRect.anchoredPosition = new Vector2(
                    Mathf.Round(w * v) - w * v,
                    s.handleRect.anchoredPosition.y
                );
            }
            if (s.fillRect != null)
            {
                float w = ((RectTransform)s.fillRect.parent).rect.width;
                s.fillRect.offsetMax = new Vector2(Mathf.Round(w * v) - w * v, s.fillRect.offsetMax.y);
            }
        }

        void MakeDropdown(Mod mod, SettingDef def, int x, int y, int w, string value)
        {
            var donor = menu.GetComponentsInChildren<TMP_Dropdown>(true).FirstOrDefault();
            if (donor == null)
                return;
            var d = Instantiate(donor.gameObject, content).GetComponent<TMP_Dropdown>();
            d.name = "Dropdown";
            d.onValueChanged = new TMP_Dropdown.DropdownEvent();
            d.ClearOptions();
            var labels = def.Choices.Select(c => TextFit.Clip(TextEscape.Plain(c.Value), w - 20, UiKit.Width)).ToList();
            d.AddOptions(labels);
            d.SetValueWithoutNotify(Math.Max(0, def.Choices.FindIndex(c => c.Key == value)));
            d.RefreshShownValue();
            foreach (var t in d.GetComponentsInChildren<TMP_Text>(true))
            {
                t.richText = false;
                t.parseCtrlCharacters = false;
            }
            UiKit.Put((RectTransform)d.transform, x, y, w, ControlH);
            d.onValueChanged.AddListener(i => Set(mod, def, def.Choices[i].Key));
            d.gameObject.SetActive(true);
        }

        // ---- key capture ----

        internal void StartCapture(Mod mod, SettingDef def, TextMeshProUGUI label)
        {
            EndCapture(false);
            capture = (mod, def, label);
            captureUntil = Time.unscaledTime + CaptureSeconds;
            label.text = "Press a key";
            label.color = Header;
            KeyInput.Capture(true);
        }

        void Capturing()
        {
            var k = Keyboard.current;
            if (k == null || Time.unscaledTime > captureUntil || k.escapeKey.wasPressedThisFrame)
            {
                EndCapture(false);
                return;
            }
            foreach (var key in k.allKeys)
                if (key.wasPressedThisFrame && key.keyCode != Key.Escape && key.keyCode != Key.None)
                {
                    var (mod, def, _) = capture.Value;
                    Set(mod, def, key.keyCode.ToString());
                    EndCapture(true);
                    return;
                }
        }

        void EndCapture(bool changed)
        {
            if (!capture.HasValue)
                return;
            var (mod, def, label) = capture.Value;
            capture = null;
            KeyInput.Capture(false);
            if (label != null)
            {
                label.color = Color.white;
                label.text =
                    (mod.Find<SettingsArea.State>() is SettingsArea.State ks ? SettingsArea.Value(ks, def) : null)
                        as string
                    ?? "";
            }
        }

        // ---- scrolling the details ----

        internal void ScrollBy(int units) => ScrollTo(scroll + units);

        void ScrollTo(int y)
        {
            int max = Math.Max(0, contentH - L.Detail.H);
            scroll = Math.Max(0, Math.Min(max, y));
            if (content != null)
                content.anchoredPosition = new Vector2(Pad, scroll);
            bool can = max > 0;
            detailMark.gameObject.SetActive(can);
            detailTrack.GetComponent<Image>().enabled = can;
            if (!can)
                return;
            int trackH = L.DetailTrack.H;
            int mh = Math.Max(4, trackH * L.Detail.H / contentH);
            int my = (int)Math.Round((double)(trackH - mh) * scroll / max);
            UiKit.Put(detailMark, 0, my, L.DetailTrack.W, mh);
        }

        internal void TrackAt(PointerEventData e)
        {
            if (
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    detailTrack,
                    e.position,
                    e.pressEventCamera,
                    out var local
                )
            )
                return;
            int y = Mathf.RoundToInt(detailTrack.rect.yMax - local.y);
            int max = Math.Max(0, contentH - L.Detail.H);
            ScrollTo((int)Math.Round((double)y / Math.Max(1, L.DetailTrack.H) * max));
        }
    }

    sealed class SliderHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        internal bool Held;
        internal Action Released;

        public void OnPointerDown(PointerEventData e) => Held = e.button == PointerEventData.InputButton.Left;

        public void OnPointerUp(PointerEventData e)
        {
            if (!Held)
                return;
            Held = false;
            Released();
        }
    }

    sealed class DetailScroll : MonoBehaviour, IScrollHandler
    {
        internal ModsPage Page;

        public void OnScroll(PointerEventData e)
        {
            if (e.scrollDelta.y != 0)
                Page.ScrollBy(e.scrollDelta.y > 0 ? -3 * UiKit.LineH : 3 * UiKit.LineH);
        }
    }

    sealed class DetailTrackInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IBeginDragHandler
    {
        internal ModsPage Page;

        public void OnPointerDown(PointerEventData e) => Page.TrackAt(e);

        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e) => Page.TrackAt(e);
    }

    // A cue strip's frames at its fps, loaded from the mod's PNG for the Mods page: looping, or during a preview
    // shown and hidden as the game shows the cue (CueRules.PreviewAt).
    sealed class CueFramesView : MonoBehaviour
    {
        Texture2D strip;
        Sprite[] frames;
        double fps,
            each;
        int cycles;
        float start;
        bool playing;
        Image image;

        public void Play(float from, double seconds, int times)
        {
            start = from;
            each = seconds;
            cycles = times;
            playing = true;
        }

        public bool Load(CueDef def)
        {
            var px = CueStage.StripPixels(def);
            if (px == null)
                return false;
            strip = new Texture2D(def.ImageW, def.ImageH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
            };
            strip.SetPixels32(px);
            strip.Apply(false, false);
            int n = def.Options.Frames;
            frames = new Sprite[n];
            for (int i = 0; i < n; i++)
                frames[i] = Sprite.Create(strip, new Rect(i * def.FrameW, 0, def.FrameW, def.ImageH), Vector2.zero, 32);
            fps = def.Options.Fps;
            start = Time.unscaledTime;
            image = GetComponent<Image>();
            image.sprite = frames[0];
            return true;
        }

        void Update()
        {
            if (frames == null || frames.Length == 0)
                return;
            double t = Time.unscaledTime - start;
            bool drawn = true;
            if (playing && CueRules.PreviewAt(t, each, cycles, out drawn, out t) < 0)
            {
                playing = false;
                drawn = true;
            }
            image.enabled = drawn;
            image.sprite = frames[(int)(Math.Max(0, t) * fps) % frames.Length];
        }

        void OnDestroy()
        {
            if (frames != null)
                foreach (var f in frames)
                    Destroy(f);
            Destroy(strip);
        }
    }
}
