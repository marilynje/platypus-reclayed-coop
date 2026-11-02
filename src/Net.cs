using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace ReclayedCoop
{
    internal enum Role { None, Host, Client }

    internal static class Msg
    {
        public const byte Hello = 1;
        public const byte Start = 2;
        public const byte Input = 3;
        public const byte Bye = 4;
        public const byte Ping = 5;
        public const byte Pong = 6;
        public const byte Goto = 7;
        public const byte Scene = 8;   // host moved between menu scenes (not in a match)
        public const byte Ship = 9;    // player 2 locked in a ship
        public const byte Cursor = 10; // host mouse + selected menu row (menus only)
        public const byte ShipState = 11; // our ship panel: joined, ready, ship, skin
        public const byte Resume = 12; // after a reconnect: match gen + last tick received
        public const byte Propose = 13; // player 2 suggests a difficulty (button name)
        public const byte Dump = 14;    // desync found at this tick: write your state too
        public const byte Unpause = 15; // RESUME pressed in the shared pause menu
    }

    /// One TCP connection between exactly two games. Frames are
    /// [int32 length][byte type][payload], length counting type + payload.
    internal static class Session
    {
        private const int Magic = 0x52435031; // "RCP1"

        internal static Role Role = Role.None;
        internal static bool Connected;            // handshake completed
        internal static string Status = "Not connected";
        internal static string PeerShip = "";
        internal static string PeerSkin = "";
        internal static double RttMs;

        // A match survives a dropped connection: the host listens again on
        // the same port, player 2 dials again, then both resend missing input.
        internal static bool Reconnecting;
        internal static float ReconnectUntil;
        private const float ReconnectSeconds = 60f;
        private static int lastPort;
        private static IPAddress lastAddress;
        private static float nextRetry;
        private static float lastRx;

        private static Socket listener;
        private static Socket sock;
        private static IAsyncResult connecting;
        private static float connectStarted;
        private static byte[] rx = new byte[64 * 1024];
        private static int rxLen;
        private static float nextPing;
        private static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        internal static bool IsOpen
        {
            get { return SteamMode ? steamPeerSet && !Reconnecting : sock != null && connecting == null; }
        }
        internal static bool Busy { get { return listener != null || sock != null || SteamMode; } }

        // Steam transport (SteamLink): messages instead of a TCP socket.
        internal static bool SteamMode;
        private static Steamworks.CSteamID steamPeer;
        private static bool steamPeerSet;
        private static bool helloSent;
        private static readonly List<KeyValuePair<Steamworks.CSteamID, byte[]>> steamInbox = new List<KeyValuePair<Steamworks.CSteamID, byte[]>>();

        internal static bool SteamPeerSet { get { return steamPeerSet; } }
        internal static bool HasSteamPeer(Steamworks.CSteamID id) { return steamPeerSet && steamPeer == id; }

        internal static void BeginSteamHost()
        {
            Close(null, false);
            SteamMode = true;
            Role = Role.Host;
            Status = "Steam invite open - waiting for a friend...";
            Plugin.Log.LogInfo("Hosting through Steam");
        }

        internal static void BeginSteamClient(Steamworks.CSteamID host)
        {
            SteamMode = true;
            Role = Role.Client;
            steamPeer = host;
            steamPeerSet = true;
            lastRx = Time.realtimeSinceStartup;
            OnSocketReady();
        }

        /// Host: the friend opened a session (first contact or after a drop).
        internal static void OnSteamPeer(Steamworks.CSteamID who)
        {
            steamPeer = who;
            steamPeerSet = true;
            lastRx = Time.realtimeSinceStartup;
            if (!Connected) OnSocketReady();
        }

        private static void SteamPump()
        {
            ReconnectPump();
            if (!steamPeerSet) return;
            SteamReceive();
            if (Connected && Lockstep.InMatch && Time.realtimeSinceStartup - lastRx > 15f)
            {
                LinkLost("no data for 15 s");
                return;
            }
            if (Connected && Time.realtimeSinceStartup >= nextPing)
            {
                nextPing = Time.realtimeSinceStartup + 1f;
                MemoryStream ms = new MemoryStream();
                BinaryWriter w = new BinaryWriter(ms);
                w.Write(clock.ElapsedTicks);
                w.Flush();
                Send(Msg.Ping, ms.ToArray());
            }
        }

        private static bool SteamReceive()
        {
            steamInbox.Clear();
            try { SteamLink.Receive(steamInbox); }
            catch (Exception e) { Plugin.Log.LogError("Steam receive: " + e.Message); return false; }
            bool any = false;
            for (int i = 0; i < steamInbox.Count; i++)
            {
                if (!SteamMode) break;
                if (steamPeerSet && steamInbox[i].Key != steamPeer) continue; // not our partner
                byte[] data = steamInbox[i].Value;
                if (data.Length < 1) continue;
                lastRx = Time.realtimeSinceStartup;
                any = true;
                byte[] payload = new byte[data.Length - 1];
                Buffer.BlockCopy(data, 1, payload, 0, payload.Length);
                if (data[0] == Msg.Hello && !helloSent) OnSocketReady(); // answer first
                Dispatch(data[0], payload);
            }
            return any;
        }

        internal static string GameId()
        {
            return typeof(global::Player).Assembly.ManifestModule.ModuleVersionId.ToString("N") + "-" +
                   typeof(Platypus.Core.Globals).Assembly.ManifestModule.ModuleVersionId.ToString("N");
        }

        // ------------------------------------------------------------ setup

        internal static void Host(int port)
        {
            Close(null, false);
            try
            {
                listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listener.Bind(new IPEndPoint(IPAddress.Any, port));
                listener.Listen(1);
                lastPort = port;
                Role = Role.Host;
                Status = "Waiting for player 2 on TCP port " + port + "...";
                Plugin.Log.LogInfo("Hosting on port " + port);
            }
            catch (Exception e)
            {
                Close("Cannot open port " + port + ": " + e.Message, false);
            }
        }

        internal static void Join(string address, int port)
        {
            Close(null, false);
            IPAddress ip = null;
            string text = (address ?? "").Trim();
            if (!IPAddress.TryParse(text, out ip))
            {
                ip = null;
                try
                {
                    foreach (IPAddress a in Dns.GetHostAddresses(text))
                        if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; }
                }
                catch (Exception) { }
            }
            if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork)
            {
                Status = "Enter the host's IPv4 address.";
                return;
            }
            lastAddress = ip;
            lastPort = port;
            Role = Role.Client;
            Status = "Connecting to " + ip + ":" + port + "...";
            Plugin.Log.LogInfo("Joining " + ip + ":" + port);
            Dial();
        }

        private static void Dial()
        {
            try
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sock.NoDelay = true;
                connectStarted = Time.realtimeSinceStartup;
                connecting = sock.BeginConnect(new IPEndPoint(lastAddress, lastPort), null, null);
            }
            catch (Exception e)
            {
                DialFailed("Connection failed: " + e.Message);
            }
        }

        private static void DialFailed(string reason)
        {
            if (Reconnecting)
            {
                try { if (sock != null) sock.Close(); } catch (Exception) { }
                sock = null;
                connecting = null;
                nextRetry = Time.realtimeSinceStartup + 2f;
                return;
            }
            Close(reason, false);
        }

        /// The connection broke without a goodbye: during a match wait for
        /// the partner to come back instead of ending it.
        internal static void LinkLost(string why)
        {
            if (Reconnecting) return;
            if (!Lockstep.InMatch || !Connected)
            {
                Lockstep.Fail("Связь со вторым игроком потеряна.", false);
                return;
            }
            Plugin.Log.LogWarning("Link lost (" + why + "), waiting up to " + ReconnectSeconds + " s for the partner");
            if (SteamMode)
            {
                Connected = false;
                helloSent = false;
                Reconnecting = true;
                ReconnectUntil = Time.realtimeSinceStartup + ReconnectSeconds;
                nextRetry = Time.realtimeSinceStartup + 1f;
                return;
            }
            try { if (sock != null) sock.Close(); } catch (Exception) { }
            sock = null;
            connecting = null;
            rxLen = 0;
            Connected = false;
            Reconnecting = true;
            ReconnectUntil = Time.realtimeSinceStartup + ReconnectSeconds;
            nextRetry = Time.realtimeSinceStartup + 1f;
            if (Role == Role.Host)
            {
                try
                {
                    listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    listener.Bind(new IPEndPoint(IPAddress.Any, lastPort));
                    listener.Listen(1);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Cannot listen again on " + lastPort + ": " + e.Message);
                }
            }
        }

        private static void ReconnectPump()
        {
            if (!Reconnecting) return;
            if (!Lockstep.InMatch) { Reconnecting = false; return; }
            if (Time.realtimeSinceStartup > ReconnectUntil)
            {
                Reconnecting = false;
                Lockstep.Fail("Второй игрок не вернулся - матч закончен.", false);
                return;
            }
            if (SteamMode)
            {
                // Player 2 says hello again until the host answers.
                if (Role == Role.Client && steamPeerSet && Time.realtimeSinceStartup >= nextRetry)
                {
                    nextRetry = Time.realtimeSinceStartup + 2f;
                    Plugin.Log.LogInfo("Reconnecting through Steam...");
                    OnSocketReady();
                }
                return;
            }
            if (Role == Role.Client && sock == null && connecting == null && Time.realtimeSinceStartup >= nextRetry)
            {
                Plugin.Log.LogInfo("Reconnecting to " + lastAddress + ":" + lastPort + "...");
                Dial();
            }
        }

        internal static void HandleCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--coop-host")
                {
                    int port = Plugin.CfgPort.Value;
                    if (i + 1 < args.Length) int.TryParse(args[i + 1], out port);
                    Host(port);
                }
                else if (args[i] == "--coop-join" && i + 1 < args.Length)
                {
                    string target = args[i + 1];
                    int port = Plugin.CfgPort.Value;
                    int colon = target.LastIndexOf(':');
                    if (colon > 0)
                    {
                        int.TryParse(target.Substring(colon + 1), out port);
                        target = target.Substring(0, colon);
                    }
                    Join(target, port);
                }
            }
        }

        // ------------------------------------------------------------ pump

        /// Called every rendered frame.
        internal static void Pump()
        {
            if (SteamMode) { SteamPump(); return; }
            ReconnectPump();
            if (listener != null && sock == null)
            {
                try
                {
                    if (listener.Poll(0, SelectMode.SelectRead))
                    {
                        sock = listener.Accept();
                        sock.NoDelay = true;
                        try { listener.Close(); } catch (Exception) { }
                        listener = null;
                        Plugin.Log.LogInfo("Player 2 connected from " + sock.RemoteEndPoint);
                        OnSocketReady();
                    }
                }
                catch (Exception e)
                {
                    if (Reconnecting) Plugin.Log.LogWarning("Accept: " + e.Message);
                    else Close("Network error: " + e.Message, false);
                }
                return;
            }
            if (connecting != null)
            {
                if (connecting.IsCompleted)
                {
                    IAsyncResult pending = connecting;
                    connecting = null;
                    try
                    {
                        sock.EndConnect(pending);
                        OnSocketReady();
                    }
                    catch (Exception e)
                    {
                        DialFailed("Connection failed: " + e.Message);
                    }
                }
                else if (Time.realtimeSinceStartup - connectStarted > (Reconnecting ? 4f : 10f))
                {
                    connecting = null;
                    DialFailed("Connection timed out. Check the IP, port and the host's firewall.");
                }
                return;
            }
            if (sock == null) return;
            Receive(0);
            // Silence for this long means the link is dead even if TCP did not say so
            // (pulled cable, sleeping PC). The partner pings every second.
            if (Connected && Lockstep.InMatch && Time.realtimeSinceStartup - lastRx > 15f)
            {
                LinkLost("no data for 15 s");
                return;
            }
            if (Connected && Time.realtimeSinceStartup >= nextPing)
            {
                nextPing = Time.realtimeSinceStartup + 1f;
                MemoryStream ms = new MemoryStream();
                BinaryWriter w = new BinaryWriter(ms);
                w.Write(clock.ElapsedTicks);
                w.Flush();
                Send(Msg.Ping, ms.ToArray());
            }
        }

        /// Waits up to waitMicroseconds for data and dispatches complete frames.
        internal static bool Receive(int waitMicroseconds)
        {
            if (SteamMode)
            {
                if (!steamPeerSet) return false;
                bool got = SteamReceive();
                if (!got && waitMicroseconds > 0) System.Threading.Thread.Sleep(1);
                return got;
            }
            if (sock == null || connecting != null) return false;
            try
            {
                if (!sock.Poll(waitMicroseconds, SelectMode.SelectRead)) return false;
                int available = sock.Available;
                if (available <= 0)
                {
                    LinkLost("closed by the remote side");
                    return false;
                }
                if (rx.Length < rxLen + available)
                {
                    byte[] bigger = new byte[Math.Max(rx.Length * 2, rxLen + available)];
                    Buffer.BlockCopy(rx, 0, bigger, 0, rxLen);
                    rx = bigger;
                }
                int n = sock.Receive(rx, rxLen, available, SocketFlags.None);
                if (n <= 0)
                {
                    LinkLost("closed by the remote side");
                    return false;
                }
                rxLen += n;
                lastRx = Time.realtimeSinceStartup;
                ParseFrames();
                return true;
            }
            catch (Exception e)
            {
                LinkLost(e.Message);
                return false;
            }
        }

        private static void ParseFrames()
        {
            int pos = 0;
            while (sock != null && rxLen - pos >= 5)
            {
                int len = BitConverter.ToInt32(rx, pos);
                if (len < 1 || len > 64 * 1024 * 1024)
                {
                    Lockstep.Fail("Protocol error (bad frame).");
                    return;
                }
                if (rxLen - pos - 4 < len) break;
                byte type = rx[pos + 4];
                byte[] payload = new byte[len - 1];
                Buffer.BlockCopy(rx, pos + 5, payload, 0, len - 1);
                pos += 4 + len;
                Dispatch(type, payload);
            }
            if (sock == null) { rxLen = 0; return; }
            if (pos > 0)
            {
                Buffer.BlockCopy(rx, pos, rx, 0, rxLen - pos);
                rxLen -= pos;
            }
        }

        private static void Dispatch(byte type, byte[] payload)
        {
            BinaryReader r = new BinaryReader(new MemoryStream(payload), Encoding.UTF8);
            switch (type)
            {
                case Msg.Hello: OnHello(r); break;
                case Msg.Input:
                    {
                        int gen = r.ReadByte();
                        int tick = r.ReadInt32();
                        Lockstep.Frame f;
                        f.H = r.ReadSByte();
                        f.V = r.ReadSByte();
                        f.Flags = r.ReadByte();
                        int hashTick = r.ReadInt32();
                        uint[] h = new uint[Lockstep.HashGroups];
                        for (int i = 0; i < h.Length; i++) h[i] = r.ReadUInt32();
                        Lockstep.OnRemoteInput(gen, tick, f, hashTick, h);
                        break;
                    }
                case Msg.Start:
                    {
                        int seed = r.ReadInt32();
                        int delay = r.ReadInt32();
                        int gen = r.ReadInt32();
                        string json = r.ReadString();
                        Lockstep.ClientStart(seed, delay, gen, json);
                        break;
                    }
                case Msg.Goto: Lockstep.ClientGoto(r.ReadInt32()); break;
                case Msg.Scene:
                    {
                        int scene = r.ReadInt32();
                        int difficulty = r.ReadInt32();
                        string level = r.ReadString();
                        int area = r.ReadInt32();
                        MenuFlow.OnHostScene(scene, difficulty, level, area);
                        break;
                    }
                case Msg.Ship: MenuFlow.OnPeerShip(r.ReadString(), r.ReadString()); break;
                case Msg.Cursor:
                    {
                        float x = r.ReadSingle();
                        float y = r.ReadSingle();
                        bool visible = r.ReadBoolean();
                        MenuFlow.OnPeerCursor(x, y, visible, r.ReadString());
                        break;
                    }
                case Msg.Ping: Send(Msg.Pong, payload); break;
                case Msg.Pong:
                    {
                        long sent = r.ReadInt64();
                        RttMs = (clock.ElapsedTicks - sent) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        break;
                    }
                case Msg.Bye:
                    r.ReadString();
                    Lockstep.Fail(Role == Role.Host ? "Второй игрок вышел." : "Хост вышел.", false);
                    break;
                case Msg.ShipState:
                    {
                        bool joined = r.ReadBoolean();
                        bool locked = r.ReadBoolean();
                        string ship = r.ReadString();
                        ShipCoop.OnRemoteState(joined, locked, ship, r.ReadString());
                        break;
                    }
                case Msg.Propose: MenuFlow.OnProposal(r.ReadString()); break;
                case Msg.Unpause: SharedPause.OnPartnerResume(); break;
                case Msg.Dump: DesyncDump.Write(r.ReadInt32(), "partner found the desync"); break;
                case Msg.Resume:
                    {
                        int gen = r.ReadInt32();
                        Lockstep.OnResume(gen, r.ReadInt32());
                        break;
                    }
                default: Lockstep.Fail("Protocol error (unknown message " + type + ")."); break;
            }
        }

        // ------------------------------------------------------------ handshake

        private static void OnSocketReady()
        {
            rxLen = 0;
            Connected = false;
            helloSent = true;
            Status = "Connected, checking versions...";
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write(Magic);
            w.Write(Plugin.Protocol);
            w.Write(Plugin.Version);
            w.Write(GameId());
            w.Write(Role == Role.Client ? Plugin.CfgShip.Value ?? "" : "");
            w.Write(Role == Role.Client ? Plugin.CfgSkin.Value ?? "" : "");
            w.Flush();
            Send(Msg.Hello, ms.ToArray());
        }

        private static void OnHello(BinaryReader r)
        {
            int magic = r.ReadInt32();
            int protocol = r.ReadInt32();
            string version = r.ReadString();
            string gameId = r.ReadString();
            string ship = r.ReadString();
            string skin = r.ReadString();
            if (magic != Magic)
            {
                Lockstep.Fail("That port is not a Platypus Reclayed CO-OP game.");
                return;
            }
            if (protocol != Plugin.Protocol)
            {
                Lockstep.Fail("Mod version mismatch: partner has " + version + ", you have " + Plugin.Version + ".");
                return;
            }
            if (gameId != GameId())
            {
                Lockstep.Fail("Different game builds (demo vs full game, or a different update). Both players need the same version.");
                return;
            }
            PeerShip = ship;
            PeerSkin = skin;
            Connected = true;
            RttMs = 0;
            Application.runInBackground = true;
            Status = Role == Role.Host
                ? "Player 2 connected. Pick difficulty, level and ship as usual - player 2 follows."
                : "Connected. Waiting for the host to start a level...";
            Plugin.Log.LogInfo("Handshake ok, partner mod " + version);
            lastRx = Time.realtimeSinceStartup;
            if (Reconnecting)
            {
                Reconnecting = false;
                Plugin.Log.LogInfo("Reconnected - resuming the match");
                Lockstep.SendResume();
                return;
            }
            MenuFlow.OnConnected();
        }

        // ------------------------------------------------------------ send

        internal static void SendStart(int seed, int delay, int gen, string json)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write(seed);
            w.Write(delay);
            w.Write(gen);
            w.Write(json);
            w.Flush();
            Send(Msg.Start, ms.ToArray());
        }

        internal static void SendScene(int scene, int difficulty, string level, int area)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write(scene);
            w.Write(difficulty);
            w.Write(level ?? "");
            w.Write(area);
            w.Flush();
            Send(Msg.Scene, ms.ToArray());
        }

        internal static void SendCursor(float x, float y, bool visible, string selected)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write(x);
            w.Write(y);
            w.Write(visible);
            w.Write(selected ?? "");
            w.Flush();
            Send(Msg.Cursor, ms.ToArray());
        }

        internal static void SendShipState(bool joined, bool locked, string ship, string skin)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write(joined);
            w.Write(locked);
            w.Write(ship ?? "");
            w.Write(skin ?? "");
            w.Flush();
            Send(Msg.ShipState, ms.ToArray());
        }

        internal static void SendUnpause()
        {
            Send(Msg.Unpause, new byte[0]);
        }

        internal static void SendDumpRequest(int tick)
        {
            Send(Msg.Dump, BitConverter.GetBytes(tick));
        }

        internal static void SendProposal(string button)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write(button ?? "");
            w.Flush();
            Send(Msg.Propose, ms.ToArray());
        }

        internal static void SendResume(int gen, int lastTick)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write(gen);
            w.Write(lastTick);
            w.Flush();
            Send(Msg.Resume, ms.ToArray());
        }

        internal static void SendShip(string ship, string skin)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write(ship ?? "");
            w.Write(skin ?? "");
            w.Flush();
            Send(Msg.Ship, ms.ToArray());
        }

        internal static void SendGoto(int scene)
        {
            Send(Msg.Goto, BitConverter.GetBytes(scene));
        }

        internal static void SendInput(int tick, Lockstep.Frame f, int hashTick, uint[] hash)
        {
            byte[] p = new byte[1 + 4 + 3 + 4 + 4 * Lockstep.HashGroups];
            p[0] = (byte)Lockstep.MatchGen;
            Buffer.BlockCopy(BitConverter.GetBytes(tick), 0, p, 1, 4);
            p[5] = (byte)f.H;
            p[6] = (byte)f.V;
            p[7] = f.Flags;
            Buffer.BlockCopy(BitConverter.GetBytes(hashTick), 0, p, 8, 4);
            for (int i = 0; i < Lockstep.HashGroups; i++)
                Buffer.BlockCopy(BitConverter.GetBytes(hash[i]), 0, p, 12 + 4 * i, 4);
            Send(Msg.Input, p);
        }

        private static void Send(byte type, byte[] payload)
        {
            if (Reconnecting && type != Msg.Hello) return; // input is resent after the reconnect
            if (SteamMode)
            {
                if (!steamPeerSet) return;
                bool ok = false;
                try { ok = SteamLink.Send(steamPeer, type, payload); }
                catch (Exception e) { Plugin.Log.LogError("Steam send: " + e.Message); }
                if (!ok && type != Msg.Hello) LinkLost("Steam send failed");
                return;
            }
            if (!SendRaw(type, payload)) LinkLost("send failed");
        }

        private static bool SendRaw(byte type, byte[] payload)
        {
            if (SteamMode)
            {
                if (!steamPeerSet) return false;
                try { return SteamLink.Send(steamPeer, type, payload); }
                catch (Exception) { return false; }
            }
            if (sock == null || connecting != null) return false;
            byte[] frame = new byte[5 + payload.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(payload.Length + 1), 0, frame, 0, 4);
            frame[4] = type;
            Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);
            try
            {
                int off = 0;
                while (off < frame.Length)
                {
                    int n = sock.Send(frame, off, frame.Length - off, SocketFlags.None);
                    if (n <= 0) return false;
                    off += n;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void Close(string reason, bool sendBye)
        {
            if (sendBye && Connected && (sock != null || SteamMode))
                SendRaw(Msg.Bye, StringPayload(reason ?? "left"));
            if (SteamMode) SteamLink.Close(steamPeer, steamPeerSet);
            bool wasSteam = SteamMode;
            SteamMode = false;
            steamPeerSet = false;
            helloSent = false;
            try { if (sock != null) sock.Close(); } catch (Exception) { }
            try { if (listener != null) listener.Close(); } catch (Exception) { }
            if (sock != null || listener != null || Role != Role.None || wasSteam)
                Plugin.Log.LogInfo("Session closed: " + (reason ?? "by user"));
            sock = null;
            listener = null;
            connecting = null;
            Connected = false;
            Reconnecting = false;
            Role = Role.None;
            rxLen = 0;
            Status = reason ?? "Not connected";
        }

        private static byte[] StringPayload(string s)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write(s);
            w.Flush();
            return ms.ToArray();
        }
    }
}
