using UnityEngine;
using System.Collections.Generic;

public class HandManager : MonoBehaviour
{
    public static HandManager Instance { get; private set; }

    public List<CardInteraction> cards; // 手札のカードリスト
    private bool cardSelected = false;  // すでにカードを選んだか
    private bool gameFinished;
    [Header("行動カード手札UI")]
    [SerializeField] private bool actionHandVisible = true;
    [SerializeField] private float handSlideSpeed = 8f;
    [SerializeField] private Vector3 closedHandPosition = new Vector3(0f, 3f, -4f);
    [SerializeField, Range(2, 4)] private int actionPlayerCount = 4;
    [Header("画面UI調整")]
    [SerializeField, Range(1f, 3f)] private float uiScale = 2f;
    [SerializeField] private Vector2 actionButtonOffset = Vector2.zero;
    [SerializeField] private Vector2 displayButtonOffset = new Vector2(0f, -105f);
    [SerializeField] private Vector2 playerCountsOffset = new Vector2(0f, -40f);
    [SerializeField] private Vector2 dayLabelOffset = new Vector2(-240f, 16f);
    private int currentDay = 1;

    public bool ActionHandVisible => actionHandVisible;
    public int ActionPlayerCount => actionPlayerCount;

    public int ToTreasurePlayerId(int actionSeatId)
    {
        if (actionPlayerCount == 3)
        {
            if (actionSeatId == 2) return 1;
            if (actionSeatId == 3) return 2;
        }
        return actionSeatId;
    }

    public void EndGame()
    {
        gameFinished = true;
        foreach (CardInteraction card in FindObjectsByType<CardInteraction>(FindObjectsSortMode.None))
            card.DisableClick();
        Debug.Log("<color=#FFD966>【ゲーム終了】勝者が決定したため、行動カード操作を終了しました。</color>");
    }

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        ApplyPlayerCount(actionPlayerCount);
        SetInitialPositions(); // 初期位置を設定
        Invoke("ArrangeHand", 1.0f); // 1秒後に手札を並べる
        Invoke(nameof(RefreshPlayerOneCardAvailability), 1.05f);
    }

    // **カードの初期位置を (0,3,-4) に設定**
    void SetInitialPositions()
    {
        foreach (var card in cards)
        {
            card.transform.position = closedHandPosition;
        }
    }

    public static void SetPlayerCountGlobally(int count)
    {
        HandManager manager = Instance != null ? Instance : FindFirstObjectByType<HandManager>();
        if (manager != null) manager.ApplyPlayerCount(count);
    }

    public void ApplyPlayerCount(int count)
    {
        actionPlayerCount = Mathf.Clamp(count, 2, 4);

        // 3人時はPlayer2席を空け、お宝側と同じPlayer1・3・4を使う。
        bool usePlayer2 = actionPlayerCount == 2 || actionPlayerCount == 4;
        bool usePlayer3 = actionPlayerCount >= 3;
        bool usePlayer4 = actionPlayerCount >= 3;

        SetOpponentActive(FindFirstObjectByType<Player2>(FindObjectsInactive.Include), usePlayer2);
        SetOpponentActive(FindFirstObjectByType<Player3>(FindObjectsInactive.Include), usePlayer3);
        SetOpponentActive(FindFirstObjectByType<Player4>(FindObjectsInactive.Include), usePlayer4);

        Debug.Log($"<color=#70E8FF>【行動カード人数】{actionPlayerCount}人プレイ</color>");
    }

    private static void SetOpponentActive(Player2 player, bool active)
    {
        if (player == null) return;
        player.gameObject.SetActive(active);
        if (player.player2Cards == null) return;
        foreach (CardInteraction card in player.player2Cards)
            if (card != null) card.gameObject.SetActive(active);
    }

    private static void SetOpponentActive(Player3 player, bool active)
    {
        if (player == null) return;
        player.gameObject.SetActive(active);
        if (player.player3Cards == null) return;
        foreach (CardInteraction card in player.player3Cards)
            if (card != null) card.gameObject.SetActive(active);
    }

    private static void SetOpponentActive(Player4 player, bool active)
    {
        if (player == null) return;
        player.gameObject.SetActive(active);
        if (player.player4Cards == null) return;
        foreach (CardInteraction card in player.player4Cards)
            if (card != null) card.gameObject.SetActive(active);
    }

    void ArrangeHand()
    {
        float[] basePositions = { -0.96f, -0.32f, 0.32f, 0.96f };

        for (int i = 0; i < cards.Count; i++)
        {
            Vector3 position = GetOpenCardPosition(i, basePositions);
            cards[i].MoveTo(actionHandVisible ? position : closedHandPosition, handSlideSpeed);
            cards[i].SetHandManager(this);

            // **最初の怪盗カードを見つけたら isPhantomThief を設定**
            if (cards[i].isPhantomThief)
            {
                Debug.Log("怪盗カードがセットされました: " + cards[i].name);
            }
        }
    }

    private Vector3 GetOpenCardPosition(int index, float[] basePositions)
    {
        float xPos = basePositions[index % basePositions.Length];
        return new Vector3(xPos, 2f, -2.5f);
    }

    public void ToggleActionHand()
    {
        if (cardSelected) return;
        SetActionHandVisible(!actionHandVisible);
    }

    public void SetActionHandVisible(bool visible)
    {
        if (actionHandVisible == visible) return;
        actionHandVisible = visible;
        float[] basePositions = { -0.96f, -0.32f, 0.32f, 0.96f };
        for (int i = 0; i < cards.Count; i++)
        {
            CardInteraction card = cards[i];
            if (card == null) continue;

            Vector3 target = actionHandVisible
                ? GetOpenCardPosition(i, basePositions)
                : closedHandPosition;
            card.MoveTo(target, handSlideSpeed);
            // 開閉のために移動しているだけなので、カードは暗くしない。
            if (!actionHandVisible) card.DisableClick(false);
        }
        if (actionHandVisible) RefreshPlayerOneCardAvailability();
    }

    public void RefreshPlayerOneCardAvailability()
    {
        Player playerOne = FindFirstObjectByType<Player>(FindObjectsInactive.Include);
        bool thiefBlocked = playerOne != null && playerOne.IsFirstOffense();
        foreach (CardInteraction card in cards)
        {
            if (card == null) continue;
            if (!actionHandVisible || gameFinished || (thiefBlocked && card.isPhantomThief))
                card.DisableClick();
            else
                card.EnableClick();
        }
    }

    private void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.button)
        {
            fontSize = Mathf.RoundToInt(15f * uiScale),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        float width = 38f * uiScale;
        float height = 155f * uiScale;
        // お宝手札ボタンのすぐ上に並べる。
        Rect buttonRect = new Rect(actionButtonOffset.x * uiScale,
            (Screen.height - height) * 0.5f - height - 10f + actionButtonOffset.y * uiScale,
            width, height);
        CameraController cameraController = Camera.main != null
            ? Camera.main.GetComponent<CameraController>()
            : null;
        bool displayViewLocked = cameraController != null &&
            (cameraController.PlayerDisplayViewActive || cameraController.IsCameraMoving);
        GUI.enabled = !cardSelected && !gameFinished && !displayViewLocked;
        string label = actionHandVisible
            ? "閉\nじ\nる\n◀"
            : "行\n動\nカ\nー\nド\nを\n見\nる\n▶";
        if (GUI.Button(buttonRect, label, style)) ToggleActionHand();
        GUI.enabled = true;

        DrawPlayerDisplayControls(style);
        DrawPlayerCounts();
        DrawDayCounter();
    }

    private void DrawPlayerDisplayControls(GUIStyle buttonStyle)
    {
        CameraController cameraController = Camera.main != null
            ? Camera.main.GetComponent<CameraController>()
            : null;
        TreasureGame.TreasureController treasureController =
            FindFirstObjectByType<TreasureGame.TreasureController>();
        if (cameraController == null || treasureController == null) return;

        bool viewing = cameraController.PlayerDisplayViewActive;
        bool canOpen = !cardSelected && !gameFinished &&
            treasureController.Phase == TreasureGame.TreasurePhase.Waiting &&
            !cameraController.IsCameraMoving;
        GUI.enabled = viewing || canOpen;

        float width = 280f * uiScale;
        float height = 50f * uiScale;
        float y = Screen.height + displayButtonOffset.y * uiScale;
        float firstX = (Screen.width - width) * 0.5f + displayButtonOffset.x * uiScale;
        string viewLabel = viewing ? "展示場を見るのをやめる" : "自分の展示場を見る（真贋チェック）";
        if (GUI.Button(new Rect(firstX, y, width, height), viewLabel, buttonStyle))
        {
            if (!viewing)
            {
                SetActionHandVisible(false);
                treasureController.SetPlayerOneHandVisible(false);
                treasureController.RevealPlayerDisplay(0);
                cameraController.SetPlayerDisplayView(true);
            }
            else
            {
                treasureController.HidePlayerDisplay(0);
                cameraController.SetPlayerDisplayView(false);
                SetActionHandVisible(true);
            }
        }
        GUI.enabled = true;
    }

    private void DrawPlayerCounts()
    {
        TreasureGame.TreasureController treasureController =
            FindFirstObjectByType<TreasureGame.TreasureController>();
        Player player1 = FindFirstObjectByType<Player>(FindObjectsInactive.Include);
        Player2 player2 = FindFirstObjectByType<Player2>(FindObjectsInactive.Include);
        Player3 player3 = FindFirstObjectByType<Player3>(FindObjectsInactive.Include);
        Player4 player4 = FindFirstObjectByType<Player4>(FindObjectsInactive.Include);

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(17f * uiScale),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        var activeIds = new List<int> { 0 };
        if (actionPlayerCount == 2 || actionPlayerCount == 4) activeIds.Add(1);
        if (actionPlayerCount >= 3) { activeIds.Add(2); activeIds.Add(3); }

        float width = 210f * uiScale;
        float totalWidth = width * activeIds.Count;
        float startX = (Screen.width - totalWidth) * 0.5f + playerCountsOffset.x * uiScale;
        for (int i = 0; i < activeIds.Count; i++)
        {
            int id = activeIds[i];
            int actionCount = id == 0 ? (player1 != null && player1.playerCards != null ? player1.playerCards.Count : 0)
                : id == 1 ? (player2 != null && player2.player2Cards != null ? player2.player2Cards.Count : 0)
                : id == 2 ? (player3 != null && player3.player3Cards != null ? player3.player3Cards.Count : 0)
                : (player4 != null && player4.player4Cards != null ? player4.player4Cards.Count : 0);
            int treasureId = ToTreasurePlayerId(id);
            int treasureCount = treasureController != null ? treasureController.GetHandCount(treasureId) : 0;
            GUI.Label(new Rect(startX + width * i, Screen.height + playerCountsOffset.y * uiScale,
                    width, 34f * uiScale),
                $"P{id + 1}  行動:{actionCount}  宝:{treasureCount}", style);
        }
    }

    private void DrawDayCounter()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(24f * uiScale),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 0.86f, 0.35f) }
        };
        float width = 110f * uiScale;
        float height = 48f * uiScale;
        GUI.Label(new Rect(Screen.width + dayLabelOffset.x * uiScale,
            dayLabelOffset.y * uiScale, width, height), $"{currentDay}日目", style);
    }
    public void SelectCard(CardInteraction selectedCard)
    {
        if (cardSelected)
        {
            Debug.Log("すでにカードが選択されています: " + selectedCard.name);
            return; // すでにカードを選んでいたら無視
        }
        cardSelected = true;
        Debug.Log("カード選択: " + selectedCard.name);

        // **怪盗カードでなければカメラを移動**
        if (!selectedCard.isPhantomThief)
        {
            Camera.main.GetComponent<CameraController>().MoveCamera();
        }


        // **他のカードを初期位置に戻す**
        foreach (var card in cards)
        {
            if (card != selectedCard)
            {
                Debug.Log(card.name + " を初期位置に戻す");
                card.MoveTo(new Vector3(0, 3, -4)); // 初期位置に戻す
                card.DisableClick(); // クリックを無効化

            }
        }

    }

    public void MoveCardsAfterThiefPhase()
    {
        currentDay++;
        actionHandVisible = true;
        Start();
        cardSelected = false;
        Debug.Log("2日目開始！");
    }

}

