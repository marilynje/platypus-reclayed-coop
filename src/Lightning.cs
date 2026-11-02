using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ReclayedCoop
{
    /// Full game (1.4): the player's lightning weapon is an entity
    /// (PlayerLightningBehaviour) placed in world coordinates. The world <->
    /// layer conversion reads Transforms, which depend on rendering and on the
    /// object pool, so the two games put it in different places (desync at
    /// tick 1, "entities"). In a match its position is kept in layer space,
    /// from simulation data only.
    internal static class Lightning
    {
        private static readonly FieldInfo nextPos = AccessTools.Field(typeof(global::BaseEntity), "m_nextPosition");
        private static readonly FieldInfo lastPos = AccessTools.Field(typeof(global::BaseEntity), "m_lastPosition");

        internal static void ApplyPatches(Harmony h)
        {
            Type lb = AccessTools.TypeByName("PlayerLightningBehaviour");
            MethodInfo force = lb != null ? AccessTools.Method(lb, "ForceGlobalPosition") : null;
            MethodInfo spawn = AccessTools.Method(typeof(global::Player), "SpawnLightningWeapon", new Type[] { typeof(Vector2) });
            if (force == null || spawn == null || nextPos == null || lastPos == null)
            {
                Plugin.Log.LogInfo("Lightning entity not in this build (demo): no lightning fix needed");
                return;
            }
            h.Patch(force, new HarmonyMethod(typeof(Lightning), "ForcePrefix"), null, null, null, null);
            h.Patch(spawn, null, new HarmonyMethod(typeof(Lightning), "SpawnPostfix"), null, null, null);
        }

        /// Spawned "at world (0,0)": the layer-space point of world (0,0),
        /// from the layer's simulated position, whatever the pool left behind.
        private static void SpawnPostfix(int __result)
        {
            if (!Lockstep.InMatch) return;
            global::BaseEntity e;
            if (!global::EntityManager.Instance.TryGetEntityWithID(__result, out e) || e == null) return;
            Transform layer = LayerOf(e);
            if (layer == null) return;
            if (e.transform.parent != layer) e.transform.SetParent(layer, false);
            Place(e, layer.InverseTransformPoint(Vector3.zero));
        }

        /// The game passes "where the lightning is drawn + a fixed offset".
        /// Same offset, but from where it is in the simulation.
        private static bool ForcePrefix(global::BaseEntity __instance, Vector2 __0)
        {
            if (!Lockstep.InMatch) return true;
            Transform parent = __instance.transform.parent;
            if (parent == null) return true;
            Vector2 drawn = __instance.transform.position;
            Vector2 offset = __0 - drawn;
            // The offsets are whole or half pixels (32 + width / 2, -5): round
            // away the float noise of (drawn + offset) - drawn.
            offset = new Vector2(Mathf.Round(offset.x * 2f) / 2f, Mathf.Round(offset.y * 2f) / 2f);
            Vector3 sim = parent.TransformPoint((Vector3)nextPos.GetValue(__instance));
            Vector3 world = new Vector3(sim.x + offset.x, sim.y + offset.y, sim.z);
            Place(__instance, parent.InverseTransformPoint(world));
            return false;
        }

        private static readonly PropertyInfo layerProp = AccessTools.Property(typeof(global::BaseEntity), "Layer");

        /// "Gameplay Setup/Entity Manager/Layer N" for the entity's layer.
        private static Transform LayerOf(global::BaseEntity e)
        {
            if (layerProp == null || global::EntityManager.Instance == null) return e.transform.parent;
            Transform t = global::EntityManager.Instance.transform.Find("Layer " + layerProp.GetValue(e, null));
            return t != null ? t : e.transform.parent;
        }

        private static void Place(global::BaseEntity e, Vector3 local)
        {
            e.transform.localPosition = local;
            nextPos.SetValue(e, local);
            lastPos.SetValue(e, local);
        }
    }
}
