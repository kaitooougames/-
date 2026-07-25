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

    public bool ActionHandVisible => actionHandVisible;
    public int ActionPlayerCount => actionPlayerCount;

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

        actionHandVisible = !actionHandVisible;
        float[] basePositions = { -0.96f, -0.32f, 0.32f, 0.96f };
        for (int i = 0; i < cards.Count; i++)
        {
            CardInteraction card = cards[i];
            if (card == null) continue;

            Vector3 target = actionHandVisible
                ? GetOpenCardPosition(i, basePositions)
                : closedHandPosition;
            card.MoveTo(target, handSlideSpeed);
            if (actionHandVisible) card.EnableClick();
            else card.DisableClick();
        }
    }

    private void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.button)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        const float width = 38f;
        const float height = 155f;
        // お宝手札ボタンのすぐ上に並べる。
        Rect buttonRect = new Rect(0f, (Screen.height - height) * 0.5f - height - 10f, width, height);
        GUI.enabled = !cardSelected && !gameFinished;
        string label = actionHandVisible
            ? "閉\nじ\nる\n◀"
            : "行\n動\nカ\nー\nド\nを\n見\nる\n▶";
        if (GUI.Button(buttonRect, label, style)) ToggleActionHand();
        GUI.enabled = true;

        DrawPlayerCounts();
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
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        var activeIds = new List<int> { 0 };
        if (actionPlayerCount == 2 || actionPlayerCount == 4) activeIds.Add(1);
        if (actionPlayerCount >= 3) { activeIds.Add(2); activeIds.Add(3); }

        float width = 190f;
        float totalWidth = width * activeIds.Count;
        float startX = (Screen.width - totalWidth) * 0.5f;
        for (int i = 0; i < activeIds.Count; i++)
        {
            int id = activeIds[i];
            int actionCount = id == 0 ? (player1 != null && player1.playerCards != null ? player1.playerCards.Count : 0)
                : id == 1 ? (player2 != null && player2.player2Cards != null ? player2.player2Cards.Count : 0)
                : id == 2 ? (player3 != null && player3.player3Cards != null ? player3.player3Cards.Count : 0)
                : (player4 != null && player4.player4Cards != null ? player4.player4Cards.Count : 0);
            int treasureCount = treasureController != null ? treasureController.GetHandCount(id) : 0;
            GUI.Label(new Rect(startX + width * i, Screen.height - 34f, width, 30f),
                $"P{id + 1}  行動:{actionCount}  宝:{treasureCount}", style);
        }
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
        actionHandVisible = true;
        Start();
        cardSelected = false;
        Debug.Log("2日目開始！");
    }

}

