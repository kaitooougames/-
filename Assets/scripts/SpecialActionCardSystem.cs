using System.Collections.Generic;
using UnityEngine;

// 特殊行動カードの配布・画像差し替え・使い切り・次ターン制限をまとめて管理する。
public static class SpecialActionCardSystem
{
    private static readonly HashSet<int> exhibitOnlyNextTurn = new HashSet<int>();
    private static readonly Dictionary<int, Quaternion> handRotations = new Dictionary<int, Quaternion>();
    private static readonly Dictionary<int, Vector3> handPositions = new Dictionary<int, Vector3>();
    private static readonly Dictionary<int, CardInteraction> exhibitTemplates =
        new Dictionary<int, CardInteraction>();
    private static readonly Dictionary<int, CardInteraction> thiefTemplates =
        new Dictionary<int, CardInteraction>();
    private static bool initialCardsDealt;

    public static void ResetSession()
    {
        initialCardsDealt = false;
        exhibitOnlyNextTurn.Clear();
        handRotations.Clear();
        handPositions.Clear();
        exhibitTemplates.Clear();
        thiefTemplates.Clear();
    }

    public static void DealInitialCards(HandManager handManager)
    {
        if (initialCardsDealt || handManager == null) return;
        initialCardsDealt = true;

        GrantCardsToSeat(0, 2, handManager);
        if (handManager.ActionPlayerCount == 2 || handManager.ActionPlayerCount == 4)
            GrantCardsToSeat(1, 2, handManager);
        if (handManager.ActionPlayerCount >= 3)
        {
            GrantCardsToSeat(2, 2, handManager);
            GrantCardsToSeat(3, 2, handManager);
        }
    }

    public static void GrantCageRewards(int[] treasurePlayerIds, HandManager handManager)
    {
        if (treasurePlayerIds == null || handManager == null) return;
        foreach (int treasureId in treasurePlayerIds)
        {
            int seat = FindActionSeat(treasureId, handManager);
            if (seat >= 0) GrantCardsToSeat(seat, 1, handManager);
        }
        handManager.RefreshActionHandLayout();
    }

    public static void GrantAllSpecialCardsToPlayerOne(HandManager handManager)
    {
        if (handManager == null) return;
        List<CardInteraction> ownerCards = GetCards(0);
        if (ownerCards == null ||
            !TryGetTemplates(0, ownerCards, out CardInteraction exhibitTemplate,
                out CardInteraction thiefTemplate))
            return;

        for (int value = 1; value <= (int)SpecialActionEffect.Balloon; value++)
        {
            SpecialActionEffect effect = (SpecialActionEffect)value;
            bool needsThiefTemplate = effect == SpecialActionEffect.DisguiseMask ||
                                      effect == SpecialActionEffect.WireBelt ||
                                      effect == SpecialActionEffect.Balloon;
            CardInteraction card = CreateCard(
                needsThiefTemplate ? thiefTemplate : exhibitTemplate, effect, 0);
            ownerCards.Add(card);
            handManager.cards.Add(card);
            card.SetHandManager(handManager);
        }
        handManager.RefreshActionHandLayout();
        handManager.RefreshPlayerOneCardAvailability();
        Debug.Log($"【テスト配布】Player1に特殊行動カード全{(int)SpecialActionEffect.Balloon}種類を配布しました。");
    }

    public static bool CanSelect(int seat, CardInteraction card)
    {
        return card != null && (!exhibitOnlyNextTurn.Contains(seat) || card.isExhibit);
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
        if (card != null && exhibitOnlyNextTurn.Contains(seat) && card.isExhibit)
            exhibitOnlyNextTurn.Remove(seat);
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
            SpecialActionEffect effect = (SpecialActionEffect)Random.Range(
                1, (int)SpecialActionEffect.Balloon + 1);
            bool needsThiefTemplate = effect == SpecialActionEffect.DisguiseMask ||
                                      effect == SpecialActionEffect.WireBelt ||
                                      effect == SpecialActionEffect.Balloon;
            CardInteraction template = needsThiefTemplate ? thiefTemplate : exhibitTemplate;
            CardInteraction card = CreateCard(template, effect, seat);
            ownerCards.Add(card);
            if (seat == 0)
            {
                handManager.cards.Add(card);
                card.SetHandManager(handManager);
            }
        }
        Debug.Log($"【特殊カード配布】P{seat + 1}に{count}枚");
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
        card.isExhibit = effect != SpecialActionEffect.WireBelt &&
                         effect != SpecialActionEffect.Balloon;
        card.isPhantomThief = effect == SpecialActionEffect.DisguiseMask ||
                              effect == SpecialActionEffect.WireBelt ||
                              effect == SpecialActionEffect.Balloon;
        card.isCage = effect == SpecialActionEffect.TransportVehicle;
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
            effect == SpecialActionEffect.WireBelt ? "ワイヤーベルト" : "バルーン";
        clone.name = $"特殊_{imageName}";
        Texture2D texture = Resources.Load<Texture2D>($"SpecialActionCards/{imageName}");
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
