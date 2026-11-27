using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ShorelandsMods.Core.Ui;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ShorelandsMods.Game.Ui
{
    // Keys for OnKey from the Input System. Nothing reaches mods while a text field (the chat, a host field, any
    // other) has the focus, while the game rebinds a key, or while the Mods page listens for one.
    sealed class KeyInput : IKeyInput
    {
        readonly Dictionary<string, Key> keys = new Dictionary<string, Key>();
        readonly Action<string> log;
        bool gameKeysFailed;
        public static bool Capturing;

        public KeyInput(Action<string> log)
        {
            this.log = log;
        }

        public bool Blocked => Capturing || InputMute.Active || Tools.Inspector.On || FieldFocused() || GameBlocks();

        public bool Pressed(string keyName)
        {
            var k = Keyboard.current;
            if (k == null || keyName == null)
                return false;
            if (!keys.TryGetValue(keyName, out var key))
                keys[keyName] = key = Enum.TryParse<Key>(keyName, false, out var parsed) ? parsed : Key.None;
            return key != Key.None && k[key].wasPressedThisFrame;
        }

        public IReadOnlyList<(string key, string what)> Taken()
        {
            var list = new List<(string, string)>();
            if (Tools.Inspector.KeyName != null)
                list.Add((Tools.Inspector.KeyName, "the key of inspect mode"));
            if (!gameKeysFailed)
                try
                {
                    GameKeys(list);
                }
                catch (Exception e)
                {
                    gameKeysFailed = true;
                    log("the game's key bindings could not be read, so key clashes with them are not shown: " + e);
                }
            return list;
        }

        // The game's actions as its Controls page names them; the others act only in menus or while typing.
        static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            ["Movement/up"] = "Move up",
            ["Movement/down"] = "Move down",
            ["Movement/left"] = "Move left",
            ["Movement/right"] = "Move right",
            ["Interact"] = "Interact",
            ["Inventory"] = "Inventory",
            ["PlayerProfile"] = "Profile",
            ["FieldUI"] = "Field",
            ["Hotbar1"] = "Slot 1",
            ["Hotbar2"] = "Slot 2",
            ["Hotbar3"] = "Slot 3",
            ["Hotbar4"] = "Slot 4",
            ["Hotbar5"] = "Slot 5",
            ["Hotbar6"] = "Slot 6",
            ["Hotbar7"] = "Slot 7",
            ["Send"] = "Chat",
            ["Cancel"] = "Menu",
        };

        // Bindings as the player set them on the game's Controls page.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void GameKeys(List<(string, string)> list)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            foreach (var map in ReworkedCopium.InputBindings.Actions.asset.actionMaps)
            {
                if (map.name != "Player" && map.name != "Chat")
                    continue;
                foreach (var action in map.actions)
                foreach (var b in action.bindings)
                {
                    if (b.isComposite || string.IsNullOrEmpty(b.effectivePath))
                        continue;
                    string name = b.isPartOfComposite ? action.name + "/" + b.name : action.name;
                    if (!Labels.TryGetValue(name, out var label))
                        continue;
                    if (InputControlPath.TryFindControl(keyboard, b.effectivePath) is KeyControl kc)
                        list.Add((kc.keyCode.ToString(), "the game's key for " + label));
                }
            }
        }

        // A text field, the chat or the game's rebinding has the keyboard.
        internal static bool Typing() => Capturing || InputMute.Active || FieldFocused() || GameBlocks();

        static bool FieldFocused()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null || !go.activeInHierarchy)
                return false;
            var f = go.GetComponent<TMP_InputField>();
            if (f != null && f.isFocused)
                return true;
            var legacy = go.GetComponent<UnityEngine.UI.InputField>();
            return legacy != null && legacy.isFocused;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool GameBlocks() =>
            ReworkedCopium.Chat.ChatManager.HasFocus() || ReworkedCopium.InputBindings.BlocksEscape;

        // While the Mods page listens for a key, the game's own rebinding flag keeps Escape from closing the menu.
        public static void Capture(bool on)
        {
            Capturing = on;
            try
            {
                Rebind.Set(on);
            }
            catch (Exception) { }
        }

        static class Rebind
        {
            static readonly AccessTools.FieldRef<bool> Active = AccessTools.StaticFieldRefAccess<bool>(
                AccessTools.Field(typeof(ReworkedCopium.InputBindings), "rebinding")
            );
            static readonly AccessTools.FieldRef<int> EndedFrame = AccessTools.StaticFieldRefAccess<int>(
                AccessTools.Field(typeof(ReworkedCopium.InputBindings), "rebindEndedFrame")
            );

            public static void Set(bool on)
            {
                Active() = on;
                if (!on)
                    EndedFrame() = UnityEngine.Time.frameCount;
            }
        }
    }
}
