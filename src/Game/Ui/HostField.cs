using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace ShorelandsMods.Game.Ui
{
    // While a host text field has the focus the game takes no keys: its Player actions are off and Enter does not
    // open the chat (the game's own switch for its chat). The owner field gives it back when it loses the focus, and
    // Check gives it back if the owner went away without that.
    static class InputMute
    {
        static HostField owner;
        static bool held,
            muted;

        public static bool Active => owner != null;

        public static void Take(HostField f)
        {
            owner = f;
            Apply();
        }

        public static void Release(HostField f)
        {
            if (owner != f || owner == null)
                return;
            owner = null;
            Apply();
        }

        // Inspect mode keeps the game's input off while it is on, like a focused field.
        public static void Hold(bool on)
        {
            held = on;
            Apply();
        }

        static void Apply()
        {
            bool want = owner is object || held;
            if (want != muted)
                Set(want);
        }

        public static void Check()
        {
            if (owner is object && (owner == null || !owner.isActiveAndEnabled || !owner.isFocused))
            {
                var f = owner;
                owner = null;
                Apply();
                if (
                    f != null
                    && EventSystem.current != null
                    && EventSystem.current.currentSelectedGameObject == f.gameObject
                )
                    EventSystem.current.SetSelectedGameObject(null);
            }
        }

        static void Set(bool muted)
        {
            var game = ReworkedCopium.GameManager.Instance;
            if (game == null || game.inputSystem == null)
                return;
            InputMute.muted = muted;
            if (muted)
            {
                ReworkedCopium.GameManager.EnableChatFocus();
                game.inputSystem.Chat.Send.Disable();
            }
            else
            {
                ReworkedCopium.GameManager.DisableChatFocus();
                game.inputSystem.Chat.Send.Enable();
            }
        }
    }

    // A TMP input field whose keys come from the Input System (Keyboard text input and key presses, with key repeat)
    // instead of IMGUI events, so the field types exactly what the game's input sees, queued test input included.
    sealed class HostField : TMP_InputField
    {
        const float RepeatDelay = 0.4f,
            RepeatEvery = 0.04f;

        readonly Queue<char> typed = new Queue<char>();
        readonly Event scratch = new Event();
        readonly Dictionary<Key, float> nextRepeat = new Dictionary<Key, float>();
        Keyboard listening;
        int selectedFrame;

        static readonly (Key key, string name)[] Editing =
        {
            (Key.Backspace, "backspace"),
            (Key.Delete, "delete"),
            (Key.LeftArrow, "left"),
            (Key.RightArrow, "right"),
            (Key.Home, "home"),
            (Key.End, "end"),
            (Key.Enter, "return"),
            (Key.NumpadEnter, "[enter]"),
        };

        public override void OnSelect(BaseEventData e)
        {
            base.OnSelect(e);
            selectedFrame = Time.frameCount;
            InputMute.Take(this);
            Listen(Keyboard.current);
        }

        public override void OnDeselect(BaseEventData e)
        {
            base.OnDeselect(e);
            Listen(null);
            InputMute.Release(this);
        }

        protected override void OnDisable()
        {
            Listen(null);
            InputMute.Release(this);
            // A closed window's field must not stay the selected object (the game would count it as typing).
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            base.OnDisable();
        }

        void Listen(Keyboard k)
        {
            if (listening == k)
                return;
            if (listening != null)
                listening.onTextInput -= OnText;
            listening = k;
            typed.Clear();
            if (k != null)
                k.onTextInput += OnText;
        }

        void OnText(char c)
        {
            if (c >= ' ' && c != (char)127 && typed.Count < 64)
                typed.Enqueue(c);
        }

        void Update()
        {
            // Enter and Escape end the editing but leave the field selected: let go of it so the game has its keys back.
            if (
                !isFocused
                && Time.frameCount > selectedFrame + 2
                && EventSystem.current != null
                && EventSystem.current.currentSelectedGameObject == gameObject
            )
                EventSystem.current.SetSelectedGameObject(null);
        }

        public override void OnUpdateSelected(BaseEventData e)
        {
            if (!isFocused)
                return;
            // The same keys arrive as IMGUI events from the OS; only the Input System's copy is used.
            while (Event.PopEvent(scratch)) { }
            var k = Keyboard.current;
            bool finished = false;
            if (k != null)
            {
                bool ctrl = k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed;
                bool shift = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
                foreach (var (key, name) in Editing)
                    if (Due(k[key], key))
                    {
                        var ev = Event.KeyboardEvent((shift ? "#" : "") + (ctrl ? "^" : "") + name);
                        if (KeyPressed(ev) == EditState.Finish)
                            finished = true;
                    }
                if (ctrl)
                    foreach (var key in new[] { Key.A, Key.C, Key.V, Key.X })
                        if (k[key].wasPressedThisFrame)
                            KeyPressed(Event.KeyboardEvent("^" + key.ToString().ToLowerInvariant()));
                if (!ctrl)
                    while (typed.Count > 0 && !finished)
                    {
                        var ev = new Event { type = EventType.KeyDown, character = typed.Dequeue() };
                        KeyPressed(ev);
                    }
            }
            typed.Clear();
            if (finished)
                DeactivateInputField();
            UpdateLabel();
            e.Use();
        }

        // A press, then repeats while held.
        bool Due(KeyControl c, Key key)
        {
            if (c.wasPressedThisFrame)
            {
                nextRepeat[key] = Time.unscaledTime + RepeatDelay;
                return true;
            }
            if (c.isPressed && nextRepeat.TryGetValue(key, out var at) && Time.unscaledTime >= at)
            {
                nextRepeat[key] = Time.unscaledTime + RepeatEvery;
                return true;
            }
            return false;
        }

        // The game's field look: plate tinted down, a hint in grey, white text, a one-unit caret.
        public static HostField Create(Transform parent, string hint, int maxLength)
        {
            var r = UiKit.Child("Field", parent);
            var img = UiKit.Plate(r, UiKit.Bg3, new Color(0.55f, 0.55f, 0.55f, 1f), true);
            var area = UiKit.Child("TextArea", r);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(4, 1);
            area.offsetMax = new Vector2(-3, -2);
            area.gameObject.AddComponent<RectMask2D>();
            var placeholder = UiKit.Label("Placeholder", area, UiKit.Hint, false);
            var text = UiKit.Label("Text", area, Color.white, false);
            foreach (var t in new[] { placeholder, text })
            {
                t.rectTransform.anchorMin = Vector2.zero;
                t.rectTransform.anchorMax = Vector2.one;
                t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            }
            placeholder.text = hint;
            // The component is added while the object is inactive, so OnEnable runs with the parts set.
            r.gameObject.SetActive(false);
            var f = r.gameObject.AddComponent<HostField>();
            f.targetGraphic = img;
            f.transition = Transition.None;
            f.textViewport = area;
            f.textComponent = text;
            f.placeholder = placeholder;
            f.fontAsset = UiKit.Font;
            f.pointSize = UiKit.TextSize;
            f.characterLimit = maxLength;
            f.richText = false;
            f.lineType = LineType.SingleLine;
            f.customCaretColor = true;
            f.caretColor = Color.white;
            f.caretWidth = 1;
            f.selectionColor = new Color(1f, 1f, 1f, 0.3f);
            f.navigation = new Navigation { mode = Navigation.Mode.None };
            r.gameObject.SetActive(true);
            return f;
        }
    }
}
