using System.Collections.Generic;

using UnityEngine;
using System;
using Photon.Pun;
using System.Linq;
using Photon.Realtime;
using Adrenak.BRW;
using ExitGames.Client.Photon;

namespace Adrenak.UniVoice.PUN2Network {
    /// <summary>
    /// UniVoice Network implementation that uses the existing Photon PUN2 connection
    /// of the application to enable voice chat.
    /// </summary>
    /// <remarks>
    /// Use this class in applications that are already using PUN2 for networking or 
    /// multiplayer and you want to add voice chat to it. This will allow voice chat
    /// between the Players that are in the room.
    /// 
    /// This network implementation doesn't "Create" or "Join" a chatroom, it simply listens
    /// to when the application joins a Photon room and makes voice chat between the Photon
    /// players happen.
    /// 
    /// Create, Join, Leave and Close Chatroom APIs are disabled here. 
    /// 
    /// TIP: To some extent, all events except <see cref="OnAudioReceived"/>, <see cref="OnAudioSent"/>
    /// are not even useful they merely forward PhotonNetwork events. For example, you can keep using 
    /// Photon events for your voice chat UI (Unless you don't want to be tightly bound to Photon, in
    /// which case use them so that you can change the UniVoice network later.)
    /// </remarks>
    public class UniVoicePUN2NetworkEmbedded : MonoBehaviourPunCallbacks, IChatroomNetwork, IOnEventCallback {
        public short OwnID =>
            (short)PhotonNetwork.LocalPlayer.ActorNumber;

        public List<short> PeerIDs {
            get {
                return PhotonNetwork.PlayerList
                    .Where(x => x.ActorNumber != PhotonNetwork.LocalPlayer.ActorNumber)
                    .Select(x => (short)x.ActorNumber).ToList();
            }
        }

        public event Action OnCreatedChatroom;
        public event Action<Exception> OnChatroomCreationFailed;
        public event Action OnClosedChatroom;

        public event Action<short> OnJoinedChatroom;
        public event Action<Exception> OnChatroomJoinFailed;
        public event Action OnLeftChatroom;

        public event Action<short> OnPeerJoinedChatroom;
        public event Action<short> OnPeerLeftChatroom;

        public event Action<short, ChatroomAudioSegment> OnAudioReceived;
        public event Action<short, ChatroomAudioSegment> OnAudioSent;

        [Obsolete("Cannot use new keyword to create instance. Use UniVoicePun2NetworkEmbedded.New() instead")]
        public UniVoicePUN2NetworkEmbedded() { }

        bool isHost;
        public byte PhotonEventCode { get; private set; }

        /// <summary>
        /// Creates a new <see cref="UniVoicePUN2NetworkEmbedded"/> instance
        /// </summary>
        /// <param name="photonEventCode">
        /// The Photon Event Code to be used to send audio event data.
        /// If you're using <see cref="PhotonNetwork.RaiseEvent(byte, object, RaiseEventOptions, SendOptions)"/>,
        /// be sure to pass a code that you are NOT using. 
        /// For more info, go to this link: https://web.archive.org/web/20220611162035/https://doc.photonengine.com/en-us/pun/current/gameplay/rpcsandraiseevent
        /// and read the RaiseEvent section
        /// </param>
        /// <param name="reliable">
        /// Whether the audio data should be sent reliably or not. 
        /// If you're sending audio in short segments (100ms or less), I advise this be false
        /// as reliable data transfer *may* cause congesion in data transfer. (Not too sure)
        /// If you're sending larger chunks of audio less frequently, it may not be an issue.
        /// Default: false
        /// </param>
        /// <returns></returns>
        public static UniVoicePUN2NetworkEmbedded New(byte photonEventCode, bool reliable = false) {
            var go = new GameObject("UniVoicePUN2NetworkEmbedded");
            go.hideFlags = HideFlags.DontSave;
            var instance = go.AddComponent<UniVoicePUN2NetworkEmbedded>();
            instance.PhotonEventCode = photonEventCode;
            return instance;
        }

        #region PUN2 CALLBACKS
        public override void OnJoinedRoom() {
            isHost = PhotonNetwork.LocalPlayer.IsMasterClient;
            if (isHost)
                OnCreatedChatroom?.Invoke();
            else {
                // Invoke peer joined for each person already in the network
                foreach (var other in PeerIDs)
                    OnPeerJoinedChatroom?.Invoke(other);
                // Invoke event that we have joined the network
                OnJoinedChatroom?.Invoke((short)PhotonNetwork.LocalPlayer.ActorNumber);
            }
        }

        public override void OnCreateRoomFailed(short returnCode, string message) {
            OnChatroomCreationFailed?.Invoke(new Exception(returnCode + " " + message));
        }

        public override void OnLeftRoom() {
            OnClosedChatroom?.Invoke();
            OnLeftChatroom?.Invoke();
        }

        public override void OnJoinRoomFailed(short returnCode, string message) {
            OnChatroomJoinFailed?.Invoke(new Exception(returnCode + " " + message));
        }

        public override void OnJoinRandomFailed(short returnCode, string message) {
            OnChatroomJoinFailed?.Invoke(new Exception(returnCode + " " + message));
        }

        public override void OnDisconnected(DisconnectCause cause) {
            OnClosedChatroom?.Invoke();
            OnLeftChatroom?.Invoke();
        }

        public override void OnPlayerEnteredRoom(Player newPlayer) {
            OnPeerJoinedChatroom?.Invoke((short)newPlayer.ActorNumber);
        }

        public override void OnPlayerLeftRoom(Player otherPlayer) {
            OnPeerLeftChatroom?.Invoke((short)otherPlayer.ActorNumber);
        }

        public override void OnMasterClientSwitched(Player newMasterClient) {
            isHost = newMasterClient.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber;
        }
        #endregion

        #region IChatroomNetwork Methods
        public void CloseChatroom(object data = null) => ShowStandardWarning();
        public void HostChatroom(object data = null) => ShowStandardWarning();
        public void JoinChatroom(object data = null) => ShowStandardWarning();
        public void LeaveChatroom(object data = null) => ShowStandardWarning();

        public void SendAudioSegment(short peerID, ChatroomAudioSegment data) {
            var player = GetPlayerByID(peerID);
            if (player == null) {
                Debug.LogError("No peer with ID " + peerID + " found! Cannot send audio");
                return;
            }

            var w = new BytesWriter();
            w.WriteInt(data.segmentIndex);
            w.WriteInt(data.frequency);
            w.WriteInt(data.channelCount);
            w.WriteFloatArray(data.samples);

            var options = new RaiseEventOptions { Receivers = ReceiverGroup.Others };
            PhotonNetwork.RaiseEvent(PhotonEventCode, w.Bytes, options, SendOptions.SendUnreliable);
            OnAudioSent?.Invoke(peerID, data);
        }
        #endregion

        // Photon Event
        public void OnEvent(EventData photonEvent) {
            if(photonEvent.Code == PhotonEventCode) {
                var segment = new ChatroomAudioSegment();
                
                // Data is read in the same order as it was written.
                var r = new BytesReader((byte[])photonEvent.CustomData);
                segment.segmentIndex = r.ReadInt();
                segment.frequency = r.ReadInt();
                segment.channelCount = r.ReadInt();
                segment.samples = r.ReadFloatArray();
                OnAudioReceived?.Invoke((short)photonEvent.Sender, segment);
            }
        }

        Player GetPlayerByID(short id) {
            foreach (var player in PhotonNetwork.PlayerList)
                if (player.ActorNumber == id)
                    return player;
            return null;
        }

        public void Dispose() {
            Destroy(gameObject);
        }

        void ShowStandardWarning() =>
            Debug.LogWarning($"{GetType().Name} doesn't host, close, join or leave chatrooms. " +
            $"It uses the Photon SDK connection. Ignoring...");
    }
}
