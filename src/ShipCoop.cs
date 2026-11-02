using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Platypus.Core;
using UnityEngine;
using G = Platypus.Core.Globals;

namespace ReclayedCoop
{
    /// Ship screen for two PCs: the host is always the left panel (slot 0),
    /// player 2 the right one (slot 1). Each PC drives its own panel with its
    /// own keyboard / mouse / gamepad; the partner's panel copies the state
    /// that arrives over the network (joined, ship, skin, ready).
    internal static class ShipCoop
    {
        internal static bool InShipScene;

        private static readonly FieldInfo playerField = AccessTools.Field(typeof(global::ShipPanelScript), "player");
        private static readonly FieldInfo indexField = AccessTools.Field(typeof(global::ShipPanelScript), "playerIndex");
        private static readonly MethodInfo lockedGet = AccessTools.PropertyGetter(typeof(global::ShipPanelScript), "IsLockedIn");
        private static readonly MethodInfo lockedSet = AccessTools.PropertySetter(typeof(global::ShipPanelScript), "IsLockedIn");
        private static readonly MethodInfo updateDisplay = AccessTools.Method(typeof(global::ShipPanelScript), "UpdateShipDisplay");
        private static readonly MethodInfo onControllerAdded = AccessTools.Method(typeof(global::ShipPanelScript), "OnControllerAdded");
        private static readonly MethodInfo onControllerChanged = AccessTools.Method(typeof(global::ShipPanelScript), "OnControllerChanged");
        private static readonly FieldInfo menuP0 = AccessTools.Field(typeof(global::ReclayedPlayMenu), "player0");
        private static readonly FieldInfo menuP1 = AccessTools.Field(typeof(global::ReclayedPlayMenu), "player1");
        private static readonly FieldInfo menuCanvas = AccessTools.Field(typeof(global::ReclayedPlayMenu), "canvasGroup");

        // Partner's panel as last reported.
        private static bool remoteJoined, remoteLocked;
        private static string remoteShip = "", remoteSkin = "";
        // What we last told the partner about our panel.
        private static string sentState;
        private static float nextResend;

        internal static bool Active
        {
            get { return InShipScene && Session.Connected && !Lockstep.InMatch; }
        }

        internal static bool PeerReady { get { return remoteJoined && remoteLocked; } }

        internal static void OnSceneLoaded(string name)
        {
            InShipScene = name == "ReclayedShipSelectScene";
            labels.Clear();
            leaveWired.Clear();
            sceneAt = Time.realtimeSinceStartup;
            remoteJoined = false;
            remoteLocked = false;
            sentState = null;
        }

        internal static void ApplyPatches(Harmony h)
        {
            Prefix(h, typeof(global::ShipPanelScript), "OnEnable", "PanelOnEnablePrefix");
            Prefix(h, typeof(global::PlayerAssignmentManager), "IsPlayerAssigned", "IsPlayerAssignedPrefix");
            Prefix(h, typeof(global::ReclayedPlayMenu), "Update", "PlayMenuUpdatePrefix");
            Prefix(h, typeof(global::ShipPanelScript), "OnNavSubmit", "SubmitPrefix");
            Prefix(h, typeof(global::ShipPanelScript), "OnNavBack", "BackPrefix");
            MethodInfo upd = AccessTools.Method(typeof(global::ShipPanelScript), "Update");
            if (upd != null) h.Patch(upd, null, new HarmonyMethod(typeof(ShipCoop), "PanelUpdatePostfix"), null, null, null);
            if (updateDisplay != null) h.Patch(updateDisplay, null, new HarmonyMethod(typeof(ShipCoop), "UpdateDisplayPostfix"), null, null, null);
        }

        private static readonly MethodInfo submitGet = AccessTools.PropertyGetter(typeof(global::ShipPanelScript), "InputSubmit");
        private static readonly MethodInfo mouseGet = AccessTools.PropertyGetter(typeof(global::ShipPanelScript), "IsMousePlayer");
        private static readonly FieldInfo readyField = AccessTools.Field(typeof(global::ShipPanelScript), "readyButton");
        private static int lockFrame = -1;

        private static bool IsLocal(global::ShipPanelScript p) { return Index(p) == Lockstep.LocalSlot; }

        private static void Unlock(global::ShipPanelScript p)
        {
            lockedSet.Invoke(p, new object[] { false });
            Audio.PlaySound("s_ui_unready", true, true);
        }

        /// Back only takes READY back; it never un-joins the local panel
        /// (leaving the co-op game is Esc twice, see Ui).
        private static bool BackPrefix(global::ShipPanelScript __instance)
        {
            if (!(Active || InviteSlot.Waiting) || !IsLocal(__instance)) return true;
            if (!Application.isFocused) return false; // keys typed into another window
            if (!Active) return true;
            return Locked(__instance);
        }

        /// READY while ready = cancel (mouse button or key).
        private static bool SubmitPrefix(global::ShipPanelScript __instance)
        {
            if (!(Active || InviteSlot.Waiting) || !IsLocal(__instance)) return true;
            // Rewired reads the keyboard even when the game is in the background:
            // Enter typed into another window must not press READY.
            if (!Application.isFocused) return false;
            if (Locked(__instance))
            {
                if (lockFrame == Time.frameCount) return false;
                Unlock(__instance);
                return false;
            }
            lockFrame = Time.frameCount;
            return true;
        }

        /// The game stops reading Submit and greys out READY once a panel is
        /// ready; keep both usable so the player can take it back.
        private static void PanelUpdatePostfix(global::ShipPanelScript __instance)
        {
            InviteSlot.AfterPanelUpdate(__instance);
            if (!(Active || InviteSlot.Waiting)) return;
            try { CoopButtons(__instance); }
            catch (Exception e) { Plugin.Log.LogError("Ship panel buttons: " + e); }
            if (!IsLocal(__instance) || !Locked(__instance)) return;
            try
            {
                // The game fades the buttons out once ready: keep CANCEL on screen.
                CanvasGroup joined = joinedField.GetValue(__instance) as CanvasGroup;
                if (joined != null)
                {
                    joined.alpha = 1f;
                    joined.interactable = true;
                    joined.blocksRaycasts = true;
                }
                bool mouse = (bool)mouseGet.Invoke(__instance, null);
                UnityEngine.UI.Button ready = readyField.GetValue(__instance) as UnityEngine.UI.Button;
                if (mouse && ready != null) ready.interactable = true;
                if (!mouse && lockFrame != Time.frameCount && Application.isFocused && (bool)submitGet.Invoke(__instance, null)) Unlock(__instance);
            }
            catch (Exception e) { Plugin.Log.LogError("Ship panel: " + e); }
        }

        private static readonly FieldInfo backField = AccessTools.Field(typeof(global::ShipPanelScript), "backButton");
        private static readonly FieldInfo joinedField = AccessTools.Field(typeof(global::ShipPanelScript), "joinedOverlay");
        private static readonly FieldInfo leftField = AccessTools.Field(typeof(global::ShipPanelScript), "leftArrow");
        private static readonly FieldInfo rightField = AccessTools.Field(typeof(global::ShipPanelScript), "rightArrow");
        private static readonly FieldInfo skinField = AccessTools.Field(typeof(global::ShipPanelScript), "skinButton");
        private static readonly Dictionary<int, string> labels = new Dictionary<int, string>();
        private static readonly HashSet<int> leaveWired = new HashSet<int>();

        /// Online: our panel has READY (CANCEL once ready) and LEAVE; the
        /// partner's panel has no buttons, the partner presses his own.
        private static void CoopButtons(global::ShipPanelScript p)
        {
            UnityEngine.UI.Button ready = readyField.GetValue(p) as UnityEngine.UI.Button;
            UnityEngine.UI.Button back = backField.GetValue(p) as UnityEngine.UI.Button;
            if (!IsLocal(p))
            {
                if (!Active) return; // waiting: the right panel is the invite
                // The partner's panel only shows his choice: nothing to press on it.
                if (ready != null && ready.gameObject.activeSelf) ready.gameObject.SetActive(false);
                if (back != null && back.gameObject.activeSelf) back.gameObject.SetActive(false);
                UnityEngine.UI.Button left = leftField.GetValue(p) as UnityEngine.UI.Button;
                UnityEngine.UI.Button right = rightField.GetValue(p) as UnityEngine.UI.Button;
                UnityEngine.UI.Button skin = skinField.GetValue(p) as UnityEngine.UI.Button;
                if (left != null && left.gameObject.activeSelf) left.gameObject.SetActive(false);
                if (right != null && right.gameObject.activeSelf) right.gameObject.SetActive(false);
                if (skin != null && skin.interactable) skin.interactable = false;
                return;
            }
            Label(ready, Locked(p) ? "Coop.Cancel" : "Coop.Ready");
            if (!Active) return; // no partner yet: BACK is the game's own
            Label(back, "Coop.Leave");
            if (back != null && leaveWired.Add(back.GetInstanceID()))
            {
                back.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                back.onClick.AddListener(OnLeaveClicked);
            }
        }

        private static float sceneAt;

        private static void OnLeaveClicked()
        {
            if (Time.realtimeSinceStartup - sceneAt < 2f)
            {
                Plugin.Log.LogWarning("LEAVE right after the ship screen opened, ignored:\n" + Environment.StackTrace);
                return;
            }
            MenuFlow.LeaveCoop();
        }

        /// The game rewrites the button texts (UpdateShipDisplay): set ours again.
        private static void UpdateDisplayPostfix() { labels.Clear(); }

        private static void Label(UnityEngine.UI.Button b, string key)
        {
            if (b == null) return;
            string old;
            if (labels.TryGetValue(b.GetInstanceID(), out old) && old == key) return;
            labels[b.GetInstanceID()] = key;
            Loc.Bind(b.gameObject, key);
        }

        private static void Prefix(Harmony h, Type type, string method, string prefix)
        {
            MethodInfo m = AccessTools.Method(type, method);
            if (m == null) { Plugin.Log.LogWarning("Ship screen: " + type.Name + "." + method + " not found"); return; }
            h.Patch(m, new HarmonyMethod(typeof(ShipCoop), prefix), null, null, null, null);
        }

        private static int Index(global::ShipPanelScript p) { return (int)indexField.GetValue(p); }
        private static bool Locked(global::ShipPanelScript p) { return (bool)lockedGet.Invoke(p, null); }

        // ------------------------------------------------------------ patches

        /// Player 2's own devices are Rewired player 0 like on any PC, but
        /// his panel is slot 1: swap which Rewired player each panel reads.
        private static bool PanelOnEnablePrefix(global::ShipPanelScript __instance)
        {
            if (!(Session.Connected && Session.Role == Role.Client && !Lockstep.InMatch)) return true;
            int rewiredId = Index(__instance) == 1 ? 0 : 1;
            Rewired.Player player = Rewired.ReInput.players.GetPlayer(rewiredId);
            playerField.SetValue(__instance, player);
            player.controllers.ControllerAddedEvent += (Action<Rewired.ControllerAssignmentChangedEventArgs>)
                Delegate.CreateDelegate(typeof(Action<Rewired.ControllerAssignmentChangedEventArgs>), __instance, onControllerAdded);
            player.controllers.AddLastActiveControllerChangedDelegate((Rewired.PlayerActiveControllerChangedDelegate)
                Delegate.CreateDelegate(typeof(Rewired.PlayerActiveControllerChangedDelegate), __instance, onControllerChanged));
            return false;
        }

        /// The partner's panel reads Rewired player 1 on both PCs: it is
        /// "joined" when the partner says so, never by a local controller.
        private static bool IsPlayerAssignedPrefix(Rewired.Player __0, ref bool __result)
        {
            if (!Active || __0 == null) return true;
            // Our own panel is in from the start: no "press fire to join" online.
            if (__0.id == 0) { __result = true; return false; }
            if (__0.id != 1) return true;
            __result = remoteJoined;
            return false;
        }

        /// Both panels must be ready; only the host starts the level.
        /// Esc with no ship joined leaves the co-op game.
        private static bool PlayMenuUpdatePrefix(global::ReclayedPlayMenu __instance)
        {
            if (InviteSlot.Waiting)
            {
                // Ready before the friend is here: wait, do not start alone.
                global::ShipPanelScript me = menuP0.GetValue(__instance) as global::ShipPanelScript;
                if (me != null && Locked(me)) { G.shipsSelected = false; return false; }
                return true;
            }
            if (!Active) return true;
            global::ShipPanelScript p0 = menuP0.GetValue(__instance) as global::ShipPanelScript;
            global::ShipPanelScript p1 = menuP1.GetValue(__instance) as global::ShipPanelScript;
            if (p0 == null || p1 == null) return true;
            global::ShipPanelScript local = Lockstep.LocalSlot == 0 ? p0 : p1;

            bool localJoined = Rewired.ReInput.players.GetPlayer(0) != null &&
                               global::PlayerAssignmentManager.IsPlayerAssigned(Rewired.ReInput.players.GetPlayer(0));

            bool ready = localJoined && Locked(local) && PeerReady;
            G.shipsSelected = ready;
            if (ready && Session.Role == Role.Host && !LoadingScreen.IsLoading)
            {
                CanvasGroup cg = menuCanvas.GetValue(__instance) as CanvasGroup;
                if (cg != null) { cg.interactable = false; cg.blocksRaycasts = false; }
                __instance.enabled = false;
                LoadingScreen.Load(BuildScenes.Gameplay, null, false, false);
            }
            return false;
        }

        // ------------------------------------------------------------ sync

        /// Every frame: report our panel when it changes (and once a second).
        internal static void Tick()
        {
            if (!Active) return;
            int slot = Lockstep.LocalSlot;
            Rewired.Player me = Rewired.ReInput.players.GetPlayer(0);
            bool joined = me != null && global::PlayerAssignmentManager.IsPlayerAssigned(me);
            global::ShipPanelScript local = FindPanel(slot);
            bool locked = local != null && Locked(local);
            string ship = Get(G.playerShips, slot);
            string skin = Get(G.playerSkin, slot);
            string state = joined + "|" + locked + "|" + ship + "|" + skin;
            if (state == sentState && Time.realtimeSinceStartup < nextResend) return;
            sentState = state;
            nextResend = Time.realtimeSinceStartup + 1f;
            Session.SendShipState(joined, locked, ship, skin);
        }

        internal static void OnRemoteState(bool joined, bool locked, string ship, string skin)
        {
            int slot = 1 - Lockstep.LocalSlot;
            bool changed = joined != remoteJoined || locked != remoteLocked || ship != remoteShip || skin != remoteSkin;
            remoteJoined = joined;
            remoteLocked = locked;
            remoteShip = ship ?? "";
            remoteSkin = skin ?? "";
            if (Session.Role == Role.Host)
            {
                Session.PeerShip = remoteShip;
                Session.PeerSkin = remoteSkin;
            }
            if (!changed || !InShipScene) return;
            if (remoteShip.Length > 0 && G.playerShips != null) G.playerShips[slot] = remoteShip;
            if (remoteSkin.Length > 0 && G.playerSkin != null) G.playerSkin[slot] = remoteSkin;
            global::ShipPanelScript panel = FindPanel(slot);
            if (panel == null) return;
            try
            {
                if (Locked(panel) != locked)
                {
                    lockedSet.Invoke(panel, new object[] { locked });
                    Audio.PlaySound(locked ? "s_ui_ready" : "s_ui_unready", true, true);
                }
                if (joined) updateDisplay.Invoke(panel, new object[] { null });
            }
            catch (Exception e) { Plugin.Log.LogError("Partner ship panel: " + e); }
        }

        private static global::ShipPanelScript FindPanel(int slot)
        {
            global::ShipPanelScript[] panels = UnityEngine.Object.FindObjectsOfType<global::ShipPanelScript>();
            for (int i = 0; i < panels.Length; i++)
                if (Index(panels[i]) == slot) return panels[i];
            return null;
        }

        private static string Get(Dictionary<int, string> d, int slot)
        {
            string v;
            return d != null && d.TryGetValue(slot, out v) && v != null ? v : "";
        }
    }
}
