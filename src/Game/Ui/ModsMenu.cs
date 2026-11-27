using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ReworkedCopium.UI;
using ShorelandsMods.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShorelandsMods.Game.Ui
{
    // The one change to the game's screens: a "Mods" button on the settings menu's Options page, made like its
    // Gameplay and Controls buttons and placed after them. The page's window grows by one button while the Options
    // page shows (its buttons keep their sizes and order), and grows to the Mods page's size while that shows; it
    // gets its size back whenever another page shows or the menu closes.
    sealed class ModsMenu
    {
        sealed class Hooked
        {
            public MonoBehaviour Menu;
            public GameObject MenuRoot,
                Options;
            public RectTransform Window,
                Title,
                Slot;
            public Vector2 WindowSize,
                TitleSize;
            public string TitleText;
            public ModsPage Page;
            public Transform[] GamePages;
        }

        readonly ModHost host;
        readonly Action<string> log;
        readonly List<Hooked> hooked = new List<Hooked>();
        float nextScan;
        bool? resolved;

        public ModsMenu(ModHost host, Action<string> log)
        {
            this.host = host;
            this.log = log;
        }

        public ModsPage OpenPage => hooked.FirstOrDefault(h => h.Page != null && h.Page.gameObject.activeSelf)?.Page;

        // The game's settings menu, touched once: without it there is no Mods button, said in one log line.
        bool Resolve()
        {
            try
            {
                G.Touch();
                return true;
            }
            catch (Exception e)
            {
                log(
                    "Mods button not added: the game's settings menu changed ("
                        + e.GetType().Name
                        + ": "
                        + e.Message
                        + ")"
                );
                return false;
            }
        }

        public void Tick()
        {
            if ((resolved ?? (resolved = Resolve()).Value) == false)
                return;
            hooked.RemoveAll(h => h.Menu == null);
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1f;
                foreach (var m in G.Menus())
                    if (m.gameObject.scene.IsValid() && hooked.All(h => h.Menu != m))
                        Hook(m);
            }
            foreach (var h in hooked)
                Keep(h);
        }

        void Hook(MonoBehaviour m)
        {
            try
            {
                var t = HarmonyLib.Traverse.Create(m);
                var options = t.Field("optionsPage").GetValue<GameObject>();
                var title = t.Field("title").GetValue<TextMeshProUGUI>();
                var menuRoot = t.Field("menu").GetValue<GameObject>();
                var buttons = options != null ? options.transform.Find("Buttons") : null;
                var controls = buttons != null ? buttons.Find("ControlsSlot") : null;
                var back = buttons != null ? buttons.Find("BackButton") : null;
                if (options == null || title == null || controls == null || back == null || !UiKit.Resolve(log))
                {
                    log("Mods button not added: the settings menu's Options page is not as expected");
                    hooked.Add(new Hooked { Menu = m, Window = null });
                    return;
                }
                var h = new Hooked
                {
                    Menu = m,
                    MenuRoot = menuRoot,
                    Options = options,
                    Window = (RectTransform)options.transform.parent,
                    Title = title.rectTransform,
                    GamePages = new[]
                    {
                        "pausePage",
                        "optionsPage",
                        "graphicsPage",
                        "audioPage",
                        "gameplayPage",
                        "controlsPage",
                    }
                        .Select(f => t.Field(f).GetValue<GameObject>())
                        .Where(g => g != null)
                        .Select(g => g.transform)
                        .ToArray(),
                };
                h.WindowSize = h.Window.sizeDelta;
                h.TitleSize = h.Title.sizeDelta;
                h.Slot = MakeSlot((RectTransform)controls, h);
                h.Slot.SetSiblingIndex(back.GetSiblingIndex());
                var pageRoot = UiKit.Child("ShorelandsMods page", h.Window);
                pageRoot.anchorMin = Vector2.zero;
                pageRoot.anchorMax = Vector2.one;
                pageRoot.offsetMin = pageRoot.offsetMax = Vector2.zero;
                pageRoot.gameObject.SetActive(false);
                h.Page = pageRoot.gameObject.AddComponent<ModsPage>();
                h.Page.Init(host, this, m, (RectTransform)back, title);
                hooked.Add(h);
                log("Mods button added to the settings menu (" + m.gameObject.scene.name + ")");
            }
            catch (Exception e)
            {
                log("Mods button not added: " + e);
                hooked.Add(new Hooked { Menu = m, Window = null });
            }
        }

        // Like the Controls slot: an 80x30 slot with an 80x22 plate button and a size 18 label, built (not copied, so
        // no label helpers come along) from its sprite, tints and sizes.
        RectTransform MakeSlot(RectTransform controls, Hooked h)
        {
            var donor = controls.GetComponentInChildren<Button>(true);
            var donorLabel = donor.GetComponentInChildren<TextMeshProUGUI>(true);
            var slot = UiKit.Child("ModsSlot", controls.parent);
            slot.sizeDelta = controls.sizeDelta;
            var b = UiKit.Child("ModsButton", slot);
            var dr = (RectTransform)donor.transform;
            b.anchorMin = dr.anchorMin;
            b.anchorMax = dr.anchorMax;
            b.pivot = dr.pivot;
            b.anchoredPosition = dr.anchoredPosition;
            b.sizeDelta = dr.sizeDelta;
            var img = UiKit.Plate(b, ((Image)donor.targetGraphic).sprite, Color.white, true);
            var button = b.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.colors = donor.colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => Show(h));
            var label = UiKit.Label("Label", b, donorLabel.color, false);
            var lr = donorLabel.rectTransform;
            label.rectTransform.anchorMin = lr.anchorMin;
            label.rectTransform.anchorMax = lr.anchorMax;
            label.rectTransform.pivot = lr.pivot;
            label.rectTransform.anchoredPosition = lr.anchoredPosition;
            label.rectTransform.sizeDelta = lr.sizeDelta;
            label.fontSize = donorLabel.fontSize;
            label.alignment = donorLabel.alignment;
            label.text = "Mods";
            UiKit.AddShadow(label);
            return slot;
        }

        void Show(Hooked h)
        {
            h.Options.SetActive(false);
            h.TitleText = h.Title.GetComponent<TMP_Text>().text;
            h.Page.Open();
        }

        // The escape hatch: the game's own settings menu (above every mod window) opened on the Mods page with this
        // mod selected, so its Switch is one click away.
        internal void OpenOn(string id)
        {
            if (resolved != true)
                return;
            var game = G.GameMenu();
            var h = hooked.FirstOrDefault(x => game != null && x.Menu == game && x.Page != null);
            if (h == null)
                return;
            G.OpenOptions(game);
            Show(h);
            h.Page.Select(id);
        }

        // Back from the Mods page: the Options page again.
        internal void Back(ModsPage page)
        {
            var h = hooked.FirstOrDefault(x => x.Page == page);
            if (h == null)
                return;
            page.Close();
            Restore(h);
            G.ShowOptions(h.Menu);
        }

        internal static void CloseMenu(MonoBehaviour menu) => G.Close(menu);

        internal void SizeWindow(ModsPage page, int w, int hgt)
        {
            var h = hooked.FirstOrDefault(x => x.Page == page);
            if (h == null)
                return;
            h.Window.sizeDelta = new Vector2(w, hgt);
            h.Title.sizeDelta = new Vector2(w, h.TitleSize.y);
        }

        void Restore(Hooked h)
        {
            if (h.Window.sizeDelta != h.WindowSize)
                h.Window.sizeDelta = h.WindowSize;
            if (h.Title.sizeDelta != h.TitleSize)
                h.Title.sizeDelta = h.TitleSize;
        }

        // Every frame: the window's size for the page that shows, and the Mods page closed when the menu closed or a
        // game page took over.
        void Keep(Hooked h)
        {
            if (h.Window == null || h.Page == null)
                return;
            bool menuOpen = h.MenuRoot == null || h.MenuRoot.activeInHierarchy;
            bool gamePage = h.GamePages.Any(p => p != null && p.gameObject.activeSelf);
            if (h.Page.gameObject.activeSelf && (!menuOpen || gamePage))
            {
                h.Page.Close();
                if (h.TitleText != null && !gamePage)
                    h.Title.GetComponent<TMP_Text>().text = h.TitleText;
            }
            if (h.Page.gameObject.activeSelf)
                return;
            if (h.Options.activeSelf)
            {
                var grow = new Vector2(h.WindowSize.x, h.WindowSize.y + h.Slot.sizeDelta.y - 2);
                if (h.Window.sizeDelta != grow)
                    h.Window.sizeDelta = grow;
                if (h.Title.sizeDelta != h.TitleSize)
                    h.Title.sizeDelta = h.TitleSize;
            }
            else
                Restore(h);
        }

        // The game's settings menu, typed only here.
        static class G
        {
            // Compiling this resolves every member used below; nothing of it runs.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void Touch()
            {
                if (HarmonyLib.AccessTools.Field(typeof(SettingsMenu), "gameMenu") == null)
                    throw new MissingFieldException("SettingsMenu", "gameMenu");
                SettingsMenu m = null;
                if (m != null && !m.IsOpen)
                {
                    m.Open();
                    m.ShowOptions();
                    m.Close();
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static MonoBehaviour[] Menus() =>
                UnityEngine.Object.FindObjectsByType<SettingsMenu>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                );

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static MonoBehaviour GameMenu() =>
                HarmonyLib.AccessTools.Field(typeof(SettingsMenu), "gameMenu")?.GetValue(null) as SettingsMenu;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void OpenOptions(MonoBehaviour m)
            {
                var menu = (SettingsMenu)m;
                if (!menu.IsOpen)
                    menu.Open();
                menu.ShowOptions();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void ShowOptions(MonoBehaviour m) => ((SettingsMenu)m).ShowOptions();

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void Close(MonoBehaviour m) => ((SettingsMenu)m).Close();
        }
    }
}
