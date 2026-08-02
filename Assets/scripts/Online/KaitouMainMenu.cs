using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KaitouOnline
{
    public sealed class KaitouMainMenu : MonoBehaviour
    {
        private const string PlayerNamePreference = "KaitouTreasure.PlayerName";
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void KaitouCopyText(string text);

        [DllImport("__Internal")]
        private static extern void KaitouPasteText(
            string gameObjectName, string callbackMethod);
#endif

        [SerializeField] private string localSceneName = "IntegratedGameScene";
        private KaitouOnlineSession session;
        private string joinCode = "";
        private string status = "遊び方を選んでください。";
        private int requestedCpuPlayers;
        private bool onlineMenu;
        private AudioSource uiAudio;
        private AudioClip clickClip;
        private string playerName = "";
        private bool playerNameSent;
        private float nextPlayerNameSendTime;

        private void Start()
        {
            Application.runInBackground = true;
            uiAudio = gameObject.AddComponent<AudioSource>();
            uiAudio.playOnAwake = false;
            uiAudio.volume = 0.42f;
            clickClip = CreateClickClip();
            playerName = PlayerPrefs.GetString(PlayerNamePreference, "");
            if (KaitouOnlineSession.Instance != null &&
                KaitouOnlineSession.Instance.IsOnline)
            {
                onlineMenu = true;
                EnsureSession();
                requestedCpuPlayers = session.CpuPlayers;
                status = session.IsHost
                    ? "接続を維持しています。今回の参加者を確定してください。"
                    : "接続を維持しています。ホストの参加者確定を待っています。";
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void EnsureSession()
        {
            if (session != null) return;
            session = KaitouOnlineSession.Instance;
            if (session == null)
            {
                GameObject obj = new GameObject("KaitouOnlineSession");
                session = obj.AddComponent<KaitouOnlineSession>();
            }
            session.StatusChanged += OnStatus;
            session.JoinCodeChanged += OnJoinCode;
            session.MessageReceived += OnMessage;
        }

        private void Unsubscribe()
        {
            if (session == null) return;
            session.StatusChanged -= OnStatus;
            session.JoinCodeChanged -= OnJoinCode;
            session.MessageReceived -= OnMessage;
        }

        private void OnGUI()
        {
            KaitouGuiFont.Apply();
            GUIStyle title = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.height * 0.055f),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUIStyle button = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(Screen.height * 0.027f),
                fontStyle = FontStyle.Bold
            };
            GUIStyle label = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.height * 0.022f),
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = Color.white }
            };

            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUI.Label(new Rect(0, Screen.height * 0.08f, Screen.width, 90), "怪盗ゲーム TREASURE", title);

            float width = Mathf.Min(620f, Screen.width * 0.82f);
            float x = (Screen.width - width) * 0.5f;
            if (!onlineMenu)
            {
                if (MenuButton(new Rect(x, Screen.height * 0.37f, width, 70), "ローカル対戦", button))
                    SceneManager.LoadScene(localSceneName);
                if (MenuButton(new Rect(x, Screen.height * 0.49f, width, 70), "オンライン対戦", button))
                {
                    onlineMenu = true;
                    EnsureSession();
                }
                return;
            }

            GUI.Label(new Rect(x, Screen.height * 0.24f, width, 58), status, label);
            if (session == null || !session.IsOnline)
            {
                GUI.Label(new Rect(x, Screen.height * 0.30f, width, 34),
                    "プレイヤー名", label);
                playerName = GUI.TextField(new Rect(x, Screen.height * 0.34f, width, 48),
                    playerName, 12, new GUIStyle(GUI.skin.textField)
                    {
                        fontSize = Mathf.RoundToInt(Screen.height * 0.024f),
                        alignment = TextAnchor.MiddleCenter
                    });
            }
            if ((session == null || !session.IsOnline) &&
                MenuButton(new Rect(x, Screen.height * 0.39f, width, 64), "部屋を作る", button))
            {
                SavePlayerNamePreference();
                EnsureSession();
                session.CreateRoom();
            }

            float clipboardButtonWidth = Mathf.Min(145f, width * 0.28f);
            float codeFieldWidth = width - clipboardButtonWidth - 8f;
            if (session == null || !session.IsOnline)
            {
                joinCode = GUI.TextField(new Rect(x, Screen.height * 0.50f, codeFieldWidth, 55),
                    joinCode, 12, new GUIStyle(GUI.skin.textField)
                {
                    fontSize = Mathf.RoundToInt(Screen.height * 0.026f),
                    alignment = TextAnchor.MiddleCenter
                });
                if (MenuButton(new Rect(x + codeFieldWidth + 8f, Screen.height * 0.50f,
                    clipboardButtonWidth, 55), "貼り付け", button))
                    PasteJoinCode();
                if (MenuButton(new Rect(x, Screen.height * 0.58f, width, 64), "参加コードで入る", button))
                {
                    SavePlayerNamePreference();
                    EnsureSession();
                    session.JoinRoom(joinCode);
                }
            }

            if (session != null && !string.IsNullOrEmpty(session.JoinCode))
            {
                if (!playerNameSent)
                {
                    int localSeat = session.LocalSeat;
                    string expectedName = string.IsNullOrWhiteSpace(playerName)
                        ? $"Player{localSeat + 1}" : playerName.Trim();
                    if (localSeat >= 0 && session.GetPlayerName(localSeat) == expectedName)
                    {
                        playerNameSent = true;
                    }
                    else if (Time.unscaledTime >= nextPlayerNameSendTime &&
                             session.SetLocalPlayerName(playerName))
                    {
                        nextPlayerNameSendTime = Time.unscaledTime + 1f;
                    }
                }
                GUI.Label(new Rect(x, Screen.height * 0.67f,
                        width - clipboardButtonWidth - 8f, 48),
                    $"参加コード：{session.JoinCode}　現在{session.ConnectedPlayers}人接続中", label);
                if (MenuButton(new Rect(x + width - clipboardButtonWidth,
                        Screen.height * 0.67f, clipboardButtonWidth, 48),
                        "コピー", button))
                    CopyJoinCode(session.JoinCode);

                if (!session.ParticipantsConfirmed)
                {
                    string connectedNames = "";
                    for (int seat = 0; seat < session.ConnectedPlayers; seat++)
                        connectedNames += (seat == 0 ? "" : " / ") + session.GetPlayerName(seat);
                    GUI.Label(new Rect(x, Screen.height * 0.71f, width, 32),
                        connectedNames, label);
                    GUI.enabled = session.IsHost && session.ConnectedPlayers >= 2;
                    if (MenuButton(new Rect(x, Screen.height * 0.75f, width, 54),
                            "参加者を確定", button))
                    {
                        requestedCpuPlayers = 0;
                        session.ConfirmParticipants();
                    }
                    GUI.enabled = true;
                    GUI.Label(new Rect(x, Screen.height * 0.82f, width, 42),
                        session.ConnectedPlayers < 2
                            ? "2人以上の接続を待っています。"
                            : session.IsHost ? "接続中の参加者を確定してください。"
                                             : "ホストの参加者確定を待っています。", label);
                }
                else
                {
                    GUI.Label(new Rect(x, Screen.height * 0.73f, width, 38),
                        $"人間{session.ConfirmedHumanPlayers}人 ＋ CPU{requestedCpuPlayers}人", label);
                    GUI.enabled = session.IsHost;
                    int maxCpu = Mathf.Max(0, 4 - session.ConfirmedHumanPlayers);
                    float optionWidth = width / (maxCpu + 1f);
                    for (int cpu = 0; cpu <= maxCpu; cpu++)
                    {
                        string cpuLabel = cpu == 0 ? "CPUなし" : $"CPU＋{cpu}人";
                        if (MenuButton(new Rect(x + cpu * optionWidth,
                                Screen.height * 0.77f, optionWidth - 8f, 48),
                                cpuLabel, button))
                        {
                            requestedCpuPlayers = cpu;
                            session.SetCpuPlayers(cpu);
                        }
                    }
                    GUI.enabled = true;
                    GUI.enabled = session.IsHost &&
                                  session.ConnectedPlayers == session.ConfirmedHumanPlayers;
                    if (MenuButton(new Rect(x, Screen.height * 0.84f, width, 58), "ゲームスタート", button))
                    {
                        StartGameState start = new StartGameState
                        {
                            sceneName = localSceneName,
                            playerCount = session.ConfirmedHumanPlayers + session.CpuPlayers,
                            humanPlayerCount = session.ConfirmedHumanPlayers,
                            cpuPlayers = session.CpuPlayers,
                            randomSeed = Random.Range(1, int.MaxValue)
                        };
                        session.Send(MessageType.StartGame, -1, Protocol.Json(start));
                    }
                    GUI.enabled = true;
                }
            }

            if (MenuButton(new Rect(20, Screen.height - 70, 180, 50), "戻る", button))
            {
                session?.Disconnect();
                onlineMenu = false;
                playerNameSent = false;
            }
        }

        private void OnStatus(string value) => status = value;
        private void OnJoinCode(string value) => joinCode = value;

        private void SavePlayerNamePreference()
        {
            PlayerPrefs.SetString(PlayerNamePreference, (playerName ?? "").Trim());
            PlayerPrefs.Save();
        }

        private void CopyJoinCode(string value)
        {
            string normalized = (value ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(normalized)) return;
            GUIUtility.systemCopyBuffer = normalized;
#if UNITY_WEBGL && !UNITY_EDITOR
            KaitouCopyText(normalized);
#endif
            status = $"参加コード {normalized} をコピーしました。";
        }

        private void PasteJoinCode()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            status = "クリップボードから参加コードを読み込んでいます…";
            KaitouPasteText(gameObject.name, nameof(ReceivePastedJoinCode));
#else
            ReceivePastedJoinCode(GUIUtility.systemCopyBuffer);
#endif
        }

        public void ReceivePastedJoinCode(string value)
        {
            joinCode = (value ?? "").Trim().ToUpperInvariant();
            if (joinCode.Length > 12) joinCode = joinCode.Substring(0, 12);
            status = string.IsNullOrEmpty(joinCode)
                ? "クリップボードに参加コードがありません。"
                : $"参加コード {joinCode} を貼り付けました。";
        }

        private bool MenuButton(Rect rect, string text, GUIStyle style)
        {
            bool clicked = GUI.Button(rect, text, style);
            if (clicked && uiAudio != null && clickClip != null)
                uiAudio.PlayOneShot(clickClip);
            return clicked;
        }

        private static AudioClip CreateClickClip()
        {
            const int sampleRate = 44100;
            const float duration = 0.075f;
            int length = Mathf.RoundToInt(sampleRate * duration);
            float[] samples = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)sampleRate;
                float progress = i / (float)length;
                float frequency = Mathf.Lerp(920f, 520f, progress);
                float envelope = Mathf.Pow(1f - progress, 2.4f);
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.55f;
            }
            AudioClip clip = AudioClip.Create("MenuClick", length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnMessage(Envelope envelope)
        {
            if (envelope.type == MessageType.LobbyState)
            {
                LobbyState state = Protocol.Parse<LobbyState>(envelope.payload);
                requestedCpuPlayers = state.cpuPlayers;
                status = state.participantsConfirmed
                    ? $"参加者{state.confirmedHumanPlayers}人を確定しました。CPU人数を選んでください。"
                    : $"現在{state.connectedPlayers}人接続中。参加者の確定を待っています。";
            }
            else if (envelope.type == MessageType.StartGame)
            {
                StartGameState start = Protocol.Parse<StartGameState>(envelope.payload);
                requestedCpuPlayers = Mathf.Max(0,
                    start.cpuPlayers);
                Random.InitState(start.randomSeed);
                SceneManager.LoadScene(string.IsNullOrEmpty(start.sceneName)
                    ? localSceneName : start.sceneName);
            }
        }
    }
}
