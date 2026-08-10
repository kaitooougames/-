using UnityEngine;
using System.Collections.Generic;

public class HandManager : MonoBehaviour
{
    public static HandManager Instance { get; private set; }

    public List<CardInteraction> cards; // 手札のカードリスト
    private bool cardSelected = false;  // すでにカードを選んだか
    private CardInteraction activeSelectedCard;
    private bool gameFinished;
    [Header("行動カード手札UI")]
    [SerializeField] private bool actionHandVisible = true;
    [SerializeField] private float handSlideSpeed = 8f;
    [SerializeField, Range(0.5f, 10f)] private float actionHandInertiaFriction = 3f;
    [SerializeField] private float actionHandMaxFlickSpeed = 2400f;
    [SerializeField] private Vector3 closedHandPosition = new Vector3(0f, 3f, -4f);
    [SerializeField, Range(2, 4)] private int actionPlayerCount = 4;
    [Header("画面UI調整")]
    [SerializeField, Range(1f, 3f)] private float uiScale = 2f;
    [SerializeField] private Vector2 actionButtonOffset = Vector2.zero;
    [SerializeField] private Vector2 displayButtonOffset = Vector2.zero;
    [SerializeField] private Vector2 playerCountsOffset = new Vector2(0f, -40f);
    [SerializeField] private Vector2 dayLabelOffset = new Vector2(-240f, 16f);
    [Header("テスト用")]
    [SerializeField] private bool showAllSpecialCardsButton = true;
    [SerializeField] private Vector2 allSpecialCardsButtonOffset = new Vector2(16f, 16f);
    private int currentDay = 1;
    private float actionScrollOffset;
    private bool draggingActionHand;
    private bool actionCardGripActive;
    private float lastActionDragX;
    private float actionHandVelocity;
    private bool honeyTrapInspectionActive;
    private bool honeyTrapInspectionConfirmed;
    private string honeyTrapInspectionMessage = "";
    private bool initializationComplete;

    public bool ActionHandVisible => actionHandVisible;
    public int ActionPlayerCount => actionPlayerCount;
    public bool CardSelected => cardSelected;
    public int CurrentDay => currentDay;

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
        // オンラインではシーンに保存された人数を一瞬でも使わない。
        // 2人の保存値で初期化されると追加怪盗が生成され、参加側・CPU側の
        // 行動手札がホストと食い違うため、Awakeの時点から部屋人数を採用する。
        if (KaitouOnline.KaitouOnlineSession.Instance != null &&
            KaitouOnline.KaitouOnlineSession.Instance.HasStartedOnlineGame)
            actionPlayerCount = Mathf.Clamp(
                KaitouOnline.KaitouOnlineSession.Instance.RoomPlayerCount, 2, 4);
        // ゲーム開始時と次ターンの選択待ちは、行動カードを開いておく。
        actionHandVisible = true;
        SpecialActionCardSystem.ResetSession();
    }

    void Start()
    {
        if (KaitouOnline.KaitouOnlineSession.Instance != null &&
            KaitouOnline.KaitouOnlineSession.Instance.HasStartedOnlineGame)
            actionPlayerCount = Mathf.Clamp(
                KaitouOnline.KaitouOnlineSession.Instance.RoomPlayerCount, 2, 4);
        ApplyPlayerCount(actionPlayerCount);
        SpecialActionCardSystem.DealInitialCards(this);
        SetInitialPositions(); // 初期位置を設定
        Invoke("ArrangeHand", 1.0f); // 1秒後に手札を並べる
        Invoke(nameof(RefreshPlayerOneCardAvailability), 1.05f);
        initializationComplete = true;
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
        int newPlayerCount = Mathf.Clamp(count, 2, 4);
        bool playerCountChanged = newPlayerCount != actionPlayerCount;
        actionPlayerCount = newPlayerCount;

        if (playerCountChanged) ResetAllPenaltyCards();

        // 3人時はPlayer2席を空け、お宝側と同じPlayer1・3・4を使う。
        bool usePlayer2 = actionPlayerCount == 2 || actionPlayerCount == 4;
        bool usePlayer3 = actionPlayerCount >= 3;
        bool usePlayer4 = actionPlayerCount >= 3;

        SetOpponentActive(FindFirstObjectByType<Player2>(FindObjectsInactive.Include), usePlayer2);
        SetOpponentActive(FindFirstObjectByType<Player3>(FindObjectsInactive.Include), usePlayer3);
        SetOpponentActive(FindFirstObjectByType<Player4>(FindObjectsInactive.Include), usePlayer4);
        SpecialActionCardSystem.ConfigureTwoPlayerNormalThieves(this, actionPlayerCount == 2);
        // Start中に即時配置すると、CardInteractionが基準Scaleを保存する前に
        // 全カードをゼロサイズへ変更してしまう。実行中の人数切替時だけ再配置する。
        if (initializationComplete)
        {
            actionScrollOffset = 0f;
            actionHandVisible = true;
            RefreshActionHandLayout(true);
        }

        Debug.Log($"<color=#70E8FF>【行動カード人数】{actionPlayerCount}人プレイ</color>");
    }

    private static void ResetAllPenaltyCards()
    {
        FindFirstObjectByType<Player>(FindObjectsInactive.Include)?.ResetPenaltyCards();
        FindFirstObjectByType<Player2>(FindObjectsInactive.Include)?.ResetPenaltyCards();
        FindFirstObjectByType<Player3>(FindObjectsInactive.Include)?.ResetPenaltyCards();
        FindFirstObjectByType<Player4>(FindObjectsInactive.Include)?.ResetPenaltyCards();
        Debug.Log("<color=#70E8FF>【人数切替】全Playerのペナルティカードをリセットしました。</color>");
    }

    private static void SetOpponentActive(Player2 player, bool active)
    {
        if (player == null) return;
        player.gameObject.SetActive(active);
        if (player.player2Cards == null) return;
        foreach (CardInteraction card in player.player2Cards)
            if (card != null)
            {
                card.gameObject.SetActive(active);
                if (active && KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
                {
                    card.EnsureVisibleForTable();
                    card.MoveToImmediate(new Vector3(0f, 2f, 2.5f));
                }
            }
    }

    private static void SetOpponentActive(Player3 player, bool active)
    {
        if (player == null) return;
        player.gameObject.SetActive(active);
        if (player.player3Cards == null) return;
        foreach (CardInteraction card in player.player3Cards)
            if (card != null)
            {
                card.gameObject.SetActive(active);
                if (active && KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
                {
                    card.EnsureVisibleForTable();
                    card.MoveToImmediate(new Vector3(-5f, 2f, 0f));
                }
            }
    }

    private static void SetOpponentActive(Player4 player, bool active)
    {
        if (player == null) return;
        player.gameObject.SetActive(active);
        if (player.player4Cards == null) return;
        foreach (CardInteraction card in player.player4Cards)
            if (card != null)
            {
                card.gameObject.SetActive(active);
                if (active && KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
                {
                    card.EnsureVisibleForTable();
                    card.MoveToImmediate(new Vector3(5f, 2f, 0f));
                }
            }
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
        float xPos = basePositions[0] + index * 0.64f + actionScrollOffset;
        return new Vector3(xPos, 2f, -2.5f);
    }

    public void RefreshActionHandLayout(bool immediate = false)
    {
        float minOffset = -Mathf.Max(0, cards.Count - 4) * 0.64f;
        actionScrollOffset = Mathf.Clamp(actionScrollOffset, minOffset, 0f);
        float[] basePositions = { -0.96f, -0.32f, 0.32f, 0.96f };
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null) continue;
            if (SpecialActionCardSystem.IsAdvanceNoticePendingCard(0, cards[i])) continue;
            if (cardSelected && cards[i] == activeSelectedCard) continue;
            cards[i].SetHandManager(this);
            Vector3 target = actionHandVisible ? GetOpenCardPosition(i, basePositions) : closedHandPosition;
            if (immediate) cards[i].MoveToImmediate(target);
            else cards[i].MoveTo(target, handSlideSpeed);
        }
    }

    public void ScrollActionHandByPixels(float pixelDelta)
    {
        if (!actionHandVisible || cardSelected || cards.Count <= 4) return;
        ApplyActionHandScroll(pixelDelta);

        // ドラッグ中は移動量を直接反映し、ここではリリース後に使う速度だけを計測する。
        if ((actionCardGripActive || draggingActionHand) && Mathf.Abs(pixelDelta) > 0.01f)
        {
            float frameTime = Mathf.Max(0.005f, Time.unscaledDeltaTime);
            float instantVelocity = pixelDelta / frameTime;
            actionHandVelocity = Mathf.Clamp(
                instantVelocity, -actionHandMaxFlickSpeed, actionHandMaxFlickSpeed);
        }
    }

    private void ApplyActionHandScroll(float pixelDelta)
    {
        float worldDelta = pixelDelta * 0.006f;
        if (Camera.main != null)
        {
            Vector3 reference = new Vector3(0f, 2f, -2.5f);
            float pixelsPerCard = Mathf.Abs(
                Camera.main.WorldToScreenPoint(reference + Vector3.right * 0.64f).x -
                Camera.main.WorldToScreenPoint(reference).x);
            worldDelta = pixelDelta / Mathf.Max(20f, pixelsPerCard) * 0.64f;
        }
        actionScrollOffset += worldDelta;
        RefreshActionHandLayout(true);
    }

    public void BeginActionCardGrip()
    {
        actionCardGripActive = true;
        draggingActionHand = false;
        actionHandVelocity = 0f;
    }

    public void EndActionCardGrip() => actionCardGripActive = false;

    private void Update()
    {
        // カード外でリリースしてイベントを取り逃しても、慣性を開始できるようにする。
        if (!Input.GetMouseButton(0))
        {
            actionCardGripActive = false;
            draggingActionHand = false;
        }

        // 宝カードと同様、掴んでいる最中は直接追従だけにする。
        if (actionCardGripActive || draggingActionHand || !actionHandVisible ||
            cardSelected || cards.Count <= 4)
            return;
        if (Mathf.Abs(actionHandVelocity) < 10f)
        {
            actionHandVelocity = 0f;
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        ApplyActionHandScroll(actionHandVelocity * deltaTime);
        actionHandVelocity *= Mathf.Exp(-actionHandInertiaFriction * deltaTime);
    }

    public void RemoveConfiscatedPlayerOneCard(CardInteraction card)
    {
        if (card == null) return;
        cards.Remove(card);
        // 現在の先頭位置を基準に、通常・特殊を区別せず残った順番で隙間を詰める。
        float minOffset = -Mathf.Max(0, cards.Count - 4) * 0.64f;
        actionScrollOffset = Mathf.Clamp(actionScrollOffset, minOffset, 0f);
        RefreshActionHandLayout(true);
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
            if (cardSelected && card == activeSelectedCard) continue;

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
        bool actionBlocked = SpecialActionCardSystem.CannotActToday(0);
        foreach (CardInteraction card in cards)
        {
            if (card == null) continue;
            // 選択済みカードはすでに手札ではなく卓上カード。
            // 遅延呼び出しや特殊効果による再判定で暗転を上書きしない。
            if (cardSelected && card == activeSelectedCard)
            {
                card.EnsureVisibleForTable();
                card.DisableClick(false);
                continue;
            }
            if (SpecialActionCardSystem.TryGetActiveAdvanceNotice(0,
                    out CardInteraction activeAdvanceNotice) && card != activeAdvanceNotice)
            {
                card.DisableClick();
                continue;
            }
            bool ruleBlocked = !SpecialActionCardSystem.CanSelect(0, card);
            if (!actionHandVisible || gameFinished || actionBlocked ||
                (thiefBlocked && card.isPhantomThief) || ruleBlocked)
                card.DisableClick();
            else
                card.EnableClick();
        }
    }

    private void OnGUI()
    {
        KaitouGuiFont.Apply();
        GUIStyle style = new GUIStyle(GUI.skin.button)
        {
            fontSize = Mathf.RoundToInt(15f * uiScale),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        float width = 56f * uiScale;
        float height = 145f * uiScale;
        float bottomGap = 75f * uiScale;
        // お宝手札ボタンの上へ、画面下基準で並べる。
        Rect buttonRect = new Rect(actionButtonOffset.x * uiScale,
            Screen.height - height * 2f - bottomGap - 8f * uiScale +
                actionButtonOffset.y * uiScale,
            width, height);
        CameraController cameraController = Camera.main != null
            ? Camera.main.GetComponent<CameraController>()
            : null;
        bool displayViewLocked = cameraController != null &&
            (cameraController.PlayerDisplayViewActive || cameraController.IsCameraMoving);
        GUI.enabled = !cardSelected && !gameFinished && !displayViewLocked;
        string label = actionHandVisible
            ? "閉\nじ\nる\n◀"
            : "行\n動\nを\n見\nる\n▶";
        if (GUI.Button(buttonRect, label, style)) ToggleActionHand();
        GUI.enabled = true;
        HandleActionHandScroll();

        DrawPlayerDisplayControls(style);
        DrawPlayerCounts();
        DrawDayCounter();
        DrawAllSpecialCardsTestButton(style);
        DrawCpuActionCardMoveTestButton(style);
        DrawHoneyTrapInspection(style);
    }

    private void DrawHoneyTrapInspection(GUIStyle style)
    {
        if (!honeyTrapInspectionActive) return;
        GUIStyle messageStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = Mathf.RoundToInt(15f * uiScale),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = false
        };
        float messageWidth = Mathf.Min(Screen.width - 80f, 1240f);
        GUI.Box(new Rect((Screen.width - messageWidth) * 0.5f, 24f, messageWidth, 70f),
            honeyTrapInspectionMessage, messageStyle);
        if (GUI.Button(new Rect(Screen.width * 0.5f - 140f, 105f, 280f, 58f),
                "確認終了", style))
            honeyTrapInspectionConfirmed = true;
    }

    public System.Collections.IEnumerator ShowHoneyTrapHand(
        int victimActionSeat, bool includeSpecialCards)
    {
        List<CardInteraction> ownerCards = GetActionCardsForSeat(victimActionSeat);
        if (ownerCards == null) yield break;
        CardInteraction playedCard = GetSelectedActionCardForSeat(victimActionSeat);
        List<CardInteraction> heldCards = ownerCards.FindAll(card => card != null &&
            (includeSpecialCards || !card.IsSpecialAction));
        // 場に出したカードはすでに見えているので動かさず、残りの保有カードだけ確認欄へ並べる。
        List<CardInteraction> visibleCards = heldCards.FindAll(card =>
            card != playedCard &&
            card != null);
        if (heldCards.Count == 0) yield break;

        SetActionHandVisible(false);
        float spacing = Mathf.Min(0.64f, 5.4f / Mathf.Max(1, visibleCards.Count - 1));
        float startX = -spacing * (visibleCards.Count - 1) * 0.5f;
        CardInteraction playerOneHandReference = cards.Find(card => card != null);
        Quaternion inspectionRotation = playerOneHandReference != null
            ? playerOneHandReference.HandPoseRotation
            : Quaternion.Euler(180f, 0f, 180f);
        for (int i = 0; i < visibleCards.Count; i++)
        {
            visibleCards[i].SetVisualVisible(true);
            visibleCards[i].DisableClick(false);
            visibleCards[i].MoveToInspection(new Vector3(startX + spacing * i, 2.1f, -1.45f),
                inspectionRotation, 8f);
        }
        string playedNote = playedCard != null && heldCards.Contains(playedCard)
            ? "（場に出ている1枚を含む）" : "";
        honeyTrapInspectionMessage = includeSpecialCards
            ? $"ハニートラップ：Player{victimActionSeat + 1}が保有する行動カード全{heldCards.Count}枚{playedNote}"
            : $"ハニートラップ：Player{victimActionSeat + 1}が保有する通常行動カード全{heldCards.Count}枚{playedNote}";
        honeyTrapInspectionConfirmed = false;
        honeyTrapInspectionActive = true;
        KaitouOnline.KaitouOnlineGameBridge.NotifyHoneyTrapInspection(
            victimActionSeat, true);
        while (!honeyTrapInspectionConfirmed) yield return null;
        honeyTrapInspectionActive = false;

        Vector3 storage = victimActionSeat == 1 ? new Vector3(0f, 2f, 2.5f) :
            victimActionSeat == 2 ? new Vector3(-5f, 2f, 0f) : new Vector3(5f, 2f, 0f);
        foreach (CardInteraction card in visibleCards)
            card.MoveTo(storage, 7f);
        yield return new WaitForSeconds(0.5f);
        honeyTrapInspectionMessage = "";
        KaitouOnline.KaitouOnlineGameBridge.NotifyHoneyTrapInspection(
            victimActionSeat, false);
    }

    private static List<CardInteraction> GetActionCardsForSeat(int seat)
    {
        if (seat == 0)
        {
            Player player = FindFirstObjectByType<Player>(FindObjectsInactive.Include);
            return player != null ? player.playerCards : null;
        }
        if (seat == 1)
        {
            Player2 player = FindFirstObjectByType<Player2>(FindObjectsInactive.Include);
            return player != null ? player.player2Cards : null;
        }
        if (seat == 2)
        {
            Player3 player = FindFirstObjectByType<Player3>(FindObjectsInactive.Include);
            return player != null ? player.player3Cards : null;
        }
        Player4 player4 = FindFirstObjectByType<Player4>(FindObjectsInactive.Include);
        return player4 != null ? player4.player4Cards : null;
    }

    private static CardInteraction GetSelectedActionCardForSeat(int seat)
    {
        if (seat == 0)
        {
            Player player = FindFirstObjectByType<Player>(FindObjectsInactive.Include);
            return player != null ? player.SelectedCard : null;
        }
        if (seat == 1)
        {
            Player2 player = FindFirstObjectByType<Player2>(FindObjectsInactive.Include);
            return player != null ? player.SelectedCard : null;
        }
        if (seat == 2)
        {
            Player3 player = FindFirstObjectByType<Player3>(FindObjectsInactive.Include);
            return player != null ? player.SelectedCard : null;
        }
        Player4 player4 = FindFirstObjectByType<Player4>(FindObjectsInactive.Include);
        return player4 != null ? player4.SelectedCard : null;
    }

    private void DrawAllSpecialCardsTestButton(GUIStyle style)
    {
        if (!showAllSpecialCardsButton) return;
        float width = 245f * uiScale;
        float height = 44f * uiScale;
        Rect rect = new Rect(
            allSpecialCardsButtonOffset.x,
            allSpecialCardsButtonOffset.y,
            width, height);
        GUI.enabled = !cardSelected && !gameFinished;
        if (GUI.Button(rect, "テスト：自分に特殊カード全種類", style))
        {
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
                KaitouOnline.KaitouOnlineGameBridge.RequestGrantAllSpecialCards();
            else
                SpecialActionCardSystem.GrantAllSpecialCardsToPlayerOne(this);
        }
        GUI.enabled = true;
    }

    private void DrawCpuActionCardMoveTestButton(GUIStyle style)
    {
        if (!KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession) return;
        float width = 245f * uiScale;
        float height = 44f * uiScale;
        float firstButtonHeight = showAllSpecialCardsButton ? height + 8f * uiScale : 0f;
        Rect rect = new Rect(
            allSpecialCardsButtonOffset.x,
            allSpecialCardsButtonOffset.y + firstButtonHeight,
            width, height);
        if (GUI.Button(rect, "テスト：CPU行動カード移動", style))
            KaitouOnline.KaitouOnlineGameBridge.RunCpuActionCardVisualTest();
    }

    private void HandleActionHandScroll()
    {
        if (actionCardGripActive) return;
        if (!actionHandVisible || cardSelected || cards.Count <= 4) return;
        Event current = Event.current;
        Rect area = new Rect(0f, Screen.height * 0.35f, Screen.width, Screen.height * 0.5f);
        if (current.type == EventType.ScrollWheel && area.Contains(current.mousePosition))
        {
            actionScrollOffset -= current.delta.y * 0.12f;
            RefreshActionHandLayout(true);
            current.Use();
        }
        else if (current.type == EventType.MouseDown && current.button == 0 && area.Contains(current.mousePosition))
        {
            draggingActionHand = true;
            actionHandVelocity = 0f;
            lastActionDragX = current.mousePosition.x;
        }
        else if (current.type == EventType.MouseDrag && draggingActionHand)
        {
            float delta = current.mousePosition.x - lastActionDragX;
            lastActionDragX = current.mousePosition.x;
            ScrollActionHandByPixels(delta);
            current.Use();
        }
        else if (current.type == EventType.MouseUp)
        {
            draggingActionHand = false;
        }
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

        float width = 220f * uiScale;
        float height = 48f * uiScale;
        float y = Screen.height - height - 14f * uiScale + displayButtonOffset.y * uiScale;
        // 旧ランダムお宝ターンボタンの左下位置を真贋チェックに使用する。
        float firstX = 14f * uiScale + displayButtonOffset.x * uiScale;
        string viewLabel = viewing
            ? "展示場を見るのをやめる"
            : "展示場を見る\n（真贋チェック）";
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
            List<CardInteraction> actionCards = id == 0
                ? player1?.playerCards
                : id == 1 ? player2?.player2Cards
                : id == 2 ? player3?.player3Cards
                : player4?.player4Cards;
            int normalActionCount = actionCards != null
                ? actionCards.FindAll(card => card != null && !card.IsSpecialAction).Count : 0;
            int specialActionCount = actionCards != null
                ? actionCards.FindAll(card => card != null && card.IsSpecialAction).Count : 0;
            int treasureId = ToTreasurePlayerId(id);
            int treasureCount = treasureController != null ? treasureController.GetHandCount(treasureId) : 0;
            string prisonStatus = SpecialActionCardSystem.IsImprisoned(id) ? "  【監獄】" :
                SpecialActionCardSystem.IsExcludedFromActionToday(id) ? "  【休み】" : "";
            string cageStatus = actionPlayerCount == 2
                ? $"  檻:{SpecialActionCardSystem.GetConsecutiveCageCount(id)}/3"
                : "";
            bool online = KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession;
            int networkSeat = online
                ? KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(id) : id;
            string shownName = online && KaitouOnline.KaitouOnlineSession.Instance != null
                ? KaitouOnline.KaitouOnlineSession.Instance.GetPlayerName(networkSeat)
                : $"P{networkSeat + 1}";
            GUI.Label(new Rect(startX + width * i, Screen.height + playerCountsOffset.y * uiScale,
                    width, 34f * uiScale),
                $"{shownName}  通常:{normalActionCount} 特殊:{specialActionCount}  " +
                $"宝:{treasureCount}{cageStatus}{prisonStatus}", style);
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
        activeSelectedCard = selectedCard;
        selectedCard.EnsureVisibleForTable();
        selectedCard.DisableClick(false);
        selectedCard.LockSelectedThiefScale();
        Debug.Log("カード選択: " + selectedCard.name);

        // 選択確定後は、選んだ1枚だけを卓上へ残して手札を収納する。
        // actionHandVisibleもfalseにすることで、直後にオンライン手札同期が入っても
        // 残りのカードが開いた位置へ戻されないようにする。
        SetActionHandVisible(false);

        // **怪盗カードでなければカメラを移動**
        if (!selectedCard.isPhantomThief &&
            !KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
        {
            Camera.main.GetComponent<CameraController>().MoveCamera();
        }


    }

    public void MoveCardsAfterThiefPhase()
    {
        SpecialActionCardSystem.FinalizeTwoPlayerCageStreaks();
        SpecialActionCardSystem.ConsumeSelectedCards(this);
        SpecialActionCardSystem.ClearTurnEffects();
        // 予告状は翌日の実行が終わるまで、ほかの行動手札を閉じたままにする。
        bool keepHandClosed = SpecialActionCardSystem.HasPendingAdvanceNotice(0);
        currentDay++;
        KaitouOnline.KaitouOnlineGameBridge.PrepareNextActionDay();
        actionHandVisible = !keepHandClosed &&
            !SpecialActionCardSystem.IsImprisoned(0);
        cardSelected = false;
        activeSelectedCard = null;
        // 従来はCameraControllerの遅い後処理までクリック再開を待っていたため、
        // ゲスト側だけ途中の同期待ちに入ると2日目以降が操作不能になった。
        // ターン番号を更新したこの地点で、自分の全手札を確実に次の選択状態へ戻す。
        foreach (CardInteraction card in cards)
        {
            if (card == null ||
                SpecialActionCardSystem.IsAdvanceNoticePendingCard(0, card)) continue;
            card.ResetForNextActionSelection();
        }
        // Start()の再実行による瞬間移動は行わず、現在位置から手札へ戻す。
        RefreshActionHandLayout();
        RefreshPlayerOneCardAvailability();
        Debug.Log("2日目開始！");
    }

    public void ForceAdvanceNoticeSelection(CardInteraction card)
    {
        if (card == null) return;
        // 先に選択中として登録し、手札を閉じる処理から予告状自身を除外する。
        cardSelected = true;
        activeSelectedCard = card;
        SetActionHandVisible(false);
    }

}

