using System.Collections.Generic;
using UnityEngine;

// 特殊行動カードの配布・画像差し替え・使い切り・次ターン制限をまとめて管理する。
public static class SpecialActionCardSystem
{
    private static readonly HashSet<int> exhibitOnlyNextTurn = new HashSet<int>();
    private static readonly HashSet<CardInteraction> detectiveExcludedCards = new HashSet<CardInteraction>();
    private static readonly Dictionary<int, int> imprisonedUntilEndOfDay =
        new Dictionary<int, int>();
    private static readonly Dictionary<int, int> excludedActionDay =
        new Dictionary<int, int>();
    private static readonly Dictionary<int, Quaternion> handRotations = new Dictionary<int, Quaternion>();
    private static readonly Dictionary<int, Vector3> handPositions = new Dictionary<int, Vector3>();
    private static readonly Dictionary<int, CardInteraction> exhibitTemplates =
        new Dictionary<int, CardInteraction>();
    private static readonly Dictionary<int, CardInteraction> thiefTemplates =
        new Dictionary<int, CardInteraction>();
    private static readonly Dictionary<int, CardInteraction> pendingAdvanceNotices =
        new Dictionary<int, CardInteraction>();
    private static readonly Dictionary<int, int> advanceNoticeActiveDay =
        new Dictionary<int, int>();
    private static readonly Dictionary<int, int> consecutiveCageCounts =
        new Dictionary<int, int>();
    private static readonly Dictionary<int, int> cageSelectionRecordedDay =
        new Dictionary<int, int>();
    private static readonly Dictionary<int, CardInteraction> twoPlayerBonusThieves =
        new Dictionary<int, CardInteraction>();
    private static bool initialCardsDealt;
    private static bool soloStageCreated;
    private static bool watchdogCreated;
    private static bool advanceNoticeCreated;
    public static bool AppraiserAnalysisTestMode { get; private set; }

    public static void ResetSession()
    {
        initialCardsDealt = false;
        soloStageCreated = false;
        watchdogCreated = false;
        advanceNoticeCreated = false;
        AppraiserAnalysisTestMode = false;
        exhibitOnlyNextTurn.Clear();
        detectiveExcludedCards.Clear();
        imprisonedUntilEndOfDay.Clear();
        excludedActionDay.Clear();
        handRotations.Clear();
        handPositions.Clear();
        exhibitTemplates.Clear();
        thiefTemplates.Clear();
        pendingAdvanceNotices.Clear();
        advanceNoticeActiveDay.Clear();
        consecutiveCageCounts.Clear();
        cageSelectionRecordedDay.Clear();
        twoPlayerBonusThieves.Clear();
    }

    public static void ConfigureTwoPlayerNormalThieves(HandManager handManager, bool enabled)
    {
        if (handManager == null) return;
        if (!enabled)
        {
            consecutiveCageCounts.Remove(0);
            consecutiveCageCounts.Remove(1);
            cageSelectionRecordedDay.Remove(0);
            cageSelectionRecordedDay.Remove(1);
        }
        for (int seat = 0; seat <= 1; seat++)
        {
            List<CardInteraction> ownerCards = GetCards(seat);
            if (ownerCards == null) continue;

            // 以前2人用として生成された追加怪盗が辞書リセット後もリストへ残った場合、
            // 3・4人戦へ絶対に持ち込まない。名前は生成時に付けた専用接尾辞で判別する。
            if (!enabled)
            {
                List<CardInteraction> staleBonusCards = ownerCards.FindAll(card =>
                    card != null && card.name.EndsWith("_2人用追加"));
                foreach (CardInteraction stale in staleBonusCards)
                {
                    ownerCards.Remove(stale);
                    if (seat == 0) handManager.cards.Remove(stale);
                    Object.Destroy(stale.gameObject);
                }
                twoPlayerBonusThieves.Remove(seat);
                consecutiveCageCounts.Remove(seat);
                cageSelectionRecordedDay.Remove(seat);
                continue;
            }

            if (!twoPlayerBonusThieves.TryGetValue(seat, out CardInteraction bonus) || bonus == null)
            {
                bonus = ownerCards.Find(card => card != null &&
                    card.name.EndsWith("_2人用追加"));
            }
            if (bonus == null)
            {
                CardInteraction template = ownerCards.Find(card => card != null &&
                    card.isPhantomThief && !card.IsSpecialAction);
                if (template == null) continue;
                GameObject clone = Object.Instantiate(template.gameObject, template.transform.parent);
                bonus = clone.GetComponent<CardInteraction>();
                bonus.name = template.name + "_2人用追加";
                bonus.InitializeHandPose(template.transform.position, template.transform.rotation);
            }
            twoPlayerBonusThieves[seat] = bonus;

            if (!ownerCards.Contains(bonus))
            {
                int thiefIndex = ownerCards.FindIndex(card => card != null &&
                    card != bonus && card.isPhantomThief && !card.IsSpecialAction);
                ownerCards.Insert(thiefIndex >= 0 ? thiefIndex + 1 : ownerCards.Count, bonus);
            }
            if (seat == 0 && !handManager.cards.Contains(bonus))
            {
                int thiefIndex = handManager.cards.FindIndex(card => card != null &&
                    card != bonus && card.isPhantomThief && !card.IsSpecialAction);
                handManager.cards.Insert(
                    thiefIndex >= 0 ? thiefIndex + 1 : handManager.cards.Count, bonus);
                bonus.SetHandManager(handManager);
            }
            bonus.gameObject.SetActive(true);
        }
    }

    public static void DealInitialCards(HandManager handManager)
    {
        if (initialCardsDealt || handManager == null) return;
        initialCardsDealt = true;

        // オンラインでは全端末が「自分をPlayer1」として盤面を並べ替えるため、
        // local seat 0,1,2,3 の順に配ると、端末ごとにネットワーク上の配布順が変わる。
        // 独壇場・番犬・予告状は1ゲーム1枚の制約を共有しているので、配布順が違うと
        // CPUの手札内容まで不一致になり、選択カードを復元できない。必ず共通の
        // network seat順で配布してから、各端末のlocal seatへ変換する。
        if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
        {
            int playerCount = Mathf.Clamp(handManager.ActionPlayerCount, 2, 4);
            for (int networkSeat = 0; networkSeat < playerCount; networkSeat++)
            {
                int localSeat = KaitouOnline.KaitouOnlineGameBridge.ToLocalSeat(networkSeat);
                KaitouOnline.KaitouOnlineGameBridge.PrepareInitialSpecialRandom(localSeat);
                GrantCardsToSeat(localSeat, 2, handManager);
            }
            return;
        }

        KaitouOnline.KaitouOnlineGameBridge.PrepareInitialSpecialRandom(0);
        GrantCardsToSeat(0, 2, handManager);
        if (handManager.ActionPlayerCount == 2 || handManager.ActionPlayerCount == 4)
        {
            KaitouOnline.KaitouOnlineGameBridge.PrepareInitialSpecialRandom(1);
            GrantCardsToSeat(1, 2, handManager);
        }
        if (handManager.ActionPlayerCount >= 3)
        {
            KaitouOnline.KaitouOnlineGameBridge.PrepareInitialSpecialRandom(2);
            GrantCardsToSeat(2, 2, handManager);
            KaitouOnline.KaitouOnlineGameBridge.PrepareInitialSpecialRandom(3);
            GrantCardsToSeat(3, 2, handManager);
        }
    }

    public static void GrantCageRewards(int[] treasurePlayerIds, HandManager handManager)
    {
        if (treasurePlayerIds == null || handManager == null) return;
        foreach (int treasureId in treasurePlayerIds)
        {
            int seat = FindActionSeat(treasureId, handManager);
            if (seat < 0) continue;
            KaitouOnline.KaitouOnlineGameBridge.PrepareCageRewardRandom(
                handManager.CurrentDay, seat);
            CardInteraction selected = GetSelectedCard(seat);
            int rewardCount = selected != null &&
                              selected.specialEffect == SpecialActionEffect.Watchdog ? 2 : 1;
            GrantCardsToSeat(seat, rewardCount, handManager);
        }
        handManager.RefreshActionHandLayout();
    }

    private static CardInteraction GetSelectedCard(int seat)
    {
        if (seat == 0)
        {
            Player p = Object.FindFirstObjectByType<Player>(FindObjectsInactive.Include);
            return p != null ? p.SelectedCard : null;
        }
        if (seat == 1)
        {
            Player2 p = Object.FindFirstObjectByType<Player2>(FindObjectsInactive.Include);
            return p != null ? p.SelectedCard : null;
        }
        if (seat == 2)
        {
            Player3 p = Object.FindFirstObjectByType<Player3>(FindObjectsInactive.Include);
            return p != null ? p.SelectedCard : null;
        }
        Player4 p4 = Object.FindFirstObjectByType<Player4>(FindObjectsInactive.Include);
        return p4 != null ? p4.SelectedCard : null;
    }

    public static void GrantAllSpecialCardsToPlayerOne(HandManager handManager)
    {
        GrantAllSpecialCardsToSeat(0, handManager);
    }

    public static void GrantAllSpecialCardsToSeat(int seat, HandManager handManager)
    {
        if (handManager == null) return;
        List<CardInteraction> ownerCards = GetCards(seat);
        if (ownerCards == null ||
            !TryGetTemplates(seat, ownerCards, out CardInteraction exhibitTemplate,
                out CardInteraction thiefTemplate))
            return;

        for (int value = 1; value <= (int)SpecialActionEffect.AdvanceNotice; value++)
        {
            SpecialActionEffect effect = (SpecialActionEffect)value;
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession &&
                effect == SpecialActionEffect.FrameUp) continue;
            // テストボタンでは、ゲーム全体の一枚制限より「P1が全種類を持つ」を優先する。
            // 二度押ししても同じ効果は増やさない。
            if (ownerCards.Exists(card =>
                    card != null && card.specialEffect == effect)) continue;
            bool needsThiefTemplate = IsThiefEffect(effect);
            CardInteraction card = CreateCard(
                needsThiefTemplate ? thiefTemplate : exhibitTemplate, effect, seat);
            if (effect == SpecialActionEffect.SoloStage) soloStageCreated = true;
            if (effect == SpecialActionEffect.Watchdog) watchdogCreated = true;
            if (effect == SpecialActionEffect.AdvanceNotice) advanceNoticeCreated = true;
            ownerCards.Add(card);
            if (seat == 0)
            {
                handManager.cards.Add(card);
                card.SetHandManager(handManager);
            }
        }
        handManager.RefreshActionHandLayout();
        handManager.RefreshPlayerOneCardAvailability();
        Debug.Log($"【テスト配布】Player{seat + 1}に特殊行動カード全種類を配布しました。");
    }

    public static void GrantUnverifiedSpecialCardsToPlayerOne(HandManager handManager)
    {
        GrantUnverifiedSpecialCardsToSeat(0, handManager);
    }

    public static void GrantUnverifiedSpecialCardsToSeat(
        int seat, HandManager handManager)
    {
        if (handManager == null) return;
        List<CardInteraction> ownerCards = GetCards(seat);
        if (ownerCards == null ||
            !TryGetTemplates(seat, ownerCards, out CardInteraction exhibitTemplate,
                out CardInteraction thiefTemplate)) return;

        SpecialActionEffect[] effects =
        {
            SpecialActionEffect.SoloStage,
            SpecialActionEffect.BlackoutModule,
            SpecialActionEffect.Watchdog,
            SpecialActionEffect.Detective,
            SpecialActionEffect.Prison,
            SpecialActionEffect.ElectricBaton,
            SpecialActionEffect.AnalysisGlasses,
            SpecialActionEffect.Appraiser,
            SpecialActionEffect.HoneyTrap,
            SpecialActionEffect.AdvanceNotice
        };
        foreach (SpecialActionEffect effect in effects)
        {
            if (ownerCards.Exists(card => card != null &&
                card.specialEffect == effect)) continue;
            CardInteraction card = CreateCard(
                IsThiefEffect(effect) ? thiefTemplate : exhibitTemplate, effect, seat);
            if (effect == SpecialActionEffect.SoloStage) soloStageCreated = true;
            if (effect == SpecialActionEffect.Watchdog) watchdogCreated = true;
            if (effect == SpecialActionEffect.AdvanceNotice) advanceNoticeCreated = true;
            ownerCards.Add(card);
            if (seat == 0)
            {
                handManager.cards.Add(card);
                card.SetHandManager(handManager);
            }
        }
        if (seat == 0)
        {
            handManager.RefreshActionHandLayout();
            handManager.RefreshPlayerOneCardAvailability();
        }
        Debug.Log($"【テスト配布】Player{seat + 1}に未確認の特殊行動カードだけを配布しました。");
    }

    public static void StartAppraiserAnalysisTest(HandManager handManager)
    {
        if (handManager == null) return;
        AppraiserAnalysisTestMode = true;
        for (int seat = 0; seat < 4; seat++)
        {
            bool active = seat == 0 ||
                (seat == 1 && (handManager.ActionPlayerCount == 2 || handManager.ActionPlayerCount == 4)) ||
                (seat >= 2 && handManager.ActionPlayerCount >= 3);
            if (!active) continue;
            GrantSpecificCardToSeat(seat, SpecialActionEffect.Appraiser, handManager);
            GrantSpecificCardToSeat(seat, SpecialActionEffect.AnalysisGlasses, handManager);
            GrantSpecificCardToSeat(seat, SpecialActionEffect.LargeTruck, handManager);
        }
        handManager.RefreshActionHandLayout();
        handManager.RefreshPlayerOneCardAvailability();
        Debug.Log("【専用テスト】分析メガネ＋鑑定士＋大型トラックを配布しました。");
    }

    private static void GrantSpecificCardToSeat(int seat, SpecialActionEffect effect,
        HandManager handManager)
    {
        if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession &&
            effect == SpecialActionEffect.FrameUp) return;
        List<CardInteraction> ownerCards = GetCards(seat);
        if (ownerCards == null || !TryGetTemplates(seat, ownerCards,
                out CardInteraction exhibitTemplate, out CardInteraction thiefTemplate)) return;
        CardInteraction card = CreateCard(IsThiefEffect(effect) ? thiefTemplate : exhibitTemplate,
            effect, seat);
        ownerCards.Add(card);
        if (seat == 0)
        {
            handManager.cards.Add(card);
            card.SetHandManager(handManager);
        }
    }

    public static void MarkDetectiveExcluded(CardInteraction card)
    {
        if (card != null) detectiveExcludedCards.Add(card);
    }

    public static bool IsDetectiveExcluded(CardInteraction card) =>
        card != null && detectiveExcludedCards.Contains(card);

    public static void ClearTurnEffects()
    {
        detectiveExcludedCards.Clear();
    }

    public static void GrantDetectiveReward(
        int actionSeat, int rewardCount, HandManager handManager)
    {
        GrantCardsToSeat(actionSeat, rewardCount, handManager);
        handManager?.RefreshActionHandLayout();
    }

    public static string BuildActionHandSignature(int actionSeat)
    {
        List<CardInteraction> cards = GetCards(actionSeat);
        if (cards == null) return "missing";
        List<string> values = new List<string>();
        foreach (CardInteraction card in cards)
        {
            if (card == null) continue;
            // 特殊カードだけでなく通常の展示・怪盗・檻も含める。
            // 逮捕ペナルティによる通常カード没収が片方だけずれた場合も、
            // 次の開示前に検出して壊れた状態で進行させない。
            values.Add($"{(int)card.specialEffect}:" +
                       $"{(card.isExhibit ? 1 : 0)}" +
                       $"{(card.isPhantomThief ? 1 : 0)}" +
                       $"{(card.isCage ? 1 : 0)}");
        }
        values.Sort(System.StringComparer.Ordinal);
        return string.Join(",", values);
    }

    public static CardInteraction EnsureOnlineCardInHand(int seat, int specialEffect,
        bool isExhibit, bool isThief, bool isCage, HandManager handManager,
        bool forceCreate = false)
    {
        List<CardInteraction> ownerCards = GetCards(seat);
        if (ownerCards == null) return null;

        CardInteraction existing = ownerCards.Find(card => card != null &&
            (int)card.specialEffect == specialEffect &&
            card.isExhibit == isExhibit && card.isPhantomThief == isThief &&
            card.isCage == isCage);
        if (existing != null && !forceCreate)
        {
            existing.gameObject.SetActive(true);
            existing.SetVisualVisible(true);
            return existing;
        }

        CardInteraction created = null;
        if (specialEffect != 0 &&
            TryGetTemplates(seat, ownerCards, out CardInteraction exhibitTemplate,
                out CardInteraction thiefTemplate))
        {
            SpecialActionEffect effect = (SpecialActionEffect)specialEffect;
            created = CreateCard(IsThiefEffect(effect) ? thiefTemplate : exhibitTemplate,
                effect, seat);
        }
        else if (specialEffect == 0)
        {
            CardInteraction template = ownerCards.Find(card => card != null &&
                !card.IsSpecialAction && card.isExhibit == isExhibit &&
                card.isPhantomThief == isThief && card.isCage == isCage);
            if (template == null)
            {
                CardInteraction[] allCards = Object.FindObjectsByType<CardInteraction>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                template = System.Array.Find(allCards, card => card != null &&
                    !card.IsSpecialAction && card.isExhibit == isExhibit &&
                    card.isPhantomThief == isThief && card.isCage == isCage);
            }
            CardInteraction poseSource = ownerCards.Find(card => card != null);
            if (template != null && poseSource != null)
            {
                GameObject clone = Object.Instantiate(template.gameObject,
                    poseSource.transform.parent);
                created = clone.GetComponent<CardInteraction>();
                created.specialEffect = SpecialActionEffect.None;
                created.isExhibit = isExhibit;
                created.isPhantomThief = isThief;
                created.isCage = isCage;
                created.InitializeHandPose(poseSource.transform.position,
                    poseSource.HandPoseRotation);
                clone.name = isExhibit ? "通常展示_同期復元" :
                    isThief ? "通常怪盗_同期復元" : "通常檻_同期復元";
                clone.SetActive(true);
                created.SetVisualVisible(true);
            }
        }

        if (created == null) return null;
        ownerCards.Add(created);
        if (seat == 0 && handManager != null)
        {
            handManager.cards.Add(created);
            created.SetHandManager(handManager);
            handManager.RefreshActionHandLayout(true);
        }
        Debug.LogWarning($"【オンライン手札復元】P{seat + 1}へ不足カードを復元：" +
                         $"特殊:{specialEffect} 展示:{isExhibit} 怪盗:{isThief} 檻:{isCage}");
        return created;
    }

    public static void SynchronizeOnlineHand(int seat,
        KaitouOnline.ActionCardInventoryEntry[] inventory, HandManager handManager)
    {
        List<CardInteraction> ownerCards = GetCards(seat);
        if (ownerCards == null || inventory == null) return;

        var desired = new Dictionary<string, KaitouOnline.ActionCardInventoryEntry>();
        foreach (KaitouOnline.ActionCardInventoryEntry entry in inventory)
        {
            if (entry.seat != seat) continue;
            desired[InventoryKey(entry.specialEffect, entry.isExhibit,
                entry.isThief, entry.isCage)] = entry;
        }

        var groups = new Dictionary<string, List<CardInteraction>>();
        foreach (CardInteraction card in new List<CardInteraction>(ownerCards))
        {
            if (card == null)
            {
                ownerCards.Remove(card);
                continue;
            }
            string key = InventoryKey((int)card.specialEffect, card.isExhibit,
                card.isPhantomThief, card.isCage);
            if (!groups.TryGetValue(key, out List<CardInteraction> cards))
            {
                cards = new List<CardInteraction>();
                groups[key] = cards;
            }
            cards.Add(card);
        }

        CardInteraction selected = GetSelectedCard(seat);
        foreach (KeyValuePair<string, List<CardInteraction>> group in groups)
        {
            int wanted = desired.TryGetValue(group.Key,
                out KaitouOnline.ActionCardInventoryEntry entry) ? entry.count : 0;
            while (group.Value.Count > wanted)
            {
                int removeIndex = group.Value.Count - 1;
                if (group.Value[removeIndex] == selected && group.Value.Count > 1)
                    removeIndex = 0;
                CardInteraction extra = group.Value[removeIndex];
                group.Value.RemoveAt(removeIndex);
                ownerCards.Remove(extra);
                if (seat == 0 && handManager != null) handManager.cards.Remove(extra);
                if (extra != null) Object.Destroy(extra.gameObject);
            }
        }

        foreach (KaitouOnline.ActionCardInventoryEntry entry in desired.Values)
        {
            string key = InventoryKey(entry.specialEffect, entry.isExhibit,
                entry.isThief, entry.isCage);
            int current = groups.TryGetValue(key, out List<CardInteraction> cards)
                ? Mathf.Min(cards.Count, entry.count) : 0;
            while (current < entry.count)
            {
                CardInteraction added = EnsureOnlineCardInHand(seat,
                    entry.specialEffect, entry.isExhibit, entry.isThief,
                    entry.isCage, handManager, true);
                if (added == null) break;
                current++;
            }
        }

        if (seat == 0 && handManager != null)
        {
            handManager.RefreshActionHandLayout(true);
            handManager.RefreshPlayerOneCardAvailability();
        }
        Debug.Log($"<color=#70E8FF>【オンライン手札同期】P{seat + 1} " +
                  $"{BuildActionHandSignature(seat)}</color>");
    }

    private static string InventoryKey(int specialEffect, bool exhibit,
        bool thief, bool cage) => $"{specialEffect}:" +
        $"{(exhibit ? 1 : 0)}{(thief ? 1 : 0)}{(cage ? 1 : 0)}";

    public static bool IsImprisoned(int seat) =>
        imprisonedUntilEndOfDay.ContainsKey(seat);

    public static int GetPrisonUntilDay(int seat) =>
        imprisonedUntilEndOfDay.TryGetValue(seat, out int day) ? day : -1;

    public static void ApplyOnlinePrisonState(int seat, int untilDay)
    {
        if (seat < 0) return;
        if (untilDay < 0)
            imprisonedUntilEndOfDay.Remove(seat);
        else
            imprisonedUntilEndOfDay[seat] = untilDay;
    }

    public static void ExcludeFromNextDay(int seat, int currentDay)
    {
        if (seat < 0) return;
        excludedActionDay[seat] = currentDay + 1;
        Debug.Log($"<color=#72E6FF>【通電ステッキ】Player{seat + 1}は{currentDay + 1}日目の行動を休みます。</color>");
    }

    public static bool IsExcludedFromActionToday(int seat)
    {
        int currentDay = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1;
        return excludedActionDay.TryGetValue(seat, out int excludedDay) &&
               excludedDay == currentDay;
    }

    public static bool CannotActToday(int seat) =>
        IsImprisoned(seat) || IsExcludedFromActionToday(seat);

    public static void Imprison(int seat, int currentDay)
    {
        if (seat < 0) return;
        // 逮捕された当日の終了時には振らず、翌日終了時から釈放判定を始める。
        imprisonedUntilEndOfDay[seat] = Mathf.Max(currentDay + 1,
            imprisonedUntilEndOfDay.TryGetValue(seat, out int day) ? day : 0);
        Debug.Log($"<color=#BFA8FF>【監獄】Player{seat + 1}を収監。{currentDay + 1}日目の終了時から釈放判定。</color>");
    }

    public static List<int> GetPrisonersEligibleForRelease(int currentDay)
    {
        List<int> eligible = new List<int>();
        foreach (KeyValuePair<int, int> prisoner in imprisonedUntilEndOfDay)
            if (currentDay >= prisoner.Value) eligible.Add(prisoner.Key);
        eligible.Sort();
        return eligible;
    }

    public static bool ResolvePrisonReleaseRoll(int seat, int result)
    {
        if (!IsImprisoned(seat)) return false;
        bool released = result <= 2;
        Debug.Log($"<color=#BFA8FF>【監獄釈放サイコロ】Player{seat + 1}：{result} → " +
                  (released ? "釈放！" : "収監継続") + "</color>");
        if (released) imprisonedUntilEndOfDay.Remove(seat);
        return released;
    }

    public static bool CanSelect(int seat, CardInteraction card)
    {
        return card != null && !CannotActToday(seat) &&
               (!exhibitOnlyNextTurn.Contains(seat) || card.isExhibit) &&
               !(IsTwoPlayerGame() && card.isCage && GetConsecutiveCageCount(seat) >= 3);
    }

    public static int GetConsecutiveCageCount(int seat) =>
        consecutiveCageCounts.TryGetValue(seat, out int count) ? count : 0;

    private static bool IsTwoPlayerGame() =>
        HandManager.Instance != null && HandManager.Instance.ActionPlayerCount == 2;

    public static void FinalizeTwoPlayerCageStreaks()
    {
        if (!IsTwoPlayerGame()) return;
        int day = HandManager.Instance.CurrentDay;
        for (int seat = 0; seat <= 1; seat++)
        {
            if (cageSelectionRecordedDay.TryGetValue(seat, out int recordedDay) && recordedDay == day)
                continue;
            consecutiveCageCounts[seat] = 0;
        }
    }

    public static bool IsExhibitOnly(int seat) => exhibitOnlyNextTurn.Contains(seat);

    public static List<CardInteraction> NormalCards(List<CardInteraction> cards)
    {
        return cards == null
            ? new List<CardInteraction>()
            : cards.FindAll(card => card != null && !card.IsSpecialAction);
    }

    public static bool IsEliminatedByNormalCards(List<CardInteraction> cards)
    {
        List<CardInteraction> normal = NormalCards(cards);
        return normal.Count == 0 || (normal.Count == 1 && normal[0].IsCageCard());
    }

    public static void NotifySelected(int seat, CardInteraction card)
    {
        if (IsTwoPlayerGame() && seat <= 1)
        {
            int day = HandManager.Instance.CurrentDay;
            if (!cageSelectionRecordedDay.TryGetValue(seat, out int recordedDay) || recordedDay != day)
            {
                consecutiveCageCounts[seat] = card != null && card.isCage
                    ? GetConsecutiveCageCount(seat) + 1
                    : 0;
                cageSelectionRecordedDay[seat] = day;
            }
        }
        if (card != null && exhibitOnlyNextTurn.Contains(seat) && card.isExhibit)
            exhibitOnlyNextTurn.Remove(seat);
        if (card != null && card.specialEffect == SpecialActionEffect.AdvanceNotice &&
            !pendingAdvanceNotices.ContainsKey(seat))
        {
            pendingAdvanceNotices[seat] = card;
            int day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1;
            advanceNoticeActiveDay[seat] = day + 1;
            Debug.Log($"<color=#FF7A7A>【予告状】P{seat + 1}は本日は盗まず、{day + 1}日目に実行します。</color>");
        }
    }

    public static bool IsAdvanceNoticePendingCard(int seat, CardInteraction card) =>
        card != null && pendingAdvanceNotices.TryGetValue(seat, out CardInteraction pending) &&
        pending == card;

    public static bool HasPendingAdvanceNotice(int seat) =>
        pendingAdvanceNotices.TryGetValue(seat, out CardInteraction card) && card != null;

    public static bool IsAdvanceNoticeActiveToday(int seat)
    {
        int day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1;
        return pendingAdvanceNotices.ContainsKey(seat) &&
               advanceNoticeActiveDay.TryGetValue(seat, out int activeDay) && day >= activeDay;
    }

    public static bool TryGetActiveAdvanceNotice(int seat, out CardInteraction card)
    {
        if (IsAdvanceNoticeActiveToday(seat) && pendingAdvanceNotices.TryGetValue(seat, out card) &&
            card != null) return true;
        card = null;
        return false;
    }

    public static bool IsAdvanceNoticeWaiting(CardInteraction card)
    {
        if (card == null) return false;
        foreach (KeyValuePair<int, CardInteraction> entry in pendingAdvanceNotices)
            if (entry.Value == card) return !IsAdvanceNoticeActiveToday(entry.Key);
        return false;
    }

    public static bool HasActiveAdvanceNoticeToday()
    {
        foreach (int seat in pendingAdvanceNotices.Keys)
            if (IsAdvanceNoticeActiveToday(seat)) return true;
        return false;
    }

    public static void ConsumeSelectedCards(HandManager handManager)
    {
        Player p1 = Object.FindFirstObjectByType<Player>(FindObjectsInactive.Include);
        Player2 p2 = Object.FindFirstObjectByType<Player2>(FindObjectsInactive.Include);
        Player3 p3 = Object.FindFirstObjectByType<Player3>(FindObjectsInactive.Include);
        Player4 p4 = Object.FindFirstObjectByType<Player4>(FindObjectsInactive.Include);

        Consume(0, p1 != null ? p1.SelectedCard : null, p1 != null ? p1.playerCards : null, handManager);
        Consume(1, p2 != null ? p2.SelectedCard : null, p2 != null ? p2.player2Cards : null, handManager);
        Consume(2, p3 != null ? p3.SelectedCard : null, p3 != null ? p3.player3Cards : null, handManager);
        Consume(3, p4 != null ? p4.SelectedCard : null, p4 != null ? p4.player4Cards : null, handManager);
    }

    private static void Consume(int seat, CardInteraction card, List<CardInteraction> ownerCards, HandManager handManager)
    {
        if (card == null || !card.IsSpecialAction) return;
        if (card.specialEffect == SpecialActionEffect.AdvanceNotice &&
            IsAdvanceNoticePendingCard(seat, card))
        {
            if (!IsAdvanceNoticeActiveToday(seat)) return;
            pendingAdvanceNotices.Remove(seat);
            advanceNoticeActiveDay.Remove(seat);
        }
        if (card.specialEffect == SpecialActionEffect.Collector)
            exhibitOnlyNextTurn.Add(seat);
        ownerCards?.Remove(card);
        if (seat == 0) handManager.cards.Remove(card);
        Object.Destroy(card.gameObject);
        Debug.Log($"【特殊カード使用済み】P{seat + 1}：{card.specialEffect}");
    }

    private static void GrantCardsToSeat(int seat, int count, HandManager handManager)
    {
        List<CardInteraction> ownerCards = GetCards(seat);
        if (ownerCards == null) return;
        if (!TryGetTemplates(seat, ownerCards, out CardInteraction exhibitTemplate,
                out CardInteraction thiefTemplate))
            return;

        for (int i = 0; i < count; i++)
        {
            SpecialActionEffect effect = RandomAvailableEffect();
            bool needsThiefTemplate = IsThiefEffect(effect);
            CardInteraction template = needsThiefTemplate ? thiefTemplate : exhibitTemplate;
            CardInteraction card = CreateCard(template, effect, seat);
            if (effect == SpecialActionEffect.SoloStage) soloStageCreated = true;
            if (effect == SpecialActionEffect.Watchdog) watchdogCreated = true;
            if (effect == SpecialActionEffect.AdvanceNotice) advanceNoticeCreated = true;
            ownerCards.Add(card);
            if (seat == 0)
            {
                handManager.cards.Add(card);
                card.SetHandManager(handManager);
            }
        }
        Debug.Log($"【特殊カード配布】P{seat + 1}に{count}枚");
    }

    private static SpecialActionEffect RandomAvailableEffect()
    {
        SpecialActionEffect effect;
        do
        {
            effect = (SpecialActionEffect)Random.Range(
                1, (int)SpecialActionEffect.AdvanceNotice + 1);
        }
        while ((KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession &&
                effect == SpecialActionEffect.FrameUp) ||
               (effect == SpecialActionEffect.SoloStage && soloStageCreated) ||
               (effect == SpecialActionEffect.Watchdog && watchdogCreated) ||
               (effect == SpecialActionEffect.AdvanceNotice && advanceNoticeCreated));
        return effect;
    }

    private static bool IsThiefEffect(SpecialActionEffect effect)
    {
        return effect == SpecialActionEffect.DisguiseMask ||
               effect == SpecialActionEffect.WireBelt ||
               effect == SpecialActionEffect.Balloon ||
               effect == SpecialActionEffect.FrameUp ||
               effect == SpecialActionEffect.SoloStage ||
               effect == SpecialActionEffect.BlackoutModule ||
               effect == SpecialActionEffect.TearGas ||
               effect == SpecialActionEffect.ElectricBaton ||
               effect == SpecialActionEffect.AnalysisGlasses ||
               effect == SpecialActionEffect.HoneyTrap ||
               effect == SpecialActionEffect.AdvanceNotice;
    }

    private static bool TryGetTemplates(int seat, List<CardInteraction> ownerCards,
        out CardInteraction exhibitTemplate, out CardInteraction thiefTemplate)
    {
        if (!exhibitTemplates.TryGetValue(seat, out exhibitTemplate) ||
            exhibitTemplate == null)
        {
            exhibitTemplate =
                ownerCards.Find(card => card != null && card.isExhibit && !card.IsSpecialAction);
            if (exhibitTemplate != null) exhibitTemplates[seat] = exhibitTemplate;
        }
        if (!thiefTemplates.TryGetValue(seat, out thiefTemplate) ||
            thiefTemplate == null)
        {
            thiefTemplate =
                ownerCards.Find(card => card != null && card.isPhantomThief && !card.IsSpecialAction);
            if (thiefTemplate != null) thiefTemplates[seat] = thiefTemplate;
        }
        if (exhibitTemplate == null || thiefTemplate == null)
        {
            Debug.LogWarning($"P{seat + 1}の展示または怪盗カードを複製できないため、特殊カードを配布できません。");
            return false;
        }
        if (!handRotations.ContainsKey(seat))
        {
            handRotations[seat] = exhibitTemplate.transform.rotation;
            handPositions[seat] = exhibitTemplate.transform.position;
        }

        return true;
    }

    private static CardInteraction CreateCard(CardInteraction template, SpecialActionEffect effect, int seat)
    {
        GameObject clone = Object.Instantiate(template.gameObject, template.transform.parent);
        CardInteraction card = clone.GetComponent<CardInteraction>();
        card.specialEffect = effect;
        // 怪盗系で展示効果も持つのは変装マスクだけ。
        card.isExhibit = (!IsThiefEffect(effect) &&
                          effect != SpecialActionEffect.Watchdog &&
                          effect != SpecialActionEffect.Prison) ||
                         effect == SpecialActionEffect.DisguiseMask;
        card.isPhantomThief = effect == SpecialActionEffect.DisguiseMask ||
                              effect == SpecialActionEffect.WireBelt ||
                              effect == SpecialActionEffect.Balloon ||
                              effect == SpecialActionEffect.FrameUp ||
                              effect == SpecialActionEffect.SoloStage ||
                              effect == SpecialActionEffect.BlackoutModule ||
                              effect == SpecialActionEffect.TearGas ||
                              effect == SpecialActionEffect.ElectricBaton ||
                              effect == SpecialActionEffect.AnalysisGlasses ||
                              effect == SpecialActionEffect.HoneyTrap ||
                              effect == SpecialActionEffect.AdvanceNotice;
        card.isCage = effect == SpecialActionEffect.TransportVehicle ||
                      effect == SpecialActionEffect.Watchdog ||
                      effect == SpecialActionEffect.Prison;
        card.InitializeHandPose(handPositions[seat], handRotations[seat]);

        string imageName = effect == SpecialActionEffect.Truck ? "トラック" :
            effect == SpecialActionEffect.LargeTruck ? "大型トラック" :
            effect == SpecialActionEffect.Collector ? "コレクター" :
            effect == SpecialActionEffect.Guard ? "警備員" :
            effect == SpecialActionEffect.EerieGuard ? "怪奇な警備員" :
            effect == SpecialActionEffect.FakeCop ? "偽警官" :
            effect == SpecialActionEffect.FoolishGuard ? "マヌケな警備員" :
            effect == SpecialActionEffect.TransportVehicle ? "護送車" :
            effect == SpecialActionEffect.DisguiseMask ? "変装マスク" :
            effect == SpecialActionEffect.WireBelt ? "ワイヤーベルト" :
            effect == SpecialActionEffect.Balloon ? "バルーン" :
            effect == SpecialActionEffect.FrameUp ? "濡れ衣" :
            effect == SpecialActionEffect.SoloStage ? "独擅場" :
            effect == SpecialActionEffect.BlackoutModule ? "停電モジュール" : "催涙スプレー";
        if (effect == SpecialActionEffect.Watchdog) imageName = "番犬";
        if (effect == SpecialActionEffect.Detective) imageName = "名探偵";
        if (effect == SpecialActionEffect.Prison) imageName = "監獄";
        if (effect == SpecialActionEffect.ElectricBaton) imageName = "通電ステッキ";
        // macOS上の画像名は「ガ」がカ＋結合濁点のNFD形式で保存されている。
        if (effect == SpecialActionEffect.AnalysisGlasses) imageName = "分析メカ\u3099ネ";
        if (effect == SpecialActionEffect.Appraiser) imageName = "鑑定士";
        if (effect == SpecialActionEffect.HoneyTrap) imageName = "ハニートラップ";
        if (effect == SpecialActionEffect.AdvanceNotice) imageName = "予告状";
        clone.name = $"特殊_{imageName}";
        Texture2D texture = Resources.Load<Texture2D>($"SpecialActionCards/{imageName}");
        if (texture == null)
            Debug.LogWarning($"特殊行動カード画像を読み込めません: SpecialActionCards/{imageName}");
        Renderer renderer = clone.GetComponentInChildren<Renderer>();
        if (renderer != null && texture != null)
        {
            Material[] materials = renderer.materials;
            if (materials.Length > 0)
            {
                materials[0].mainTexture = texture;
                if (materials[0].HasProperty("_BaseMap")) materials[0].SetTexture("_BaseMap", texture);
                renderer.materials = materials;
            }
        }
        // 没収済みで非表示になった通常カードを複製元にしても、報酬カードは手札へ表示する。
        clone.SetActive(true);
        card.SetVisualVisible(true);
        return card;
    }

    private static List<CardInteraction> GetCards(int seat)
    {
        if (seat == 0)
        {
            Player p = Object.FindFirstObjectByType<Player>(FindObjectsInactive.Include);
            return p != null ? p.playerCards : null;
        }
        if (seat == 1)
        {
            Player2 p = Object.FindFirstObjectByType<Player2>(FindObjectsInactive.Include);
            return p != null ? p.player2Cards : null;
        }
        if (seat == 2)
        {
            Player3 p = Object.FindFirstObjectByType<Player3>(FindObjectsInactive.Include);
            return p != null ? p.player3Cards : null;
        }
        Player4 p4 = Object.FindFirstObjectByType<Player4>(FindObjectsInactive.Include);
        return p4 != null ? p4.player4Cards : null;
    }

    private static int FindActionSeat(int treasureId, HandManager manager)
    {
        if (manager.ActionPlayerCount == 3)
        {
            if (treasureId == 0) return 0;
            if (treasureId == 1) return 2;
            if (treasureId == 2) return 3;
            return -1;
        }
        for (int seat = 0; seat < 4; seat++)
            if (manager.ToTreasurePlayerId(seat) == treasureId) return seat;
        return -1;
    }
}
