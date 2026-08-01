using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TreasureGame
{
public enum TreasurePhase { Waiting, SelectingDisplays, Displaying, Inspecting, Robbing, RobberDisplay, EndingTurn, GameOver }

public class TreasureController : MonoBehaviour
{
    [SerializeField, Range(2, 4)] private int playerCount = 4;
    [SerializeField] private List<Player> players = new List<Player>();
    [SerializeField] private List<Transform> playerPositions = new List<Transform>();
    [SerializeField] private List<Transform> displayPositions = new List<Transform>();
    [SerializeField] private float moveDuration = 0.65f;
    [SerializeField] private float handSlideDuration = 0.85f;
    [SerializeField] private float displaySlideDistance = 2.5f;
    [SerializeField] private float victoryFlipDuration = 0.35f;
    [SerializeField] private float victoryFlipInterval = 0.07f;
    [SerializeField] private bool demoSelectAllPlayersOnStart;

    private readonly List<Treasure> allTreasures = new List<Treasure>();
    private readonly Dictionary<int, GameObject> templates = new Dictionary<int, GameObject>();
    private readonly Dictionary<Player, List<Treasure>> displaySelections = new Dictionary<Player, List<Treasure>>();
    private readonly Dictionary<Player, int> requiredDisplayCounts = new Dictionary<Player, int>();
    private readonly Dictionary<Player, TreasureType> displayTypeRestrictions = new Dictionary<Player, TreasureType>();
    private readonly HashSet<Player> optionalRelicDoubleDisplayPlayers = new HashSet<Player>();
    private readonly List<Player> displayPlayers = new List<Player>();
    private readonly List<RobberyDeclaration> robberies = new List<RobberyDeclaration>();
    private readonly List<Treasure> stolenThisRobbery = new List<Treasure>();
    private readonly Dictionary<Player, List<Treasure>> stolenByRobber = new Dictionary<Player, List<Treasure>>();
    // 今ターン盗んだカードと盗んだ本人。展示完了まで本人のほかの手札を暗くする。
    private readonly HashSet<Player> stolenFocusPlayers = new HashSet<Player>();
    private readonly Dictionary<Player, Dictionary<Player, int>> stolenCountsByVictim =
        new Dictionary<Player, Dictionary<Player, int>>();
    private readonly Dictionary<Player, List<Treasure>> robberDisplaySelections = new Dictionary<Player, List<Treasure>>();
    private readonly Dictionary<Player, SpecialActionEffect> robberyEffects = new Dictionary<Player, SpecialActionEffect>();
    private readonly HashSet<Player> analysisCompleted = new HashSet<Player>();
    private readonly List<Treasure> analysisSelections = new List<Treasure>();
    private Player analysisRobber;
    private int requiredAnalysisCount;
    private bool analysisResolving;
    private bool appraiserRearrangeActive;
    private Player appraiserRearrangePlayer;
    private readonly HashSet<TreasureType> appraiserRearrangeTypes = new HashSet<TreasureType>();
    private Treasure appraiserFirstSelection;
    private readonly HashSet<Treasure> treasuresInTransit = new HashSet<Treasure>();
    private readonly HashSet<Player> blockedFromWinningThisTurn = new HashSet<Player>();
    private readonly HashSet<int> eliminatedPlayerIds = new HashSet<int>();
    private readonly List<int> queuedArrestRewardPlayerIds = new List<int>();
    private static readonly TreasureType[] RevealTypeOrder =
    {
        TreasureType.Gold,
        TreasureType.Jewel,
        TreasureType.Relic,
        TreasureType.Painting
    };
    private int robberyIndex;
    private int stealsRemaining;
    private bool freeMoveInProgress;
    private bool endTurnAfterCurrentDisplay;
    private bool arrestRewardDisplayActive;
    private int nextTreasureNetworkId;
    private string gameResultText = string.Empty;
    public TreasurePhase Phase { get; private set; } = TreasurePhase.Waiting;
    public int PlayerCount => playerCount;
    public Player ActiveRobber => robberyIndex < robberies.Count ? robberies[robberyIndex].Player : null;
    public bool PlayerOneHandVisible => players.Count > 0 && players[0].HandVisible;
    public bool CanScrollPlayerOneHandBackward => players.Count > 0 && players[0].CanScrollHandBackward;
    public bool CanScrollPlayerOneHandForward => players.Count > 0 && players[0].CanScrollHandForward;
    public bool FreeInteractionMode { get; private set; }
    public string GameResultText => gameResultText;
    public int GetHandCount(int playerId)
    {
        return ValidPlayer(playerId) ? players[playerId].Stock.Count : 0;
    }

    public int GetHandTypeCount(int playerId, TreasureType type)
    {
        if (!ValidPlayer(playerId)) return 0;
        int count = 0;
        foreach (Treasure treasure in players[playerId].Stock)
            if (treasure != null && treasure.Type == type) count++;
        return count;
    }

    public void SetPlayerEliminated(int playerId, bool eliminated = true)
    {
        if (!ValidPlayer(playerId)) return;
        if (eliminated) eliminatedPlayerIds.Add(playerId);
        else eliminatedPlayerIds.Remove(playerId);
        Debug.Log($"【脱落同期】Player{playerId + 1}：{(eliminated ? "勝利対象外" : "参加中")}");
    }
    public string InstructionText
    {
        get
        {
            if (Phase == TreasurePhase.GameOver) return gameResultText;
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession &&
                !string.IsNullOrEmpty(KaitouOnline.KaitouOnlineGameBridge.PriorityMessage))
                return KaitouOnline.KaitouOnlineGameBridge.PriorityMessage;
            if (FreeInteractionMode) return "FREEモード：手札を押すと展示、展示品を押すとPlayer1の手札へ戻ります。";
            if (Phase == TreasurePhase.SelectingDisplays)
            {
                Player playerOne = players.Count > 0 ? players[0] : null;
                if (playerOne != null && requiredDisplayCounts.TryGetValue(playerOne, out int required) &&
                    displaySelections.TryGetValue(playerOne, out List<Treasure> selected))
                {
                    int remaining = Mathf.Max(0, required - selected.Count);
                    if (remaining > 0) return arrestRewardDisplayActive
                        ? $"檻の逮捕報酬により、展示する宝をあと{remaining}つ選んでください。"
                        : $"展示する宝をあと{remaining}つ選んでください。";
                }
                if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
                {
                    Player waiting = displayPlayers.Find(p =>
                        displaySelections.TryGetValue(p, out List<Treasure> cards) &&
                        cards.Count < requiredDisplayCounts[p]);
                    if (waiting != null)
                        return $"{OnlinePlayerLabel(waiting)}が展示する宝を選んでいます。";
                }
                return string.Empty;
            }
            if (Phase == TreasurePhase.Robbing)
                return ActiveRobber != null && ActiveRobber.PlayerId == 0
                    ? $"他の展示室から宝をあと{stealsRemaining}つ盗んでください。"
                    : ActiveRobber != null &&
                      KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession
                        ? $"{OnlinePlayerLabel(ActiveRobber)}が怪盗中です。"
                        : string.Empty;
            if (Phase == TreasurePhase.Inspecting)
                return analysisRobber != null && analysisRobber.PlayerId == 0
                    ? $"分析メガネ：真贋を確認する宝をあと{Mathf.Max(0, requiredAnalysisCount - analysisSelections.Count)}つ選んでください。"
                    : analysisRobber != null &&
                      KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession
                        ? $"{OnlinePlayerLabel(analysisRobber)}が分析メガネを使用中です。"
                        : string.Empty;
            if (Phase == TreasurePhase.RobberDisplay)
            {
                Player playerOne = players.Count > 0 ? players[0] : null;
                if (playerOne != null && stolenByRobber.ContainsKey(playerOne))
                {
                    int selectedCount = robberDisplaySelections.TryGetValue(playerOne, out List<Treasure> selected)
                        ? selected.Count : 0;
                    int remaining = RequiredRobberDisplayCount(playerOne) - selectedCount;
                    if (remaining > 0)
                        return $"盗んだ宝から、展示する宝をあと{remaining}つ選んでください。";
                }
                if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
                {
                    Player waiting = new List<Player>(stolenByRobber.Keys).Find(p =>
                        !robberDisplaySelections.TryGetValue(p, out List<Treasure> cards) ||
                        cards.Count < RequiredRobberDisplayCount(p));
                    if (waiting != null)
                        return $"{OnlinePlayerLabel(waiting)}が盗品から展示する宝を選んでいます。";
                }
                return string.Empty;
            }
            if (Phase == TreasurePhase.Displaying) return string.Empty;
            if (Phase == TreasurePhase.EndingTurn) return string.Empty;
            return KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession
                ? KaitouOnline.KaitouOnlineGameBridge.WaitingMessage
                : string.Empty;
        }
    }

    public void ToggleFreeInteractionMode()
    {
        FreeInteractionMode = !FreeInteractionMode;
        Debug.Log($"<color=#70E8FF>【FREE】{(FreeInteractionMode ? "ON：全ての宝を確認できます" : "OFF")}</color>");
        RefreshInteraction();
    }

    public void TogglePlayerOneHand()
    {
        if (players.Count == 0) return;
        players[0].SetHandVisible(!players[0].HandVisible, handSlideDuration);
        RefreshInteraction();
    }

    public void SetPlayerOneHandVisible(bool visible)
    {
        if (players.Count == 0 || players[0].HandVisible == visible) return;
        players[0].SetHandVisible(visible, handSlideDuration);
        RefreshInteraction();
    }

    public void RevealPlayerDisplay(int playerId)
    {
        if (!ValidPlayer(playerId)) return;
        var facedownCards = new List<Treasure>();
        foreach (Treasure card in players[playerId].DisplayedTreasures)
            if (card != null && !card.IsFaceUp) facedownCards.Add(card);
        if (facedownCards.Count == 0)
        {
            Debug.Log($"【真贋確認】Player{playerId + 1}に未公開の展示品はありません。");
            return;
        }
        foreach (Treasure card in facedownCards)
            card.AnimateFlipToFaceUp(victoryFlipDuration);
        Debug.Log($"<color=#FFD966>【真贋確認】Player{playerId + 1}の展示品を公開します。</color>");
    }

    public void HidePlayerDisplay(int playerId)
    {
        if (!ValidPlayer(playerId)) return;
        int hiddenCount = 0;
        foreach (Treasure card in players[playerId].DisplayedTreasures)
        {
            if (card == null || !card.IsFaceUp) continue;
            card.AnimateFlipToFaceDown(victoryFlipDuration);
            hiddenCount++;
        }
        if (hiddenCount > 0)
            Debug.Log($"<color=#B0B0B0>【真贋確認終了】Player{playerId + 1}の展示品を裏向きへ戻します。</color>");
    }

    public List<Treasure> RevealDisplayedTypeExceptPlayer(int excludedPlayerId, TreasureType type)
    {
        var revealed = new List<Treasure>();
        for (int i = 0; i < playerCount; i++)
        {
            if (i == excludedPlayerId) continue;
            foreach (Treasure card in players[i].DisplayedTreasures)
            {
                if (card == null || card.Type != type) continue;
                if (!card.IsFaceUp) card.AnimateFlipToFaceUp(victoryFlipDuration);
                revealed.Add(card);
            }
        }
        Debug.Log($"<color=#FFD966>【鑑定士】P{excludedPlayerId + 1}以外の{type}を{revealed.Count}枚公開。</color>");
        return revealed;
    }

    public void HideRevealedTreasures(IEnumerable<Treasure> cards)
    {
        if (cards == null) return;
        foreach (Treasure card in cards)
            if (card != null && card.IsFaceUp)
                card.AnimateFlipToFaceDown(victoryFlipDuration);
    }

    public bool BeginAppraiserRearrangement(int playerId, IEnumerable<TreasureType> types)
    {
        if (!ValidPlayer(playerId) || types == null) return false;
        appraiserRearrangeTypes.Clear();
        foreach (TreasureType type in types) appraiserRearrangeTypes.Add(type);
        appraiserRearrangePlayer = players[playerId];
        appraiserFirstSelection = null;
        appraiserRearrangeActive = appraiserRearrangeTypes.Count > 0 &&
            appraiserRearrangePlayer.DisplayedTreasures.Any(card =>
                card != null && appraiserRearrangeTypes.Contains(card.Type));
        if (!appraiserRearrangeActive) return false;
        foreach (Treasure card in appraiserRearrangePlayer.DisplayedTreasures)
            if (card != null && appraiserRearrangeTypes.Contains(card.Type))
                card.AnimateFlipToFaceUp(victoryFlipDuration);
        RefreshInteraction();
        return true;
    }

    public void FinishAppraiserRearrangement()
    {
        if (!appraiserRearrangeActive) return;
        appraiserRearrangeActive = false;
        appraiserRearrangePlayer = null;
        appraiserRearrangeTypes.Clear();
        appraiserFirstSelection = null;
        RefreshInteraction();
    }

    public int[] GetDisplayedTreasureNetworkOrder(int playerId)
    {
        return ValidPlayer(playerId)
            ? players[playerId].GetDisplayedTreasureNetworkOrder()
            : System.Array.Empty<int>();
    }

    public void ApplyOnlineAppraiserOrder(int networkSeat, int[] treasureIds)
    {
        int localIndex = LocalPlayerIndexForNetworkSeat(networkSeat);
        if (!ValidPlayer(localIndex)) return;
        players[localIndex].ApplyDisplayedTreasureNetworkOrder(treasureIds);
    }

    public IEnumerator RedisplayAppraisedTreasures(IEnumerable<Treasure> cards)
    {
        if (cards == null) yield break;
        var targets = new List<Treasure>();
        foreach (Treasure card in cards)
            if (card != null && !targets.Contains(card)) targets.Add(card);
        if (targets.Count == 0) yield break;

        Phase = TreasurePhase.Displaying;
        RefreshInteraction();
        // 並び替え結果が見えないよう、まず現在位置から全カードを展示場外へ退避する。
        foreach (Treasure card in targets)
        {
            card.Owner.GetDisplayPose(card, out _, out Quaternion rotation);
            Vector3 exit = card.transform.position +
                           rotation * Vector3.forward * (displaySlideDistance * 3f);
            card.AnimateTo(exit, rotation, moveDuration);
        }
        yield return new WaitForSeconds(moveDuration);

        // 見えない位置で裏向きにし、新しい並び順の入口へ移してから一斉再展示する。
        foreach (Treasure card in targets)
        {
            card.SetVisibleToLocalPlayer(false);
            card.SetFaceUp(false);
            card.Owner.GetDisplayPose(card, out Vector3 target, out Quaternion rotation);
            card.MoveTo(DisplayEntrance(target, rotation), rotation);
            card.SetVisibleToLocalPlayer(true);
            card.AnimateTo(target, rotation, moveDuration);
        }
        yield return new WaitForSeconds(moveDuration);
        Phase = TreasurePhase.Waiting;
        RefreshInteraction();
    }

    public void ShuffleAppraiserDisplay(int playerId, IEnumerable<TreasureType> types)
    {
        if (!ValidPlayer(playerId) || types == null) return;
        foreach (TreasureType type in types)
            players[playerId].ShuffleDisplayedType(type, moveDuration);
    }

    public void TestRevealAllDisplayedTreasures()
    {
        StopCoroutine(nameof(RevealAllDisplayedTreasuresForTest));
        StartCoroutine(nameof(RevealAllDisplayedTreasuresForTest));
    }

    private IEnumerator RevealAllDisplayedTreasuresForTest()
    {
        var displayedCards = new List<Treasure>();
        for (int i = 0; i < playerCount; i++)
            displayedCards.AddRange(players[i].DisplayedTreasures);

        foreach (Treasure card in displayedCards)
            card.SetFaceUp(false);

        yield return null;
        yield return RevealCardsByTypeOrder(displayedCards);
    }

    public void ScrollPlayerOneHand(float amount)
    {
        if (players.Count == 0 || !players[0].HandVisible) return;
        players[0].ScrollHand(amount, handSlideDuration * 0.7f);
    }

    public void DragPlayerOneHand(float screenPixels)
    {
        if (players.Count == 0 || !players[0].HandVisible) return;
        players[0].DragHandScreenPixels(screenPixels, Camera.main);
    }

    // 過去Git版と同じ慣性距離。直接ドラッグとは分けて余韻だけに使用する。
    public void DragPlayerOneHandLegacyInertia(float screenPixels)
    {
        if (players.Count == 0 || !players[0].HandVisible) return;
        players[0].DragHand(screenPixels / 75f);
    }

    private void Start()
    {
        CollectTemplates();
        foreach (Treasure t in FindObjectsByType<Treasure>(FindObjectsSortMode.None)) t.gameObject.SetActive(false);
        SetupGame(playerCount);
        if (demoSelectAllPlayersOnStart) BeginDisplayPhase(new[] { 0, 1, 2, 3 });
    }

    // オンラインのシーン読込直後、Startで山札を作る前に人数だけ確定する。
    public void PreparePlayerCount(int newPlayerCount)
    {
        playerCount = Mathf.Clamp(newPlayerCount, 2, 4);
    }

    public void RestartWithPlayerCount(int newPlayerCount)
    {
        if (Phase != TreasurePhase.Waiting && Phase != TreasurePhase.GameOver) return;
        StopAllCoroutines();
        foreach (Treasure treasure in allTreasures)
        {
            if (treasure == null) continue;
            treasure.gameObject.SetActive(false);
            Destroy(treasure.gameObject);
        }
        allTreasures.Clear();
        SetupGame(newPlayerCount);
    }

    private void SetupGame(int requestedPlayerCount)
    {
        treasuresInTransit.Clear();
        blockedFromWinningThisTurn.Clear();
        queuedArrestRewardPlayerIds.Clear();
        eliminatedPlayerIds.Clear();
        endTurnAfterCurrentDisplay = false;
        gameResultText = string.Empty;
        playerCount = Mathf.Clamp(requestedPlayerCount, 2, 4);
        for (int i = 0; i < players.Count; i++)
        {
            bool participating = i < playerCount;
            players[i].gameObject.SetActive(participating);
            if (!participating) continue;
            int seat = GetSeatIndex(i, playerCount);
            players[i].Configure(i, playerPositions[seat], displayPositions[seat]);
        }
        CreateDeck();
        for (int i = 0; i < playerCount; i++) players[i].SortHandForLayout();
        LayoutImmediate();
        BeginTurn();
        Debug.Log($"<color=#70E8FF>【人数設定】{playerCount}人プレイ。各プレイヤーへ宝を12枚配布しました。</color>");
    }

    private static int GetSeatIndex(int logicalPlayerIndex, int currentPlayerCount)
    {
        // 3人時は向かい側（Player2位置）を空席にし、Player3・4位置を使う。
        if (currentPlayerCount == 3 && logicalPlayerIndex > 0) return logicalPlayerIndex + 1;
        return logicalPlayerIndex;
    }

    // 行動カード処理からターン開始時に呼ぶ。
    public void BeginTurn()
    {
        displaySelections.Clear(); requiredDisplayCounts.Clear(); displayTypeRestrictions.Clear();
        optionalRelicDoubleDisplayPlayers.Clear();
        displayPlayers.Clear(); robberies.Clear(); stolenThisRobbery.Clear();
        stolenByRobber.Clear(); stolenCountsByVictim.Clear(); robberDisplaySelections.Clear();
        stolenFocusPlayers.Clear();
        blockedFromWinningThisTurn.Clear();
        queuedArrestRewardPlayerIds.Clear();
        endTurnAfterCurrentDisplay = false;
        Phase = TreasurePhase.Waiting;
        Debug.Log("<color=#70E8FF>【お宝】新しいターンを開始できます。</color>");
        RefreshInteraction();
    }

    // 展示カードを出した全プレイヤー番号を渡す。全員が選ぶと一斉展示する。
    public void BeginDisplayPhase(int[] playerIds)
    {
        if (playerIds == null) { Phase = TreasurePhase.Waiting; RefreshInteraction(); return; }
        int[] counts = new int[playerIds.Length];
        for (int i = 0; i < counts.Length; i++) counts[i] = 1;
        BeginDisplayPhase(playerIds, counts);
    }

    // 2枚展示・3枚展示など、プレイヤーごとに選ぶ枚数を指定する。
    public void BeginDisplayPhase(int[] playerIds, int[] displayCounts)
    {
        BeginDisplayPhase(playerIds, displayCounts, null);
    }

    // typeRestrictionsは-1で制限なし、それ以外はTreasureTypeの値。
    public void BeginDisplayPhase(int[] playerIds, int[] displayCounts, int[] typeRestrictions)
    {
        displaySelections.Clear(); requiredDisplayCounts.Clear(); displayTypeRestrictions.Clear();
        optionalRelicDoubleDisplayPlayers.Clear(); displayPlayers.Clear();
        if (playerIds == null) { Phase = TreasurePhase.Waiting; RefreshInteraction(); return; }
        int inputCount = Mathf.Min(playerIds.Length, displayCounts != null ? displayCounts.Length : 0);
        for (int i = 0; i < inputCount; i++)
        {
            int id = playerIds[i];
            if (!ValidPlayer(id)) continue;
            if (players[id].Stock.Count == 0)
            {
                Debug.Log($"<color=#B0B0B0>【展示パス】プレイヤー{id + 1}：展示できる手札がありません。</color>");
                continue;
            }
            Player player = players[id];
            if (requiredDisplayCounts.ContainsKey(player)) continue;
            int required = Mathf.Clamp(displayCounts[i], 1, player.Stock.Count);
            displayPlayers.Add(player);
            requiredDisplayCounts[player] = required;
            displaySelections[player] = new List<Treasure>();
            if (typeRestrictions != null && i < typeRestrictions.Length &&
                System.Enum.IsDefined(typeof(TreasureType), typeRestrictions[i]))
                displayTypeRestrictions[player] = (TreasureType)typeRestrictions[i];
            else if (typeRestrictions != null && i < typeRestrictions.Length &&
                     typeRestrictions[i] == -2)
                optionalRelicDoubleDisplayPlayers.Add(player);
        }
        if (displayPlayers.Count == 0)
        {
            arrestRewardDisplayActive = false;
            Phase = TreasurePhase.Waiting; RefreshInteraction(); return;
        }
        if (requiredDisplayCounts.ContainsKey(players[0]) && !players[0].HandVisible)
            players[0].SetHandVisible(true, handSlideDuration);
        Phase = TreasurePhase.SelectingDisplays;
        Debug.Log($"<color=#FFD966>【展示選択】{DisplayRequirementList()}。全員決定後に同時展示します。</color>");
        RefreshInteraction();
        KaitouOnline.KaitouOnlineGameBridge.ReplayDisplayChoices();
    }

    // 脱落者処理：指定プレイヤーの残り手札を、選択なしで全て同時展示する。
    public void DisplayAllTreasures(int playerId)
    {
        DisplayAllTreasures(new[] { playerId });
    }

    public void DisplayAllTreasures(int[] playerIds)
    {
        if (playerIds == null || Phase != TreasurePhase.Waiting) return;
        displaySelections.Clear(); requiredDisplayCounts.Clear(); displayTypeRestrictions.Clear();
        optionalRelicDoubleDisplayPlayers.Clear(); displayPlayers.Clear();
        foreach (int playerId in playerIds)
        {
            if (!ValidPlayer(playerId)) continue;
            Player player = players[playerId];
            if (player.Stock.Count == 0)
            {
                Debug.Log($"【全展示パス】プレイヤー{playerId + 1}に手札がありません。");
                continue;
            }
            displayPlayers.Add(player);
            displaySelections[player] = new List<Treasure>(player.Stock);
            requiredDisplayCounts[player] = player.Stock.Count;
            Debug.Log($"<color=#FFD966>【脱落者の全展示】プレイヤー{playerId + 1}が手札{player.Stock.Count}枚を全て展示します。</color>");
        }
        if (displayPlayers.Count == 0) return;
        StartCoroutine(ResolveDisplays());
    }

    // 行動カード側から、怪盗展示後に1枚展示する逮捕成功者を予約する。
    public void QueueArrestRewardDisplays(int[] playerIds)
    {
        queuedArrestRewardPlayerIds.Clear();
        if (playerIds == null) return;
        foreach (int id in playerIds)
            if (ValidPlayer(id))
                queuedArrestRewardPlayerIds.Add(id);
        Debug.Log($"【檻の逮捕報酬】{queuedArrestRewardPlayerIds.Count}人が怪盗展示後に1枚展示します。");
    }

    // 行動カード開示時に怪盗プレイヤーと宣言枚数を渡す。枚数の大きい順に実行する。
    public void BeginRobberyPhase(int[] playerIds, int[] declaredCounts, int[] specialEffects = null)
    {
        robberies.Clear(); robberyEffects.Clear(); analysisCompleted.Clear();
        if (playerIds == null || declaredCounts == null) { ContinueToArrestRewardsOrEndTurn(); return; }
        int count = Mathf.Min(playerIds.Length, declaredCounts.Length);
        for (int i = 0; i < count; i++)
        {
            if (!ValidPlayer(playerIds[i])) continue;
            Player robber = players[playerIds[i]];
            SpecialActionEffect effect = specialEffects != null && i < specialEffects.Length &&
                                         System.Enum.IsDefined(typeof(SpecialActionEffect), specialEffects[i])
                ? (SpecialActionEffect)specialEffects[i]
                : SpecialActionEffect.None;
            robberies.Add(new RobberyDeclaration(robber,
                Mathf.Clamp(declaredCounts[i], 1,
                    effect == SpecialActionEffect.AdvanceNotice ? 10 : 6)));
            robberyEffects[robber] = effect;
        }
        robberies.Sort((a, b) =>
        {
            int countOrder = b.Count.CompareTo(a.Count);
            return countOrder != 0 ? countOrder : a.Player.PlayerId.CompareTo(b.Player.PlayerId);
        });
        robberyIndex = 0;
        stolenByRobber.Clear(); robberDisplaySelections.Clear();
        stolenFocusPlayers.Clear();
        Debug.Log($"<color=#FF9F70>【怪盗開始】実行順：{RobberyList()}</color>");
        StartNextRobbery();
    }

    public void HandleTreasureClick(Treasure card)
    {
        if (!CanInteract(card)) return;
        if (appraiserRearrangeActive)
        {
            if (appraiserFirstSelection == null)
            {
                appraiserFirstSelection = card;
                card.SetForcedDim(false);
            }
            else
            {
                if (appraiserFirstSelection.Type == card.Type)
                    appraiserRearrangePlayer.SwapDisplayedTreasures(
                        appraiserFirstSelection, card, moveDuration);
                appraiserFirstSelection = null;
            }
            RefreshInteraction();
            return;
        }
        if (FreeInteractionMode)
        {
            if (card.Location == TreasureLocation.Hand) StartCoroutine(FreeDisplay(card));
            else StartCoroutine(FreeReturnToPlayerOne(card));
            return;
        }
        if (Phase == TreasurePhase.SelectingDisplays)
        {
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            {
                card.SetInteractable(false);
                KaitouOnline.KaitouOnlineGameBridge.SubmitDisplayTreasure(card);
                return;
            }
            List<Treasure> selected = displaySelections[card.Owner];
            if (!selected.Contains(card)) selected.Add(card);
            if (optionalRelicDoubleDisplayPlayers.Contains(card.Owner) &&
                selected.Count == 1 && card.Type != TreasureType.Relic)
                requiredDisplayCounts[card.Owner] = 1;
            Debug.Log($"【展示選択】P{card.Owner.PlayerId + 1}：{selected.Count}/{requiredDisplayCounts[card.Owner]}枚");
            card.SetInteractable(false);
            if (AllDisplaySelectionsComplete()) StartCoroutine(ResolveDisplays());
        }
        else if (Phase == TreasurePhase.Robbing)
        {
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            {
                card.SetInteractable(false);
                KaitouOnline.KaitouOnlineGameBridge.SubmitStealTreasure(card);
                return;
            }
            StartCoroutine(Steal(card));
        }
        else if (Phase == TreasurePhase.Inspecting)
        {
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            {
                card.SetInteractable(false);
                KaitouOnline.KaitouOnlineGameBridge.SubmitAnalysisTreasure(card);
                return;
            }
            analysisSelections.Add(card);
            card.AnimateFlipToFaceUp(0.35f);
            Debug.Log($"【分析メガネ】P{analysisRobber.PlayerId + 1}：{analysisSelections.Count}/{requiredAnalysisCount}枚確認");
            if (analysisSelections.Count >= requiredAnalysisCount)
            {
                analysisResolving = true;
                StartCoroutine(FinishAnalysisInspection());
            }
        }
        else if (Phase == TreasurePhase.RobberDisplay)
        {
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            {
                card.SetInteractable(false);
                KaitouOnline.KaitouOnlineGameBridge.SubmitRobberDisplayTreasure(card);
                return;
            }
            ApplyRobberDisplayChoice(card);
        }
        RefreshInteraction();
    }

    private void ApplyRobberDisplayChoice(Treasure card)
    {
        if (card == null) return;
        {
            if (!robberDisplaySelections.TryGetValue(card.Owner, out List<Treasure> selected))
            {
                selected = new List<Treasure>();
                robberDisplaySelections[card.Owner] = selected;
            }
            selected.Add(card);
            int required = RequiredRobberDisplayCount(card.Owner);
            Debug.Log($"【怪盗後の展示選択】プレイヤー{card.Owner.PlayerId + 1}：{selected.Count}/{required}枚");
            card.SetInteractable(false);
            if (AllRobberDisplaySelectionsComplete()) StartCoroutine(ResolveRobberDisplays());
        }
    }

    public void ApplyOnlineRobberDisplayChoice(int actorSeat, int treasureId)
    {
        if (Phase != TreasurePhase.RobberDisplay) return;
        Treasure card = allTreasures.Find(value =>
            value != null && value.NetworkId == treasureId);
        if (card == null || card.Owner == null ||
            NetworkSeatForLocalPlayerIndex(card.Owner.PlayerId) != actorSeat ||
            !CanInteract(card))
        {
            Debug.LogWarning($"【オンライン怪盗展示】宝ID {treasureId} を展示できません。");
            return;
        }
        ApplyRobberDisplayChoice(card);
        RefreshInteraction();
    }

    public void ApplyOnlineDisplayChoice(int treasureId)
    {
        if (Phase != TreasurePhase.SelectingDisplays) return;
        Treasure card = allTreasures.Find(value =>
            value != null && value.NetworkId == treasureId);
        if (card == null || card.Owner == null ||
            card.Location != TreasureLocation.Hand ||
            !displaySelections.TryGetValue(card.Owner, out List<Treasure> selected))
        {
            Debug.LogWarning($"【オンライン展示】宝ID {treasureId} を現在の展示対象へ適用できません。");
            return;
        }
        if (selected.Contains(card)) return;
        selected.Add(card);
        if (optionalRelicDoubleDisplayPlayers.Contains(card.Owner) &&
            selected.Count == 1 && card.Type != TreasureType.Relic)
            requiredDisplayCounts[card.Owner] = 1;
        card.SetInteractable(false);
        Debug.Log($"<color=#70E8FF>【オンライン展示選択適用】" +
                  $"seat={card.NetworkOwnerSeat} treasure={treasureId}</color>");
        if (AllDisplaySelectionsComplete()) StartCoroutine(ResolveDisplays());
        else RefreshInteraction();
    }

    public void ApplyOnlineStealChoice(int actorSeat, int treasureId)
    {
        if (Phase != TreasurePhase.Robbing || ActiveRobber == null) return;
        int activeSeat = NetworkSeatForLocalPlayerIndex(ActiveRobber.PlayerId);
        if (activeSeat != actorSeat)
        {
            Debug.LogWarning($"【オンライン怪盗】手番外の選択を拒否しました。" +
                             $"actor={actorSeat} active={activeSeat}");
            return;
        }
        Treasure card = allTreasures.Find(value =>
            value != null && value.NetworkId == treasureId);
        if (card == null || !CanStealCard(card))
        {
            Debug.LogWarning($"【オンライン怪盗】宝ID {treasureId} は盗めません。");
            return;
        }
        StartCoroutine(Steal(card));
    }

    public void ApplyOnlineAnalysisChoice(int actorSeat, int treasureId)
    {
        if (Phase != TreasurePhase.Inspecting || analysisRobber == null ||
            analysisResolving) return;
        int activeSeat = NetworkSeatForLocalPlayerIndex(analysisRobber.PlayerId);
        if (activeSeat != actorSeat) return;
        Treasure card = allTreasures.Find(value =>
            value != null && value.NetworkId == treasureId);
        if (card == null || card.Location != TreasureLocation.Display ||
            card.Owner == analysisRobber || analysisSelections.Contains(card) ||
            analysisSelections.Count >= requiredAnalysisCount) return;

        analysisSelections.Add(card);
        // 真贋を見られるのは分析者本人だけ。他の画面では選択位置のみ明るくする。
        if (analysisRobber.PlayerId == 0)
            card.AnimateFlipToFaceUp(0.35f);
        RefreshInteraction();
        if (analysisSelections.Count >= requiredAnalysisCount)
        {
            analysisResolving = true;
            StartCoroutine(FinishAnalysisInspection());
        }
    }

    public bool CanInteract(Treasure card)
    {
        if (card == null) return false;
        if (appraiserRearrangeActive)
            return card.Owner == appraiserRearrangePlayer &&
                   card.Location == TreasureLocation.Display &&
                   appraiserRearrangeTypes.Contains(card.Type);
        if (FreeInteractionMode) return !freeMoveInProgress;
        if (Phase == TreasurePhase.SelectingDisplays)
            return card.Location == TreasureLocation.Hand && displayPlayers.Contains(card.Owner)
                && (card.Owner.PlayerId != 0 || card.Owner.HandVisible)
                && displaySelections.TryGetValue(card.Owner, out List<Treasure> selected)
                && (!displayTypeRestrictions.TryGetValue(card.Owner, out TreasureType requiredType) ||
                    card.Type == requiredType)
                && (!optionalRelicDoubleDisplayPlayers.Contains(card.Owner) ||
                    selected.Count == 0 || card.Type == TreasureType.Relic)
                && selected.Count < requiredDisplayCounts[card.Owner] && !selected.Contains(card);
        if (Phase == TreasurePhase.Robbing)
            return (!KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession ||
                    ActiveRobber != null && ActiveRobber.PlayerId == 0) &&
                CanStealCard(card);
        if (Phase == TreasurePhase.Inspecting)
            return !analysisResolving && analysisRobber != null && analysisRobber.PlayerId == 0 &&
                   card.Location == TreasureLocation.Display && card.Owner != analysisRobber &&
                   !analysisSelections.Contains(card) &&
                   analysisSelections.Count < requiredAnalysisCount;
        if (Phase == TreasurePhase.RobberDisplay)
            return card.Location == TreasureLocation.Hand && stolenByRobber.TryGetValue(card.Owner, out List<Treasure> stolen)
                && (card.Owner.PlayerId != 0 || card.Owner.HandVisible)
                && stolen.Contains(card)
                && (!robberDisplaySelections.TryGetValue(card.Owner, out List<Treasure> selected) ||
                    (selected.Count < RequiredRobberDisplayCount(card.Owner) && !selected.Contains(card)));
        return false;
    }

    private bool CanStealCard(Treasure card)
    {
        return card != null && ActiveRobber != null &&
               card.Location == TreasureLocation.Display &&
               card.Owner != ActiveRobber && stealsRemaining > 0 &&
               (!robberyEffects.TryGetValue(ActiveRobber, out SpecialActionEffect effect) ||
                effect != SpecialActionEffect.Balloon || card.Type != TreasureType.Gold);
    }

    public bool CanOnlineCpuSteal(Treasure card) =>
        Phase == TreasurePhase.Robbing && CanStealCard(card);

    public bool CanOnlineCpuAnalyze(Treasure card)
    {
        return Phase == TreasurePhase.Inspecting && !analysisResolving &&
               analysisRobber != null && card != null &&
               card.Location == TreasureLocation.Display &&
               card.Owner != analysisRobber && !analysisSelections.Contains(card) &&
               analysisSelections.Count < requiredAnalysisCount;
    }

    private IEnumerator ResolveDisplays()
    {
        KaitouOnline.KaitouOnlineGameBridge.ClearDisplayChoiceQueue();
        Phase = TreasurePhase.Displaying; RefreshInteraction();
        yield return MoveSelectedCardsBelowScreen(displaySelections);
        foreach (var pair in displaySelections) RecordDisplayBatch(pair.Key, pair.Value);
        foreach (var pair in displaySelections)
            foreach (Treasure card in pair.Value)
                pair.Key.AddToDisplay(card);
        foreach (var pair in displaySelections)
        {
            foreach (Treasure card in pair.Value)
            {
                card.SetVisibleToLocalPlayer(true);
                pair.Key.GetDisplayPose(card, out Vector3 p, out Quaternion r);
                card.MoveTo(DisplayEntrance(p, r), r);
                card.AnimateTo(p, r, moveDuration);
            }
        }
        yield return new WaitForSeconds(moveDuration);
        arrestRewardDisplayActive = false;
        if (displaySelections.ContainsKey(players[0]) && players[0].HandVisible)
            players[0].SetHandVisible(false, handSlideDuration);
        if (endTurnAfterCurrentDisplay)
        {
            endTurnAfterCurrentDisplay = false;
            StartCoroutine(EndTurn());
        }
        else
        {
            Phase = TreasurePhase.Waiting;
            RefreshInteraction();
        }
        Debug.Log("<color=#93E66C>【展示完了】全員のカードを同時に展示しました。</color>");
    }

    private IEnumerator FreeDisplay(Treasure card)
    {
        freeMoveInProgress = true; RefreshInteraction();
        Player owner = card.Owner;
        owner.GetDisplayPose(card, out Vector3 target, out Quaternion rotation);
        Vector3 exit = card.transform.position + rotation * Vector3.forward * displaySlideDistance;
        card.AnimateTo(exit, rotation, moveDuration);
        yield return new WaitForSeconds(moveDuration);

        owner.AddToDisplay(card);
        owner.GetDisplayPose(card, out target, out rotation);
        card.MoveTo(DisplayEntrance(target, rotation), rotation);
        card.AnimateTo(target, rotation, moveDuration);
        yield return new WaitForSeconds(moveDuration);

        Debug.Log($"<color=#93E66C>【FREE展示】{card.name} をP{owner.PlayerId + 1}の展示場へ移動しました。</color>");
        freeMoveInProgress = false; RefreshInteraction();
    }

    private IEnumerator FreeReturnToPlayerOne(Treasure card)
    {
        freeMoveInProgress = true; RefreshInteraction();
        Player oldOwner = card.Owner;
        Player playerOne = players[0];
        oldOwner.RemoveDisplayed(card);
        if (!playerOne.HandVisible) playerOne.SetHandVisible(true, moveDuration);
        playerOne.AddToStock(card);
        playerOne.SortHandForLayout();
        playerOne.EnsureCardVisible(card);
        playerOne.GetHandPose(card, out Vector3 target, out Quaternion handRotation);

        Vector3 slideTarget = target;
        slideTarget.y = card.transform.position.y;
        card.SetLocation(TreasureLocation.Hand);
        card.AnimateTo(slideTarget, card.transform.rotation, moveDuration);
        yield return new WaitForSeconds(moveDuration);

        card.SetFaceUp(true);
        card.MoveTo(target, handRotation);
        playerOne.AnimateHandLayout(moveDuration * 0.35f);
        yield return new WaitForSeconds(moveDuration * 0.35f);

        Debug.Log($"<color=#93E66C>【FREE回収】{card.name} をP1の手札へ戻しました。</color>");
        freeMoveInProgress = false; RefreshInteraction();
    }

    private IEnumerator Steal(Treasure card)
    {
        Phase = TreasurePhase.Displaying;
        RefreshInteraction();
        Player oldOwner = card.Owner;
        if (!stolenCountsByVictim.TryGetValue(ActiveRobber, out Dictionary<Player, int> victimCounts))
        {
            victimCounts = new Dictionary<Player, int>();
            stolenCountsByVictim.Add(ActiveRobber, victimCounts);
        }
        victimCounts[oldOwner] = victimCounts.TryGetValue(oldOwner, out int previousCount)
            ? previousCount + 1 : 1;
        oldOwner.RemoveDisplayed(card); // 他の展示品はここでは詰めない。
        ActiveRobber.AddToStock(card);
        card.SetNetworkIdentity(card.NetworkId,
            NetworkSeatForLocalPlayerIndex(ActiveRobber.PlayerId));
        if (ActiveRobber.PlayerId == 0 && !ActiveRobber.HandVisible)
            ActiveRobber.SetHandVisible(true, handSlideDuration);
        stolenThisRobbery.Add(card);
        if (!stolenByRobber.TryGetValue(ActiveRobber, out List<Treasure> stolen))
        {
            stolen = new List<Treasure>();
            stolenByRobber.Add(ActiveRobber, stolen);
        }
        stolen.Add(card);
        treasuresInTransit.Add(card);
        ActiveRobber.SortHandForLayout();
        ActiveRobber.EnsureCardVisible(card);
        ActiveRobber.GetHandPose(card, out Vector3 p, out Quaternion r);
        Quaternion slideRotation = card.transform.rotation;
        Vector3 slideTarget = GetRobberyDestination(ActiveRobber.PlayerId, card.transform.position.y);
        card.SetLocation(TreasureLocation.Hand);
        RefreshInteraction();
        card.AnimateTo(slideTarget, slideRotation, moveDuration);
        yield return new WaitForSeconds(moveDuration);

        // 手札へ到着してから初めて、他の手札と同じ表向き・角度・高さに揃える。
        card.SetFaceUp(true);
        card.MoveTo(p, r);
        // 移動中は暗転させない。手札へ到着した時点から、
        // 盗品以外の手札だけを暗くする。
        stolenFocusPlayers.Add(ActiveRobber);
        RefreshInteraction();
        ActiveRobber.AnimateHandLayout(moveDuration * 0.35f);
        yield return new WaitForSeconds(moveDuration * 0.35f);
        treasuresInTransit.Remove(card);
        stealsRemaining--;
        Debug.Log($"【怪盗】プレイヤー{ActiveRobber.PlayerId + 1} が {card.name} を盗みました。残り {stealsRemaining} 枚");
        if (stealsRemaining <= 0)
        {
            StartCoroutine(FinishRobberyAndStartNext(ActiveRobber));
        }
        else
        {
            Phase = TreasurePhase.Robbing;
            RefreshInteraction();
            KaitouOnline.KaitouOnlineGameBridge.ReplayTreasureActionChoices();
        }
    }

    private IEnumerator FinishRobberyAndStartNext(Player robber)
    {
        if (robber != null && robberyEffects.TryGetValue(robber, out SpecialActionEffect effect) &&
            effect == SpecialActionEffect.HoneyTrap &&
            stolenCountsByVictim.TryGetValue(robber, out Dictionary<Player, int> victimCounts))
        {
            HandManager manager = HandManager.Instance;
            foreach (KeyValuePair<Player, int> victim in victimCounts)
            {
                int victimSeat = manager != null
                    ? ActionSeatFromTreasurePlayerId(victim.Key.PlayerId, manager)
                    : victim.Key.PlayerId;
                Debug.Log($"<color=#FF87D7>【ハニートラップ】P{robber.PlayerId + 1}が" +
                          $"P{victim.Key.PlayerId + 1}から{victim.Value}枚盗難。" +
                          (victim.Value >= 2 ? "保有する行動カードを全公開。" : "保有する通常行動カードを公開。") +
                          "</color>");
                if (robber.PlayerId == 0 && manager != null)
                    yield return manager.StartCoroutine(
                        manager.ShowHoneyTrapHand(victimSeat, victim.Value >= 2));
                else
                    yield return new WaitForSeconds(0.65f);
            }
        }
        robberyIndex++;
        StartNextRobbery();
    }

    private static int ActionSeatFromTreasurePlayerId(int treasurePlayerId, HandManager manager)
    {
        if (manager != null && manager.ActionPlayerCount == 3)
        {
            if (treasurePlayerId == 1) return 2;
            if (treasurePlayerId == 2) return 3;
        }
        return treasurePlayerId;
    }

    private IEnumerator ResolveRobberDisplays()
    {
        KaitouOnline.KaitouOnlineGameBridge.ClearRobberDisplayChoiceQueue();
        Phase = TreasurePhase.Displaying; RefreshInteraction();
        Debug.Log("<color=#FFD966>【怪盗後の同時展示】選択された盗品を一斉に展示します。</color>");
        yield return MoveSelectedCardsBelowScreen(robberDisplaySelections);
        foreach (var pair in robberDisplaySelections)
        {
            RecordDisplayBatch(pair.Key, pair.Value);
            foreach (Treasure card in pair.Value)
            {
                pair.Key.AddToDisplay(card);
                card.SetVisibleToLocalPlayer(true);
                pair.Key.GetDisplayPose(card, out Vector3 p, out Quaternion r);
                card.MoveTo(DisplayEntrance(p, r), r);
                card.AnimateTo(p, r, moveDuration);
            }
        }
        yield return new WaitForSeconds(moveDuration);
        stolenFocusPlayers.Clear();
        RefreshInteraction();
        if (robberDisplaySelections.ContainsKey(players[0]) && players[0].HandVisible)
            players[0].SetHandVisible(false, handSlideDuration);
        ContinueToArrestRewardsOrEndTurn();
    }

    private void StartNextRobbery()
    {
        stolenThisRobbery.Clear();
        if (robberyIndex >= robberies.Count)
        {
            KaitouOnline.KaitouOnlineGameBridge.ClearStealChoiceQueue();
            if (stolenByRobber.Count == 0) { ContinueToArrestRewardsOrEndTurn(); return; }
            if (stolenByRobber.ContainsKey(players[0]) && !players[0].HandVisible)
                players[0].SetHandVisible(true, handSlideDuration);
            Phase = TreasurePhase.RobberDisplay;
            Debug.Log($"<color=#FFD966>【怪盗後の展示選択】盗品から必要枚数を選んでください。バルーンは最大3枚、それ以外は1枚です。</color>");
            RefreshInteraction();
            KaitouOnline.KaitouOnlineGameBridge.ReplayTreasureActionChoices();
            return;
        }
        if (robberyEffects.TryGetValue(ActiveRobber, out SpecialActionEffect pendingEffect) &&
            pendingEffect == SpecialActionEffect.AnalysisGlasses &&
            !analysisCompleted.Contains(ActiveRobber))
        {
            StartCoroutine(BeginAnalysisInspection(ActiveRobber));
            return;
        }
        int available = 0;
        bool balloonRobbery = robberyEffects.TryGetValue(ActiveRobber, out SpecialActionEffect activeEffect) &&
                              activeEffect == SpecialActionEffect.Balloon;
        for (int i = 0; i < playerCount; i++)
        {
            if (players[i] == ActiveRobber) continue;
            foreach (Treasure treasure in players[i].DisplayedTreasures)
                if (!balloonRobbery || treasure.Type != TreasureType.Gold) available++;
        }
        stealsRemaining = Mathf.Min(robberies[robberyIndex].Count, available);
        if (stealsRemaining == 0)
        {
            Debug.Log($"<color=#B0B0B0>【怪盗パス】プレイヤー{ActiveRobber.PlayerId + 1}：盗める展示品がありません。</color>");
            robberyIndex++;
            StartNextRobbery();
            return;
        }
        Phase = TreasurePhase.Robbing;
        Debug.Log($"<color=#FF9F70>【怪盗中】プレイヤー{ActiveRobber.PlayerId + 1}：{stealsRemaining}枚盗んでください。</color>");
        RefreshInteraction();
        KaitouOnline.KaitouOnlineGameBridge.ReplayTreasureActionChoices();
    }

    private IEnumerator BeginAnalysisInspection(Player robber)
    {
        analysisRobber = robber;
        analysisSelections.Clear();
        analysisResolving = false;
        var candidates = new List<Treasure>();
        for (int i = 0; i < playerCount; i++)
        {
            if (players[i] == robber) continue;
            candidates.AddRange(players[i].DisplayedTreasures);
        }
        requiredAnalysisCount = Mathf.Min(3, candidates.Count);
        if (requiredAnalysisCount == 0)
        {
            analysisCompleted.Add(robber);
            analysisRobber = null;
            StartNextRobbery();
            yield break;
        }

        Phase = TreasurePhase.Inspecting;
        RefreshInteraction();
        KaitouOnline.KaitouOnlineGameBridge.ReplayTreasureActionChoices();
        Debug.Log($"<color=#70E8FF>【分析メガネ】P{robber.PlayerId + 1}が展示品{requiredAnalysisCount}枚を確認します。</color>");

        if (robber.PlayerId != 0 &&
            !KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
        {
            while (analysisSelections.Count < requiredAnalysisCount && candidates.Count > 0)
            {
                int index = Random.Range(0, candidates.Count);
                Treasure chosen = candidates[index];
                candidates.RemoveAt(index);
                analysisSelections.Add(chosen);
                RefreshInteraction();
                yield return new WaitForSeconds(0.35f);
            }
            analysisResolving = true;
            RefreshInteraction();
            yield return StartCoroutine(FinishAnalysisInspection());
        }
    }

    private IEnumerator FinishAnalysisInspection()
    {
        RefreshInteraction();
        yield return new WaitForSeconds(1.8f);
        if (analysisRobber != null && analysisRobber.PlayerId == 0)
            foreach (Treasure treasure in analysisSelections)
                treasure.AnimateFlipToFaceDown(0.35f);
        yield return new WaitForSeconds(0.45f);
        if (analysisRobber != null) analysisCompleted.Add(analysisRobber);
        analysisSelections.Clear();
        analysisRobber = null;
        analysisResolving = false;
        Phase = TreasurePhase.Displaying;
        StartNextRobbery();
    }

    private int RequiredRobberDisplayCount(Player player)
    {
        if (!stolenByRobber.TryGetValue(player, out List<Treasure> stolen)) return 0;
        bool balloon = robberyEffects.TryGetValue(player, out SpecialActionEffect effect) &&
                       effect == SpecialActionEffect.Balloon;
        bool advanceNotice = robberyEffects.TryGetValue(player, out effect) &&
                             effect == SpecialActionEffect.AdvanceNotice;
        return Mathf.Min(balloon ? 3 : advanceNotice ? 2 : 1, stolen.Count);
    }

    private bool AllRobberDisplaySelectionsComplete()
    {
        foreach (Player player in stolenByRobber.Keys)
        {
            if (!robberDisplaySelections.TryGetValue(player, out List<Treasure> selected) ||
                selected.Count < RequiredRobberDisplayCount(player))
                return false;
        }
        return true;
    }

    private IEnumerator EndTurn()
    {
        Phase = TreasurePhase.EndingTurn; RefreshInteraction();
        Debug.Log("<color=#70E8FF>【ターン終了】展示場の空きを詰めて整理します。</color>");
        for (int playerIndex = 0; playerIndex < playerCount; playerIndex++)
        {
            Player player = players[playerIndex];
            player.SortHandForLayout();
            for (int i = 0; i < player.Stock.Count; i++)
            {
                Treasure card = player.Stock[i]; player.GetHandPose(card, out Vector3 p, out Quaternion r); card.AnimateTo(p, r, moveDuration);
            }
            // ここで初めて盗難跡の空きを詰める。リストの追加順は変えない。
            player.CompactDisplaySlots();
            foreach (Treasure card in player.DisplayedTreasures)
            {
                player.GetDisplayPose(card, out Vector3 p, out Quaternion r); card.AnimateTo(p, r, moveDuration);
            }
        }
        yield return new WaitForSeconds(moveDuration);
        if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
        {
            int day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1;
            KaitouOnline.VictoryResolutionState state =
                KaitouOnline.KaitouOnlineSession.Instance != null &&
                KaitouOnline.KaitouOnlineSession.Instance.IsHost
                    ? BuildOnlineVictoryResolution(day)
                    : new KaitouOnline.VictoryResolutionState { day = day };
            KaitouOnline.KaitouOnlineGameBridge.BeginVictoryResolution(state);
            while (KaitouOnline.KaitouOnlineGameBridge.IsWaitingForVictoryResolution(day))
                yield return null;
        }
        else if (!EvaluateVictory()) BeginTurn();
    }

    public KaitouOnline.VictoryResolutionState BuildOnlineVictoryResolution(int day)
    {
        var reachedPlayers = new List<Player>();
        var survivors = new List<Player>();
        for (int i = 0; i < playerCount; i++)
            if (!eliminatedPlayerIds.Contains(players[i].PlayerId))
                survivors.Add(players[i]);
        if (playerCount > 1 && survivors.Count == 1)
            reachedPlayers.Add(survivors[0]);

        for (int i = 0; i < playerCount; i++)
        {
            Player player = players[i];
            if (reachedPlayers.Contains(player)) continue;
            if (VictoryScore(player) >= 7 &&
                !eliminatedPlayerIds.Contains(player.PlayerId) &&
                !blockedFromWinningThisTurn.Contains(player))
                reachedPlayers.Add(player);
        }
        if (reachedPlayers.Count == 0)
            return new KaitouOnline.VictoryResolutionState
            {
                day = day, gameOver = false,
                winnerSeats = System.Array.Empty<int>(),
                revealSeats = System.Array.Empty<int>()
            };

        Player best = reachedPlayers[0];
        for (int i = 1; i < reachedPlayers.Count; i++)
            if (CompareVictoryPriority(reachedPlayers[i], best) > 0) best = reachedPlayers[i];
        var winners = reachedPlayers.FindAll(player =>
            CompareVictoryPriority(player, best) == 0);
        return new KaitouOnline.VictoryResolutionState
        {
            day = day,
            gameOver = true,
            winnerSeats = winners.ConvertAll(player =>
                NetworkSeatForLocalPlayerIndex(player.PlayerId)).ToArray(),
            revealSeats = reachedPlayers.ConvertAll(player =>
                NetworkSeatForLocalPlayerIndex(player.PlayerId)).ToArray()
        };
    }

    public void ApplyOnlineVictoryResolution(KaitouOnline.VictoryResolutionState state)
    {
        if (!state.gameOver)
        {
            BeginTurn();
            return;
        }
        var reachedPlayers = new List<Player>();
        if (state.revealSeats != null)
            foreach (int seat in state.revealSeats)
            {
                int localIndex = LocalPlayerIndexForNetworkSeat(seat);
                if (ValidPlayer(localIndex)) reachedPlayers.Add(players[localIndex]);
            }
        string[] labels = state.winnerSeats == null
            ? System.Array.Empty<string>()
            : System.Array.ConvertAll(state.winnerSeats, seat => $"Player{seat + 1}");
        gameResultText = labels.Length <= 1
            ? $"{(labels.Length == 1 ? labels[0] : "Player")}の勝利！"
            : $"{string.Join("・", labels)}の引き分け勝利！";
        Phase = TreasurePhase.GameOver;
        RefreshInteraction();
        int localNetworkSeat = NetworkSeatForLocalPlayerIndex(0);
        if (state.winnerSeats != null &&
            System.Array.IndexOf(state.winnerSeats, localNetworkSeat) >= 0)
            ShowLocalWinnerDisplay();
        StartCoroutine(RevealVictoryDisplays(reachedPlayers));
        Debug.Log($"<color=#FFD700>【オンラインゲーム終了】{gameResultText}</color>");
    }

    private void ContinueToArrestRewardsOrEndTurn()
    {
        if (queuedArrestRewardPlayerIds.Count == 0)
        {
            StartCoroutine(EndTurn());
            return;
        }

        var rewardCounts = new Dictionary<int, int>();
        foreach (int playerId in queuedArrestRewardPlayerIds)
            rewardCounts[playerId] = rewardCounts.TryGetValue(playerId, out int count) ? count + 1 : 1;
        int[] rewardPlayers = new List<int>(rewardCounts.Keys).ToArray();
        int[] displayCounts = new int[rewardPlayers.Length];
        for (int i = 0; i < rewardPlayers.Length; i++)
            displayCounts[i] = rewardCounts[rewardPlayers[i]];
        queuedArrestRewardPlayerIds.Clear();
        endTurnAfterCurrentDisplay = true;
        arrestRewardDisplayActive = true;
        string rewardNames = string.Join("・", System.Array.ConvertAll(rewardPlayers, id => $"Player{id + 1}"));
        Debug.Log($"<color=#FFD966>【檻の逮捕報酬により展示】{rewardNames}が宝を1枚展示します。</color>");
        BeginDisplayPhase(rewardPlayers, displayCounts);
        if (Phase == TreasurePhase.Waiting)
        {
            endTurnAfterCurrentDisplay = false;
            StartCoroutine(EndTurn());
        }
    }

    private void RecordDisplayBatch(Player player, List<Treasure> cards)
    {
        if (cards.Count >= 2) blockedFromWinningThisTurn.Add(player);
        foreach (Treasure card in cards)
            if (card.Type == TreasureType.Gold)
            {
                blockedFromWinningThisTurn.Add(player);
                break;
            }
    }

    private bool EvaluateVictory()
    {
        var reachedPlayers = new List<Player>();
        var survivors = new List<Player>();
        for (int i = 0; i < playerCount; i++)
            if (!eliminatedPlayerIds.Contains(players[i].PlayerId))
                survivors.Add(players[i]);
        if (playerCount > 1 && survivors.Count == 1)
            reachedPlayers.Add(survivors[0]);

        for (int i = 0; i < playerCount; i++)
        {
            Player player = players[i];
            int score = VictoryScore(player);
            Debug.Log($"【勝利判定】P{player.PlayerId + 1}：本物換算{score}点" +
                (eliminatedPlayerIds.Contains(player.PlayerId) ? "（脱落・勝利対象外）" :
                 blockedFromWinningThisTurn.Contains(player) ? "（今ターンは勝利不可）" : ""));
            if (reachedPlayers.Contains(player)) continue;
            if (score >= 7 && !eliminatedPlayerIds.Contains(player.PlayerId) &&
                !blockedFromWinningThisTurn.Contains(player)) reachedPlayers.Add(player);
        }
        if (reachedPlayers.Count == 0) return false;

        Player best = reachedPlayers[0];
        for (int i = 1; i < reachedPlayers.Count; i++)
            if (CompareVictoryPriority(reachedPlayers[i], best) > 0) best = reachedPlayers[i];

        var finalWinners = new List<Player>();
        foreach (Player player in reachedPlayers)
            if (CompareVictoryPriority(player, best) == 0) finalWinners.Add(player);

        var winnerLabels = new List<string>();
        foreach (Player winner in finalWinners) winnerLabels.Add($"プレイヤー{winner.PlayerId + 1}");
        gameResultText = finalWinners.Count == 1
            ? $"{winnerLabels[0]}の勝利！"
            : $"{string.Join("・", winnerLabels)}の引き分け勝利！";
        Phase = TreasurePhase.GameOver;
        RefreshInteraction();
        if (finalWinners.Contains(players[0])) ShowLocalWinnerDisplay();
        StartCoroutine(RevealVictoryDisplays(reachedPlayers));
        Debug.Log($"<color=#FFD700>【ゲーム終了】{gameResultText}</color>");
        return true;
    }

    public bool EvaluateVictoryNow() => EvaluateVictory();

    private void ShowLocalWinnerDisplay()
    {
        HandManager.Instance?.SetActionHandVisible(false);
        SetPlayerOneHandVisible(false);
        RevealPlayerDisplay(0);
        CameraController cameraController =
            Object.FindFirstObjectByType<CameraController>();
        cameraController?.SetPlayerDisplayView(true);
    }

    private IEnumerator RevealVictoryDisplays(List<Player> reachedPlayers)
    {
        var displayedCards = new List<Treasure>();
        foreach (Player player in reachedPlayers)
            displayedCards.AddRange(player.DisplayedTreasures);

        yield return RevealCardsByTypeOrder(displayedCards);
    }

    private IEnumerator RevealCardsByTypeOrder(List<Treasure> cards)
    {
        foreach (TreasureType type in RevealTypeOrder)
        {
            bool revealedAny = false;
            foreach (Treasure card in cards)
            {
                if (card.Type != type) continue;
                card.AnimateFlipToFaceUp(victoryFlipDuration);
                revealedAny = true;
            }

            // 同じ種類は同じフレームに開始し、完了後に次の種類へ進む。
            if (revealedAny)
                yield return new WaitForSeconds(victoryFlipDuration + victoryFlipInterval);
        }
    }

    private static int VictoryScore(Player player)
    {
        int score = 0;
        foreach (Treasure card in player.DisplayedTreasures)
        {
            if (card.Authenticity != Authenticity.Real) continue;
            score += card.Type == TreasureType.Gold ? 2 : 1;
        }
        return score;
    }

    private static int CompareVictoryPriority(Player a, Player b)
    {
        TreasureType[] priority =
        {
            TreasureType.Gold, TreasureType.Relic, TreasureType.Jewel, TreasureType.Painting
        };
        foreach (TreasureType type in priority)
        {
            int comparison = CountReal(a, type).CompareTo(CountReal(b, type));
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private static int CountReal(Player player, TreasureType type)
    {
        int count = 0;
        foreach (Treasure card in player.DisplayedTreasures)
            if (card.Type == type && card.Authenticity == Authenticity.Real) count++;
        return count;
    }

    private void RefreshInteraction()
    {
        Player playerOne = players.Count > 0 ? players[0] : null;
        bool playerOneSelectingDisplay = Phase == TreasurePhase.SelectingDisplays && playerOne != null &&
            requiredDisplayCounts.TryGetValue(playerOne, out int required) &&
            displaySelections.TryGetValue(playerOne, out List<Treasure> selected) && selected.Count < required;
        bool playerOneRobbing = Phase == TreasurePhase.Robbing && ActiveRobber == playerOne;
        bool analysisActive = Phase == TreasurePhase.Inspecting &&
                              analysisRobber != null;
        bool hasPendingStolenDisplay = stolenFocusPlayers.Count > 0;

        foreach (Treasure treasure in allTreasures)
        {
            bool selectedForDisplay = Phase == TreasurePhase.SelectingDisplays &&
                treasure.Location == TreasureLocation.Hand &&
                treasure.Owner != null &&
                displaySelections.TryGetValue(treasure.Owner,
                    out List<Treasure> ownerDisplaySelections) &&
                ownerDisplaySelections.Contains(treasure);
            bool selectedForRobberDisplay = Phase == TreasurePhase.RobberDisplay &&
                treasure.Location == TreasureLocation.Hand &&
                treasure.Owner != null &&
                robberDisplaySelections.TryGetValue(treasure.Owner,
                    out List<Treasure> ownerRobberDisplaySelections) &&
                ownerRobberDisplaySelections.Contains(treasure);
            bool selectedForAnalysis = analysisSelections.Contains(treasure);
            bool forceAnalysisDim = false;
            if (analysisActive && treasure.Location == TreasureLocation.Display)
            {
                if (analysisRobber.PlayerId == 0)
                {
                    // 分析者本人の画面：自分の展示室だけ対象外として暗くする。
                    // 他人の展示品はすべて明るいまま選択できる。
                    forceAnalysisDim = treasure.Owner == analysisRobber;
                }
                else
                {
                    // 分析者以外の画面：選ばれた宝だけ明るくし、
                    // どの3枚が分析されたかは分かるが真贋は見せない。
                    forceAnalysisDim = !selectedForAnalysis;
                }
            }
            bool forceNonStolenHandDim = treasure.Owner != null &&
                stolenFocusPlayers.Contains(treasure.Owner) &&
                stolenByRobber.TryGetValue(treasure.Owner,
                    out List<Treasure> ownerStolenCards) &&
                treasure.Location == TreasureLocation.Hand &&
                !treasuresInTransit.Contains(treasure) &&
                !ownerStolenCards.Contains(treasure);
            treasure.SetForcedDim(forceAnalysisDim || forceNonStolenHandDim ||
                                  selectedForDisplay || selectedForRobberDisplay);
            bool visibleToPlayerOne = FreeInteractionMode || treasuresInTransit.Contains(treasure) ||
                treasure.Location == TreasureLocation.Display ||
                (treasure.Owner != null && treasure.Owner.PlayerId == 0);
            treasure.SetVisibleToLocalPlayer(visibleToPlayerOne);
            bool canClick = CanInteract(treasure);
            bool shouldDim = false;
            if (playerOneSelectingDisplay)
                shouldDim = treasure.Owner == playerOne && treasure.Location == TreasureLocation.Hand && !canClick;
            else if (playerOneRobbing)
            {
                bool alreadyStolen = playerOne != null &&
                    stolenByRobber.TryGetValue(playerOne, out List<Treasure> stolenCards) &&
                    stolenCards.Contains(treasure);
                shouldDim = !canClick && !alreadyStolen;
            }
            else if (hasPendingStolenDisplay)
            {
                bool isFocusedOwner = treasure.Owner != null &&
                    stolenFocusPlayers.Contains(treasure.Owner);
                bool isStolenCard = isFocusedOwner &&
                    stolenByRobber.TryGetValue(treasure.Owner,
                        out List<Treasure> focusedOwnerStolenCards) &&
                    focusedOwnerStolenCards.Contains(treasure);
                shouldDim = isFocusedOwner &&
                    treasure.Location == TreasureLocation.Hand &&
                    !treasuresInTransit.Contains(treasure) && !isStolenCard;
            }
            treasure.SetInteractionState(canClick, shouldDim);
        }
    }

    private void LayoutImmediate() { for (int i = 0; i < playerCount; i++) players[i].LayoutCards(); RefreshInteraction(); }
    private bool ValidPlayer(int id) => id >= 0 && id < playerCount;

    // 表面を見せたまま反転させないため、展示場のローカル+Z側へ隠してから裏面で入れる。
    private IEnumerator MoveSelectedCardsBelowScreen(Dictionary<Player, List<Treasure>> selections)
    {
        foreach (var pair in selections)
        {
            foreach (Treasure card in pair.Value)
            {
                pair.Key.GetDisplayPose(card, out Vector3 target, out Quaternion rotation);
                Vector3 exit = card.transform.position + rotation * Vector3.forward * displaySlideDistance;
                card.AnimateTo(exit, rotation, moveDuration);
            }
        }
        yield return new WaitForSeconds(moveDuration);
    }

    private Vector3 DisplayEntrance(Vector3 displayPosition, Quaternion displayRotation) =>
        displayPosition + displayRotation * Vector3.forward * displaySlideDistance;
    private Vector3 GetRobberyDestination(int playerId, float fixedY)
    {
        int seatIndex = GetSeatIndex(playerId, playerCount);
        switch (seatIndex)
        {
            case 0: return new Vector3(0f, fixedY, -4f);
            case 1: return new Vector3(0f, fixedY, 4f);
            case 2: return new Vector3(-4.5f, fixedY, 0f);
            default: return new Vector3(4.5f, fixedY, 0f);
        }
    }
    private static string PlayerList(List<Player> list)
    {
        var labels = new List<string>();
        foreach (Player player in list) labels.Add($"P{player.PlayerId + 1}");
        return string.Join("・", labels);
    }
    private bool AllDisplaySelectionsComplete()
    {
        foreach (Player player in displayPlayers)
            if (displaySelections[player].Count < requiredDisplayCounts[player]) return false;
        return true;
    }
    private string DisplayRequirementList()
    {
        var labels = new List<string>();
        foreach (Player player in displayPlayers)
            labels.Add($"P{player.PlayerId + 1}は{requiredDisplayCounts[player]}枚");
        return string.Join("・", labels);
    }
    private string RobberyList()
    {
        var labels = new List<string>();
        foreach (RobberyDeclaration robbery in robberies) labels.Add($"P{robbery.Player.PlayerId + 1}（{robbery.Count}枚）");
        return string.Join(" → ", labels);
    }

    private void CreateDeck()
    {
        nextTreasureNetworkId = 0;
        DealTypeRandomly(TreasureType.Gold, playerCount, 0);
        DealTypeRandomly(TreasureType.Painting, playerCount * 2, playerCount);
        DealTypeRandomly(TreasureType.Jewel, playerCount * 2, playerCount * 2);
        DealTypeRandomly(TreasureType.Relic, playerCount, playerCount * 3);
    }
    private void DealTypeRandomly(TreasureType type, int real, int fake)
    {
        var specs = new List<Spec>();
        for (int i=0;i<real;i++) specs.Add(new Spec(type, Authenticity.Real));
        for (int i=0;i<fake;i++) specs.Add(new Spec(type, Authenticity.Fake));
        System.Random onlineRandom = null;
        if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession &&
            KaitouOnline.KaitouOnlineSession.Instance != null)
            onlineRandom = new System.Random(unchecked(
                KaitouOnline.KaitouOnlineSession.Instance.GameSeed ^
                ((int)type + 1) * 73856093));
        for (int i = specs.Count - 1; i > 0; i--)
        {
            int j = onlineRandom != null
                ? onlineRandom.Next(0, i + 1)
                : Random.Range(0, i + 1);
            (specs[i], specs[j]) = (specs[j], specs[i]);
        }
        for (int i = 0; i < specs.Count; i++)
        {
            int networkOwnerSeat = i % playerCount;
            int localOwnerIndex = LocalPlayerIndexForNetworkSeat(networkOwnerSeat);
            Player owner = players[localOwnerIndex];
            Treasure card = CreateCard(specs[i], owner);
            if (card == null) continue;
            card.SetNetworkIdentity(nextTreasureNetworkId++, networkOwnerSeat);
            owner.AddToStock(card);
            allTreasures.Add(card);
        }
    }

    private int LocalPlayerIndexForNetworkSeat(int networkSeat)
    {
        if (!KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession ||
            KaitouOnline.KaitouOnlineSession.Instance == null)
            return networkSeat;
        return KaitouOnline.KaitouOnlineGameBridge.ToLocalSeat(networkSeat);
    }

    private int NetworkSeatForLocalPlayerIndex(int localPlayerIndex)
    {
        if (!KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession ||
            KaitouOnline.KaitouOnlineSession.Instance == null)
            return localPlayerIndex;
        return KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(localPlayerIndex);
    }

    private string OnlinePlayerLabel(Player player)
    {
        return player == null ? "相手" :
            $"Player{NetworkSeatForLocalPlayerIndex(player.PlayerId) + 1}";
    }
    private Treasure CreateCard(Spec spec, Player owner)
    {
        if (!templates.TryGetValue(Key(spec.Type,spec.Authenticity),out GameObject source)) return null;
        GameObject go=Instantiate(source,transform); go.SetActive(true); Treasure card=go.GetComponent<Treasure>(); card.enabled=true;
        card.InitializeFromSceneTemplate(spec.Type,spec.Authenticity,owner,this); return card;
    }
    private void CollectTemplates()
    {
        foreach(Treasure t in FindObjectsByType<Treasure>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(ReadName(t.name,out TreasureType type,out Authenticity auth)&&!templates.ContainsKey(Key(type,auth))) templates[Key(type,auth)]=t.gameObject;
    }
    private static bool ReadName(string n,out TreasureType t,out Authenticity a)
    {
        t=TreasureType.Gold;a=Authenticity.Real;if(n.Contains("金"))return true;
        if(n.Contains("絵画"))t=TreasureType.Painting;else if(n.Contains("宝石"))t=TreasureType.Jewel;else if(n.Contains("遺物"))t=TreasureType.Relic;else return false;
        if(n.Contains("偽"))a=Authenticity.Fake;else if(!n.Contains("真"))return false;return true;
    }
    private static int Key(TreasureType t,Authenticity a)=>(int)t*2+(int)a;
    private readonly struct Spec { public readonly TreasureType Type; public readonly Authenticity Authenticity; public Spec(TreasureType t,Authenticity a){Type=t;Authenticity=a;} }
    private readonly struct RobberyDeclaration { public readonly Player Player; public readonly int Count; public RobberyDeclaration(Player p,int c){Player=p;Count=c;} }
}
}
