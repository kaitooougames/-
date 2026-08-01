using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ArrestHandler : MonoBehaviour
{
    public static ArrestHandler Instance { get; private set; }
    public ArrestEffect arrestEffectPrefab; // 逮捕エフェクトのプレハブ
    private List<CardInteraction> fieldCards = new List<CardInteraction>(); // 場のカード
    private List<CardInteraction> phantomThieves = new List<CardInteraction>(); // 怪盗カード
    private List<CardInteraction> cages = new List<CardInteraction>(); // 檻カード
    public List<ArrestEffect> currentBattingEffects = new List<ArrestEffect>();
    private readonly List<int> successfulCagePlayerIds = new List<int>();
    private readonly Dictionary<CardInteraction, int> fieldCardOwners = new Dictionary<CardInteraction, int>();
    private readonly HashSet<int> allCagePlayerIdsThisTurn = new HashSet<int>();
    private CardInteraction pendingFrameUpCard;
    private readonly List<CardInteraction> pendingFrameUpTargets = new List<CardInteraction>();
    private int pendingFrameUpOwnerId = -1;
    public bool HasPendingFrameUpChoice => pendingFrameUpCard != null;
    [SerializeField] ArrestHandler arrestHandler; // ArrestHandlerを参照


    private void Start()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }
        Instance = this;
        CardInteraction.OnAllCardsFlipped -= ResolveArrests; // 二重登録を防ぐために先に解除
        // カードのめくりイベントに登録
        CardInteraction.OnAllCardsFlipped += ResolveArrests;
    }

    private void OnDestroy()
    {
        // イベントの登録解除（メモリリーク防止）
        CardInteraction.OnAllCardsFlipped -= ResolveArrests;
        if (Instance == this) Instance = null;
    }

    public void ResolveArrests()
    {
        Debug.Log($"ResolveArrests() が呼び出された: {Time.frameCount}");
        // **場のカードを取得**
        fieldCards.Clear();
        phantomThieves.Clear();
        cages.Clear();
        successfulCagePlayerIds.Clear();
        fieldCardOwners.Clear();
        allCagePlayerIdsThisTurn.Clear();

        // **各プレイヤーのカードを取得**
        Player player = FindObjectOfType<Player>();
        Player2 player2 = FindObjectOfType<Player2>();
        Player3 player3 = FindObjectOfType<Player3>();
        Player4 player4 = FindObjectOfType<Player4>();

        if (player != null && !player.isEliminated && player.SelectedCard != null)
        {
            if (SpecialActionCardSystem.IsDetectiveExcluded(player.SelectedCard))
                goto Player2Card;
            player.SelectedCard.SelectedNumber = player.SelectedNumber;
            Debug.Log($"[DEBUG] {player.SelectedCard.name} の SelectedNumber = {player.SelectedCard.SelectedNumber}");
            fieldCards.Add(player.SelectedCard);
            fieldCardOwners[player.SelectedCard] = 0;
        }

Player2Card:
        if (player2 != null && !player2.isEliminated && player2.SelectedCard != null)
        {
            if (SpecialActionCardSystem.IsDetectiveExcluded(player2.SelectedCard))
                goto Player3Card;
            player2.SelectedCard.SelectedNumber = player2.SelectedNumber;
            Debug.Log($"[DEBUG] {player2.SelectedCard.name} の SelectedNumber = {player2.SelectedCard.SelectedNumber}");
            fieldCards.Add(player2.SelectedCard);
            fieldCardOwners[player2.SelectedCard] = 1;
        }

Player3Card:
        if (player3 != null && !player3.isEliminated && player3.SelectedCard != null)
        {
            if (SpecialActionCardSystem.IsDetectiveExcluded(player3.SelectedCard))
                goto Player4Card;
            player3.SelectedCard.SelectedNumber = player3.SelectedNumber;
            Debug.Log($"[DEBUG] {player3.SelectedCard.name} の SelectedNumber = {player3.SelectedCard.SelectedNumber}");
            fieldCards.Add(player3.SelectedCard);
            fieldCardOwners[player3.SelectedCard] = 2;
        }

Player4Card:
        if (player4 != null && !player4.isEliminated && player4.SelectedCard != null)
        {
            if (SpecialActionCardSystem.IsDetectiveExcluded(player4.SelectedCard))
                goto CardsCollected;
            player4.SelectedCard.SelectedNumber = player4.SelectedNumber;
            Debug.Log($"[DEBUG] {player4.SelectedCard.name} の SelectedNumber = {player4.SelectedCard.SelectedNumber}");
            fieldCards.Add(player4.SelectedCard);
            fieldCardOwners[player4.SelectedCard] = 3;
        }

CardsCollected:
        // **取得した場のカードをデバッグ表示**
        Debug.Log($"場のカードの数: {fieldCards.Count}");
        foreach (var card in fieldCards)
        {
            Debug.Log($"カード名: {card.name}, 怪盗: {card.isPhantomThief}, 檻: {card.isCage}, 選択数: {card.SelectedNumber}");
        }

        // **怪盗カードを分類**
        foreach (var card in fieldCards)
        {
            if (card.isPhantomThief && !SpecialActionCardSystem.IsAdvanceNoticeWaiting(card))
            {
                phantomThieves.Add(card);
            }
            else if (card.isCage)
            {
                cages.Add(card);
                int cageOwner = FindPlayerIdByCard(card);
                if (cageOwner >= 0) allCagePlayerIdsThisTurn.Add(cageOwner);
            }
        }

        List<CardInteraction> watchdogs =
            cages.Where(c => c.specialEffect == SpecialActionEffect.Watchdog).ToList();
        if (watchdogs.Count > 0)
        {
            foreach (CardInteraction disabledCage in cages.Where(c =>
                         c.specialEffect != SpecialActionEffect.Watchdog))
            {
                Debug.Log($"【番犬】{disabledCage.name}の檻効果を無効化");
                if (arrestEffectPrefab != null)
                {
                    ArrestEffect effect = Instantiate(
                        arrestEffectPrefab, disabledCage.transform.position, Quaternion.identity);
                    effect.ShowCageDisabled();
                }
            }
            cages = watchdogs;
        }

        // **怪盗と檻の数をデバッグ表示**
        Debug.Log($"怪盗の数: {phantomThieves.Count}, 檻の数: {cages.Count}");
        foreach (var thief in phantomThieves)
        {
            Debug.Log($"怪盗カード: {thief.name}, 選択数: {thief.SelectedNumber}");
        }

        // 競合などの逮捕演出がカード状態へ影響する前に、檻の対象順を確定する。
        // 競合した怪盗も候補から外さない。同数時は全端末共通のPlayer番号で固定する。
        List<CardInteraction> orderedCageTargets = phantomThieves
            .OrderBy(t => t.SelectedNumber)
            .ThenBy(t => t.specialEffect == SpecialActionEffect.ElectricBaton ? 0 : 1)
            .ThenBy(t => t.specialEffect == SpecialActionEffect.SoloStage ? 1 : 0)
            .ThenBy(CageTargetPlayerOrder)
            .ToList();

        // **怪盗の競合処理**
        HandleThiefConflict(); // 🔹 檻がいなくても競合チェックを実行
        HandleSoloStageEffect();
        HandleSpecialGuardEffects();

        // **檻が0なら怪盗の競合のみ行い、以降の処理はスキップ**
        if (cages.Count == 0) return; 
        if (phantomThieves.Count == 0) return;

        // **檻の競合処理**
        if (cages.Count > phantomThieves.Count)
        {
            Debug.Log("檻の競合が発生！怪盗は逮捕されない。");
            HandleCageConflict(); // 🔹 競合エフェクトを表示
            return; // 逮捕処理なし
        }

        // **1対1で逮捕処理**
        ArrestThieves(orderedCageTargets);

    }
    private void HandleThiefConflict()
    {
        // **怪盗の競合（同じ数字の怪盗がいたプレイヤー同士のみ逮捕）**
        var groupedThieves = phantomThieves.GroupBy(t => t.SelectedNumber);

        foreach (var group in groupedThieves)
        {
            Debug.Log($"怪盗グループ: 数字 {group.Key}, 人数: {group.Count()}");

            if (group.Count() > 1) // 同じ数字を選んだ怪盗が2人以上いる場合
            {
                Debug.Log($"怪盗の競合発生！ 数字: {group.Key} → 競合した怪盗のみ逮捕");

                foreach (var thief in group)
                {
                    if (thief.specialEffect == SpecialActionEffect.SoloStage)
                        continue;
                    Debug.Log($"逮捕対象の怪盗: {thief.name}");
                    Arrest(thief);
                }
            }
        }
    }

    void HandleCageConflict()
    {
        if (currentBattingEffects.Count > 0)
        {
            Debug.Log("すでにBattingエフェクトが生成されています。");
            return; // すでに生成されていたら処理をスキップ
        }

        foreach (var cage in cages)
        {
            ArrestEffect effect = Instantiate(arrestEffectPrefab, cage.transform.position, Quaternion.identity);
            effect.ShowBatting();
         
            Debug.Log($"Battingエフェクトを生成: {cage.name}");
        }
    }


    private void ArrestThieves(List<CardInteraction> orderedCageTargets)
    {
        // 警備員ですでに逮捕済みでも、檻の対応対象なら檻は成功・報酬あり。
        // 逮捕ペナルティそのものはArrest内で二重発動を防ぐ。
        if (orderedCageTargets == null) orderedCageTargets = new List<CardInteraction>();

        if (cages.All(c => c.specialEffect == SpecialActionEffect.Watchdog))
        {
            ArrestThievesWithWatchdogs(orderedCageTargets);
            return;
        }

        bool prisonPlayed = cages.Any(c => c.specialEffect == SpecialActionEffect.Prison);
        for (int i = 0; i < cages.Count && i < orderedCageTargets.Count; i++)
        {
            CardInteraction target = orderedCageTargets[i];
            Debug.Log($"檻が {target.name}（宣言数{target.SelectedNumber}）を逮捕！");
            Arrest(target);
            if (target.specialEffect == SpecialActionEffect.ElectricBaton)
                ApplyElectricBatonCageEffect();
            if (prisonPlayed)
            {
                int thiefSeat = FindPlayerIdByCard(target);
                SpecialActionCardSystem.Imprison(
                    thiefSeat, HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1);
            }
            int cagePlayerId = FindPlayerIdByCard(cages[i]);
            if (cagePlayerId >= 0 && !successfulCagePlayerIds.Contains(cagePlayerId))
                successfulCagePlayerIds.Add(cagePlayerId);
        }
    }

    private void ApplyElectricBatonCageEffect()
    {
        int currentDay = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1;
        // 番犬に檻効果を無効化されたカードも「この日に檻を出した」対象へ含める。
        foreach (int cageSeat in allCagePlayerIdsThisTurn)
            SpecialActionCardSystem.ExcludeFromNextDay(cageSeat, currentDay);
        Debug.Log("<color=#72E6FF>【通電ステッキ発動】番犬による無効化を含め、檻を出した全プレイヤーを翌日除外。</color>");
    }

    private void ArrestThievesWithWatchdogs(List<CardInteraction> orderedCageTargets)
    {
        int thiefCount = orderedCageTargets.Count;
        int watchdogCount = cages.Count;
        int effectiveCages = Mathf.Max(watchdogCount, Mathf.Min(2, thiefCount));
        if (effectiveCages > thiefCount)
        {
            Debug.Log($"【番犬競合】怪盗{thiefCount}人／番犬の檻{effectiveCages}個分：逮捕失敗");
            HandleCageConflict();
            return;
        }

        for (int i = 0; i < effectiveCages && i < thiefCount; i++)
        {
            CardInteraction watchdog = cages[i % watchdogCount];
            CardInteraction thief = orderedCageTargets[i];
            Debug.Log($"【番犬】{watchdog.name}が{thief.name}を逮捕！");
            Arrest(thief);
            if (thief.specialEffect == SpecialActionEffect.ElectricBaton)
                ApplyElectricBatonCageEffect();
            int ownerId = FindPlayerIdByCard(watchdog);
            if (ownerId >= 0)
                successfulCagePlayerIds.Add(ownerId);
        }
    }

    private int CageTargetPlayerOrder(CardInteraction card)
    {
        int localSeat = FindPlayerIdByCard(card);
        return KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession
            ? KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(localSeat)
            : localSeat;
    }

    private void HandleSoloStageEffect()
    {
        if (!phantomThieves.Any(t => t.specialEffect == SpecialActionEffect.SoloStage)) return;
        foreach (CardInteraction thief in phantomThieves)
        {
            if (thief.specialEffect == SpecialActionEffect.SoloStage)
                continue;
            Debug.Log($"【独壇場】{thief.name}を即逮捕");
            Arrest(thief);
        }
    }

    private void HandleSpecialGuardEffects()
    {
        bool guardPlayed = fieldCards.Any(c => c.specialEffect == SpecialActionEffect.Guard);
        bool eerieGuardPlayed = fieldCards.Any(c => c.specialEffect == SpecialActionEffect.EerieGuard);
        bool fakeCopPlayed = fieldCards.Any(c => c.specialEffect == SpecialActionEffect.FakeCop);
        bool foolishGuardPlayed = fieldCards.Any(c => c.specialEffect == SpecialActionEffect.FoolishGuard);

        foreach (CardInteraction thief in phantomThieves)
        {
            if (thief.specialEffect == SpecialActionEffect.TearGas)
                continue;
            int number = thief.SelectedNumber;
            bool caught = (guardPlayed && number >= 3 && number <= 6) ||
                          (eerieGuardPlayed && number % 2 == 1) ||
                          (fakeCopPlayed && (number == 2 || number == 5 || number == 6));
            if (!caught) continue;
            Debug.Log($"【特殊警備員の即時逮捕】{thief.name}（宣言数{number}）");
            Arrest(thief);
        }

        if (!foolishGuardPlayed) return;
        foreach (CardInteraction card in fieldCards)
        {
            // 特殊展示ではなく、通常の展示カードを出したプレイヤーだけが対象。
            if (!card.isExhibit || card.IsSpecialAction) continue;
            Debug.Log($"【マヌケな警備員】通常展示カード使用者 {card.name} を逮捕");
            Arrest(card);
        }
    }

    public int[] GetSuccessfulCagePlayerIds()
    {
        return successfulCagePlayerIds.ToArray();
    }

    public void SetSuccessfulCagePlayerIds(int[] playerIds)
    {
        successfulCagePlayerIds.Clear();
        if (playerIds == null) return;
        foreach (int playerId in playerIds)
            if (playerId >= 0 && !successfulCagePlayerIds.Contains(playerId))
                successfulCagePlayerIds.Add(playerId);
    }

    public bool ArrestFromExternalEffect(CardInteraction card, bool immediateEffect = false)
    {
        return Arrest(card, immediateEffect);
    }

    private int FindPlayerIdByCard(CardInteraction card)
    {
        return card != null && fieldCardOwners.TryGetValue(card, out int playerId)
            ? playerId
            : -1;
    }

    private bool Arrest(CardInteraction card, bool immediateEffect = false)
    {
        if (card == null) return false;
        if (card.specialEffect == SpecialActionEffect.FrameUp && TryStartFrameUp(card))
            return true;
        Debug.Log($"{card.name} が逮捕されました！");
        bool arrested = false;

        // Player を検索
        Player player = FindPlayerByCard(card);
        if (player != null && !player.HasBeenArrested)
        {
            player.ShowArrestEffect(immediateEffect);
            arrested = true;
        }

        // Player2 を検索
        Player2 player2 = FindPlayer2ByCard(card);
        if (player2 != null && !player2.HasBeenArrested)
        {
            player2.ShowArrestEffect(immediateEffect);
            arrested = true;
        }

        // Player3 を検索
        Player3 player3 = FindPlayer3ByCard(card);
        if (player3 != null && !player3.HasBeenArrested)
        {
            player3.ShowArrestEffect(immediateEffect);
            arrested = true;
        }

        // Player4 を検索
        Player4 player4 = FindPlayer4ByCard(card);
        if (player4 != null && !player4.HasBeenArrested)
        {
            player4.ShowArrestEffect(immediateEffect);
            arrested = true;
        }
        return arrested;
    }

    private bool TryStartFrameUp(CardInteraction card)
    {
        if (pendingFrameUpCard == card) return true;
        var targets = fieldCards.Where(c => c != null && c != card && c.isExhibit &&
            !c.IsSpecialAction && !IsCardOwnerArrested(c)).ToList();
        if (targets.Count == 0) return false;

        int ownerId = FindPlayerIdByCard(card);
        if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession || ownerId == 0)
        {
            pendingFrameUpCard = card;
            pendingFrameUpOwnerId = ownerId;
            pendingFrameUpTargets.Clear();
            pendingFrameUpTargets.AddRange(targets);
            Debug.Log("【濡れ衣】逮捕を移す通常展示プレイヤーを選んでください。");
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            {
                int day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1;
                int networkSource = KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(ownerId);
                if (KaitouOnline.KaitouOnlineGameBridge.TryGetFrameUpChoice(
                        day, networkSource, out int networkTarget))
                    ApplyOnlineFrameUpChoice(networkSource, networkTarget);
            }
        }
        else
        {
            TransferFrameUp(card, targets[Random.Range(0, targets.Count)]);
        }
        return true;
    }

    private void TransferFrameUp(CardInteraction source, CardInteraction target)
    {
        EndOwnerActionWithoutPenalty(source);
        Debug.Log($"【濡れ衣】{source.name}の逮捕を{target.name}へ移しました。");
        ArrestPlayerById(FindPlayerIdByCard(target));
        pendingFrameUpCard = null;
        pendingFrameUpOwnerId = -1;
        pendingFrameUpTargets.Clear();
    }

    private bool ArrestPlayerById(int playerId)
    {
        if (playerId == 0)
        {
            Player p = FindFirstObjectByType<Player>();
            if (p != null && !p.HasBeenArrested) { p.ShowArrestEffect(); return true; }
        }
        else if (playerId == 1)
        {
            Player2 p = FindFirstObjectByType<Player2>();
            if (p != null && !p.HasBeenArrested) { p.ShowArrestEffect(); return true; }
        }
        else if (playerId == 2)
        {
            Player3 p = FindFirstObjectByType<Player3>();
            if (p != null && !p.HasBeenArrested) { p.ShowArrestEffect(); return true; }
        }
        else if (playerId == 3)
        {
            Player4 p = FindFirstObjectByType<Player4>();
            if (p != null && !p.HasBeenArrested) { p.ShowArrestEffect(); return true; }
        }
        return false;
    }

    public void ApplyOnlineFrameUpChoice(int networkSourceSeat, int networkTargetSeat)
    {
        if (pendingFrameUpCard == null) return;
        int sourceSeat = KaitouOnline.KaitouOnlineGameBridge.ToLocalSeat(networkSourceSeat);
        int targetSeat = KaitouOnline.KaitouOnlineGameBridge.ToLocalSeat(networkTargetSeat);
        if (pendingFrameUpOwnerId != sourceSeat) return;
        CardInteraction target = pendingFrameUpTargets.FirstOrDefault(value =>
            FindPlayerIdByCard(value) == targetSeat);
        CardInteraction source = pendingFrameUpCard;
        EndOwnerActionWithoutPenalty(source);
        bool arrested = ArrestPlayerById(targetSeat);
        Debug.Log($"【濡れ衣同期】Player{targetSeat + 1}を逮捕：{arrested}");
        pendingFrameUpCard = null;
        pendingFrameUpOwnerId = -1;
        pendingFrameUpTargets.Clear();
    }

    private void EndOwnerActionWithoutPenalty(CardInteraction card)
    {
        Player p1 = FindPlayerByCard(card);
        if (p1 != null) { p1.EndActionWithoutArrestPenalty(); return; }
        Player2 p2 = FindPlayer2ByCard(card);
        if (p2 != null) { p2.EndActionWithoutArrestPenalty(); return; }
        Player3 p3 = FindPlayer3ByCard(card);
        if (p3 != null) { p3.EndActionWithoutArrestPenalty(); return; }
        Player4 p4 = FindPlayer4ByCard(card);
        if (p4 != null) p4.EndActionWithoutArrestPenalty();
    }

    private bool IsCardOwnerArrested(CardInteraction card)
    {
        Player p1 = FindPlayerByCard(card);
        if (p1 != null) return p1.HasBeenArrested;
        Player2 p2 = FindPlayer2ByCard(card);
        if (p2 != null) return p2.HasBeenArrested;
        Player3 p3 = FindPlayer3ByCard(card);
        if (p3 != null) return p3.HasBeenArrested;
        Player4 p4 = FindPlayer4ByCard(card);
        return p4 != null && p4.HasBeenArrested;
    }

    private void OnGUI()
    {
        KaitouGuiFont.Apply();
        if (pendingFrameUpCard == null ||
            (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession &&
             pendingFrameUpOwnerId != 0)) return;
        GUIStyle messageStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 30,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        GUI.Box(new Rect(Screen.width * 0.5f - 380f, 28f, 760f, 105f),
            "濡れ衣：逮捕を移すプレイヤーを選んでください", messageStyle);
        float width = 190f;
        float startX = Screen.width * 0.5f - pendingFrameUpTargets.Count * width * 0.5f;
        for (int i = 0; i < pendingFrameUpTargets.Count; i++)
        {
            CardInteraction target = pendingFrameUpTargets[i];
            int playerId = FindPlayerIdByCard(target);
            int displayedPlayerId = KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession
                ? KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(playerId)
                : playerId;
            if (GUI.Button(new Rect(startX + i * width, 145f, width - 12f, 72f),
                    $"Player {displayedPlayerId + 1}", buttonStyle))
            {
                if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
                {
                    int day = HandManager.Instance != null
                        ? HandManager.Instance.CurrentDay : 1;
                    int localSource = pendingFrameUpOwnerId;
                    int localTarget = playerId;
                    int networkSource = KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(
                        localSource);
                    int networkTarget = KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(
                        localTarget);
                    // 押した端末では即時反映し、通信の戻り待ちでボタンが無反応に見えないようにする。
                    ApplyOnlineFrameUpChoice(networkSource, networkTarget);
                    KaitouOnline.KaitouOnlineGameBridge.SubmitFrameUpChoice(
                        day, localSource, localTarget);
                }
                else
                    TransferFrameUp(pendingFrameUpCard, target);
                break;
            }
        }
    }

    // Player の検索
    private Player FindPlayerByCard(CardInteraction card)
    {
        return FindObjectsOfType<Player>().FirstOrDefault(p => p.SelectedCard != null && p.SelectedCard == card);
    }

    // Player2 の検索
    private Player2 FindPlayer2ByCard(CardInteraction card)
    {
        return FindObjectsOfType<Player2>().FirstOrDefault(p => p.SelectedCard != null && p.SelectedCard == card);
    }

    // Player3 の検索
    private Player3 FindPlayer3ByCard(CardInteraction card)
    {
        return FindObjectsOfType<Player3>().FirstOrDefault(p => p.SelectedCard != null && p.SelectedCard == card);
    }

    // Player4 の検索
    private Player4 FindPlayer4ByCard(CardInteraction card)
    {
        return FindObjectsOfType<Player4>().FirstOrDefault(p => p.SelectedCard != null && p.SelectedCard == card);
    }

}
