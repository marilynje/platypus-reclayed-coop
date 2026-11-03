using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Platypus.Core;
using Steamworks;
using UnityEngine;

namespace ReclayedCoop
{
    /// Steam invite + Steam networking (relay, no IP or port forwarding):
    /// the host makes a friends-only lobby and opens the Steam invite dialog;
    /// the friend joins it (from the invite or from "+connect_lobby <id>" when
    /// Steam starts the game) and both talk through SteamNetworkingMessages.
    internal static class SteamLink
    {
        private const int Channel = 0;
        private const int SendReliable = 8;

        private static bool callbacksReady;
        private static Callback<GameLobbyJoinRequested_t> joinRequested;
        private static Callback<SteamNetworkingMessagesSessionRequest_t> sessionRequest;
        private static Callback<SteamNetworkingMessagesSessionFailed_t> sessionFailed;
        private static CallResult<LobbyCreated_t> lobbyCreated;
        private static CallResult<LobbyEnter_t> lobbyEntered;

        internal static CSteamID Lobby = CSteamID.Nil;
        private static ulong pendingLobby;
        private static bool menuRequested;
        private static readonly IntPtr[] inbox = new IntPtr[64];

        internal static bool Available
        {
            get
            {
                try { return SteamAPI.IsSteamRunning() && SteamUser.GetSteamID().IsValid(); }
                catch (Exception) { return false; }
            }
        }

        private static bool Init()
        {
            if (callbacksReady) return true;
            if (!Available) return false;
            try
            {
                joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
                sessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
                sessionFailed = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(OnSessionFailed);
                lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
                lobbyEntered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
                callbacksReady = true;
                Plugin.Log.LogInfo("Steam ready: " + SteamFriends.GetPersonaName());
            }
            catch (Exception e) { Plugin.Log.LogError("Steam init: " + e); }
            return callbacksReady;
        }

        /// "+connect_lobby <id>": Steam started the game from an invite.
        internal static void HandleCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "+connect_lobby") ulong.TryParse(args[i + 1], out pendingLobby);
            if (pendingLobby != 0) Plugin.Log.LogInfo("Started from a Steam invite, lobby " + pendingLobby);
        }

        /// Every frame.
        internal static void Tick()
        {
            if (!callbacksReady && Time.frameCount % 60 == 0) Init();
            if (!callbacksReady) return;
            try { SteamAPI.RunCallbacks(); } catch (Exception) { }
            if (pendingLobby != 0 && !Lockstep.InMatch && !LoadingScreen.IsLoading)
            {
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "MainMenuScene")
                {
                    // Accepted an invite in the middle of something: go to the menu first.
                    if (!menuRequested) { menuRequested = true; Lockstep.LoadScene(BuildScenes.MainMenu, null); }
                    return;
                }
                menuRequested = false;
                ulong id = pendingLobby;
                pendingLobby = 0;
                Join(new CSteamID(id));
            }
        }

        // ------------------------------------------------------------ host

        internal static bool Host()
        {
            if (!Init()) { Plugin.Log.LogWarning("Invite: Steam is not running"); return false; }
            Session.BeginSteamHost();
            lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 2));
            return true;
        }

        /// "Invite a Friend" again: the lobby is already there, only the dialog.
        internal static void OpenInvite()
        {
            if (Session.SteamMode && Session.Role == Role.Host && Lobby != CSteamID.Nil)
                OpenDialog();
            else if (!Session.Busy)
                Host();
        }

        private static void OnLobbyCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (ioFailure || r.m_eResult != EResult.k_EResultOK)
            {
                Session.Close("Steam lobby failed (" + r.m_eResult + ").", false);
                return;
            }
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(Lobby, "rcoop", Plugin.Protocol.ToString());
            Plugin.Log.LogInfo("Steam lobby " + Lobby.m_SteamID + " created, opening the invite dialog");
            OpenDialog();
        }

        /// The invite window is part of the Steam overlay: without it nothing opens.
        private static void OpenDialog()
        {
            bool overlay = false;
            try { overlay = SteamUtils.IsOverlayEnabled(); } catch (Exception) { }
            if (!overlay)
            {
                Plugin.Log.LogWarning("Steam overlay is off: the invite window cannot open");
                Ui.ShowMessage(Loc.Get("Coop.NoOverlay"));
            }
            SteamFriends.ActivateGameOverlayInviteDialog(Lobby);
        }

        /// The friend's first message opens a session: accept it (host only,
        /// one partner).
        private static void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t r)
        {
            SteamNetworkingIdentity id = r.m_identityRemote;
            CSteamID who = id.GetSteamID();
            if (!Session.SteamMode) return;
            if (Session.Role == Role.Host && !Session.HasSteamPeer(who) && Session.SteamPeerSet && !Session.Reconnecting) return;
            SteamNetworkingMessages.AcceptSessionWithUser(ref id);
            Plugin.Log.LogInfo("Steam session with " + Name(who));
            if (Session.Role == Role.Host) Session.OnSteamPeer(who);
        }

        private static void OnSessionFailed(SteamNetworkingMessagesSessionFailed_t r)
        {
            if (Session.SteamMode) Session.LinkLost("Steam session failed");
        }

        // ------------------------------------------------------------ friend

        private static void OnJoinRequested(GameLobbyJoinRequested_t r)
        {
            Plugin.Log.LogInfo("Accepted a Steam invite from " + Name(r.m_steamIDFriend));
            if (Lockstep.InMatch) return;
            pendingLobby = r.m_steamIDLobby.m_SteamID;
        }

        internal static void Join(CSteamID lobby)
        {
            if (!Init()) return;
            Session.Close(null, false);
            Session.Status = "Joining the Steam game...";
            lobbyEntered.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        private static void OnLobbyEntered(LobbyEnter_t r, bool ioFailure)
        {
            if (ioFailure || r.m_EChatRoomEnterResponse != 1)
            {
                Ui.ShowMessage(Loc.Get("Coop.JoinFailed"));
                return;
            }
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(Lobby);
            if (owner == SteamUser.GetSteamID()) return; // the host entering its own lobby
            Plugin.Log.LogInfo("Joined Steam lobby of " + Name(owner));
            Session.BeginSteamClient(owner);
        }

        // ------------------------------------------------------------ transport

        internal static bool Send(CSteamID peer, byte type, byte[] payload)
        {
            SteamNetworkingIdentity id = new SteamNetworkingIdentity();
            id.SetSteamID(peer);
            byte[] buf = new byte[1 + payload.Length];
            buf[0] = type;
            Buffer.BlockCopy(payload, 0, buf, 1, payload.Length);
            GCHandle h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                EResult r = SteamNetworkingMessages.SendMessageToUser(ref id, h.AddrOfPinnedObject(), (uint)buf.Length, SendReliable, Channel);
                return r == EResult.k_EResultOK;
            }
            finally { h.Free(); }
        }

        /// Messages waiting from Steam: (sender, bytes).
        internal static int Receive(List<KeyValuePair<CSteamID, byte[]>> into)
        {
            int n = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, inbox, inbox.Length);
            for (int i = 0; i < n; i++)
            {
                SteamNetworkingMessage_t m = SteamNetworkingMessage_t.FromIntPtr(inbox[i]);
                byte[] data = new byte[m.m_cbSize];
                if (m.m_cbSize > 0) Marshal.Copy(m.m_pData, data, 0, m.m_cbSize);
                into.Add(new KeyValuePair<CSteamID, byte[]>(m.m_identityPeer.GetSteamID(), data));
                SteamNetworkingMessage_t.Release(inbox[i]);
            }
            return n;
        }

        internal static void Close(CSteamID peer, bool hadPeer)
        {
            pendingLobby = 0;
            menuRequested = false;
            try
            {
                if (lobbyCreated != null) lobbyCreated.Cancel();
                if (lobbyEntered != null) lobbyEntered.Cancel();
                if (hadPeer)
                {
                    SteamNetworkingIdentity id = new SteamNetworkingIdentity();
                    id.SetSteamID(peer);
                    SteamNetworkingMessages.CloseSessionWithUser(ref id);
                }
                if (Lobby.IsValid()) SteamMatchmaking.LeaveLobby(Lobby);
            }
            catch (Exception) { }
            Lobby = CSteamID.Nil;
        }

        internal static string Name(CSteamID id)
        {
            try { return SteamFriends.GetFriendPersonaName(id); }
            catch (Exception) { return id.m_SteamID.ToString(); }
        }
    }
}
