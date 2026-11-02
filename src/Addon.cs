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
        private static readonly FieldInfo displayField = AccessTools.Field(typeof(Platypus.Core.ModMeta), "displayName");

        private static float nextCheck;
        private static bool enabled;
        private static bool? logged;

        internal static void EnsureInstalled()
        {
            try
            {
                string dir = Path.Combine(Application.streamingAssetsPath, Folder);
                string workshop = WorkshopCopy();
                if (workshop != null)
                {
                    // Subscribed on the Workshop: that entry is the switch, no second one.
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    Plugin.Log.LogInfo("Add-on entry from the Steam Workshop: " + workshop);
                    return;
                }
                string file = Path.Combine(dir, "modmeta.json");
                // The game lists Data\ of every enabled add-on: an empty one is fine.
                Directory.CreateDirectory(Path.Combine(dir, "Data"));
                if (File.Exists(file) && File.ReadAllText(file) == Meta) return;
                File.WriteAllText(file, Meta);
                Plugin.Log.LogInfo("Add-on entry written: " + file);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not write the add-on entry: " + e.Message); }
        }

        /// steamapps\workshop\content\2912430\<item>\ with our modmeta.json, or null.
        private static string WorkshopCopy()
        {
            // <steamapps>\common\<game>\Platypus_Data
            string steamapps = Path.GetFullPath(Path.Combine(Application.dataPath, Path.Combine("..", Path.Combine("..", ".."))));
            string content = Path.Combine(steamapps, Path.Combine("workshop", Path.Combine("content", "2912430")));
            if (!Directory.Exists(content)) return null;
            foreach (string item in Directory.GetDirectories(content))
            {
                string meta = Path.Combine(item, "modmeta.json");
                if (File.Exists(meta) && IsOurs(File.ReadAllText(meta))) return item;
            }
            return null;
        }

        private static bool IsOurs(string json)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(json, "\"displayName\"\\s*:\\s*\"" + Folder + "\"");
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
                    if (meta == null) continue;
                    // Ours from StreamingAssets (folder name) or from the Workshop (item id
                    // as the folder, the display name is what identifies it).
                    if ((nameField.GetValue(meta) as string) == Folder) return true;
                    if (displayField != null && (displayField.GetValue(meta) as string) == Folder) return true;
                }
            }
            catch (Exception) { }
            return false;
        }
    }
}
