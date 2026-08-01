using System;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KaitouOnline
{
    public sealed class KaitouOnlineSession : MonoBehaviour
    {
        public static KaitouOnlineSession Instance { get; private set; }
        public event Action<string> StatusChanged;
        public event Action<string> JoinCodeChanged;
        public event Action<Envelope> MessageReceived;

        [SerializeField, Range(2, 4)] private int roomPlayerCount = 2;
        [SerializeField, Range(0, 4)] private int confirmedHumanPlayers;
        [SerializeField, Range(0, 2)] private int cpuPlayers;
        [SerializeField] private bool participantsConfirmed;
        private string joinCode = "";
        private int sequence;
        private bool connecting;
        private int gameSeed;
        private bool onlineGameStarted;
        private readonly string[] playerNames = { "Player1", "Player2", "Player3", "Player4" };

        public string JoinCode => joinCode;
        public int RoomPlayerCount => roomPlayerCount;
        public int ConfirmedHumanPlayers => confirmedHumanPlayers;
        public bool ParticipantsConfirmed => participantsConfirmed;
        public int CpuPlayers => cpuPlayers;
        public int LocalSeat =>
            NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening
                ? -1
                : NetworkManager.Singleton.IsHost ? 0 :
                  Mathf.Max(1, (int)NetworkManager.Singleton.LocalClientId);
        public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        public int ConnectedPlayers => NetworkManager.Singleton == null
            ? 0 : NetworkManager.Singleton.ConnectedClientsIds.Count;
        public bool IsOnline => NetworkManager.Singleton != null &&
                                NetworkManager.Singleton.IsListening;
        public int GameSeed => gameSeed;
        public bool HasStartedOnlineGame => onlineGameStarted;
        public string GetPlayerName(int seat)
        {
            if (seat < 0 || seat >= playerNames.Length) return $"Player{seat + 1}";
            return string.IsNullOrWhiteSpace(playerNames[seat])
                ? $"Player{seat + 1}" : playerNames[seat];
        }

        public void SetLocalPlayerName(string value)
        {
            if (!IsOnline || LocalSeat < 0) return;
            string normalized = NormalizePlayerName(value, LocalSeat);
            if (IsHost)
            {
                playerNames[LocalSeat] = normalized;
                Send(MessageType.PlayerNameUpdate, -1, Protocol.Json(
                    new OnlinePlayerName { seat = LocalSeat, value = normalized }));
                BroadcastLobby();
            }
            else
            {
                SendAction(new ActionRequest
                {
                    action = "set_player_name",
                    data = Protocol.Json(new OnlinePlayerName
                        { seat = LocalSeat, value = normalized })
                });
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            EnsureNetworkManager();
            RegisterCallbacks();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
            UnregisterCallbacks();
        }

        public async void CreateRoom() => await CreateRoomAsync();
        public async void JoinRoom(string code) => await JoinRoomAsync(code);

        public async Task CreateRoomAsync()
        {
            if (connecting) return;
            connecting = true;
            try
            {
                confirmedHumanPlayers = 0;
                participantsConfirmed = false;
                cpuPlayers = 0;
                roomPlayerCount = 2;
                await SignInAsync();
                NetworkManager manager = NetworkManager.Singleton;
                UnityTransport transport = manager.GetComponent<UnityTransport>();
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(3);
                joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                transport.SetRelayServerData(new RelayServerData(allocation, RelayConnectionType()));
                if (!manager.StartHost()) throw new InvalidOperationException("ホストを開始できませんでした。");
                RegisterMessageHandler();
                JoinCodeChanged?.Invoke(joinCode);
                Report($"部屋を作成しました。参加コード：{joinCode}");
                BroadcastLobby();
            }
            catch (Exception ex) { Report("部屋作成に失敗しました：" + ex.Message); }
            finally { connecting = false; }
        }

        public async Task JoinRoomAsync(string code)
        {
            if (connecting) return;
            string normalized = (code ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(normalized))
            {
                Report("参加コードを入力してください。");
                return;
            }
            connecting = true;
            try
            {
                await SignInAsync();
                NetworkManager manager = NetworkManager.Singleton;
                UnityTransport transport = manager.GetComponent<UnityTransport>();
                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(normalized);
                transport.SetRelayServerData(new RelayServerData(allocation, RelayConnectionType()));
                if (!manager.StartClient()) throw new InvalidOperationException("参加を開始できませんでした。");
                RegisterMessageHandler();
                joinCode = normalized;
                JoinCodeChanged?.Invoke(joinCode);
                Report("部屋へ参加しています…");
            }
            catch (Exception ex) { Report("参加に失敗しました：" + ex.Message); }
            finally { connecting = false; }
        }

        public void SendAction(ActionRequest request)
        {
            Send(MessageType.ActionRequest, -1, Protocol.Json(request));
        }

        public void SetCpuPlayers(int count)
        {
            if (!IsHost || onlineGameStarted || !participantsConfirmed) return;
            cpuPlayers = Mathf.Clamp(count, 0,
                Mathf.Max(0, 4 - confirmedHumanPlayers));
            roomPlayerCount = Mathf.Clamp(confirmedHumanPlayers + cpuPlayers, 2, 4);
            BroadcastLobby();
        }

        public bool ConfirmParticipants()
        {
            if (!IsHost || onlineGameStarted || ConnectedPlayers < 2 || ConnectedPlayers > 4)
                return false;
            confirmedHumanPlayers = ConnectedPlayers;
            participantsConfirmed = true;
            cpuPlayers = 0;
            roomPlayerCount = confirmedHumanPlayers;
            BroadcastLobby();
            Report($"参加者{confirmedHumanPlayers}人を確定しました。CPU人数を選んでください。");
            return true;
        }

        public void ReturnToLobby()
        {
            if (!IsHost || !IsOnline) return;
            onlineGameStarted = false;
            participantsConfirmed = false;
            confirmedHumanPlayers = 0;
            cpuPlayers = 0;
            roomPlayerCount = Mathf.Clamp(ConnectedPlayers, 2, 4);
            Send(MessageType.ReturnToLobby, -1, "");
            BroadcastLobby();
            SceneManager.LoadScene("MainMenu");
        }

        public void Send(MessageType type, int targetSeat, string payload)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return;
            if (type == MessageType.StartGame)
            {
                StartGameState start = Protocol.Parse<StartGameState>(payload);
                roomPlayerCount = Mathf.Clamp(start.playerCount, 2, 4);
                confirmedHumanPlayers = Mathf.Clamp(start.humanPlayerCount, 2, 4);
                cpuPlayers = Mathf.Clamp(start.cpuPlayers, 0, 2);
                participantsConfirmed = true;
                gameSeed = start.randomSeed;
                onlineGameStarted = true;
            }
            else if (type == MessageType.RestartGame)
            {
                StartGameState restart = Protocol.Parse<StartGameState>(payload);
                roomPlayerCount = Mathf.Clamp(restart.playerCount, 2, 4);
                confirmedHumanPlayers = Mathf.Clamp(restart.humanPlayerCount, 2, 4);
                cpuPlayers = Mathf.Clamp(restart.cpuPlayers, 0, 2);
                participantsConfirmed = true;
                gameSeed = restart.randomSeed;
                onlineGameStarted = true;
            }
            else if (type == MessageType.ReturnToLobby)
                onlineGameStarted = false;
            Envelope envelope = new Envelope
            {
                version = Protocol.Version,
                type = type,
                senderSeat = LocalSeat,
                targetSeat = targetSeat,
                sequence = ++sequence,
                payload = payload ?? ""
            };
            if (manager.IsServer)
            {
                MessageReceived?.Invoke(envelope);
                foreach (ulong id in manager.ConnectedClientsIds)
                    if (id != manager.LocalClientId) SendTo(id, envelope);
            }
            else SendTo(NetworkManager.ServerClientId, envelope);
        }

        public void Disconnect()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            joinCode = "";
            onlineGameStarted = false;
            participantsConfirmed = false;
            confirmedHumanPlayers = 0;
            cpuPlayers = 0;
            Report("切断しました。");
        }

        private async Task SignInAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        private void EnsureNetworkManager()
        {
            NetworkManager manager = FindFirstObjectByType<NetworkManager>();
            if (manager == null)
            {
                GameObject obj = new GameObject("KaitouNetworkManager");
                UnityTransport transport = obj.AddComponent<UnityTransport>();
                manager = obj.AddComponent<NetworkManager>();
                if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
                manager.NetworkConfig.NetworkTransport = transport;
                DontDestroyOnLoad(obj);
            }
            else if (manager.GetComponent<UnityTransport>() == null)
                manager.gameObject.AddComponent<UnityTransport>();
            if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
            manager.NetworkConfig.NetworkTransport = manager.GetComponent<UnityTransport>();
            manager.transform.SetParent(null);
            DontDestroyOnLoad(manager.gameObject);
        }

        private void RegisterCallbacks()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return;
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        private void UnregisterCallbacks()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return;
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;
            if (manager.CustomMessagingManager != null)
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(Protocol.MessageName);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "IntegratedGameScene") return;

            NetworkManager manager = NetworkManager.Singleton;
            bool listening = manager != null && manager.IsListening;
            Debug.Log($"<color=#70E8FF>【オンライン引継ぎ】scene={scene.name} " +
                      $"session={Instance != null} listening={listening} " +
                      $"host={(manager != null && manager.IsHost)} clients={ConnectedPlayers} " +
                      $"players={roomPlayerCount}</color>");

            if (!listening)
            {
                Report("ゲーム画面への移動時にオンライン接続が切れました。");
                return;
            }

            KaitouOnlineGameBootstrap.ApplyOnlineRoomNow(this);
        }

        private void RegisterMessageHandler()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.CustomMessagingManager == null) return;
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(Protocol.MessageName);
            manager.CustomMessagingManager.RegisterNamedMessageHandler(Protocol.MessageName, OnMessage);
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!IsHost) return;
            if (!onlineGameStarted && participantsConfirmed &&
                ConnectedPlayers != confirmedHumanPlayers)
                ResetLobbySelection();
            if (clientId != NetworkManager.Singleton.LocalClientId)
            {
                SeatAssignment assignment = new SeatAssignment { seat = (int)clientId, host = false };
                SendTo(clientId, Make(MessageType.SeatAssigned, (int)clientId, Protocol.Json(assignment)));
            }
            BroadcastLobby();
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Debug.LogWarning($"【オンライン切断】clientId={clientId} / " +
                             $"local={NetworkManager.Singleton?.LocalClientId}");
            if (NetworkManager.Singleton != null &&
                clientId == NetworkManager.Singleton.LocalClientId)
            {
                Report("オンライン接続が切れました。");
                KaitouOnlineGameBridge.MarkConnectionLost(
                    string.IsNullOrEmpty(NetworkManager.Singleton.DisconnectReason)
                        ? "オンライン接続が切れました。ゲームを停止しています。"
                        : "オンライン接続が切れました：" +
                          NetworkManager.Singleton.DisconnectReason);
            }
            else if (IsHost)
            {
                Report($"参加者が切断しました（現在 {ConnectedPlayers}人）");
                if (onlineGameStarted)
                    KaitouOnlineGameBridge.MarkConnectionLost(
                        "相手とのオンライン接続が切れました。ゲームを停止しています。");
                else
                    ResetLobbySelection();
                BroadcastLobby();
            }
        }

        private void ResetLobbySelection()
        {
            participantsConfirmed = false;
            confirmedHumanPlayers = 0;
            cpuPlayers = 0;
            roomPlayerCount = Mathf.Clamp(ConnectedPlayers, 2, 4);
        }

        private void BroadcastLobby()
        {
            LobbyState state = new LobbyState
            {
                playerCount = roomPlayerCount,
                connectedPlayers = ConnectedPlayers,
                confirmedHumanPlayers = confirmedHumanPlayers,
                cpuPlayers = cpuPlayers,
                participantsConfirmed = participantsConfirmed,
                gameStarted = false,
                playerNames = (string[])playerNames.Clone()
            };
            Send(MessageType.LobbyState, -1, Protocol.Json(state));
        }

        private Envelope Make(MessageType type, int target, string payload) => new Envelope
        {
            version = Protocol.Version,
            type = type,
            senderSeat = LocalSeat,
            targetSeat = target,
            sequence = ++sequence,
            payload = payload
        };

        private void SendTo(ulong clientId, Envelope envelope)
        {
            string json = JsonUtility.ToJson(envelope);
            // WriteValueSafe(string)はUTF-16（1文字2byte）なので、その容量で確保する。
            using FastBufferWriter writer =
                new FastBufferWriter(json.Length * sizeof(char) + 8, Allocator.Temp);
            writer.WriteValueSafe(json);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                Protocol.MessageName, clientId, writer, NetworkDelivery.ReliableSequenced);
        }

        private void OnMessage(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string json);
            Envelope envelope = JsonUtility.FromJson<Envelope>(json);
            if (envelope.version != Protocol.Version)
            {
                Report($"通信バージョンが一致しません（相手:{envelope.version} / 自分:{Protocol.Version}）。Macアプリを再ビルドしてください。");
                return;
            }
            if (envelope.type == MessageType.LobbyState)
            {
                LobbyState lobby = Protocol.Parse<LobbyState>(envelope.payload);
                roomPlayerCount = Mathf.Clamp(lobby.playerCount, 2, 4);
                confirmedHumanPlayers = Mathf.Clamp(lobby.confirmedHumanPlayers, 0, 4);
                cpuPlayers = Mathf.Clamp(lobby.cpuPlayers, 0, 2);
                participantsConfirmed = lobby.participantsConfirmed;
                ApplyPlayerNames(lobby.playerNames);
            }
            else if (envelope.type == MessageType.ActionRequest && IsHost)
            {
                ActionRequest request = Protocol.Parse<ActionRequest>(envelope.payload);
                if (request.action == "set_player_name")
                {
                    OnlinePlayerName update = Protocol.Parse<OnlinePlayerName>(request.data);
                    update.seat = envelope.senderSeat;
                    update.value = NormalizePlayerName(update.value, update.seat);
                    playerNames[update.seat] = update.value;
                    Send(MessageType.PlayerNameUpdate, -1, Protocol.Json(update));
                    BroadcastLobby();
                }
            }
            else if (envelope.type == MessageType.PlayerNameUpdate)
            {
                OnlinePlayerName update = Protocol.Parse<OnlinePlayerName>(envelope.payload);
                if (update.seat >= 0 && update.seat < playerNames.Length)
                    playerNames[update.seat] = NormalizePlayerName(update.value, update.seat);
            }
            else if (envelope.type == MessageType.StartGame)
            {
                StartGameState start = Protocol.Parse<StartGameState>(envelope.payload);
                roomPlayerCount = Mathf.Clamp(start.playerCount, 2, 4);
                confirmedHumanPlayers = Mathf.Clamp(start.humanPlayerCount, 2, 4);
                cpuPlayers = Mathf.Clamp(start.cpuPlayers, 0, 2);
                participantsConfirmed = true;
                gameSeed = start.randomSeed;
                onlineGameStarted = true;
            }
            else if (envelope.type == MessageType.ReturnToLobby)
            {
                onlineGameStarted = false;
                participantsConfirmed = false;
                confirmedHumanPlayers = 0;
                cpuPlayers = 0;
                if (SceneManager.GetActiveScene().name != "MainMenu")
                    SceneManager.LoadScene("MainMenu");
            }
            else if (envelope.type == MessageType.RestartGame)
            {
                StartGameState restart = Protocol.Parse<StartGameState>(envelope.payload);
                roomPlayerCount = Mathf.Clamp(restart.playerCount, 2, 4);
                confirmedHumanPlayers = Mathf.Clamp(restart.humanPlayerCount, 2, 4);
                cpuPlayers = Mathf.Clamp(restart.cpuPlayers, 0, 2);
                participantsConfirmed = true;
                gameSeed = restart.randomSeed;
                onlineGameStarted = true;
            }
            if (IsHost && senderId != NetworkManager.Singleton.LocalClientId)
            {
                MessageReceived?.Invoke(envelope);
                foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
                    if (id != senderId && id != NetworkManager.Singleton.LocalClientId)
                        SendTo(id, envelope);
                return;
            }
            MessageReceived?.Invoke(envelope);
        }

        private void ApplyPlayerNames(string[] values)
        {
            if (values == null) return;
            for (int seat = 0; seat < playerNames.Length && seat < values.Length; seat++)
                playerNames[seat] = NormalizePlayerName(values[seat], seat);
        }

        private static string NormalizePlayerName(string value, int seat)
        {
            string result = (value ?? "").Trim();
            if (result.Length > 12) result = result.Substring(0, 12);
            return string.IsNullOrEmpty(result) ? $"Player{seat + 1}" : result;
        }

        private static string RelayConnectionType()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return "wss";
#else
            return "dtls";
#endif
        }

        private void Report(string value)
        {
            Debug.Log("[Kaitou Online] " + value);
            StatusChanged?.Invoke(value);
        }
    }
}
