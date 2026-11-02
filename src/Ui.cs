using System;
using UnityEngine;

namespace ReclayedCoop
{
    /// Small IMGUI overlay: F8 opens the co-op window.
    internal static class Ui
    {
        private const int WindowId = 0x52435031;
        private static bool windowOpen;
        private static Rect windowRect = new Rect(24, 24, 420, 0);
        private static string portText;
        private static string addressText;
        private static string message = "";
        private static float messageUntil;
        private static GUIStyle labelStyle;
        private static GUIStyle shadowStyle;
        private static GUIStyle warnStyle;

        /// "CO-OP" entry in the game's main menu.
        internal static void OpenFromMenu()
        {
            windowRect = new Rect((Screen.width - 420) * 0.5f, Screen.height * 0.2f, 420, 0);
            SetOpen(true);
        }

        internal static void CloseWindow()
        {
            if (windowOpen) SetOpen(false);
        }

        private static void SetOpen(bool open)
        {
            windowOpen = open;
            MenuButton.SetMenuInput(!open);
        }

        internal static void ShowMessage(string text)
        {
            message = text ?? "";
            messageUntil = Time.realtimeSinceStartup + 10f;
            Overlay.Message(message, 4f);
        }

        internal static void Draw()
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.fontSize = 16;
                labelStyle.normal.textColor = Color.white;
                shadowStyle = new GUIStyle(labelStyle);
                shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
                warnStyle = new GUIStyle(labelStyle);
                warnStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
                warnStyle.wordWrap = true;
            }
            if (portText == null)
            {
                portText = Plugin.CfgPort.Value.ToString();
                addressText = Plugin.CfgAddress.Value;
            }

            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.F8)
            {
                SetOpen(!windowOpen);
                e.Use();
            }

            bool playing = Lockstep.InMatch && Lockstep.GameplayScene;
            if (ShipCoop.Active && e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (Time.realtimeSinceStartup < leaveArmedUntil) { e.Use(); MenuFlow.LeaveCoop(); return; }
                leaveArmedUntil = Time.realtimeSinceStartup + 2f;
                Overlay.Message("Нажмите Esc ещё раз, чтобы выйти из совместной игры.", 2f);
                e.Use();
            }
            if (Lockstep.InMatch) DrawPause(); else Overlay.Pause(null, null);

            if (windowOpen)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, "Platypus Reclayed CO-OP " + Plugin.Version);
            }


        }

        private static void Shadowed(Rect r, string text, GUIStyle style)
        {
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, shadowStyle);
            GUI.Label(r, text, style);
        }

        private static void DrawHint()
        {
            if (Lockstep.GameplayScene && !Session.Busy) return; // ordinary single-player level
            string text = Session.Busy ? "CO-OP: " + Session.Status + "   [F8]" : "F8 - online co-op";
            Shadowed(new Rect(12, Screen.height - 32, Screen.width - 24, 26), text, labelStyle);
        }

        private static float leaveArmedUntil;

        /// Simulation paused: waiting for the partner (slow PC, loading) or
        /// for him to reconnect.
        private static void DrawPause()
        {
            if (Session.Reconnecting)
                Overlay.Pause("СВЯЗЬ ПОТЕРЯНА", "Ждём второго игрока... " + Mathf.Max(0, (int)(Session.ReconnectUntil - Time.realtimeSinceStartup)) + " с");
            else if (Lockstep.Stalled && Time.realtimeSinceStartup - Lockstep.StalledSince > 0.7f)
                Overlay.Pause("ПАУЗА", "Ждём второго игрока...");
            else
                Overlay.Pause(null, null);
        }

        private static void DrawHud()
        {
            string text = "CO-OP  you are P" + (Lockstep.LocalSlot + 1) +
                          "   ping " + (int)Session.RttMs + " ms   delay " + Lockstep.Delay +
                          "   tick " + Lockstep.Tick;
            Shadowed(new Rect(12, 8, Screen.width - 24, 26), text, labelStyle);
            if (Lockstep.Warning.Length > 0)
                Shadowed(new Rect(12, 34, Screen.width - 24, 26), Lockstep.Warning, warnStyle);
        }

        private static void DrawWindow(int id)
        {
            if (!Patches.Healthy)
            {
                GUILayout.Label("Online play is disabled: this game build differs from the one the mod was made for (" + Patches.Problem + "). See BepInEx/LogOutput.log.");
                if (GUILayout.Button("Close  [F8]")) SetOpen(false);
                GUI.DragWindow();
                return;
            }

            GUILayout.Label(Session.Status);
            if (!Session.Busy)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Port", GUILayout.Width(110));
                portText = GUILayout.TextField(portText ?? "", 5);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Host IPv4", GUILayout.Width(110));
                addressText = GUILayout.TextField(addressText ?? "", 64);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Create game"))
                {
                    int port;
                    if (ParsePort(out port))
                    {
                        Plugin.CfgPort.Value = port;
                        Session.Host(port);
                    }
                }
                if (GUILayout.Button("Join game"))
                {
                    int port;
                    if (ParsePort(out port))
                    {
                        Plugin.CfgPort.Value = port;
                        Plugin.CfgAddress.Value = (addressText ?? "").Trim();
                        Session.Join(addressText, port);
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("Host: Create game, then choose difficulty, level and ship as usual.\n" +
                                "Player 2: type the host's IPv4 and the same port, then Join game.\n" +
                                "Over the internet the host forwards this TCP port or both use a shared VPN.");
            }
            else
            {
                if (Session.Connected)
                    GUILayout.Label("Ping " + (int)Session.RttMs + " ms   -   you are player " + (Lockstep.LocalSlot + 1));
                if (Lockstep.InMatch)
                    GUILayout.Label("Match tick " + Lockstep.Tick + ", input delay " + Lockstep.Delay + " frames");
                if (GUILayout.Button(Lockstep.InMatch ? "Leave match" : "Disconnect"))
                {
                    if (Lockstep.InMatch) Lockstep.Fail("Left the match.");
                    else Session.Close("Disconnected.", true);
                }
            }
            GUILayout.Label("Controls: arrows / WASD + Space / Ctrl, or a gamepad. Pause is disabled online.");
            if (GUILayout.Button("Close  [F8]")) SetOpen(false);
            GUI.DragWindow();
        }

        private static bool ParsePort(out int port)
        {
            if (int.TryParse((portText ?? "").Trim(), out port) && port > 0 && port < 65536) return true;
            ShowMessage("Port must be a number from 1 to 65535.");
            return false;
        }
    }
}
