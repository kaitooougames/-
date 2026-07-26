using System.Collections.Generic;
using UnityEngine;

// 特殊行動カードの配布・画像差し替え・使い切り・次ターン制限をまとめて管理する。
public static class SpecialActionCardSystem
{
    private static readonly HashSet<int> exhibitOnlyNextTurn = new HashSet<int>();
    private static readonly Dictionary<int, Quaternion> handRotations = new Dictionary<int, Quaternion>();
    private static readonly Dictionary<int, Vector3> handPositions = new Dictionary<int, Vector3>();
    private static bool initialCardsDealt;

    public static void ResetSession()
    {
        initialCardsDealt = false;
        exhibitOnlyNextTurn.Clear();
        handRotations.Clear();
        handPositions.Clear();
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
        CardInteraction template = ownerCards.Find(card => card != null && card.isExhibit && !card.IsSpecialAction);
        if (template == null)
        {
            Debug.LogWarning($"P{seat + 1}の展示カードを複製できないため、特殊カードを配布できません。");
            return;
        }
        if (!handRotations.ContainsKey(seat))
        {
            handRotations[seat] = template.transform.rotation;
            handPositions[seat] = template.transform.position;
        }

        for (int i = 0; i < count; i++)
        {
            SpecialActionEffect effect = (SpecialActionEffect)Random.Range(1, 4);
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

    private static CardInteraction CreateCard(CardInteraction template, SpecialActionEffect effect, int seat)
    {
        GameObject clone = Object.Instantiate(template.gameObject, template.transform.parent);
        CardInteraction card = clone.GetComponent<CardInteraction>();
        card.specialEffect = effect;
        card.isExhibit = true;
        card.isPhantomThief = false;
        card.isCage = false;
        card.InitializeHandPose(handPositions[seat], handRotations[seat]);

        string imageName = effect == SpecialActionEffect.Truck ? "トラック" :
            effect == SpecialActionEffect.LargeTruck ? "大型トラック" : "コレクター";
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
