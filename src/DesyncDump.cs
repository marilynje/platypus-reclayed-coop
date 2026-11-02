using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using G = Platypus.Core.Globals;

namespace ReclayedCoop
{
    /// Remembers what every hashed value was for the last ticks, so that on a
    /// desync both PCs can write the exact tick to a file and the two files
    /// can be compared line by line (BepInEx\desync-tick*-P*.txt).
    internal static class DesyncDump
    {
        private const int Ticks = 16;

        private struct Row
        {
            public int Key;
            public string Type;
            public float X, Y, Hp;
            public int Life;
        }

        private static readonly int[] slotTick = new int[Ticks];
        private static readonly Row[][] rows = new Row[Ticks][];
        private static readonly int[] counts = new int[Ticks];
        private static readonly string[] extra = new string[Ticks];
        private static int recording = -1;
        private static int cursor;

        internal static void Reset()
        {
            for (int i = 0; i < Ticks; i++) { slotTick[i] = -1; counts[i] = 0; extra[i] = null; }
        }

        /// Hash.Compute for tick t (once per tick) records into the ring.
        internal static void Begin(int tick)
        {
            int s = tick % Ticks;
            slotTick[s] = tick;
            counts[s] = 0;
            recording = s;
            cursor = 0;
            extra[s] = "globals life=" + G.levelLifetime + " deaths=" + G.deathCounter + " credits=" + G.credits +
                       " continues=" + G.continues + " bonus=" + G.bonus + " endBonus=" + G.endBonus +
                       " killTimer=" + G.bossKillSpeedTimer + " area=" + G.currentArea + " diff=" + G.Difficulty +
                       " mode=" + G.currentPlayMode + " vertscroll=" + G.vertscroll.ToString("R") +
                       "\nrandom=" + Rng.Hash().ToString("x8") + Scene();
        }

        /// Scroll layers and the parents of ships: render-side transforms the
        /// simulation reads.
        private static string Scene()
        {
            StringBuilder sb = new StringBuilder();
            global::OffsetWithPlayerPos[] layers = UnityEngine.Object.FindObjectsOfType<global::OffsetWithPlayerPos>();
            for (int i = 0; i < layers.Length; i++)
                sb.Append("\nlayer ").Append(PathOf(layers[i].transform)).Append(" en=").Append(layers[i].enabled)
                  .Append(" y=").Append(layers[i].transform.position.y.ToString("R"));
            global::PlayerManager pm = Lockstep.PM;
            if (pm != null && pm.m_players != null)
                for (int i = 0; i < pm.m_players.Length; i++)
                    if (pm.m_players[i] != null)
                    {
                        Transform t = pm.m_players[i].transform;
                        sb.Append("\nship").Append(i).Append(' ').Append(PathOf(t)).Append(" y=").Append(t.position.y.ToString("R"))
                          .Append(" parentY=").Append(t.parent != null ? t.parent.position.y.ToString("R") : "-");
                    }
            global::EntityManager em = global::EntityManager.Instance;
            if (em != null) sb.Append("\nentityManager ").Append(PathOf(em.transform)).Append(" y=").Append(em.transform.position.y.ToString("R"));
            return sb.ToString();
        }

        private static string PathOf(Transform t)
        {
            string p = t.name;
            for (Transform x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }

        internal static void End() { if (recording >= 0) counts[recording] = cursor; recording = -1; }

        internal static void Players(string text)
        {
            if (recording >= 0) extra[recording] += "\n" + text;
        }

        internal static void Entity(int key, global::BaseEntity e, Vector3 pos)
        {
            if (recording < 0) return;
            Row[] r = rows[recording];
            if (r == null || r.Length <= cursor)
            {
                Row[] bigger = new Row[Math.Max(4096, cursor * 2)];
                if (r != null) Array.Copy(r, bigger, cursor);
                rows[recording] = r = bigger;
            }
            r[cursor].Key = key;
            r[cursor].Type = e == null ? "null" : e.GetType().Name + "@" + (e.transform.parent != null ? e.transform.parent.name + ":" + e.transform.parent.position.y.ToString("R") : "-");
            r[cursor].X = pos.x;
            r[cursor].Y = pos.y;
            r[cursor].Hp = e == null ? 0f : e.hitPoints;
            r[cursor].Life = e == null ? 0 : e.lifetime;
            cursor++;
        }

        /// One file per remembered tick, so the two PCs always share some.
        internal static void Write(int tick, string why)
        {
            try
            {
                int written = 0;
                for (int s = 0; s < Ticks; s++)
                {
                    int t = slotTick[s];
                    if (t < 0) continue;
                    StringBuilder sb = new StringBuilder();
                    sb.Append("tick ").Append(t).Append(" player ").Append(Lockstep.LocalSlot + 1).Append(" (").Append(why).Append(" at ").Append(tick).Append(")\n");
                    sb.Append(extra[s]).Append('\n');
                    sb.Append("entities ").Append(counts[s]).Append('\n');
                    Row[] r = rows[s];
                    for (int i = 0; i < counts[s]; i++)
                        sb.Append(r[i].Key).Append(' ').Append(r[i].Type).Append(' ')
                          .Append(r[i].X.ToString("R")).Append(' ').Append(r[i].Y.ToString("R")).Append(' ')
                          .Append(r[i].Hp.ToString("R")).Append(' ').Append(r[i].Life).Append('\n');
                    File.WriteAllText(Path.Combine(BepInEx.Paths.BepInExRootPath, "desync-tick" + t + "-P" + (Lockstep.LocalSlot + 1) + ".txt"), sb.ToString());
                    written++;
                }
                Plugin.Log.LogWarning("Desync dump: " + written + " tick(s) written around tick " + tick);
            }
            catch (Exception e) { Plugin.Log.LogError("Desync dump: " + e); }
        }
    }
}
