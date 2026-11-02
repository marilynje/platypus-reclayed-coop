using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ReclayedCoop
{
    /// --coop-autotest (with --coop-host / --coop-join): both games go through
    /// the menus and play by themselves, so a match can run unattended and
    /// the logs / desync dumps show whether the two simulations stay equal.
    internal static class AutoTest
    {
        internal static readonly bool Active = Array.IndexOf(Environment.GetCommandLineArgs(), "--coop-autotest") >= 0;
        /// --coop-shiptest: the host goes straight from the main menu to the
        /// ship screen (to look at the invite button without clicking through).
        internal static readonly bool ShipTest = Array.IndexOf(Environment.GetCommandLineArgs(), "--coop-shiptest") >= 0;
        private static bool shipTestDone;

        private static readonly MethodInfo submit = AccessTools.Method(typeof(global::ShipPanelScript), "OnNavSubmit");
        private static readonly MethodInfo lockedGet = AccessTools.PropertyGetter(typeof(global::ShipPanelScript), "IsLockedIn");
        private static readonly FieldInfo indexField = AccessTools.Field(typeof(global::ShipPanelScript), "playerIndex");
        private static readonly bool steamHost = Array.IndexOf(Environment.GetCommandLineArgs(), "--coop-steamhost") >= 0;
        private static bool steamTried;
        private static float pausedAt = -1f;
        private static float difficultyAt = -1f;
        private static float lockAt = -1f;

        internal static void ApplyPatches(Harmony h)
        {
            if (!Active) return;
            MethodInfo assigned = AccessTools.Method(typeof(global::PlayerAssignmentManager), "IsPlayerAssigned");
            h.Patch(assigned, new HarmonyMethod(typeof(AutoTest), "AssignedPrefix"), null, null, null, null);
            Plugin.Log.LogWarning("AUTOTEST: menus and input are driven by the mod");
        }

        /// Nobody presses Fire to join: the local player counts as joined.
        private static bool AssignedPrefix(Rewired.Player __0, ref bool __result)
        {
            if (__0 == null || __0.id != 0 || !ShipCoop.Active) return true;
            __result = true;
            return false;
        }

        /// The ship screen straight from the main menu still has the menu's
        /// background level (menu1_data, no enemies): take the first world.
        private static void FirstWorld()
        {
            try
            {
                object root = AccessTools.Field(typeof(global::ModManager), "modRoot").GetValue(null);
                Array panels = root != null ? AccessTools.Method(root.GetType(), "GetLevelPanels").Invoke(root, null) as Array : null;
                object first = panels != null && panels.Length > 0 ? panels.GetValue(0) : null;
                string level = first != null ? AccessTools.Field(first.GetType(), "mainLevel").GetValue(first) as string : null;
                if (string.IsNullOrEmpty(level)) { Plugin.Log.LogWarning("SHIPTEST: no world found"); return; }
                Platypus.Core.Globals.currentLevel = level;
                Platypus.Core.Globals.currentArea = 1;
                Plugin.Log.LogWarning("SHIPTEST: world " + level);
            }
            catch (Exception e) { Plugin.Log.LogWarning("SHIPTEST: world: " + e.Message); }
        }

        internal static void Tick()
        {
            if (ShipTest && !shipTestDone && Session.Role != Role.Client && !LoadingScreen.IsLoading &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenuScene" && Time.realtimeSinceStartup > 8f)
            {
                shipTestDone = true;
                InviteSlot.Intent = true;
                InviteSlot.IntentAt = float.MaxValue;
                FirstWorld();
                Plugin.Log.LogWarning("SHIPTEST: loading the ship screen");
                Lockstep.LoadScene(Platypus.Core.BuildScenes.ReclayedShipSelect, null);
            }
            if (steamHost && !steamTried && !Session.Busy && FindMenuReady())
            {
                steamTried = true;
                Plugin.Log.LogWarning("AUTOTEST: hosting through Steam");
                SteamLink.Host();
            }
            if (Active && Lockstep.InMatch && Platypus.Core.Globals.paused)
            {
                if (pausedAt < 0f) pausedAt = Time.realtimeSinceStartup;
                else if (Session.Role == Role.Host && Time.realtimeSinceStartup - pausedAt > 3f)
                {
                    Plugin.Log.LogWarning("AUTOTEST: host resumes");
                    pausedAt = -1f;
                    SharedPause.ResumeLocal();
                }
            }
            else pausedAt = -1f;
            if (!Active || !Session.Connected || Lockstep.InMatch) { difficultyAt = -1f; lockAt = -1f; return; }
            float now = Time.realtimeSinceStartup;

            if (Session.Role == Role.Host && !ShipCoop.InShipScene && !LoadingScreen.IsLoading)
            {
                global::DifficultyButton[] rows = UnityEngine.Object.FindObjectsOfType<global::DifficultyButton>();
                if (rows.Length > 0)
                {
                    if (difficultyAt < 0f) difficultyAt = now + 3f;
                    else if (now >= difficultyAt)
                    {
                        difficultyAt = float.MaxValue;
                        for (int i = 0; i < rows.Length; i++)
                        {
                            Button b = rows[i].GetComponent<Button>();
                            if (b == null || !b.interactable || !rows[i].gameObject.activeInHierarchy) continue;
                            Plugin.Log.LogWarning("AUTOTEST: host picks difficulty " + rows[i].name);
                            b.onClick.Invoke();
                            break;
                        }
                    }
                }
            }

            if (ShipCoop.Active && !LoadingScreen.IsLoading)
            {
                if (lockAt < 0f) lockAt = now + 3f + Lockstep.LocalSlot;
                else if (now >= lockAt)
                {
                    global::ShipPanelScript[] panels = UnityEngine.Object.FindObjectsOfType<global::ShipPanelScript>();
                    for (int i = 0; i < panels.Length; i++)
                    {
                        if ((int)indexField.GetValue(panels[i]) != Lockstep.LocalSlot) continue;
                        if ((bool)lockedGet.Invoke(panels[i], null)) continue;
                        Plugin.Log.LogWarning("AUTOTEST: ship ready");
                        submit.Invoke(panels[i], null);
                    }
                    lockAt = now + 2f;
                }
            }
        }

        private static bool FindMenuReady()
        {
            return Time.frameCount % 30 == 0 && SteamLink.Available &&
                   UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenuScene";
        }

        /// In the match: a fixed flight pattern per player, fire always held;
        /// the host presses Pause once at tick 900.
        internal static Lockstep.Frame Input(int tick)
        {
            int slot = Lockstep.LocalSlot;
            int phase = (tick / 45 + slot * 3) % 8;
            Lockstep.Frame f;
            f.H = (sbyte)(phase == 0 || phase == 1 ? 127 : phase == 4 || phase == 5 ? -127 : 0);
            f.V = (sbyte)(phase == 2 || phase == 3 ? 127 : phase == 6 || phase == 7 ? -127 : 0);
            bool fire = ((tick / 70 + slot) % 3) != 0; // not in step: firing changes ship speed
            f.Flags = (byte)((fire ? Lockstep.Fire : 0) | ((tick % 120) < 2 ? Lockstep.Submit : 0) | (slot == 0 && tick == 900 ? Lockstep.PauseFlag : 0));
            return f;
        }
    }
}
