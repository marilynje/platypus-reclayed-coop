using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ReclayedCoop
{
    /// Ship screen, right panel (player 2's place) while nobody is in it: the
    /// game's "press fire to join" is hidden and the panel's bottom row (where
    /// READY / BACK go) is one red "Invite a Friend" button, a copy of the
    /// panel's own READY button. It opens the Steam invite; the friend's game
    /// comes straight to this screen (MenuFlow.OnConnected).
    internal static class InviteSlot
    {
        private const string ObjectName = "ReclayedCoop Invite";

        private static readonly FieldInfo indexField = AccessTools.Field(typeof(global::ShipPanelScript), "playerIndex");
        private static readonly FieldInfo notJoinedField = AccessTools.Field(typeof(global::ShipPanelScript), "notJoinedOverlay");
        private static readonly FieldInfo readyField = AccessTools.Field(typeof(global::ShipPanelScript), "readyButton");
        private static readonly FieldInfo backField = AccessTools.Field(typeof(global::ShipPanelScript), "backButton");
        private static readonly FieldInfo singleRowField = AccessTools.Field(typeof(global::ShipPanelScript), "pressToJoinSingleRow");
        private static readonly FieldInfo multiRowField = AccessTools.Field(typeof(global::ShipPanelScript), "pressToJoinMultiRow");

        /// The host came here through "Online Co-op" in the main menu (not
        /// through Start): the ship screen waits for the friend.
        internal static bool Intent;
        internal static float IntentAt;

        private static GameObject button;
        private static CanvasGroup overlay;
        private static bool failed;

        internal static void OnSceneLoaded()
        {
            button = null;
            overlay = null;
            failed = false;
        }

        /// The host's ship screen before the friend is here.
        internal static bool Waiting { get { return Wanted; } }

        private static bool Wanted
        {
            get
            {
                return ShipCoop.InShipScene && Intent && !Lockstep.InMatch && !LoadingScreen.IsLoading &&
                       !Session.Connected && Session.Role != Role.Client;
            }
        }

        /// Every frame.
        internal static void Tick()
        {
            bool want = Wanted;
            if (want && button == null && !failed) Build();
            if (button == null) return;
            // Someone took the right panel (a second local player): no invite.
            bool show = want && overlay != null && overlay.alpha > 0.5f;
            if (button.activeSelf != show) button.SetActive(show);
        }

        /// After the game's ShipPanelScript.Update, which shows its prompt
        /// again every frame: player 2's place says nothing in online co-op.
        internal static void AfterPanelUpdate(global::ShipPanelScript panel)
        {
            if ((int)indexField.GetValue(panel) != 1) return;
            if (!(Wanted || ShipCoop.Active)) return;
            GameObject single = singleRowField.GetValue(panel) as GameObject;
            GameObject multi = multiRowField.GetValue(panel) as GameObject;
            if (single != null && single.activeSelf) single.SetActive(false);
            if (multi != null && multi.activeSelf) multi.SetActive(false);
        }

        private static void Build()
        {
            global::ShipPanelScript panel = null;
            global::ShipPanelScript[] panels = UnityEngine.Object.FindObjectsOfType<global::ShipPanelScript>();
            for (int i = 0; i < panels.Length; i++)
                if ((int)indexField.GetValue(panels[i]) == 1) panel = panels[i];
            if (panel == null) return; // not set up yet, next frame

            overlay = notJoinedField.GetValue(panel) as CanvasGroup;
            Button ready = readyField.GetValue(panel) as Button;
            Button back = backField.GetValue(panel) as Button;
            RectTransform root = panel.transform as RectTransform;
            if (overlay == null || ready == null || root == null)
            {
                failed = true;
                Plugin.Log.LogWarning("Invite button: ship panel layout not found");
                return;
            }

            button = UnityEngine.Object.Instantiate(ready.gameObject, root, false);
            button.name = ObjectName;
            button.SetActive(true);
            button.transform.SetAsLastSibling();

            // Cover the whole bottom row: READY and BACK together.
            RectTransform rt = button.transform as RectTransform;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            Grow(root, ready.transform as RectTransform, ref min, ref max);
            if (back != null) Grow(root, back.transform as RectTransform, ref min, ref max);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = max - min;
            rt.anchoredPosition = (min + max) * 0.5f - root.rect.center;

            Button b = button.GetComponent<Button>();
            if (b != null)
            {
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(OnClick);
                b.interactable = true;
            }
            Loc.Bind(button, "Coop.Invite");
            MenuButton.Unhighlight(button);
            Plugin.Log.LogInfo("Invite button added to the ship screen (" + rt.sizeDelta + " at " + rt.anchoredPosition + ")");
        }

        private static void Grow(RectTransform root, RectTransform r, ref Vector2 min, ref Vector2 max)
        {
            if (r == null) return;
            Vector3[] c = new Vector3[4];
            r.GetWorldCorners(c);
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = root.InverseTransformPoint(c[i]);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
        }

        private static void OnClick()
        {
            Audio.PlaySound("s_ui_submit", true, true);
            if (!SteamLink.Available) { Ui.ShowMessage(Loc.Get("Coop.NoSteam")); return; }
            // Started with --coop-host: stop waiting by IP, the invite is a Steam lobby.
            if (Session.Busy && !Session.SteamMode && !Session.Connected) Session.Close(null, false);
            SteamLink.OpenInvite();
        }
    }
}
