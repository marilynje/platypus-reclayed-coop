using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReclayedCoop
{
    /// Adds an "Online Co-op" entry to the game's own main menu (full game: Main,
    /// demo: DemoMain) by cloning the row the menu selects first. Clicking it
    /// opens the co-op window. Polled from Runner.Update, so no extra Harmony patch.
    internal static class MenuButton
    {
        private const string ObjectName = "ReclayedCoopButton";
        private const int SubMenuMain = 2;
        private const int SubMenuDemoMain = 8;

        private static readonly FieldInfo menuField = AccessTools.Field(typeof(global::MainMenuSubMenu), "menu");
        private static readonly FieldInfo firstField = AccessTools.Field(typeof(global::MainMenuSubMenu), "firstElement");
        private static readonly HashSet<int> dumped = new HashSet<int>();
        private static readonly List<GameObject> pending = new List<GameObject>();
        private static float nextCheck;
        private static string lastPanels;

        internal static void Tick()
        {
            // Not GameplayScene: the main menu runs a demo level in the background.
            if (Lockstep.InMatch || Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 0.25f;

            // Text components may re-apply the template's localized string a
            // frame after Instantiate, so the label is set once more here.
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i] != null) SetLabel(pending[i]);
                pending.RemoveAt(i);
            }

            global::MainMenuSubMenu[] menus = UnityEngine.Object.FindObjectsOfType<global::MainMenuSubMenu>();
            string panels = "";
            for (int i = 0; i < menus.Length; i++) panels += " " + menus[i].name + "=" + menuField.GetValue(menus[i]);
            if (panels != lastPanels)
            {
                lastPanels = panels;
                Plugin.Log.LogInfo("Active main-menu panels:" + (panels.Length == 0 ? " none" : panels));
            }

            for (int i = 0; i < menus.Length; i++)
            {
                int kind = Convert.ToInt32(menuField.GetValue(menus[i]));
                if (kind != SubMenuMain && kind != SubMenuDemoMain) continue;
                // Back in the main menu without a partner: the next Start is single player.
                if (!Session.Busy && Time.realtimeSinceStartup > InviteSlot.IntentAt + 1f) InviteSlot.Intent = false;
                Transform existing = FindDeep(menus[i].transform, ObjectName);
                if (existing != null)
                {
                    bool on = Addon.Enabled;
                    if (existing.gameObject.activeSelf != on) existing.gameObject.SetActive(on);
                    continue;
                }
                if (!Addon.Enabled) continue;
                try { Inject(menus[i]); }
                catch (Exception e)
                {
                    Plugin.Log.LogError("CO-OP menu button: " + e);
                    nextCheck = float.MaxValue; // do not spam the log every frame
                }
            }
        }

        private static void Inject(global::MainMenuSubMenu menu)
        {
            if (FindDeep(menu.transform, ObjectName) != null) return;

            int id = menu.GetInstanceID();
            bool firstLook = !dumped.Contains(id);
            if (firstLook) { dumped.Add(id); Plugin.Log.LogInfo(Dump(menu)); }

            // The element the menu selects first is a real menu row: clone it.
            Selectable first = firstField.GetValue(menu) as Selectable;
            if (first == null) first = menu.GetComponentInChildren<Selectable>();
            if (first == null)
            {
                if (firstLook) Plugin.Log.LogWarning("CO-OP button: no selectable row in " + menu.name);
                return;
            }
            Transform template = first.transform;
            Transform parent = template.parent;

            List<RectTransform> column = new List<RectTransform>();
            for (int i = 0; i < parent.childCount; i++)
            {
                RectTransform c = parent.GetChild(i) as RectTransform;
                if (c != null && c.gameObject.activeSelf && c.GetComponent<Selectable>() != null) column.Add(c);
            }
            column.Sort(delegate(RectTransform a, RectTransform b) { return b.anchoredPosition.y.CompareTo(a.anchoredPosition.y); });

            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            go.name = ObjectName;
            go.transform.SetSiblingIndex(template.GetSiblingIndex() + 1);
            RectTransform rt = go.transform as RectTransform;

            // Without a layout group the clone would sit on top of the template:
            // place it one row below and push the rows underneath down.
            RectTransform tr = template as RectTransform;
            if (rt != null && tr != null && parent.GetComponent<LayoutGroup>() == null && column.Count >= 2)
            {
                float step = Mathf.Abs(column[0].anchoredPosition.y - column[1].anchoredPosition.y);
                if (step < 1f) step = tr.rect.height;
                rt.anchoredPosition = tr.anchoredPosition - new Vector2(0f, step);
                for (int i = 0; i < column.Count; i++)
                    if (column[i].anchoredPosition.y < tr.anchoredPosition.y - 0.5f)
                        column[i].anchoredPosition -= new Vector2(0f, step);
            }

            Button button = go.GetComponent<Button>();
            Button start = template.GetComponent<Button>();
            if (button != null)
            {
                // Keep the game's original action separate from the single-player
                // wrapper: online must not call the wrapper and clear its intent.
                Button.ButtonClickedEvent startAction = start != null ? start.onClick : null;
                if (start != null)
                {
                    start.onClick = new Button.ButtonClickedEvent();
                    start.onClick.AddListener(delegate
                    {
                        InviteSlot.Intent = false;
                        // A lobby may still be waiting after backing out of co-op.
                        // Also cancel pending Steam callbacks before starting solo.
                        Session.Close(null, true);
                        Ui.CloseWindow();
                        CoopPanel.Hide();
                        Plugin.Log.LogInfo("Single player chosen: co-op session cleared");
                        startAction.Invoke();
                    });
                }
                // Same as the game's first row (Start): difficulty, world, ships;
                // the ship screen then has "Invite a Friend" (InviteSlot).
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(delegate
                {
                    InviteSlot.Intent = true;
                    InviteSlot.IntentAt = Time.realtimeSinceStartup;
                    Plugin.Log.LogInfo("Online Co-op chosen: host goes through the menus");
                    if (startAction != null) startAction.Invoke();
                });
            }
            else
            {
                go.AddComponent<ClickRelay>();
            }
            SetLabel(go);
            Unhighlight(go);
            pending.Add(go);

            // Keyboard / gamepad: let Unity pick neighbours by position so the
            // new row is reachable with up / down.
            for (int i = 0; i < parent.childCount; i++)
            {
                Selectable sel = parent.GetChild(i).GetComponent<Selectable>();
                if (sel == null) continue;
                Navigation nav = sel.navigation;
                if (nav.mode == Navigation.Mode.Explicit)
                {
                    nav.mode = Navigation.Mode.Vertical;
                    sel.navigation = nav;
                }
            }
            Plugin.Log.LogInfo("CO-OP button added to the main menu (" + menu.name + "/" + parent.name + ", cloned " + template.name + ").");
        }

        private static void SetLabel(GameObject go)
        {
            Loc.Bind(go, "Coop.Menu");
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        private static string Dump(global::MainMenuSubMenu menu)
        {
            StringBuilder sb = new StringBuilder();
            Component first = firstField.GetValue(menu) as Component;
            sb.Append("Main menu '" + menu.name + "' kind=" + menuField.GetValue(menu) + " first=" + (first == null ? "null" : first.name));
            DumpNode(sb, menu.transform, 1);
            return sb.ToString();
        }

        private static void DumpNode(StringBuilder sb, Transform t, int depth)
        {
            if (depth > 2) return;
            sb.Append('\n').Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)");
            RectTransform r = t as RectTransform;
            if (r != null) sb.Append(" pos=").Append(r.anchoredPosition);
            Component[] comps = t.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
                if (comps[i] != null && !(comps[i] is Transform)) sb.Append(" [").Append(comps[i].GetType().Name).Append(']');
            for (int i = 0; i < t.childCount; i++) DumpNode(sb, t.GetChild(i), depth + 1);
        }

        private static readonly FieldInfo bsText = AccessTools.Field(typeof(global::ButtonScript), "text");
        private static readonly FieldInfo bsTextMaterial = AccessTools.Field(typeof(global::ButtonScript), "textMaterial");
        private static readonly FieldInfo bsIndicator = AccessTools.Field(typeof(global::ButtonScript), "selectedIndicator");
        private static readonly FieldInfo bsWasSelected = AccessTools.Field(typeof(global::ButtonScript), "wasSelected");
        private static readonly FieldInfo ptMaterial = AccessTools.Field(typeof(global::PText), "material");

        /// Copies of the menu's selected row inherit its highlight: show the
        /// normal look; ButtonScript switches it again when really selected.
        internal static void Unhighlight(GameObject row)
        {
            global::ButtonScript bs = row.GetComponent<global::ButtonScript>();
            if (bs == null) return;
            RectTransform ind = bsIndicator.GetValue(bs) as RectTransform;
            if (ind != null) ind.gameObject.SetActive(false);
            bsWasSelected.SetValue(bs, false);
            NormalLetters(bs);
        }

        /// Letters with the normal (not the selected) material.
        internal static void NormalLetters(global::ButtonScript bs)
        {
            global::PText pt = bsText.GetValue(bs) as global::PText;
            Material normal = bsTextMaterial.GetValue(bs) as Material;
            if (pt == null || normal == null) return;
            if (ptMaterial.GetValue(pt) as Material == normal) return;
            ptMaterial.SetValue(pt, normal);
            pt.UpdateMaterial();
        }

        /// Make ButtonScript re-apply its own look on its next Update.
        internal static void RefreshLook(global::ButtonScript bs)
        {
            bool selected = UnityEngine.EventSystems.EventSystem.current != null &&
                            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject == bs.gameObject;
            bsWasSelected.SetValue(bs, !selected);
        }

        /// While the co-op window is open the game's menu must not react to
        /// Enter / Space / arrows typed into it.
        internal static void SetMenuInput(bool enabled)
        {
            EventSystem es = EventSystem.current;
            if (es != null) es.sendNavigationEvents = enabled;
        }

        /// Fallback for rows that are not UnityEngine.UI.Button.
        private class ClickRelay : MonoBehaviour, IPointerClickHandler, ISubmitHandler
        {
            public void OnPointerClick(PointerEventData e) { Ui.OpenFromMenu(); }
            public void OnSubmit(BaseEventData e) { Ui.OpenFromMenu(); }
        }
    }
}
