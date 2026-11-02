using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Platypus.Core;
using UnityEngine;
using G = Platypus.Core.Globals;

namespace ReclayedCoop
{
    /// Deterministic lockstep over Unity's fixed update. A "tick" starts in the
    /// first patched FixedUpdate of a physics step and ends after that step
    /// (WaitForFixedUpdate). Before any game code of tick t runs, both
    /// players' inputs for t are known on both machines.
    internal static class Lockstep
    {
        internal struct Frame
        {
            public sbyte H;
            public sbyte V;
            public byte Flags;
        }

        internal const byte Fire = 1;
        internal const byte Submit = 2;
        internal const byte PauseFlag = 4;
        internal const int HashGroups = 4;
        internal static readonly string[] HashGroupNames = { "globals", "players", "entities", "random" };

        internal static bool InMatch;      // between Start and the end of the session's match
        internal static bool InTick;       // inside a fixed step that ran a lockstep tick
        internal static bool Frozen;       // a scene change was requested inside a tick
        internal static bool InternalLoad; // our own LoadingScreen.Load call
        internal static bool GameplayScene;
        internal static int Tick;          // next tick index (during tick t this is t + 1)
        internal static int Delay;
        internal static int MatchGen;
        internal static double SimTime = 100.0;
        internal static double LevelStartSim = 100.0;
        internal static string Warning = "";
        internal static bool Stalled;       // waiting for the partner's input: simulation paused
        internal static float StalledSince;
        internal static int LastRemoteTick = -1;
        private static int preparedTick = -1;

        private static float lastFixedTime = -1f;
        private static bool longWait;
        private static bool failing;
        private static global::LevelManager level;
        private static global::LevelManager frozenLevel;
        private static global::PlayerManager playerManager;

        private static readonly Dictionary<int, Frame> localFrames = new Dictionary<int, Frame>();
        private static readonly Dictionary<int, Frame> remoteFrames = new Dictionary<int, Frame>();
        private static readonly Dictionary<int, uint[]> localHashes = new Dictionary<int, uint[]>();
        private static readonly Dictionary<int, uint[]> remoteHashes = new Dictionary<int, uint[]>();
        private static readonly List<int> scratch = new List<int>();

        internal static readonly Frame[] Cur = new Frame[2];
        internal static readonly byte[] Edge = new byte[2];
        private static readonly byte[] prevFlags = new byte[2];
        private static bool latchFire;
        private static bool latchSubmit;
        private static bool latchPause;
        private static bool pauseAtStepEnd;

        internal static int LocalSlot { get { return Session.Role == Role.Client ? 1 : 0; } }
        internal static global::PlayerManager PM { get { return playerManager; } }
        internal static global::LevelManager LM { get { return level; } }

        // ------------------------------------------------------------ match lifecycle

        internal static void BeginMatch(int seed, int delay, int gen)
        {
            InMatch = true;
            InTick = false;
            Frozen = false;
            frozenLevel = null;
            Tick = 0;
            preparedTick = -1;
            LastRemoteTick = -1;
            Stalled = false;
            Delay = delay;
            MatchGen = gen;
            SimTime = 100.0;
            LevelStartSim = SimTime;
            lastFixedTime = -1f;
            longWait = true;
            Warning = "";
            localFrames.Clear();
            remoteFrames.Clear();
            localHashes.Clear();
            remoteHashes.Clear();
            for (int i = 0; i < 2; i++) { Cur[i] = default(Frame); Edge[i] = 0; prevFlags[i] = 0; }
            latchFire = false;
            latchSubmit = false;
            Rng.Init(seed);
            DesyncDump.Reset();
            Clock.Reset();
            Application.runInBackground = true;
            Plugin.Log.LogInfo("Match " + gen + " begins: seed=" + seed + " delay=" + delay + " frames, you are player " + (LocalSlot + 1));
        }

        internal static void EndMatch()
        {
            if (InMatch) Plugin.Log.LogInfo("Match ends at tick " + Tick);
            InTick = false;
            Rng.Reset();
            InMatch = false;
            Stalled = false;
            Frozen = false;
            frozenLevel = null;
            localFrames.Clear();
            remoteFrames.Clear();
            localHashes.Clear();
            remoteHashes.Clear();
            Clock.Reset();
        }

        internal static void Fail(string reason)
        {
            Fail(reason, true);
        }

        internal static void Fail(string reason, bool sendBye)
        {
            if (failing) return;
            failing = true;
            try
            {
                Plugin.Log.LogWarning("Session ended: " + reason);
                SharedPause.Clear();
                Ui.ShowMessage(reason);
                bool wasMatch = InMatch;
                EndMatch();
                Session.Close(reason, sendBye);
                if (wasMatch || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "MainMenuScene")
                    LoadScene(BuildScenes.MainMenu, null);
            }
            finally
            {
                failing = false;
            }
        }

        internal static void LoadScene(BuildScenes scene, Snapshot snapshot)
        {
            InternalLoad = true;
            try { LoadingScreen.Load(scene, snapshot, false, false); }
            catch (Exception e) { Plugin.Log.LogError("Load " + scene + ": " + e); }
            finally { InternalLoad = false; }
        }

        /// Host: the host's game is about to load a level from outside the
        /// simulation (menus). Returns the snapshot both games will load.
        internal static Snapshot HostStart(Snapshot requested)
        {
            string json = requested != null ? requested.ToJSON() : CaptureSnapshot();
            if (string.IsNullOrEmpty(json))
            {
                Fail("Could not capture the game state for player 2.");
                return requested;
            }
            int seed = (int)(DateTime.UtcNow.Ticks & 0x7fffffff) ^ 0x2545F491;
            int delay = ChooseDelay();
            int gen = (MatchGen + 1) & 0xff;
            Session.SendStart(seed, delay, gen, json);
            if (!Session.IsOpen) return requested;
            BeginMatch(seed, delay, gen);
            return Snapshot.FromJSON(json);
        }

        private static string CaptureSnapshot()
        {
            const string name = "reclayedcoop_start";
            try
            {
                // Player 2: requested ship/skin, otherwise the host's choice
                // (the game's DeduplicateP2Skin then picks a different colour).
                FillSecondSlot(G.playerShips, Session.PeerShip);
                FillSecondSlot(G.playerSkin, Session.PeerSkin);
                SnapshotSaveManager.CreateGameSnapshot(name);
                Snapshot s = SnapshotSaveManager.GetSnapshot(name);
                string json = s != null ? s.ToJSON() : null;
                try { SnapshotSaveManager.DeleteSnapshot(name); } catch (Exception) { }
                return json;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("CaptureSnapshot: " + e);
                return null;
            }
        }

        private static void FillSecondSlot(Dictionary<int, string> slots, string requested)
        {
            if (slots == null) return;
            if (!string.IsNullOrEmpty(requested)) { slots[1] = requested; return; }
            string current;
            if (slots.TryGetValue(1, out current) && !string.IsNullOrEmpty(current)) return;
            string first;
            if (slots.TryGetValue(0, out first) && !string.IsNullOrEmpty(first)) slots[1] = first;
        }

        private static int ChooseDelay()
        {
            int configured = Plugin.CfgDelay.Value;
            if (configured >= 0) return Math.Min(configured, 15);
            if (Session.RttMs <= 0) return 2;
            int frames = (int)Math.Ceiling(Session.RttMs / 2.0 / 16.6667) + 1;
            return Math.Max(1, Math.Min(10, frames));
        }

        internal static void ClientStart(int seed, int delay, int gen, string json)
        {
            if (Session.Role != Role.Client) return;
            BeginMatch(seed, delay, gen);
            LoadScene(BuildScenes.Gameplay, Snapshot.FromJSON(json));
        }

        internal static void ClientGoto(int scene)
        {
            if (Session.Role != Role.Client) return;
            if (scene == (int)BuildScenes.MainMenu)
            {
                if (!InMatch) return;
                EndMatch();
                Session.Status = "The host returned to the menu. Still connected - waiting for the next level...";
                LoadScene(BuildScenes.MainMenu, null);
                return;
            }
            if (!InMatch) return;
            LoadScene((BuildScenes)scene, null);
        }

        // ------------------------------------------------------------ scenes

        /// The main menu plays a demo level in the background; it has a
        /// LevelManager too but is never part of a match.
        internal static bool IsMenuLevel(global::LevelManager lm)
        {
            return lm == null || lm.gameObject.scene.name == "MainMenuScene";
        }

        internal static void OnLevelStart(global::LevelManager lm)
        {
            if (lm == level || IsMenuLevel(lm)) return;
            level = lm;
            if (Frozen && lm != frozenLevel) { Frozen = false; frozenLevel = null; }
            GameplayScene = true;
            if (InMatch)
            {
                longWait = true;
                lastFixedTime = -1f;
                LevelStartSim = SimTime;
                Plugin.Log.LogInfo("Level started at tick " + Tick);
            }
        }

        internal static void OnPlayerManagerStart(global::PlayerManager pm)
        {
            playerManager = pm;
        }

        internal static void OnSceneChanged(string what)
        {
            global::LevelManager lm = UnityEngine.Object.FindObjectOfType<global::LevelManager>();
            if (IsMenuLevel(lm))
            {
                level = null;
                playerManager = null;
                GameplayScene = false;
                if (Frozen) { Frozen = false; frozenLevel = null; }
            }
            else if (lm != level)
            {
                OnLevelStart(lm);
                if (playerManager == null) playerManager = UnityEngine.Object.FindObjectOfType<global::PlayerManager>();
            }
            if (InMatch) Plugin.Log.LogInfo("Scene " + what + ": gameplay=" + GameplayScene + " tick=" + Tick);
        }

        /// A level asked to change scene inside a tick. That happens on both
        /// machines at the same tick, so let it through but stop simulating
        /// the old level until the new one starts.
        internal static void FreezeForTransition()
        {
            if (Frozen) return;
            Frozen = true;
            frozenLevel = level;
            Plugin.Log.LogInfo("Scene change requested at tick " + (Tick - 1));
        }

        // ------------------------------------------------------------ ticks

        /// Prefix of every simulation FixedUpdate.
        internal static void BeginTickIfNeeded()
        {
            if (!InMatch || !GameplayScene || Frozen) return;
            float fixedTime = Time.fixedTime;
            if (InTick)
            {
                if (fixedTime == lastFixedTime) return;
                EndTick(); // the end-of-step hook did not run; recover
            }
            lastFixedTime = fixedTime;
            try
            {
                RunTick();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Tick " + Tick + ": " + e);
                Fail("Internal error in the co-op mod: " + e.Message);
            }
        }

        internal static void EndOfFixedStep()
        {
            if (InTick) EndTick();
            if (pauseAtStepEnd) { pauseAtStepEnd = false; if (InMatch) SharedPause.Enter(); }
        }

        private static void EndTick()
        {
            InTick = false;
        }

        private static void RunTick()
        {
            Rng.Enter();
            try { RunTickBody(); }
            finally { Rng.Exit(); }
        }

        private static void RunTickBody()
        {
            InTick = true;
            int t = Tick;
            uint[] hash = null;
            if (preparedTick != t)
            {
                // Once per tick, even if the partner's input arrives steps later.
                preparedTick = t;
                SimTime += Time.fixedDeltaTime;
                DesyncDump.Begin(t);
                hash = Hash.Compute();
                DesyncDump.Players(Hash.Describe(hash));
                DesyncDump.End();
                localHashes[t] = hash;
                Frame sampled = AutoTest.Active ? AutoTest.Input(t) : SampleLocal();
                localFrames[t + Delay] = sampled;
                Session.SendInput(t + Delay, sampled, t, hash);
                if (!InMatch) return;
            }
            else hash = localHashes.ContainsKey(t) ? localHashes[t] : Hash.Compute();

            Frame own;
            Frame other;
            if (!localFrames.TryGetValue(t, out own)) own = default(Frame);
            if (t < Delay) other = default(Frame);
            else if (!WaitRemote(t, out other))
            {
                // Pause the simulation for this physics step; the game keeps
                // drawing and the overlay says what we are waiting for.
                if (!Stalled) { Stalled = true; StalledSince = Time.realtimeSinceStartup; }
                return;
            }
            if (Stalled)
            {
                Stalled = false;
                Plugin.Log.LogInfo("Resumed at tick " + t + " after " + (int)((Time.realtimeSinceStartup - StalledSince) * 1000) + " ms");
            }

            int ls = LocalSlot;
            Cur[ls] = own;
            Cur[1 - ls] = other;
            for (int s = 0; s < 2; s++)
            {
                Edge[s] = (byte)(Cur[s].Flags & ~prevFlags[s]);
                prevFlags[s] = Cur[s].Flags;
            }

            CompareHashes();
            if (!InMatch) return;

            Tick = t + 1;
            Patches.PreTick();
            if (((Edge[0] | Edge[1]) & PauseFlag) != 0) pauseAtStepEnd = true;

            if (t % 600 == 0)
                Plugin.Log.LogInfo("SYNC tick=" + t + " " + Hash.Describe(hash));
            if (t % 120 == 0) Trim(t - 600);
        }

        private static bool WaitRemote(int t, out Frame f)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            // Short blocking wait for normal jitter; longer gaps stall the
            // step so the game stays responsive (see RunTick).
            long limit = Stalled ? 5 : 150;
            while (!remoteFrames.TryGetValue(t, out f))
            {
                if (!InMatch || !Session.IsOpen) return false;
                Session.Receive(2000);
                if (sw.ElapsedMilliseconds > limit) return false;
            }
            if (sw.ElapsedMilliseconds > 250) Plugin.Log.LogInfo("Waited " + sw.ElapsedMilliseconds + " ms for tick " + t);
            longWait = false;
            return true;
        }

        internal static void OnRemoteInput(int gen, int tick, Frame f, int hashTick, uint[] hash)
        {
            if (!InMatch || gen != MatchGen) return;
            remoteFrames[tick] = f;
            if (tick > LastRemoteTick) LastRemoteTick = tick;
            if (hashTick >= 0) remoteHashes[hashTick] = hash;
        }

        internal static void SendResume()
        {
            Session.SendResume(MatchGen, LastRemoteTick);
        }

        /// The partner is back and has our input up to lastTick: resend the rest.
        internal static void OnResume(int gen, int lastTick)
        {
            if (!InMatch || gen != MatchGen)
            {
                Fail("Матч нельзя продолжить - второй игрок уже не в нём.");
                return;
            }
            uint[] none = new uint[HashGroups];
            List<int> ticks = new List<int>(localFrames.Keys);
            ticks.Sort();
            int sent = 0;
            foreach (int t in ticks)
            {
                if (t <= lastTick) continue;
                Session.SendInput(t, localFrames[t], -1, none);
                sent++;
            }
            Plugin.Log.LogInfo("Resume: partner had tick " + lastTick + ", resent " + sent + " input frame(s)");
        }

        private static void CompareHashes()
        {
            int badTick = -1;
            int badGroup = -1;
            scratch.Clear();
            foreach (KeyValuePair<int, uint[]> kv in remoteHashes)
            {
                uint[] mine;
                if (!localHashes.TryGetValue(kv.Key, out mine)) continue;
                scratch.Add(kv.Key);
                if (badTick >= 0) continue;
                for (int g = 0; g < HashGroups; g++)
                {
                    if (mine[g] != kv.Value[g]) { badTick = kv.Key; badGroup = g; break; }
                }
            }
            foreach (int k in scratch) remoteHashes.Remove(k);
            if (badTick < 0) return;

            string text = "Desync at tick " + badTick + " (" + HashGroupNames[badGroup] + ").";
            Plugin.Log.LogWarning(text + " Local now: " + Hash.Describe(Hash.Compute()));
            DesyncDump.Write(badTick, "found the desync");
            Session.SendDumpRequest(badTick);
            if (Plugin.CfgStopOnDesync.Value) Fail(text + " The two games no longer match.");
            else Warning = text;
        }

        private static void Trim(int before)
        {
            TrimDict(localFrames, before);
            TrimDict(remoteFrames, before);
            TrimDict(localHashes, before);
            TrimDict(remoteHashes, before);
        }

        private static void TrimDict<T>(Dictionary<int, T> d, int before)
        {
            scratch.Clear();
            foreach (int k in d.Keys) if (k < before) scratch.Add(k);
            foreach (int k in scratch) d.Remove(k);
        }

        // ------------------------------------------------------------ local input

        /// Every rendered frame: remember presses that might fall between ticks.
        internal static void LatchLocalInput()
        {
            if (!InMatch || !Rewired.ReInput.isReady) return;
            Rewired.ReInput.PlayerHelper players = Rewired.ReInput.players;
            for (int i = 0; i < players.playerCount; i++)
            {
                Rewired.Player p = players.GetPlayer(i);
                if (p != null && p.GetButtonDown("Fire")) latchFire = true;
            }
            Rewired.Player sys = players.SystemPlayer;
            if (sys != null && sys.GetButtonDown("UISubmit")) latchSubmit = true;
            if (sys != null && sys.GetButtonDown("Pause"))
            {
                if (G.paused) SharedPause.ResumeLocal();
                else if (GameplayScene) latchPause = true;
            }
        }

        private static Frame SampleLocal()
        {
            float x = 0f;
            float y = 0f;
            bool fire = latchFire;
            bool submit = latchSubmit;
            bool pause = latchPause;
            latchFire = false;
            latchSubmit = false;
            latchPause = false;
            if (Rewired.ReInput.isReady)
            {
                Rewired.ReInput.PlayerHelper players = Rewired.ReInput.players;
                for (int i = 0; i < players.playerCount; i++)
                {
                    Rewired.Player p = players.GetPlayer(i);
                    if (p == null) continue;
                    x += p.GetAxis("MoveHorizontal");
                    y += p.GetAxis("MoveVertical");
                    if (p.GetButton("Fire")) fire = true;
                }
                Rewired.Player sys = players.SystemPlayer;
                if (sys != null && sys.GetButton("UISubmit")) submit = true;
                Rewired.Keyboard kb = Rewired.ReInput.controllers.Keyboard;
                if (kb != null)
                {
                    if (kb.GetKey(KeyCode.LeftArrow) || kb.GetKey(KeyCode.A)) x -= 1f;
                    if (kb.GetKey(KeyCode.RightArrow) || kb.GetKey(KeyCode.D)) x += 1f;
                    if (kb.GetKey(KeyCode.UpArrow) || kb.GetKey(KeyCode.W)) y += 1f;
                    if (kb.GetKey(KeyCode.DownArrow) || kb.GetKey(KeyCode.S)) y -= 1f;
                    if (kb.GetKey(KeyCode.Space) || kb.GetKey(KeyCode.LeftControl) || kb.GetKey(KeyCode.RightControl)) fire = true;
                    if (kb.GetKey(KeyCode.Return) || kb.GetKey(KeyCode.KeypadEnter)) submit = true;
                }
            }
            Frame f;
            f.H = Quantize(x);
            f.V = Quantize(y);
            f.Flags = (byte)((fire ? Fire : 0) | (submit ? Submit : 0) | (pause ? PauseFlag : 0));
            return f;
        }

        private static sbyte Quantize(float v)
        {
            if (v > 1f) v = 1f;
            if (v < -1f) v = -1f;
            if (Mathf.Abs(v) < 0.2f) return 0;
            return (sbyte)Mathf.RoundToInt(v * 127f);
        }

        internal static float Axis(int slot, bool vertical)
        {
            Frame f = Cur[slot & 1];
            return (vertical ? f.V : f.H) / 127f;
        }

        internal static bool FireHeld(int slot)
        {
            return (Cur[slot & 1].Flags & Fire) != 0;
        }
    }

    /// Keeps the simulation's UnityEngine.Random stream apart from everything
    /// else (sound pitch, menus, UI letters), so only simulation code advances it.
    internal static class Rng
    {
        private static UnityEngine.Random.State sim;
        private static UnityEngine.Random.State outer;
        private static int depth;

        internal static bool InSim { get { return depth > 0; } }

        internal static void Init(int seed)
        {
            UnityEngine.Random.State keep = UnityEngine.Random.state;
            UnityEngine.Random.InitState(seed);
            sim = UnityEngine.Random.state;
            UnityEngine.Random.state = keep;
            depth = 0;
        }

        internal static void Enter()
        {
            if (!Lockstep.InMatch) return;
            if (depth++ == 0)
            {
                outer = UnityEngine.Random.state;
                UnityEngine.Random.state = sim;
            }
        }

        internal static void Exit()
        {
            if (depth == 0) return;
            if (--depth == 0)
            {
                sim = UnityEngine.Random.state;
                UnityEngine.Random.state = outer;
            }
        }

        internal static void Reset()
        {
            if (depth > 0) UnityEngine.Random.state = outer;
            depth = 0;
        }

        internal static unsafe uint Hash()
        {
            UnityEngine.Random.State s = depth > 0 ? UnityEngine.Random.state : sim;
            uint* p = (uint*)&s;
            uint h = 2166136261u;
            int words = sizeof(UnityEngine.Random.State) / 4;
            for (int i = 0; i < words; i++) h = ReclayedCoop.Hash.Mix(h, p[i]);
            return h;
        }
    }

    /// Simulation clock: replaces Time.time in game code during a match so
    /// timers depend on the tick count, not on each PC's frame timing.
    internal static class Clock
    {
        private static readonly Dictionary<object, int> waits = new Dictionary<object, int>();
        [ThreadStatic] internal static object CurrentIterator;

        internal static bool SimContext { get { return Lockstep.InTick || Rng.InSim; } }

        internal static void Reset()
        {
            waits.Clear();
            CurrentIterator = null;
        }

        internal static float GetTime()
        {
            if (!Lockstep.InMatch) return Time.time;
            if (SimContext) return (float)Lockstep.SimTime;
            float frac = Time.time - Time.fixedTime;
            if (frac < 0f) frac = 0f;
            if (frac > 0.1f) frac = 0.1f;
            return (float)Lockstep.SimTime + frac; // visual interpolation only
        }

        internal static float GetTimeSinceLevelLoad()
        {
            if (!Lockstep.InMatch) return Time.timeSinceLevelLoad;
            return (float)(Lockstep.SimTime - Lockstep.LevelStartSim);
        }

        /// Replaces "new WaitForSeconds(x)" in Player.Respawn. Inside a match the
        /// coroutine resumes after the fixed step in which the tick target is
        /// reached (see Patches.RespawnMoveNextPrefix), identically on both PCs.
        internal static object RespawnWait(float seconds)
        {
            if (!Lockstep.InMatch || CurrentIterator == null) return new WaitForSeconds(seconds);
            int ticks = Mathf.Max(1, Mathf.CeilToInt(seconds * 60f - 0.001f));
            waits[CurrentIterator] = Lockstep.Tick + ticks;
            return new WaitForFixedUpdate();
        }

        internal static bool StillWaiting(object iterator)
        {
            int target;
            if (!waits.TryGetValue(iterator, out target)) return false;
            if (Lockstep.InMatch && Lockstep.Tick < target) return true;
            waits.Remove(iterator);
            return false;
        }
    }

    internal static class Hash
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] public float F;
            [FieldOffset(0)] public uint U;
        }

        internal static uint Mix(uint h, uint v)
        {
            h ^= v;
            h *= 16777619u;
            h ^= h >> 15;
            return h;
        }

        internal static uint Bits(float f)
        {
            FloatBits b;
            b.U = 0;
            b.F = f;
            return b.U;
        }

        internal static uint[] Compute()
        {
            uint[] r = new uint[Lockstep.HashGroups];
            r[0] = GlobalsHash();
            r[1] = PlayersHash();
            r[2] = EntitiesHash();
            r[3] = Rng.Hash();
            return r;
        }

        private static uint GlobalsHash()
        {
            uint h = 2166136261u;
            h = Mix(h, (uint)G.levelLifetime);
            h = Mix(h, (uint)G.deathCounter);
            h = Mix(h, (uint)G.credits);
            h = Mix(h, (uint)G.continues);
            h = Mix(h, (uint)G.bonus);
            h = Mix(h, (uint)G.endBonus);
            h = Mix(h, (uint)G.bossKillSpeedTimer);
            h = Mix(h, (uint)G.currentArea);
            h = Mix(h, (uint)(int)G.Difficulty);
            h = Mix(h, (uint)(int)G.currentPlayMode);
            h = Mix(h, Bits(G.vertscroll));
            return h;
        }

        private static uint PlayersHash()
        {
            global::PlayerManager pm = Lockstep.PM;
            if (pm == null || pm.m_players == null) return 0;
            uint h = 2166136261u;
            foreach (global::Player p in pm.m_players)
            {
                if (p == null) { h = Mix(h, 0xdeadu); continue; }
                h = Mix(h, p.IsActive ? 1u : 0u);
                h = Mix(h, (uint)p.Lives);
                h = Mix(h, (uint)p.Score);
                h = Mix(h, (uint)(int)p.CurrentWeapon);
                h = Mix(h, (uint)p.PodCount);
                h = Mix(h, (uint)p.ShieldCount);
                Vector2 np = Patches.PlayerNextPosition(p);
                h = Mix(h, Bits(np.x));
                h = Mix(h, Bits(np.y));
            }
            return h;
        }

        private static uint EntitiesHash()
        {
            global::EntityManager em = global::EntityManager.Instance;
            if (em == null) return 0;
            Dictionary<int, global::BaseEntity> table = Patches.EntityTable(em);
            if (table == null) return 1;
            uint h = 2166136261u;
            h = Mix(h, (uint)table.Count);
            foreach (KeyValuePair<int, global::BaseEntity> kv in table)
            {
                h = Mix(h, (uint)kv.Key);
                global::BaseEntity e = kv.Value;
                if (e == null) { h = Mix(h, 1u); continue; }
                Vector3 pos = Patches.EntityNextPosition(e);
                DesyncDump.Entity(kv.Key, e, pos);
                h = Mix(h, Bits(pos.x));
                h = Mix(h, Bits(pos.y));
                h = Mix(h, Bits(e.hitPoints));
                h = Mix(h, (uint)e.lifetime);
            }
            return h;
        }

        internal static string Describe(uint[] h)
        {
            string s = "hash=" + h[0].ToString("x8") + "/" + h[1].ToString("x8") + "/" + h[2].ToString("x8") + "/" + h[3].ToString("x8");
            s += " life=" + G.levelLifetime + " area=" + G.currentArea;
            global::PlayerManager pm = Lockstep.PM;
            if (pm != null && pm.m_players != null)
            {
                for (int i = 0; i < pm.m_players.Length; i++)
                {
                    global::Player p = pm.m_players[i];
                    if (p == null) continue;
                    Vector2 np = Patches.PlayerNextPosition(p);
                    s += " P" + (i + 1) + "=" + (p.IsActive ? "on" : "off") + "@" + np.x.ToString("0.00") + "," + np.y.ToString("0.00") + " lives=" + p.Lives + " score=" + p.Score;
                }
            }
            global::EntityManager em = global::EntityManager.Instance;
            if (em != null)
            {
                Dictionary<int, global::BaseEntity> table = Patches.EntityTable(em);
                if (table != null) s += " entities=" + table.Count;
            }
            return s;
        }
    }
}
