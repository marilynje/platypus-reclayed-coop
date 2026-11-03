// Platypus Reclayed CO-OP: unofficial deterministic two-player netplay.
// Same idea as platypus-coop for the original game: both players run their
// own copy of the game; only per-tick input and state checksums cross the wire.
// Written in C# 5 so any C# compiler (VS Roslyn, .NET SDK or the .NET
// Framework csc.exe that ships with Windows) can build it.
using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReclayedCoop
{
    [BepInPlugin(Plugin.Guid, "Platypus Reclayed CO-OP", Plugin.Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "marilynje.platypus.reclayedcoop";
        public const string Version = "0.2.2";
        public const int Protocol = 7;

        internal static ManualLogSource Log;
        internal static ConfigEntry<int> CfgPort;
        internal static ConfigEntry<string> CfgAddress;
        internal static ConfigEntry<int> CfgDelay;
        internal static ConfigEntry<string> CfgShip;
        internal static ConfigEntry<string> CfgSkin;
        internal static ConfigEntry<bool> CfgStopOnDesync;
        internal static ConfigEntry<bool> CfgHud;
        internal static ConfigEntry<bool> CfgShowIp;

        private void Awake()
        {
            Log = Logger;
            CfgPort = Config.Bind("Network", "Port", 27833, "TCP port. The host must allow it through the firewall / router.");
            CfgAddress = Config.Bind("Network", "HostAddress", "127.0.0.1", "Last IPv4 address used for Join.");
            CfgDelay = Config.Bind("Network", "InputDelayFrames", -1, "Input buffer in 1/60 s frames. -1 = automatic from ping.");
            CfgShip = Config.Bind("Player2", "Ship", "", "Ship id the joining player wants (empty = game default for player 2).");
            CfgSkin = Config.Bind("Player2", "Skin", "", "Skin id the joining player wants (empty = game default).");
            CfgStopOnDesync = Config.Bind("Debug", "StopOnDesync", true, "End the session when the two games disagree.");
            CfgHud = Config.Bind("Interface", "ShowNetHud", false, "Show ping / delay line during a match.");
            CfgShowIp = Config.Bind("Interface", "ShowIpJoin", false, "Show 'Host by IP' / 'Join by IP' in the co-op menu. --coop-host / --coop-join work either way.");

            try
            {
                Patches.Apply(new Harmony(Guid));
                MenuFlow.ApplyPatches(new Harmony(Guid + ".menu"));
                ShipCoop.ApplyPatches(new Harmony(Guid + ".ships"));
                AutoTest.ApplyPatches(new Harmony(Guid + ".autotest"));
                SharedPause.ApplyPatches(new Harmony(Guid + ".pause"));
                CoopPanel.ApplyPatches(new Harmony(Guid + ".panel"));
                Loc.ApplyPatches(new Harmony(Guid + ".loc"));
                Lightning.ApplyPatches(new Harmony(Guid + ".lightning"));
            }
            catch (Exception e)
            {
                Log.LogError("Patching failed, mod disabled: " + e);
                return;
            }

            Addon.EnsureInstalled();
            EnsureRunner();

            Application.quitting += OnQuitting;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Log.LogInfo("Platypus Reclayed CO-OP " + Version + " loaded (lockstep, protocol " + Protocol + ")");
            Session.HandleCommandLine();
            SteamLink.HandleCommandLine();
        }

        private static Runner runner;

        /// Hidden objects do not get OnApplicationQuit: tell the partner here.
        private static void OnQuitting()
        {
            Session.Close("Partner closed the game.", true);
        }

        /// The game clears scene objects on its own (even DontDestroyOnLoad
        /// ones), so the runner is hidden from it and re-created if it goes.
        private static void EnsureRunner()
        {
            if (runner != null) return;
            GameObject go = new GameObject("ReclayedCoopRunner");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
            Log.LogInfo("Runner created");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Log.LogInfo("Scene loaded: " + scene.name);
            EnsureRunner();
            Lockstep.OnSceneChanged("loaded " + scene.name);
            ShipCoop.OnSceneLoaded(scene.name);
            MenuFlow.OnSceneLoaded(scene.name);
            InviteSlot.OnSceneLoaded();
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            Lockstep.OnSceneChanged("unloaded " + scene.name);
        }
    }

    /// Lives on its own DontDestroyOnLoad object: per-frame network pump,
    /// end-of-fixed-step hook and the IMGUI overlay.
    internal class Runner : MonoBehaviour
    {
        private bool updating;

        private void Start()
        {
            Plugin.Log.LogInfo("Runner started");
            StartCoroutine(EndOfFixedStepLoop());
        }

        private void OnDestroy()
        {
            Plugin.Log.LogWarning("Runner destroyed: " + Environment.StackTrace);
        }

        private IEnumerator EndOfFixedStepLoop()
        {
            WaitForFixedUpdate wait = new WaitForFixedUpdate();
            while (true)
            {
                yield return wait;
                try { Lockstep.EndOfFixedStep(); }
                catch (Exception e) { Plugin.Log.LogError("EndOfFixedStep: " + e); }
            }
        }

        private void Update()
        {
            if (!updating) { updating = true; Plugin.Log.LogInfo("Runner updating"); }
            try
            {
                Lockstep.LatchLocalInput();
                Session.Pump();
                MenuButton.Tick();
                MenuFlow.Tick();
                MenuFlow.FastTick();
                ShipCoop.Tick();
                InviteSlot.Tick();
                AutoTest.Tick();
                SteamLink.Tick();
                Overlay.Update();
                CoopPanel.Refresh();
                // Loading screens re-apply the game settings, which turns this
                // off: an unfocused game would stop and stall the partner.
                if (Session.Busy && !Application.runInBackground) Application.runInBackground = true;
            }
            catch (Exception e) { Plugin.Log.LogError("Update: " + e); }
        }

        private void OnGUI()
        {
            try
            {
                CoopPanel.OnGuiEvent(Event.current);
                Ui.Draw();
            }
            catch (Exception e) { Plugin.Log.LogError("OnGUI: " + e); }
        }

        private void OnApplicationQuit()
        {
            Session.Close("Partner closed the game.", true);
        }
    }
}
