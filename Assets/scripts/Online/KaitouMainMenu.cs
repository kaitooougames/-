using UnityEngine;
using UnityEngine.SceneManagement;

namespace KaitouOnline
{
    public sealed class KaitouMainMenu : MonoBehaviour
    {
        [SerializeField] private string localSceneName = "IntegratedGameScene";
        private KaitouOnlineSession session;
        private string joinCode = "";
        private string status = "遊び方を選んでください。";
        private int requestedPlayers = 2;
        private bool onlineMenu;
        private AudioSource uiAudio;
        private AudioClip clickClip;

        private void Start()
        {
            Application.runInBackground = true;
            uiAudio = gameObject.AddComponent<AudioSource>();
            uiAudio.playOnAwake = false;
            uiAudio.volume = 0.42f;
            clickClip = CreateClickClip();
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
            GUI.Label(new Rect(x, Screen.height * 0.33f, width, 42),
                $"部屋人数：{requestedPlayers}人", label);
            for (int count = 2; count <= 4; count++)
            {
                if (MenuButton(new Rect(x + (count - 2) * (width / 3f), Screen.height * 0.39f,
                        width / 3f - 8f, 55), count + "人", button))
                    requestedPlayers = count;
            }

            if (MenuButton(new Rect(x, Screen.height * 0.48f, width, 64), "部屋を作る", button))
            {
                EnsureSession();
                session.CreateRoom(requestedPlayers);
            }

            joinCode = GUI.TextField(new Rect(x, Screen.height * 0.59f, width, 55),
                joinCode, 12, new GUIStyle(GUI.skin.textField)
                {
                    fontSize = Mathf.RoundToInt(Screen.height * 0.026f),
                    alignment = TextAnchor.MiddleCenter
                });
            if (MenuButton(new Rect(x, Screen.height * 0.67f, width, 64), "参加コードで入る", button))
            {
                EnsureSession();
                session.JoinRoom(joinCode);
            }

            if (session != null && !string.IsNullOrEmpty(session.JoinCode))
            {
                GUI.Label(new Rect(x, Screen.height * 0.76f, width, 48),
                    $"参加コード：{session.JoinCode}　接続：{session.ConnectedPlayers}/{session.RoomPlayerCount}", label);
                GUI.enabled = session.IsHost &&
                              session.ConnectedPlayers >= session.RoomPlayerCount;
                if (MenuButton(new Rect(x, Screen.height * 0.83f, width, 64), "対戦開始", button))
                {
                    StartGameState start = new StartGameState
                    {
                        sceneName = localSceneName,
                        playerCount = session.RoomPlayerCount,
                        randomSeed = Random.Range(1, int.MaxValue)
                    };
                    session.Send(MessageType.StartGame, -1, Protocol.Json(start));
                }
                GUI.enabled = true;
            }

            if (MenuButton(new Rect(20, Screen.height - 70, 180, 50), "戻る", button))
            {
                session?.Disconnect();
                onlineMenu = false;
            }
        }

        private void OnStatus(string value) => status = value;
        private void OnJoinCode(string value) => joinCode = value;

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
                requestedPlayers = state.playerCount;
                status = $"参加者を待っています（{state.connectedPlayers}/{state.playerCount}）";
            }
            else if (envelope.type == MessageType.StartGame)
            {
                StartGameState start = Protocol.Parse<StartGameState>(envelope.payload);
                requestedPlayers = Mathf.Clamp(start.playerCount, 2, 4);
                Random.InitState(start.randomSeed);
                SceneManager.LoadScene(string.IsNullOrEmpty(start.sceneName)
                    ? localSceneName : start.sceneName);
            }
        }
    }
}
