using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using G = Platypus.Core.Globals;

namespace ReclayedCoop
{
    /// Pause for both players with the game's own pause menu. Pause travels
    /// inside the input of a tick, so both games stop on the same tick;
    /// Resume is a message (the simulation is stopped, there are no ticks).
    internal static class SharedPause
    {
        private static bool fromPartner;

        internal static void ApplyPatches(Harmony h)
        {
            Prefix(h, "OnResumePressed", "ResumePrefix");
            Prefix(h, "OnQuitPressed", "QuitPrefix");
            Prefix(h, "OnRestartGamePressed", "RestartPrefix");
        }

        private static void Prefix(Harmony h, string method, string prefix)
        {
            MethodInfo m = AccessTools.Method(typeof(global::PauseMenu), method);
            if (m == null) { Plugin.Log.LogWarning("PauseMenu." + method + " not found"); return; }
            h.Patch(m, new HarmonyMethod(typeof(SharedPause), prefix), null, null, null, null);
        }

        /// End of the physics step in which either player's input said Pause.
        internal static void Enter()
        {
            if (G.paused) return;
            G.paused = true;
            Time.timeScale = 0f;
            Audio.Pause();
            Plugin.Log.LogInfo("Shared pause at tick " + Lockstep.Tick);
        }

        /// Local Pause / Esc while the menu is open = Resume for both.
        internal static void ResumeLocal()
        {
            global::PauseMenu menu = UnityEngine.Object.FindObjectOfType<global::PauseMenu>();
            MethodInfo resume = AccessTools.Method(typeof(global::PauseMenu), "OnResumePressed");
            if (menu != null && resume != null) resume.Invoke(menu, null);
            else Clear();
        }

        internal static void OnPartnerResume()
        {
            if (!G.paused) return;
            fromPartner = true;
            try { ResumeLocal(); }
            finally { fromPartner = false; }
        }

        /// Without the menu (leaving, errors): make sure the game runs again.
        internal static void Clear()
        {
            if (!G.paused && Time.timeScale != 0f) return;
            G.paused = false;
            Time.timeScale = 1f;
            Audio.Unpause();
        }

        private static bool ResumePrefix()
        {
            if (Lockstep.InMatch && !fromPartner && Session.Connected) Session.SendUnpause();
            return true;
        }

        /// QUIT in the pause menu = leaving the co-op game.
        private static bool QuitPrefix()
        {
            if (!Lockstep.InMatch) return true;
            Clear();
            MenuFlow.LeaveCoop();
            return false;
        }

        /// RESTART would restart one game only.
        private static bool RestartPrefix()
        {
            return !Lockstep.InMatch;
        }
    }
}
