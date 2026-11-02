using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using G = Platypus.Core.Globals;

namespace ReclayedCoop
{
    /// Pause screen and messages drawn with the game's own font (taken from
    /// any text the game shows), on top of everything, instead of IMGUI.
    internal static class Overlay
    {
        private static GameObject root;
        private static Image dim;
        private static Image box;
        private static TextMeshProUGUI title;
        private static TextMeshProUGUI body;
        private static TMP_FontAsset font;
        private static Material fontMaterial;
        private static bool cyrillic;
        // The game's clay letters (MorphText), copied from a main menu row.
        private static GameObject morphTemplate;
        private static global::PText titleMorph;
        private static string titleMorphText;
        private static readonly System.Reflection.FieldInfo pTextLocalized =
            HarmonyLib.AccessTools.Field(typeof(global::PText), "useLocalizedText");

        private static string msgTitle = "";
        private static string msgBody = "";
        private static float msgUntil;
        private static string pauseTitle, pauseBody;

        // Texts the rest of the mod passes in -> localisation keys (Loc.cs).
        private static readonly Dictionary<string, string> keys = new Dictionary<string, string>
        {
            { "ПАУЗА", "Coop.Paused" },
            { "Ждём второго игрока...", "Coop.WaitingP2" },
            { "СВЯЗЬ ПОТЕРЯНА", "Coop.ConnLost" },
            { "Хост вышел.", "Coop.HostLeft" },
            { "Второй игрок вышел.", "Coop.P2Left" },
            { "Вы вышли из совместной игры.", "Coop.YouLeft" },
            { "Нажмите Esc ещё раз, чтобы выйти из совместной игры.", "Coop.EscAgain" },
            { "Связь со вторым игроком потеряна.", "Coop.LinkLost" },
            { "Второй игрок не вернулся - матч закончен.", "Coop.P2NotBack" },
            { "Матч нельзя продолжить - второй игрок уже не в нём.", "Coop.CantContinue" },
            { "Ждём, пока второй игрок выберет корабль...", "Coop.WaitShip" },
            { "Корабль выбран. Ждём хоста...", "Coop.ShipReady" },
            { "Хост выбирает - ждите.", "Coop.HostChoosing" },
            { "Назад может вернуть только хост (или F8 - Disconnect).", "Coop.OnlyHostBack" },
            { "Второй игрок готов. Выберите корабль.", "Coop.P2Ready" },
            { "GAMES OUT OF SYNC", "Coop.OutOfSync" },
            { "Cancelled.", "Coop.Cancelled" },
            { "Partner closed the game.", "Coop.PartnerClosed" },
            { "You left the match.", "Coop.YouLeftMatch" },
        };

        /// The text in the game's language. checkFont: the overlay's TMP font may
        /// lack the letters (Cyrillic, CJK): then English.
        internal static string Text(string ru, bool checkFont)
        {
            if (string.IsNullOrEmpty(ru)) return ru;
            string key, local, en;
            const string waiting = "Ждём второго игрока...";
            if (keys.TryGetValue(ru, out key)) { local = Loc.Get(key); en = Loc.English(key); }
            else if (ru.StartsWith(waiting))
            {
                string n = ru.Substring(waiting.Length).Replace("с", "").Trim();
                local = Loc.Get("Coop.WaitingP2") + (n.Length > 0 ? " " + Loc.Get("Coop.Seconds", n) : "");
                en = Loc.English("Coop.WaitingP2") + (n.Length > 0 ? " " + Loc.English("Coop.Seconds", n) : "");
            }
            else return ru;
            return checkFont && font != null && !font.HasCharacters(local) ? en : local;
        }

        /// A short message (4 s), e.g. "the host left".
        internal static void Message(string text, float seconds)
        {
            string t = text ?? "";
            if (t.StartsWith("Desync")) { msgTitle = "GAMES OUT OF SYNC"; msgBody = t; }
            else { msgTitle = t; msgBody = ""; }
            msgUntil = Time.realtimeSinceStartup + seconds;
        }

        /// Called every frame by the UI: null title = no pause screen.
        internal static void Pause(string t, string b)
        {
            pauseTitle = t;
            pauseBody = b;
        }

        private static readonly bool previewPause = Array.IndexOf(Environment.GetCommandLineArgs(), "--coop-overlaytest") >= 0;

        private static GameObject hud;
        private static global::PText hudPText;
        private static RectTransform hudRect;
        private static Material menuLetters;
        private static readonly System.Reflection.FieldInfo pTextMaterial =
            HarmonyLib.AccessTools.Field(typeof(global::PText), "material");
        private static readonly System.Reflection.FieldInfo rowTextMaterial =
            HarmonyLib.AccessTools.Field(typeof(global::ButtonScript), "textMaterial");
        private static TextMeshProUGUI hudText;
        private static float hudNext;
        private static string hudShown;

        /// During a level: only the ping, top centre, drawn like the game's
        /// own "CREDITS" line (a copy of it).
        private static void UpdateHud()
        {
            bool show = Lockstep.InMatch && Lockstep.GameplayScene && Session.Connected && !G.paused;
            if (!show)
            {
                if (hud != null && hud.activeSelf) hud.SetActive(false);
                return;
            }
            if (hud == null && !BuildHud()) return;
            if (!hud.activeSelf) { hud.SetActive(true); hudNext = 0f; hudShown = null; }
            if (Time.realtimeSinceStartup < hudNext) return;
            hudNext = Time.realtimeSinceStartup + 1f;
            string text = "Ping: " + Mathf.RoundToInt((float)Session.RttMs);
            PlaceHud();
            if (text == hudShown) return;
            hudShown = text;
            if (hudPText != null) hudPText.Set(text, true);
            else if (hudText != null) hudText.text = text.ToUpperInvariant() + " MS";
        }

        /// Left edge of the game picture: past the black bars, if any.
        private static void PlaceHud()
        {
            if (hudRect == null) return;
            Camera cam = Camera.main;
            float left = cam != null ? Mathf.Clamp01(cam.rect.xMin) : 0f;
            float canvasWidth = 1080f * Screen.width / Mathf.Max(1, Screen.height);
            hudRect.anchoredPosition = new Vector2(left * canvasWidth + 24f, -14f);
        }

        private static bool BuildHud()
        {
            // The game's footer line "Credits: 01".
            global::PText credits = null;
            foreach (global::PText p in UnityEngine.Object.FindObjectsOfType<global::PText>())
            {
                TMP_Text t = p.GetComponent<TMP_Text>();
                if (p.transform.parent != null && p.transform.parent.name == "Footer") { credits = p; break; }
                if (t != null && t.text != null && t.text.StartsWith("Credits")) credits = p;
            }
            if (credits == null && font == null) return false;

            hud = new GameObject("ReclayedCoopHud");
            UnityEngine.Object.DontDestroyOnLoad(hud);
            Canvas c = hud.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 31000;
            CanvasScaler cs = hud.AddComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080);
            cs.matchWidthOrHeight = 1f;

            if (credits != null)
            {
                GameObject copy = UnityEngine.Object.Instantiate(credits.gameObject, hud.transform, false);
                copy.name = "ReclayedCoop Ping";
                // Keep only what draws the letters.
                foreach (MonoBehaviour mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
                    if (!(mb is global::PText) && !(mb is global::MorphTextHandler) && !(mb is global::MorphLetter) &&
                        !(mb is TMP_Text) && !(mb is Graphic))
                        UnityEngine.Object.DestroyImmediate(mb);
                RectTransform r = copy.GetComponent<RectTransform>();
                if (r != null)
                {
                    r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
                    r.pivot = new Vector2(0f, 1f);
                    hudRect = r;
                    TMP_Text tt = copy.GetComponent<TMP_Text>();
                    if (tt != null) tt.alignment = TextAlignmentOptions.TopLeft;
                    r.localScale = new Vector3(0.8f, 0.8f, 1f); // half the game's own CREDITS line
                }
                hudPText = copy.GetComponent<global::PText>();
                if (hudPText != null && menuLetters != null)
                {
                    pTextMaterial.SetValue(hudPText, menuLetters);
                    hudPText.UpdateMaterial();
                }
                if (hudPText != null && pTextLocalized != null) pTextLocalized.SetValue(hudPText, false);
                Plugin.Log.LogInfo("Ping display: copy of the game's credits line");
                return true;
            }

            hudText = NewText("Ping", hud.transform, 30f);
            hudText.enableAutoSizing = false;
            hudText.alignment = TextAlignmentOptions.Top;
            RectTransform tr = hudText.rectTransform;
            tr.anchorMin = new Vector2(0.3f, 1f);
            tr.anchorMax = new Vector2(0.7f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.anchoredPosition = new Vector2(0f, -10f);
            tr.sizeDelta = new Vector2(0f, 44f);
            return true;
        }

        internal static void Update()
        {
            UpdateHud();
            FindFont();
            if (previewPause && string.IsNullOrEmpty(pauseTitle)) { pauseTitle = "ПАУЗА"; pauseBody = "Ждём второго игрока..."; }
            bool paused = !string.IsNullOrEmpty(pauseTitle);
            bool message = !paused && Time.realtimeSinceStartup < msgUntil && msgTitle.Length > 0;
            if (!paused && !message) { if (root != null && root.activeSelf) root.SetActive(false); return; }
            if (!Build()) return;
            if (!root.activeSelf) root.SetActive(true);
            dim.enabled = paused;
            string head = Text(paused ? pauseTitle : msgTitle, titleMorph == null);
            if (titleMorph != null)
            {
                string up = (head ?? "").ToUpperInvariant();
                if (up != titleMorphText) { titleMorphText = up; titleMorph.Set(up, true); }
                Set(title, "");
            }
            else Set(title, head);
            Set(body, Text(paused ? pauseBody : msgBody, true));
        }

        private static void Set(TextMeshProUGUI t, string s)
        {
            s = s ?? "";
            if (t.text != s) t.text = s;
        }

        private static void FindFont()
        {
            if (morphTemplate == null)
            {
                global::ButtonScript[] rows = UnityEngine.Object.FindObjectsOfType<global::ButtonScript>();
                for (int i = 0; i < rows.Length; i++)
                {
                    global::PText pt = rows[i].GetComponentInChildren<global::PText>(true);
                    if (pt == null || pt.GetComponentInChildren<global::MorphTextHandler>(true) == null) continue;
                    menuLetters = rowTextMaterial.GetValue(rows[i]) as Material;
                    morphTemplate = UnityEngine.Object.Instantiate(pt.gameObject);
                    morphTemplate.name = "ReclayedCoop MorphTemplate";
                    morphTemplate.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(morphTemplate);
                    Plugin.Log.LogInfo("Overlay: using the game's clay letters for titles");
                    if (root != null) { UnityEngine.Object.Destroy(root); root = null; titleMorph = null; }
                    break;
                }
            }
            if (font != null) return;
            TMP_Text[] texts = UnityEngine.Object.FindObjectsOfType<TMP_Text>();
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].font == null || texts[i].gameObject.name.StartsWith("ReclayedCoop")) continue;
                font = texts[i].font;
                fontMaterial = texts[i].fontSharedMaterial;
                cyrillic = font.HasCharacters("ПАУЗАЖдёмвгоиры");
                Plugin.Log.LogInfo("Overlay font: " + font.name + (cyrillic ? " (Cyrillic)" : " (no Cyrillic: English text)"));
                if (root != null) { UnityEngine.Object.Destroy(root); root = null; }
                return;
            }
        }

        private static bool Build()
        {
            if (root != null) return true;
            if (font == null) return false;
            root = new GameObject("ReclayedCoopOverlay");
            UnityEngine.Object.DontDestroyOnLoad(root);
            Canvas c = root.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 32000;
            CanvasScaler cs = root.AddComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080);
            cs.matchWidthOrHeight = 1f;

            dim = NewImage("Dim", root.transform, new Color(0.03f, 0.02f, 0.08f, 0.6f));
            Stretch(dim.rectTransform, Vector2.zero, Vector2.one);

            box = NewImage("Box", root.transform, new Color(0.08f, 0.05f, 0.18f, 0.85f));
            Stretch(box.rectTransform, new Vector2(0.18f, 0.38f), new Vector2(0.82f, 0.62f));
            Outline o = box.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.9f);
            o.effectDistance = new Vector2(4f, -4f);

            title = NewText("Title", box.transform, 72f);
            Stretch(title.rectTransform, new Vector2(0.03f, 0.42f), new Vector2(0.97f, 0.95f));
            body = NewText("Body", box.transform, 40f);
            Stretch(body.rectTransform, new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.42f));
            body.color = new Color(1f, 0.85f, 0.35f);

            if (morphTemplate != null)
            {
                GameObject m = UnityEngine.Object.Instantiate(morphTemplate, box.transform, false);
                m.name = "ReclayedCoop TitleMorph";
                RectTransform mr = m.GetComponent<RectTransform>();
                if (mr != null)
                {
                    mr.anchorMin = mr.anchorMax = new Vector2(0.5f, 0.68f);
                    mr.pivot = new Vector2(0.5f, 0.5f);
                    mr.anchoredPosition = Vector2.zero;
                    mr.localScale = new Vector3(2f, 2f, 1f);
                }
                titleMorph = m.GetComponent<global::PText>();
                if (titleMorph != null && pTextLocalized != null) pTextLocalized.SetValue(titleMorph, false);
                titleMorphText = null;
                m.SetActive(true);
            }
            return true;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject("ReclayedCoop " + name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, float size)
        {
            GameObject go = new GameObject("ReclayedCoop " + name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            if (fontMaterial != null) t.fontSharedMaterial = fontMaterial;
            t.fontSize = size;
            t.enableAutoSizing = true;
            t.fontSizeMin = size * 0.4f;
            t.fontSizeMax = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        private static void Stretch(RectTransform r, Vector2 min, Vector2 max)
        {
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }
    }
}
