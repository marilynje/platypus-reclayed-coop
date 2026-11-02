using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Platypus.Core;
using UnityEngine;
using G = Platypus.Core.Globals;

namespace ReclayedCoop
{
    /// All game hooks. Every patch is applied separately and logged; if a
    /// critical one fails (different game build) online play stays disabled.
    internal static class Patches
    {
        internal static bool Healthy = true;
        internal static string Problem = "";

        private static Harmony harmony;
        private static Assembly game;
        private static int applied;

        // Field accessors built with DynamicMethod (works with C# 5 compilers).
        private static Action<global::Player, float> setHorizontal;
        private static Action<global::Player, float> setVertical;
        private static Func<global::Player, List<Vector2>> getInputPool;
        private static Func<global::Player, Vector2> getNextPosition;
        private static Func<global::Player, bool> getIsActive;
        private static Func<global::EntityManager, Dictionary<int, global::BaseEntity>> getEntityTable;
        private static Func<global::BaseEntity, Vector3> getEntityNextPosition;
        private static Action<global::LevelManager, bool> setSkipGameOver;
        private static Func<global::Player, Rewired.Player> getRewiredPlayer;
        private static Action<global::Player, Rewired.Player> setRewiredPlayer;
        private static Func<global::BaseEntity, float> getEntityLastUpdate;
        private static Func<global::BaseEntity, float> getEntityNextUpdate;
        private static MethodInfo updateInvincibility;
        private static MethodInfo playerManagerUpdate;

        private static MethodInfo rewiredGetAxis;
        private static MethodInfo rewiredGetButton;
        private static MethodInfo timeGetTime;
        private static MethodInfo timeSinceLevelLoad;
        private static ConstructorInfo waitForSecondsCtor;

        private static bool allowPlayerManagerUpdate;
        private static readonly List<Rewired.Player> joinList = new List<Rewired.Player>();
        private static int simWraps;
        private static int respawnWraps;

        // ------------------------------------------------------------ setup

        internal static void Apply(Harmony h)
        {
            harmony = h;
            game = typeof(global::Player).Assembly;

            // Full game (1.4): Player.Update queues axes in inputPool and
            // UpdateMovement averages them. Demo: horizontalInput/verticalInput.
            FieldInfo inputPool = typeof(global::Player).GetField("inputPool", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (inputPool != null && inputPool.FieldType == typeof(List<Vector2>))
            {
                getInputPool = Getter<global::Player, List<Vector2>>("inputPool");
            }
            else
            {
                setHorizontal = Setter<global::Player, float>("horizontalInput");
                setVertical = Setter<global::Player, float>("verticalInput");
            }
            getNextPosition = Getter<global::Player, Vector2>("nextPosition");
            getIsActive = Getter<global::Player, bool>("isActive");
            getEntityTable = Getter<global::EntityManager, Dictionary<int, global::BaseEntity>>("entityIDTable");
            getEntityNextPosition = Getter<global::BaseEntity, Vector3>("m_nextPosition");
            setSkipGameOver = Setter<global::LevelManager, bool>("skipGameOverPressed");
            getRewiredPlayer = Getter<global::Player, Rewired.Player>("_rewiredPlayer");
            setRewiredPlayer = Setter<global::Player, Rewired.Player>("_rewiredPlayer");
            getEntityLastUpdate = Getter<global::BaseEntity, float>("m_lastUpdateTime");
            getEntityNextUpdate = Getter<global::BaseEntity, float>("m_nextUpdateTime");
            updateInvincibility = Find(typeof(global::Player), "UpdateInvincibility", Type.EmptyTypes);
            playerManagerUpdate = Find(typeof(global::PlayerManager), "Update", Type.EmptyTypes);

            rewiredGetAxis = Find(typeof(Rewired.Player), "GetAxis", new Type[] { typeof(string) });
            rewiredGetButton = Find(typeof(Rewired.Player), "GetButton", new Type[] { typeof(string) });
            timeGetTime = typeof(Time).GetProperty("time").GetGetMethod();
            timeSinceLevelLoad = typeof(Time).GetProperty("timeSinceLevelLoad").GetGetMethod();
            waitForSecondsCtor = typeof(WaitForSeconds).GetConstructor(new Type[] { typeof(float) });

            // 1. Tick gate: the first FixedUpdate of a physics step starts the tick.
            string[] fixedTypes = {
                "Player", "LevelManager", "EntityManager", "BaseEntity", "BackgroundSky", "BackgroundSmok",
                "BackgroundTileLayer", "CustomColliderPositioner", "SpriteAnimator", "Audio", "MusicManager",
                "DisplayContinue", "DisplayCreditsForImage", "DisplayPlayerAssign", "DisplayPlayerDoubler",
                "DisplayPlayerWeaponAnimation", "DisplaySpeedKillTimer", "ProgressBar", "CreditsTicker" };
            foreach (string t in fixedTypes)
            {
                bool critical = t == "Player" || t == "LevelManager" || t == "EntityManager" || t == "BaseEntity";
                Patch(t, "FixedUpdate", Type.EmptyTypes, "GateFixedUpdate", null, null, "GateFixedUpdateFinalizer", critical);
            }

            // 2. Simulation set-up code draws from the simulation random stream.
            string[][] simInit = {
                new string[] { "EntityManager", "Awake" }, new string[] { "EntityManager", "Start" },
                new string[] { "LevelManager", "Start" }, new string[] { "PlayerManager", "Start" },
                new string[] { "BackgroundManager", "Awake" }, new string[] { "BackgroundManager", "OnEnable" },
                new string[] { "BackgroundManager", "Initialise" }, new string[] { "BackgroundLayer", "Awake" },
                new string[] { "BackgroundSky", "Awake" }, new string[] { "BackgroundTileLayer", "Awake" },
                new string[] { "CameraManager", "Awake" }, new string[] { "CustomColliderPositioner", "Awake" },
                new string[] { "BaseEntity", "Awake" }, new string[] { "SpritePixelCollider2D", "Awake" },
                new string[] { "SpritePixelCollider2D", "OnEnable" }, new string[] { "OffsetWithPlayerPos", "Start" } };
            foreach (string[] p in simInit)
                Patch(p[0], p[1], Type.EmptyTypes, "SimEnter", null, null, "SimExitFinalizer", false);

            // 3. Sound pitch and UI letters must not consume simulation randomness.
            Type audio = game.GetType("Audio");
            if (audio != null)
                foreach (MethodInfo m in audio.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (m.Name == "PlaySound") PatchMethod(m, "IsolatePrefix", "IsolatePostfix", null, null, false);
            Patch("MorphLetter", "Tick", Type.EmptyTypes, "IsolatePrefix", "IsolatePostfix", null, null, false);

            // 4. Player input and timing.
            Patch("Player", "Update", Type.EmptyTypes, null, null, "RewriteTranspiler", null, true);
            Patch("Player", "FixedUpdate", Type.EmptyTypes, null, "PlayerFixedUpdatePostfix", "RewriteTranspiler", null, true);
            Patch("Player", "UpdateMovement", Type.EmptyTypes, "UpdateMovementPrefix", null, "RewriteTranspiler", null, true);
            Patch("Player", "UpdateInvincibility", Type.EmptyTypes, "OnlyInSimulation", null, "RewriteTranspiler", null, true);
            Patch("Player", "MoveIntoBounds", Type.EmptyTypes, "OnlyInSimulation", null, null, null, true);
            // Ship speed depends on the local PC's last used device (mouse = other speed).
            Patch("Player", "GetSpeed", Type.EmptyTypes, "GetSpeedPrefix", null, null, null, true);
            Patch("Player", "Damage", new Type[] { typeof(bool) }, null, null, "RewriteTranspiler", null, true);
            Patch("Player", "Activate", new Type[] { typeof(Rewired.Player), typeof(bool) }, null, null, "RewriteTranspiler", null, true);
            Patch("Player", "set_CurrentWeapon", null, null, null, "RewriteTranspiler", null, true);
            Patch("Player", "AnimateEndOfLevel", Type.EmptyTypes, null, null, "RewriteTranspiler", null, false);
            Patch("BaseEntity", "FixedUpdate", Type.EmptyTypes, null, null, "RewriteTranspiler", null, true);
            Patch("BaseEntity", "Update", Type.EmptyTypes, null, null, "RewriteTranspiler", null, false);
            Patch("BaseEntity", "Initialise", null, null, null, "RewriteTranspiler", null, true);
            Patch("BackgroundTileLayer", "Tick", Type.EmptyTypes, null, null, "RewriteTranspiler", null, false);
            Patch("BackgroundTileLayer", "Update", Type.EmptyTypes, null, null, "RewriteTranspiler", null, false);
            Patch("BackgroundSmok", "FixedUpdate", Type.EmptyTypes, null, null, "RewriteTranspiler", null, false);
            Patch("BackgroundSmok", "Update", Type.EmptyTypes, null, null, "RewriteTranspiler", null, false);
            Patch("ReclayedStarBehaviour", "OnTick", Type.EmptyTypes, null, null, "RewriteTranspiler", null, false);
            MethodInfo respawn = RespawnMoveNext();
            if (respawn == null) Critical("Player.Respawn coroutine not found");
            else PatchMethod(respawn, "RespawnMoveNextPrefix", null, "RewriteTranspiler", "RespawnMoveNextFinalizer", true);

            // 5. Which players exist is decided by the network, not by local controllers.
            Patch("PlayerManager", "Update", Type.EmptyTypes, "PlayerManagerUpdatePrefix", null, null, null, true);
            // The two-player vertical scroll reads the drawn (frame-rate smoothed)
            // ship positions: use the simulation positions instead.
            Patch("PlayerManager", "CalculateOffsetBasedOnPlayerPositions", Type.EmptyTypes, null, null, "SimPositionTranspiler", null, true);
            Patch("OffsetWithPlayerPos", "Start", Type.EmptyTypes, null, "LayerStartPostfix", null, null, true);
            // Screen edges for ships and background layers come from the camera,
            // whose width depends on the window size of each PC (pixel rounding
            // of the letterbox): compute them from the game's target aspect.
            Patch("PlayerManager", "Start", Type.EmptyTypes, null, null, "ViewportTranspiler", null, true);
            Patch("BackgroundLayer", "Initialise", null, null, null, "ViewportTranspiler", null, false);
            Patch("BackgroundSky", "Initialise", null, null, null, "ViewportTranspiler", null, false);
            Patch("BackgroundSmok", "Initialize", null, null, null, "ViewportTranspiler", null, false);
            Patch("BackgroundTileLayer", "Initialise", null, null, null, "ViewportTranspiler", null, false);
            Patch("PlayerManager", "Start", Type.EmptyTypes, null, "PlayerManagerStartPostfix", null, null, true);
            Patch("PlayerManager", "FindNextFreePlayer", new Type[] { typeof(Rewired.Player) }, "FindNextFreePlayerPrefix", null, null, null, true);
            Patch("PlayerAssignmentManager", "get_ActivePlayers", Type.EmptyTypes, "ActivePlayersPrefix", null, null, null, true);
            Patch("PlayerAssignmentManager", "get_ActivePlayerCount", Type.EmptyTypes, "ActivePlayerCountPrefix", null, null, null, true);
            Patch("PlayerAssignmentManager", "LateUpdate", Type.EmptyTypes, "SkipInMatch", null, null, null, true);
            Patch("LevelManager", "Start", Type.EmptyTypes, null, "LevelManagerStartPostfix", null, null, true);
            Patch("LevelManager", "Update", Type.EmptyTypes, "SkipInMatch", null, null, null, true);

            // 6. Pausing one game would freeze the other: no pause during a match.
            Patch("PauseMenu", "UpdatePauseLogic", Type.EmptyTypes, "SkipInMatch", null, null, null, false);
            Patch("PauseMenu", "OnApplicationFocus", new Type[] { typeof(bool) }, "SkipWhileOnline", null, null, null, false);
            Patch("AttractModeTimer", "Update", Type.EmptyTypes, "SkipWhileOnline", null, null, null, false);

            // 7. Scene changes: host starts levels for both, client follows.
            Patch("LoadingScreen", "Load", null, "LoadPrefix", null, null, null, true);

            Plugin.Log.LogInfo("Patches applied: " + applied + (Healthy ? "" : " - ONLINE DISABLED: " + Problem));
        }

        private static void Critical(string what)
        {
            Plugin.Log.LogError("Critical hook missing: " + what);
            if (Healthy) Problem = what;
            Healthy = false;
        }

        private static void Patch(string typeName, string method, Type[] args, string prefix, string postfix, string transpiler, string finalizer, bool critical)
        {
            Type type = game.GetType(typeName);
            if (type == null)
            {
                if (critical) Critical("type " + typeName); else Plugin.Log.LogWarning("Type not found: " + typeName);
                return;
            }
            MethodInfo target = Find(type, method, args);
            if (target == null)
            {
                if (critical) Critical(typeName + "." + method); else Plugin.Log.LogWarning("Method not found: " + typeName + "." + method);
                return;
            }
            PatchMethod(target, prefix, postfix, transpiler, finalizer, critical);
        }

        private static void PatchMethod(MethodInfo target, string prefix, string postfix, string transpiler, string finalizer, bool critical)
        {
            try
            {
                harmony.Patch(target, Hm(prefix), Hm(postfix), Hm(transpiler), Hm(finalizer), null);
                applied++;
            }
            catch (Exception e)
            {
                string name = target.DeclaringType.Name + "." + target.Name;
                Plugin.Log.LogError("Patch failed " + name + ": " + e);
                if (critical) Critical(name);
            }
        }

        private static HarmonyMethod Hm(string name)
        {
            if (name == null) return null;
            MethodInfo m = typeof(Patches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) throw new MissingMethodException("Patches", name);
            return new HarmonyMethod(m);
        }

        /// Declared method by name; args == null means "the only one with that name".
        private static MethodInfo Find(Type type, string name, Type[] args)
        {
            MethodInfo found = null;
            foreach (MethodInfo m in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.Name != name) continue;
                if (args != null)
                {
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length != args.Length) continue;
                    bool same = true;
                    for (int i = 0; i < ps.Length; i++) if (ps[i].ParameterType != args[i]) { same = false; break; }
                    if (!same) continue;
                    return m;
                }
                if (found != null) return null; // ambiguous
                found = m;
            }
            return found;
        }

        private static MethodInfo RespawnMoveNext()
        {
            foreach (Type nested in typeof(global::Player).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (!nested.Name.StartsWith("<Respawn>")) continue;
                MethodInfo m = nested.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null) return m;
            }
            return null;
        }

        private static Func<T, F> Getter<T, F>(string name)
        {
            FieldInfo fi = AccessTools.Field(typeof(T), name);
            if (fi == null || fi.FieldType != typeof(F)) { Critical(typeof(T).Name + "." + name); return null; }
            DynamicMethod dm = new DynamicMethod("rc_get_" + name, typeof(F), new Type[] { typeof(T) }, typeof(T), true);
            ILGenerator il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, fi);
            il.Emit(OpCodes.Ret);
            return (Func<T, F>)dm.CreateDelegate(typeof(Func<T, F>));
        }

        private static Action<T, F> Setter<T, F>(string name)
        {
            FieldInfo fi = AccessTools.Field(typeof(T), name);
            if (fi == null || fi.FieldType != typeof(F)) { Critical(typeof(T).Name + "." + name); return null; }
            DynamicMethod dm = new DynamicMethod("rc_set_" + name, null, new Type[] { typeof(T), typeof(F) }, typeof(T), true);
            ILGenerator il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, fi);
            il.Emit(OpCodes.Ret);
            return (Action<T, F>)dm.CreateDelegate(typeof(Action<T, F>));
        }

        // ------------------------------------------------------------ accessors for Lockstep/Hash

        internal static Vector2 PlayerNextPosition(global::Player p)
        {
            return getNextPosition != null ? getNextPosition(p) : Vector2.zero;
        }

        internal static Dictionary<int, global::BaseEntity> EntityTable(global::EntityManager em)
        {
            return getEntityTable != null ? getEntityTable(em) : null;
        }

        internal static Vector3 EntityNextPosition(global::BaseEntity e)
        {
            return getEntityNextPosition != null ? getEntityNextPosition(e) : Vector3.zero;
        }

        /// Runs at the start of every tick, after both inputs are known.
        internal static void PreTick()
        {
            byte any = (byte)(Lockstep.Edge[0] | Lockstep.Edge[1]);
            global::LevelManager lm = Lockstep.LM;
            if (lm != null && (any & (Lockstep.Fire | Lockstep.Submit)) != 0)
            {
                int dc = G.deathCounter;
                if ((int)G.currentPlayMode == 3 && dc > 550 && dc < 950) lm.restartEndlessPressed = true;
                else if (dc > 950 && setSkipGameOver != null) setSkipGameOver(lm, true);
            }

            global::PlayerManager pm = Lockstep.PM;
            if (pm == null || playerManagerUpdate == null) return;
            joinList.Clear();
            if (Rewired.ReInput.isReady && pm.m_players != null)
            {
                for (int s = 0; s < 2 && s < pm.m_players.Length; s++)
                {
                    if ((Lockstep.Edge[s] & Lockstep.Fire) == 0) continue;
                    global::Player pl = pm.m_players[s];
                    Rewired.Player rp = Rewired.ReInput.players.GetPlayer(s);
                    if (pl != null && rp != null && !pl.IsActive) joinList.Add(rp);
                }
            }
            // Runs join/continue logic and the two-player vertical scroll inside
            // the tick, where Time.deltaTime equals the fixed step.
            allowPlayerManagerUpdate = true;
            try { playerManagerUpdate.Invoke(pm, null); }
            catch (TargetInvocationException e) { Plugin.Log.LogError("PlayerManager.Update: " + e.InnerException); }
            finally { allowPlayerManagerUpdate = false; }
            SnapToSimulation(pm);
        }

        private static readonly MethodInfo applyOffset = AccessTools.Method(typeof(global::OffsetWithPlayerPos), "ApplyOffset");
        private static readonly FieldInfo animatingLevelEnd = AccessTools.Field(typeof(global::Player), "animatingLevelEnd");
        // Layers whose Start already stored their initial position: moving one
        // before that would make it remember the moved position (off by the scroll).
        private static readonly List<global::OffsetWithPlayerPos> startedLayers = new List<global::OffsetWithPlayerPos>();

        private static void LayerStartPostfix(global::OffsetWithPlayerPos __instance)
        {
            startedLayers.RemoveAll(delegate(global::OffsetWithPlayerPos l) { return l == null; });
            startedLayers.Add(__instance);
        }

        /// Between ticks the game draws ships and the scroll layers where the
        /// last rendered frame left them, and the simulation reads those
        /// transforms (shot origins, aiming). Put them where the simulation
        /// says before any of it runs, so both PCs read the same values.
        private static readonly List<List<global::BaseEntity>> byDepth = new List<List<global::BaseEntity>>();

        /// Same as BaseEntity.Update at the end of the step (t = 1): parent
        /// position + simulation position. Parents first: some entities ride
        /// on other entities (guns on boats).
        private static void SnapEntities()
        {
            global::EntityManager em = global::EntityManager.Instance;
            if (em == null || getEntityLastUpdate == null) return;
            Dictionary<int, global::BaseEntity> table = EntityTable(em);
            if (table == null) return;
            for (int d = 0; d < byDepth.Count; d++) byDepth[d].Clear();
            foreach (KeyValuePair<int, global::BaseEntity> kv in table)
            {
                global::BaseEntity e = kv.Value;
                if (e == null || !e.IsEntityAlive) continue;
                if (getEntityNextUpdate(e) - getEntityLastUpdate(e) == 0f) continue; // Update would not move it
                int depth = 0;
                for (Transform t = e.transform.parent; t != null; t = t.parent) depth++;
                while (byDepth.Count <= depth) byDepth.Add(new List<global::BaseEntity>());
                byDepth[depth].Add(e);
            }
            for (int d = 0; d < byDepth.Count; d++)
            {
                List<global::BaseEntity> list = byDepth[d];
                for (int i = 0; i < list.Count; i++)
                {
                    Transform t = list[i].transform;
                    Vector3 want = (t.parent != null ? t.parent.position : Vector3.zero) + EntityNextPosition(list[i]);
                    Vector3 cur = t.position;
                    if (cur.x != want.x || cur.y != want.y || cur.z != want.z) t.position = want; // exact, not Unity's approximate ==
                }
            }
        }

        private static void SnapToSimulation(global::PlayerManager pm)
        {
            if (applyOffset != null)
                for (int i = 0; i < startedLayers.Count; i++)
                    if (startedLayers[i] != null) applyOffset.Invoke(startedLayers[i], new object[] { G.vertscroll });

            SnapEntities();

            if (pm == null || pm.m_players == null) return;
            // Ship N reads its network input through Rewired player N on both
            // PCs (the game binds ships by the local PC's controllers, so player
            // 2's own keyboard would be Rewired player 0 on his PC).
            if (getRewiredPlayer != null && Rewired.ReInput.isReady)
                for (int i = 0; i < pm.m_players.Length; i++)
                {
                    global::Player p = pm.m_players[i];
                    if (p == null) continue;
                    Rewired.Player want = Rewired.ReInput.players.GetPlayer(p.playerIndex & 1);
                    Rewired.Player cur = getRewiredPlayer(p);
                    if (cur != null && want != null && cur.id != want.id) setRewiredPlayer(p, want);
                }
            for (int i = 0; i < pm.m_players.Length; i++)
            {
                global::Player p = pm.m_players[i];
                if (p == null) continue;
                if (animatingLevelEnd != null && (bool)animatingLevelEnd.GetValue(p)) continue;
                Transform t = p.transform;
                Vector3 origin = t.parent != null ? t.parent.position : Vector3.zero;
                Vector2 np = PlayerNextPosition(p);
                t.position = origin + new Vector3(np.x, np.y, 0f);
            }
        }

        // ------------------------------------------------------------ patch bodies

        private static bool GateFixedUpdate(out bool __state)
        {
            Lockstep.BeginTickIfNeeded();
            __state = false;
            if (Lockstep.InMatch && (Lockstep.Frozen || Lockstep.Stalled)) return false;
            if (Lockstep.InMatch && Lockstep.InTick && Lockstep.GameplayScene)
            {
                Rng.Enter();
                __state = true;
            }
            return true;
        }

        private static Exception GateFixedUpdateFinalizer(Exception __exception, bool __state)
        {
            if (__state) Rng.Exit();
            return __exception;
        }

        private static void SimEnter()
        {
            if (Lockstep.InMatch && Lockstep.GameplayScene)
            {
                Rng.Enter();
                simWraps++;
            }
        }

        private static Exception SimExitFinalizer(Exception __exception)
        {
            if (simWraps > 0)
            {
                simWraps--;
                Rng.Exit();
            }
            return __exception;
        }

        private static void IsolatePrefix(out UnityEngine.Random.State __state)
        {
            __state = UnityEngine.Random.state;
        }

        private static void IsolatePostfix(UnityEngine.Random.State __state)
        {
            if (Lockstep.InMatch) UnityEngine.Random.state = __state;
        }

        private static bool OnlyInSimulation()
        {
            return !Lockstep.InMatch || Clock.SimContext;
        }

        private static bool SkipInMatch()
        {
            return !Lockstep.InMatch;
        }

        private static bool SkipWhileOnline()
        {
            return !Session.Busy && !Lockstep.InMatch;
        }

        private static void UpdateMovementPrefix(global::Player __instance)
        {
            if (!Lockstep.InMatch || !Lockstep.InTick) return;
            int slot = __instance.playerIndex & 1;
            if (getInputPool != null)
            {
                // One entry: the average UpdateMovement takes is exactly the tick input.
                List<Vector2> pool = getInputPool(__instance);
                pool.Clear();
                pool.Add(new Vector2(Lockstep.Axis(slot, false), Lockstep.Axis(slot, true)));
            }
            else if (setHorizontal != null)
            {
                setHorizontal(__instance, Lockstep.Axis(slot, false));
                setVertical(__instance, Lockstep.Axis(slot, true));
            }
        }

        private static readonly FieldInfo shipDataField = AccessTools.Field(typeof(global::Player), "shipData");
        private static readonly FieldInfo pressFireField = AccessTools.Field(typeof(global::Player), "press_fire");

        /// Online the mouse is not a ship control: always the keyboard / pad speed.
        private static bool GetSpeedPrefix(global::Player __instance, ref float __result)
        {
            if (!Lockstep.InMatch || shipDataField == null || pressFireField == null) return true;
            Platypus.Core.ShipData data = shipDataField.GetValue(__instance) as Platypus.Core.ShipData;
            if (data == null) { __result = 8f; return false; }
            __result = (bool)pressFireField.GetValue(__instance) ? data.speedWhileFiring : data.speed;
            return false;
        }

        private static void PlayerFixedUpdatePostfix(global::Player __instance)
        {
            if (!Lockstep.InMatch || !Lockstep.InTick || updateInvincibility == null || getIsActive == null) return;
            if (getIsActive(__instance)) updateInvincibility.Invoke(__instance, null);
        }

        private static bool RespawnMoveNextPrefix(object __instance, ref bool __result)
        {
            if (Lockstep.InMatch && Clock.StillWaiting(__instance))
            {
                __result = true; // keep yielding WaitForFixedUpdate
                return false;
            }
            Clock.CurrentIterator = __instance;
            if (Lockstep.InMatch)
            {
                Rng.Enter();
                respawnWraps++;
            }
            return true;
        }

        private static Exception RespawnMoveNextFinalizer(Exception __exception)
        {
            Clock.CurrentIterator = null;
            if (respawnWraps > 0)
            {
                respawnWraps--;
                Rng.Exit();
            }
            return __exception;
        }

        private static bool PlayerManagerUpdatePrefix()
        {
            return !Lockstep.InMatch || allowPlayerManagerUpdate;
        }

        private static void PlayerManagerStartPostfix(global::PlayerManager __instance)
        {
            Lockstep.OnPlayerManagerStart(__instance);
        }

        private static void LevelManagerStartPostfix(global::LevelManager __instance)
        {
            Lockstep.OnLevelStart(__instance);
        }

        private static bool FindNextFreePlayerPrefix(global::PlayerManager __instance, Rewired.Player __0, ref int __result)
        {
            if (!Lockstep.InMatch) return true;
            __result = -1;
            if (__0 != null && __instance.m_players != null)
            {
                int id = __0.id;
                if (id >= 0 && id < __instance.m_players.Length && __instance.m_players[id] != null && __instance.m_players[id].RewiredPlayer == null)
                    __result = id;
            }
            return false;
        }

        private static bool ActivePlayersPrefix(ref IEnumerable<Rewired.Player> __result)
        {
            if (!Lockstep.InMatch) return true;
            List<Rewired.Player> list = new List<Rewired.Player>();
            if (allowPlayerManagerUpdate)
            {
                list.AddRange(joinList);
            }
            else if (Rewired.ReInput.isReady)
            {
                for (int s = 0; s < 2; s++)
                {
                    Rewired.Player rp = Rewired.ReInput.players.GetPlayer(s);
                    if (rp != null) list.Add(rp);
                }
            }
            __result = list;
            return false;
        }

        private static bool ActivePlayerCountPrefix(ref int __result)
        {
            if (!Lockstep.InMatch) return true;
            __result = 2;
            return false;
        }

        private static bool LoadPrefix(BuildScenes __0, ref Snapshot __1)
        {
            if (Lockstep.InternalLoad || !Session.Connected) return true;
            if (Lockstep.InMatch && Clock.SimContext)
            {
                // Requested by the level itself: happens identically on both PCs.
                Lockstep.FreezeForTransition();
                return true;
            }
            if (Session.Role == Role.Host)
            {
                if (__0 == BuildScenes.Gameplay)
                {
                    // Both ship screens must be locked in before the level starts.
                    if (!Lockstep.InMatch && !MenuFlow.HostReadyToStart(__1)) return false;
                    __1 = Lockstep.HostStart(__1);
                    return true;
                }
                if (!Lockstep.InMatch) MenuFlow.HostSceneChange(__0);
                if (Lockstep.InMatch)
                {
                    Session.SendGoto((int)__0);
                    if (__0 == BuildScenes.MainMenu) Lockstep.EndMatch();
                }
                return true;
            }
            if (__0 == BuildScenes.Gameplay && !Lockstep.InMatch)
            {
                // Player 2 locked in a ship: hand it to the host and wait.
                MenuFlow.ClientShipLocked();
                return false;
            }
            if (__0 == BuildScenes.Gameplay || __0 == BuildScenes.ReclayedShipSelect || __0 == BuildScenes.ClassicLevelSelect ||
                __0 == BuildScenes.ReclayedLevelSelect || __0 == BuildScenes.WeaponSelectScene)
            {
                Ui.ShowMessage("Хост выбирает - ждите.");
                return false;
            }
            if (__0 == BuildScenes.MainMenu && !Lockstep.InMatch)
            {
                Ui.ShowMessage("Назад может вернуть только хост (или F8 - Disconnect).");
                return false;
            }
            if (__0 == BuildScenes.MainMenu && Lockstep.InMatch)
            {
                // From a score screen: stay connected for the next game.
                // From inside a level: the partner cannot continue alone.
                bool leavingLevel = Lockstep.GameplayScene;
                Lockstep.EndMatch();
                if (leavingLevel) Session.Close("You left the match.", true);
            }
            return true;
        }

        // ------------------------------------------------------------ IL rewriting

        /// Time.time -> Clock.GetTime, Time.timeSinceLevelLoad -> Clock.GetTimeSinceLevelLoad,
        /// new WaitForSeconds(x) -> Clock.RespawnWait(x),
        /// Rewired.Player.GetAxis/GetButton(string) -> network input during a match.
        private static IEnumerable<CodeInstruction> ViewportTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            MethodInfo vp = typeof(Camera).GetMethod("ViewportToWorldPoint", new Type[] { typeof(Vector3) });
            MethodInfo sim = typeof(Patches).GetMethod("SimViewportToWorld", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            int replaced = 0;
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            foreach (CodeInstruction ci in list)
            {
                if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && (ci.operand as MethodInfo) == vp)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = sim;
                    replaced++;
                }
            }
            string name = original != null ? original.DeclaringType.Name + "." + original.Name : "?";
            Plugin.Log.LogDebug("Rewrote " + replaced + " camera call(s) in " + name);
            return list;
        }

        private static readonly FieldInfo outputAspectField = AccessTools.Field(typeof(global::CameraManager), "outputAspectRatio");
        private static readonly MethodInfo aspectToRatio = AccessTools.Method(typeof(global::CameraManager), "AspectRatioTypeToRatio");

        /// Camera.ViewportToWorldPoint for an orthographic camera, with the
        /// game's target aspect instead of the letterboxed window's.
        private static Vector3 SimViewportToWorld(Camera cam, Vector3 v)
        {
            if (!Lockstep.InMatch || cam == null || !cam.orthographic) return cam.ViewportToWorldPoint(v);
            float aspect = 0f;
            try
            {
                global::CameraManager cm = cam.GetComponent<global::CameraManager>();
                if (cm == null) cm = UnityEngine.Object.FindObjectOfType<global::CameraManager>();
                if (cm != null && outputAspectField != null && aspectToRatio != null)
                    aspect = (float)aspectToRatio.Invoke(aspectToRatio.IsStatic ? null : cm, new object[] { outputAspectField.GetValue(cm) });
            }
            catch (Exception e) { Plugin.Log.LogWarning("Target aspect: " + e.Message); }
            if (aspect <= 0f) aspect = 4f / 3f;
            float h = cam.orthographicSize;
            Vector3 p = cam.transform.position;
            return new Vector3(p.x + (v.x - 0.5f) * 2f * h * aspect, p.y + (v.y - 0.5f) * 2f * h, p.z + v.z);
        }

        private static IEnumerable<CodeInstruction> SimPositionTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo localPos = typeof(Transform).GetProperty("localPosition").GetGetMethod();
            MethodInfo simPos = typeof(Patches).GetMethod("SimLocalPosition", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            int replaced = 0;
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            foreach (CodeInstruction ci in list)
            {
                if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && (ci.operand as MethodInfo) == localPos)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = simPos;
                    replaced++;
                }
            }
            Plugin.Log.LogDebug("Rewrote " + replaced + " call(s) in PlayerManager.CalculateOffsetBasedOnPlayerPositions");
            if (replaced == 0) Plugin.Log.LogWarning("Nothing to rewrite in PlayerManager.CalculateOffsetBasedOnPlayerPositions (game update?)");
            return list;
        }

        private static Vector3 SimLocalPosition(Transform t)
        {
            Vector3 lp = t.localPosition;
            if (!Lockstep.InMatch) return lp;
            global::Player p = t.GetComponent<global::Player>();
            if (p == null) return lp;
            Vector2 np = PlayerNextPosition(p);
            return new Vector3(np.x, np.y, lp.z);
        }

        private static IEnumerable<CodeInstruction> RewriteTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            MethodInfo clockTime = typeof(Clock).GetMethod("GetTime", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo clockSince = typeof(Clock).GetMethod("GetTimeSinceLevelLoad", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo clockWait = typeof(Clock).GetMethod("RespawnWait", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo netAxis = typeof(Patches).GetMethod("NetAxis", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo netButton = typeof(Patches).GetMethod("NetButton", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            int replaced = 0;
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            foreach (CodeInstruction ci in list)
            {
                if (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt)
                {
                    MethodInfo mi = ci.operand as MethodInfo;
                    if (mi == null) continue;
                    if (mi == timeGetTime) { ci.opcode = OpCodes.Call; ci.operand = clockTime; replaced++; }
                    else if (mi == timeSinceLevelLoad) { ci.opcode = OpCodes.Call; ci.operand = clockSince; replaced++; }
                    else if (mi == rewiredGetAxis) { ci.opcode = OpCodes.Call; ci.operand = netAxis; replaced++; }
                    else if (mi == rewiredGetButton) { ci.opcode = OpCodes.Call; ci.operand = netButton; replaced++; }
                }
                else if (ci.opcode == OpCodes.Newobj && (ci.operand as ConstructorInfo) == waitForSecondsCtor)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = clockWait;
                    replaced++;
                }
            }
            string name = original != null ? original.DeclaringType.Name + "." + original.Name : "?";
            Plugin.Log.LogDebug("Rewrote " + replaced + " call(s) in " + name);
            if (replaced == 0) Plugin.Log.LogWarning("Nothing to rewrite in " + name + " (game update?)");
            return list;
        }

        /// Player.Update accumulates axes every rendered frame; in a match the
        /// tick sets them instead (UpdateMovementPrefix).
        private static float NetAxis(Rewired.Player player, string action)
        {
            if (Lockstep.InMatch) return 0f;
            return player.GetAxis(action);
        }

        private static bool NetButton(Rewired.Player player, string action)
        {
            if (Lockstep.InMatch) return action == "Fire" && Lockstep.FireHeld(player.id);
            return player.GetButton(action);
        }
    }
}
