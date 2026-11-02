using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Platypus.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using G = Platypus.Core.Globals;

namespace ReclayedCoop
{
    /// Menus before a match, both players together. The host plays the
    /// menus as usual (Start, difficulty) and invites from the ship screen;
    /// the friend's game goes straight to that ship screen, each picks his
    /// own ship and the level starts once both have locked in. Connected while
    /// the host is in the main menu, player 2 waits there for the host.
    internal static class MenuFlow
    {
        private const int SubMenuDifficulty = 6;

        private static readonly MethodInfo navigateTo = AccessTools.Method(typeof(global::MainMenuManager), "NavigateTo");
        private static readonly Type subMenuType = AccessTools.Inner(typeof(global::MainMenuManager), "SubMenu");

        private static bool navigatePending;
        private static float navigateAfter;
        private static bool menuLocked;
        private static bool peerShipReady;
        private static bool hostWaiting;
        private static Snapshot pendingSnapshot;
        private static float nextCheck;

        internal static void OnConnected()
        {
            peerShipReady = false;
            hostWaiting = false;
            pendingSnapshot = null;
            proposal = "";
            Ui.CloseWindow();
            CoopPanel.Hide();
            if (Session.Role == Role.Host)
            {
                // Invited from the ship screen: bring the friend here.
                if (ShipCoop.InShipScene) HostSceneChange(BuildScenes.ReclayedShipSelect);
                else Session.Status = "Второй игрок подключён.";
            }
            else Session.Status = "Хост выбирает...";
        }

        internal static void OnSceneLoaded(string name)
        {
            if (name == "MainMenuScene" && Session.Connected && !Lockstep.InMatch)
            {
                peerShipReady = false;
                proposal = "";
            }
        }

        private static void RequestDifficultyPanel()
        {
            navigatePending = true;
            navigateAfter = Time.realtimeSinceStartup + 0.5f;
        }

        internal static void Tick()
        {
            if (Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 0.2f;

            bool inMenu = Session.Connected && !Lockstep.InMatch && FindMenu() != null;

            if (navigatePending && Session.Connected && !Lockstep.InMatch && Time.realtimeSinceStartup >= navigateAfter)
            {
                global::MainMenuManager mm = FindMenu();
                if (mm != null && !LoadingScreen.IsLoading)
                {
                    navigatePending = false;
                    try
                    {
                        navigateTo.Invoke(mm, new object[] { Enum.ToObject(subMenuType, SubMenuDifficulty) });
                        Plugin.Log.LogInfo("Co-op: both players on the difficulty panel");
                    }
                    catch (Exception e) { Plugin.Log.LogError("Navigate to difficulty: " + e); }
                }
            }
            if (!Session.Connected) navigatePending = false;

            // Player 2 only watches the host's menu.
            SetLock(false);
            UpdateMarks(inMenu);
        }

        private static readonly FieldInfo subMenuKind = AccessTools.Field(typeof(global::MainMenuSubMenu), "menu");

        private static bool OnDifficultyPanel()
        {
            global::MainMenuSubMenu[] panels = UnityEngine.Object.FindObjectsOfType<global::MainMenuSubMenu>();
            for (int i = 0; i < panels.Length; i++)
                if (Convert.ToInt32(subMenuKind.GetValue(panels[i])) == SubMenuDifficulty) return true;
            return false;
        }

        private static global::MainMenuManager FindMenu()
        {
            return UnityEngine.Object.FindObjectOfType<global::MainMenuManager>();
        }

        /// Player 2 sees the host's cursor and selection instead of his own;
        /// his clicks on difficulty buttons are dropped (DifficultyClickPrefix).
        private static void SetLock(bool locked)
        {
            EventSystem es = EventSystem.current;
            if (es != null && es.sendNavigationEvents == locked) es.sendNavigationEvents = !locked;
            if (locked != menuLocked) Cursor.visible = !locked;
            menuLocked = locked;
        }

        internal static void ApplyPatches(Harmony h)
        {
            Type store = AccessTools.TypeByName("Blackdrop.Platforms.PlatformStoreLocalFile");
            if (store != null)
                foreach (string name in new string[] { "Save", "Load" })
                {
                    MethodInfo m = AccessTools.Method(store, name);
                    if (m != null) h.Patch(m, null, null, null, new HarmonyMethod(typeof(MenuFlow), "SaveFileFinalizer"), null);
                }
            MethodInfo click = AccessTools.Method(typeof(global::DifficultyButton), "OnButtonClicked");
            if (click == null) { Plugin.Log.LogWarning("DifficultyButton.OnButtonClicked not found"); return; }
            h.Patch(click, new HarmonyMethod(typeof(MenuFlow), "DifficultyClickPrefix"), null, null, null, null);
        }

        /// Player 2 cannot start the game: his click proposes that difficulty.
        private static Exception SaveFileFinalizer(Exception __exception)
        {
            if (__exception is System.IO.IOException)
            {
                Plugin.Log.LogWarning("Save file busy (another copy of the game?), skipped: " + __exception.Message);
                return null;
            }
            return __exception;
        }

        private static bool DifficultyClickPrefix(global::DifficultyButton __instance)
        {
            if (!(Session.Connected && Session.Role == Role.Client && !Lockstep.InMatch)) return true;
            string row = __instance.gameObject.name;
            if (proposal.Length > 0 && row != proposal)
            {
                // One proposal at a time: click the green one again to take it back.
                Audio.PlaySound("s_ui_unready", true, true);
                return false;
            }
            proposal = proposal == row ? "" : row;
            Session.SendProposal(proposal);
            Audio.PlaySound(proposal.Length > 0 ? "s_ui_submit" : "s_ui_unready", true, true);
            nextCheck = 0f;
            return false;
        }

        // ------------------------------------------------------------ difficulty marks
        // Hover is the game's own highlight (each player sees only his own).
        // The row player 2 picked is green on both PCs until he clicks it again.

        private static string proposal = "";
        private static readonly HashSet<int> greenRows = new HashSet<int>();
        private static global::DifficultyButton[] rows = new global::DifficultyButton[0];
        private static readonly Color GuestColor = new Color(0.45f, 0.78f, 0.36f); // player 2 proposed it (wins)

        internal static void OnProposal(string button)
        {
            if (Session.Role != Role.Host) return;
            proposal = button ?? "";
            Audio.PlaySound("s_ui_submit", true, true);
            nextCheck = 0f;
        }

        /// Every 0.2 s: which difficulty rows exist right now.
        private static void UpdateMarks(bool inMenu)
        {
            rows = inMenu ? UnityEngine.Object.FindObjectsOfType<global::DifficultyButton>() : new global::DifficultyButton[0];
        }

        /// Every frame: the game's morphing letters reset their colour to
        /// white whenever they change, so the tint is applied continuously.
        private static void TintRows()
        {
            if (rows.Length == 0) return;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] == null) continue;
                bool green = proposal.Length > 0 && rows[i].gameObject.name == proposal;
                Tint(rows[i].transform, green ? GuestColor : Color.white);
                global::ButtonScript bs = rows[i].GetComponent<global::ButtonScript>();
                if (bs == null) continue;
                int id = rows[i].GetInstanceID();
                if (green)
                {
                    // Not red-tinted green when hovered: keep the normal letters.
                    MenuButton.NormalLetters(bs);
                    greenRows.Add(id);
                }
                else if (greenRows.Remove(id)) MenuButton.RefreshLook(bs);
            }
        }

        private static void Tint(Transform row, Color c)
        {
            global::MorphLetter[] letters = row.GetComponentsInChildren<global::MorphLetter>(true);
            for (int i = 0; i < letters.Length; i++)
            {
                Image img = letters[i].GetComponent<Image>();
                if (img == null || img.color.a <= 0f) continue; // hidden letter slot
                Color want = new Color(c.r, c.g, c.b, img.color.a);
                if (img.color != want) img.color = want;
            }
            TMPro.TMP_Text[] texts = row.GetComponentsInChildren<TMPro.TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                Color want = new Color(c.r, c.g, c.b, texts[i].color.a);
                if (texts[i].color != want) texts[i].color = want;
            }
        }

        private static string SelectedName()
        {
            EventSystem es = EventSystem.current;
            return es != null && es.currentSelectedGameObject != null ? es.currentSelectedGameObject.name : "";
        }

        // ------------------------------------------------------------ host cursor

        private static float nextCursorSend;
        private static Vector2 sentPos = new Vector2(-1f, -1f);
        private static string sentSel;
        private static bool sentVisible;
        private static Vector2 peerPos;
        private static bool peerVisible;
        private static string peerSel = "";
        private static Texture2D cursorTex;

        internal static void FastTick()
        {
            if (!Session.Connected || Lockstep.InMatch) return;
            TintRows();
            // Both players report the row they are on.
            {
                if (Time.realtimeSinceStartup < nextCursorSend) return;
                nextCursorSend = Time.realtimeSinceStartup + 1f / 30f;
                Vector2 m = MousePosition();
                Vector2 n = new Vector2(m.x / Mathf.Max(1, Screen.width), m.y / Mathf.Max(1, Screen.height));
                EventSystem es = EventSystem.current;
                string sel = es != null && es.currentSelectedGameObject != null ? es.currentSelectedGameObject.name : "";
                bool vis = Cursor.visible;
                if ((n - sentPos).sqrMagnitude < 1e-8f && sel == sentSel && vis == sentVisible) return;
                sentPos = n;
                sentSel = sel;
                sentVisible = vis;
                Session.SendCursor(n.x, n.y, vis, sel);
                return;
            }
        }

        private static Vector2 MousePosition()
        {
            try { return Rewired.ReInput.controllers.Mouse.screenPosition; }
            catch (Exception) { }
            try { return Input.mousePosition; }
            catch (Exception) { return Vector2.zero; }
        }

        private static GameObject FindRow(string name)
        {
            global::MainMenuSubMenu[] panels = UnityEngine.Object.FindObjectsOfType<global::MainMenuSubMenu>();
            for (int i = 0; i < panels.Length; i++)
            {
                Selectable[] rows = panels[i].GetComponentsInChildren<Selectable>();
                for (int j = 0; j < rows.Length; j++)
                    if (rows[j].name == name && rows[j].gameObject.activeInHierarchy) return rows[j].gameObject;
            }
            return null;
        }

        internal static void OnPeerCursor(float x, float y, bool visible, string selected)
        {
            peerPos = new Vector2(x, y);
            peerVisible = visible;
            peerSel = selected ?? "";
        }

        internal static void DrawPeerCursor()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (Session.Role != Role.Client || !Session.Connected || Lockstep.InMatch || !peerVisible) return;
            if (cursorTex == null) cursorTex = MakeArrow();
            float scale = Mathf.Max(1f, Screen.height / 540f);
            float x = peerPos.x * Screen.width;
            float y = (1f - peerPos.y) * Screen.height;
            GUI.DrawTexture(new Rect(x, y, cursorTex.width * scale, cursorTex.height * scale), cursorTex);
        }

        /// 12x19 arrow, white with a black outline.
        private static Texture2D MakeArrow()
        {
            string[] rows = {
                "X...........",
                "XX..........",
                "XOX.........",
                "XOOX........",
                "XOOOX.......",
                "XOOOOX......",
                "XOOOOOX.....",
                "XOOOOOOX....",
                "XOOOOOOOX...",
                "XOOOOOOOOX..",
                "XOOOOOOOOOX.",
                "XOOOOOOXXXXX",
                "XOOOXOOX....",
                "XOOXXOOX....",
                "XOX..XOOX...",
                "XX...XOOX...",
                "X.....XOOX..",
                "......XOOX..",
                ".......XX...",
            };
            Texture2D t = new Texture2D(12, rows.Length, TextureFormat.RGBA32, false);
            t.filterMode = FilterMode.Point;
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < 12; c++)
                {
                    char ch = rows[r][c];
                    Color col = ch == 'X' ? Color.black : ch == 'O' ? Color.white : new Color(0, 0, 0, 0);
                    t.SetPixel(c, rows.Length - 1 - r, col);
                }
            t.Apply();
            return t;
        }

        /// This player leaves on purpose: the partner is told (and goes to
        /// the main menu), we go to the main menu.
        internal static void LeaveCoop()
        {
            if (!Session.Busy) return;
            Plugin.Log.LogInfo("Leaving the co-op game");
            Lockstep.Fail("Вы вышли из совместной игры.", true);
        }

        // ------------------------------------------------------------ host

        internal static void HostSceneChange(BuildScenes scene)
        {
            if (!Session.Connected) return;
            if (scene != BuildScenes.ReclayedShipSelect && scene != BuildScenes.MainMenu) return;
            peerShipReady = false;
            hostWaiting = false;
            Session.SendScene((int)scene, (int)G.Difficulty, G.currentLevel, G.currentArea);
            if (scene == BuildScenes.ReclayedShipSelect) Session.Status = "Выберите корабль. Второй игрок выбирает свой.";
        }

        internal static bool HostReadyToStart(Snapshot requested)
        {
            if (peerShipReady || ShipCoop.PeerReady) return true;
            hostWaiting = true;
            pendingSnapshot = requested;
            Session.Status = "Ждём, пока второй игрок выберет корабль...";
            Ui.ShowMessage(Session.Status);
            return false;
        }

        internal static void OnPeerShip(string ship, string skin)
        {
            if (Session.Role != Role.Host || Lockstep.InMatch) return;
            Session.PeerShip = ship ?? "";
            Session.PeerSkin = skin ?? "";
            peerShipReady = true;
            Plugin.Log.LogInfo("Player 2 locked in ship '" + ship + "' skin '" + skin + "'");
            if (!hostWaiting)
            {
                Session.Status = "Второй игрок готов. Выберите корабль.";
                Ui.ShowMessage(Session.Status);
                return;
            }
            hostWaiting = false;
            Snapshot s = Lockstep.HostStart(pendingSnapshot);
            pendingSnapshot = null;
            if (Lockstep.InMatch) Lockstep.LoadScene(BuildScenes.Gameplay, s);
        }

        // ------------------------------------------------------------ player 2

        internal static void OnHostScene(int scene, int difficulty, string level, int area)
        {
            if (Session.Role != Role.Client || Lockstep.InMatch) return;
            G.Difficulty = (GameDifficulty)difficulty;
            G.startingDifficulty = (GameDifficulty)difficulty;
            if (!string.IsNullOrEmpty(level)) G.currentLevel = level;
            G.currentArea = area;
            if ((BuildScenes)scene == BuildScenes.ReclayedShipSelect)
                Session.Status = "Выберите свой корабль.";
            Lockstep.LoadScene((BuildScenes)scene, null);
        }

        internal static void ClientShipLocked()
        {
            string ship = Pick(G.playerShips);
            string skin = Pick(G.playerSkin);
            Session.SendShip(ship, skin);
            Session.Status = "Корабль выбран. Ждём хоста...";
            Ui.ShowMessage(Session.Status);
            Plugin.Log.LogInfo("Sent ship '" + ship + "' skin '" + skin + "' to the host");
        }

        private static string Pick(Dictionary<int, string> slots)
        {
            string v;
            if (slots != null && slots.TryGetValue(0, out v) && !string.IsNullOrEmpty(v)) return v;
            return "";
        }
    }
}
