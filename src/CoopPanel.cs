using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReclayedCoop
{
    /// The co-op screen in the game's own menu style: a copy of the main menu
    /// panel with Invite a Friend / Back rows and a status line. Opening it
    /// starts the Steam lobby and the invite dialog right away; when the friend
    /// joins, both games go on to the difficulty screen. The IP rows are still
    /// here, shown only with [Interface] ShowIpJoin = true.
    internal static class CoopPanel
    {
        private const string PanelName = "ReclayedCoopPanel";
        private const int MaxIpLength = 21;

        private static GameObject panel;
        private static GameObject owner;          // the main menu panel we replaced
        private static GameObject steamRow, hostRow, ipRow, joinRow, backRow, statusRow;
        private static string ipText;
        private static readonly Dictionary<GameObject, string> shown = new Dictionary<GameObject, string>();

        internal static bool Visible { get { return panel != null && panel.activeInHierarchy; } }

        internal static void Show(global::MainMenuSubMenu menu)
        {
            if (menu == null) { Ui.OpenFromMenu(); return; }
            if (ipText == null) ipText = (Plugin.CfgAddress.Value ?? "").Trim();
            if (panel == null || owner != menu.gameObject) Build(menu);
            if (panel == null) { Ui.OpenFromMenu(); return; }
            owner.SetActive(false);
            panel.SetActive(true);
            Select(steamRow);
            if (!Session.Busy && SteamLink.Available) OnSteam();
            Refresh();
        }

        /// Leaves the panel without bringing the old menu back (the menu flow
        /// navigates somewhere else itself).
        internal static void Hide()
        {
            if (panel != null) panel.SetActive(false);
        }

        internal static void ApplyPatches(HarmonyLib.Harmony h)
        {
            System.Reflection.MethodInfo back = AccessTools.Method(typeof(global::MainMenuManager), "NavigateBack");
            if (back != null) h.Patch(back, new HarmonyLib.HarmonyMethod(typeof(CoopPanel), "NavigateBackPrefix"), null, null, null, null);
        }

        private static bool NavigateBackPrefix()
        {
            if (!Visible) return true;
            Back();
            return false;
        }

        private static void Back()
        {
            if (Session.Busy && !Session.Connected) Session.Close("Cancelled.", false);
            Hide();
            if (owner != null)
            {
                owner.SetActive(true);
                global::MainMenuSubMenu sm = owner.GetComponent<global::MainMenuSubMenu>();
                Selectable s = sm != null ? AccessTools.Field(typeof(global::MainMenuSubMenu), "firstElement").GetValue(sm) as Selectable : null;
                if (s != null) s.Select();
            }
        }

        private static void Build(global::MainMenuSubMenu menu)
        {
            if (panel != null) UnityEngine.Object.Destroy(panel);
            shown.Clear();
            owner = menu.gameObject;

            Selectable first = AccessTools.Field(typeof(global::MainMenuSubMenu), "firstElement").GetValue(menu) as Selectable;
            if (first == null) { panel = null; return; }

            panel = UnityEngine.Object.Instantiate(owner, owner.transform.parent, false);
            panel.name = PanelName;
            panel.SetActive(false);
            // Not a game menu panel: the game must not toggle or select it.
            UnityEngine.Object.DestroyImmediate(panel.GetComponent<global::MainMenuSubMenu>());

            // Keep one row (the copy of the menu's first row) as the template.
            Transform template = null;
            List<GameObject> drop = new List<GameObject>();
            for (int i = 0; i < panel.transform.childCount; i++)
            {
                Transform c = panel.transform.GetChild(i);
                if (template == null && c.name == first.name) template = c;
                else drop.Add(c.gameObject);
            }
            foreach (GameObject g in drop) UnityEngine.Object.DestroyImmediate(g);
            if (template == null) { UnityEngine.Object.Destroy(panel); panel = null; return; }

            bool ip = Plugin.CfgShowIp.Value;
            steamRow = Row(template, "Coop.Invite", OnSteam);
            hostRow = ip ? Row(template, "Coop.HostIp", OnHost) : null;
            ipRow = ip ? Row(template, null, delegate { Select(joinRow); }) : null;
            joinRow = ip ? Row(template, "Coop.JoinIp", OnJoin) : null;
            backRow = Row(template, "MainMenu.Back", Back);
            statusRow = Row(template, null, null);
            UnityEngine.Object.DestroyImmediate(template.gameObject);

            Button sb = statusRow.GetComponent<Button>();
            if (sb != null) sb.interactable = false;

            List<GameObject> order = new List<GameObject>();
            foreach (GameObject g in new GameObject[] { steamRow, hostRow, ipRow, joinRow, backRow })
                if (g != null) order.Add(g);
            for (int i = 0; i < order.Count; i++)
            {
                Selectable s = order[i].GetComponent<Selectable>();
                Navigation nav = new Navigation();
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = order[(i + order.Count - 1) % order.Count].GetComponent<Selectable>();
                nav.selectOnDown = order[(i + 1) % order.Count].GetComponent<Selectable>();
                s.navigation = nav;
            }
            Navigation none = new Navigation();
            none.mode = Navigation.Mode.None;
            statusRow.GetComponent<Selectable>().navigation = none;
        }

        /// key: a localisation key (the game keeps the row in its language), or
        /// null for a row whose text the panel sets every frame.
        private static GameObject Row(Transform template, string key, UnityEngine.Events.UnityAction action)
        {
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, template.parent, false);
            go.name = "Coop " + (key ?? "Status");
            Button b = go.GetComponent<Button>();
            if (b != null)
            {
                b.onClick = new Button.ButtonClickedEvent();
                if (action != null) b.onClick.AddListener(action);
            }
            if (key != null) Loc.Bind(go, key); else SetText(go, "");
            MenuButton.Unhighlight(go);
            return go;
        }

        private static void SetText(GameObject row, string text)
        {
            string old;
            if (shown.TryGetValue(row, out old) && old == text) return;
            shown[row] = text;
            global::ButtonScript bs = row.GetComponent<global::ButtonScript>();
            if (bs != null) bs.SetTextManual(text);
        }

        private static void Select(GameObject go)
        {
            EventSystem es = EventSystem.current;
            if (es != null && go != null) es.SetSelectedGameObject(go);
        }

        private static void OnSteam()
        {
            if (Session.Busy && !(Session.SteamMode && Session.Role == Role.Host)) return;
            SteamLink.OpenInvite();
            Refresh();
        }

        private static void OnHost()
        {
            if (Session.Busy) return;
            Session.Host(Plugin.CfgPort.Value);
            Refresh();
        }

        private static void OnJoin()
        {
            if (Session.Busy) return;
            string ip = (ipText ?? "").Trim();
            if (ip.Length == 0) { Select(ipRow); return; }
            Plugin.CfgAddress.Value = ip;
            Session.Join(ip, Plugin.CfgPort.Value);
            Refresh();
        }

        /// Called every frame from Runner.Update.
        internal static void Refresh()
        {
            if (!Visible) return;
            // The game showed one of its own menu panels: this screen is gone.
            global::MainMenuSubMenu[] shown = UnityEngine.Object.FindObjectsOfType<global::MainMenuSubMenu>();
            if (shown.Length > 0) { Hide(); return; }
            if (ipRow != null)
            {
                bool editing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == ipRow;
                bool caret = editing && ((int)(Time.realtimeSinceStartup * 2f) & 1) == 0;
                SetText(ipRow, "IP " + (ipText.Length > 0 ? ipText : (editing ? "" : Loc.Get("Coop.IpPrompt"))) + (caret ? "_" : editing ? " " : ""));
            }
            SetText(statusRow, StatusLine());
        }

        private static string StatusLine()
        {
            if (Session.Connected) return Loc.Get("Coop.Connected");
            if (Session.SteamMode) return Loc.Get(Session.Role == Role.Host ? "Coop.InviteHint" : "Coop.JoiningSteam");
            if (Session.Busy) return Session.Role == Role.Host
                ? Loc.Get("Coop.WaitingPort", Plugin.CfgPort.Value)
                : Loc.Get("Coop.Connecting");
            if (!SteamLink.Available) return Loc.Get("Coop.NoSteam");
            return "";
        }

        /// Typing the host's address while the IP row is selected. IMGUI
        /// events work whatever input system the game uses.
        internal static void OnGuiEvent(Event e)
        {
            if (!Visible || ipRow == null || e == null || e.type != EventType.KeyDown) return;
            EventSystem es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject != ipRow) return;
            if (e.keyCode == KeyCode.Backspace)
            {
                if (ipText.Length > 0) ipText = ipText.Substring(0, ipText.Length - 1);
                e.Use();
                return;
            }
            char c = e.character;
            if ((char.IsLetterOrDigit(c) || c == '.' || c == ':' || c == '-') && ipText.Length < MaxIpLength)
            {
                ipText += c;
                e.Use();
            }
        }
    }
}
