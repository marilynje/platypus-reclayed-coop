using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ReclayedCoop
{
    /// The mod as an entry in the game's own Add-ons list: an empty add-on
    /// folder in StreamingAssets. Ticking it in the game's menu turns online
    /// co-op on (the invite button on the ship screen), unticking turns it off.
    internal static class Addon
    {
        internal const string Folder = "Online Co-op";

        private const string Meta =
@"[
  {
    ""isOfficial"": false,
    ""isLegacy"": false,
    ""allowAssetBundles"": false,
    ""allowVeryEasyDifficulty"": true,
    ""allowNastyDifficulty"": true,
    ""displayName"": ""Online Co-op"",
    ""supportsCreditsPanel"": true,
    ""supportsAchievementsPanel"": true,
    ""supportsGalleryPanel"": true,
    ""allowReclayedAchievements"": true,
    ""allowClassicAchievements"": true,
    ""displayAchievementBlockingIcon"": false,

    ""isAddon"": true,
    ""addonTargetAll"": true,
    ""addonTargets"": [],
    ""baseMod"": """"
  }
]
";

        private static readonly FieldInfo rootField = AccessTools.Field(typeof(global::ModManager), "modRoot");
        private static readonly FieldInfo addonsField = AccessTools.Field(typeof(Platypus.Core.ModInstance), "addons");
        private static readonly FieldInfo metaField = AccessTools.Field(typeof(Platypus.Core.ModInstance), "meta");
        private static readonly FieldInfo nameField = AccessTools.Field(typeof(Platypus.Core.ModMeta), "modName");

        private static float nextCheck;
        private static bool enabled;
        private static bool? logged;

        internal static void EnsureInstalled()
        {
            try
            {
                string dir = Path.Combine(Application.streamingAssetsPath, Folder);
                string file = Path.Combine(dir, "modmeta.json");
                // The game lists Data\ of every enabled add-on: an empty one is fine.
                Directory.CreateDirectory(Path.Combine(dir, "Data"));
                if (File.Exists(file) && File.ReadAllText(file) == Meta) return;
                File.WriteAllText(file, Meta);
                Plugin.Log.LogInfo("Add-on entry written: " + file);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not write the add-on entry: " + e.Message); }
        }

        /// Ticked in the game's Add-ons menu (checked twice a second).
        internal static bool Enabled
        {
            get
            {
                if (AutoTest.ShipTest) return true;
                if (Time.realtimeSinceStartup < nextCheck) return enabled;
                nextCheck = Time.realtimeSinceStartup + 0.5f;
                enabled = Read();
                if (logged != enabled) { logged = enabled; Plugin.Log.LogInfo("Add-on '" + Folder + "' " + (enabled ? "enabled" : "disabled")); }
                return enabled;
            }
        }

        private static bool Read()
        {
            try
            {
                object root = rootField.GetValue(null);
                Array addons = root != null ? addonsField.GetValue(root) as Array : null;
                if (addons == null) return false;
                foreach (object a in addons)
                {
                    object meta = a != null ? metaField.GetValue(a) : null;
                    if (meta != null && (nameField.GetValue(meta) as string) == Folder) return true;
                }
            }
            catch (Exception) { }
            return false;
        }
    }
}
