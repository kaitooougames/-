using System.Collections.Generic;
using UnityEngine;

namespace TreasureGame
{
public class Player : MonoBehaviour
{
    [SerializeField] private int playerId;
    [SerializeField] private Transform handAnchor;
    [SerializeField] private Transform displayAnchor;
    [SerializeField] private List<Treasure> stock = new List<Treasure>();
    [SerializeField] private List<Treasure> displayedTreasures = new List<Treasure>();
    [SerializeField] private float handCardSpacing = 0.30f;
    [SerializeField, Min(1)] private int handVisibleCardCount = 12;
    [SerializeField] private float handStorageDistance = 6.5f;
    [SerializeField] private float displayColumnSpacing = 0.50f;
    [SerializeField] private float displayRowSpacing = 0.36f;
    [SerializeField] private float displayMaxDepth = 1.5f;
    private readonly Dictionary<Treasure, int> displayTypeSlots = new Dictionary<Treasure, int>();
    private readonly int[] nextDisplayTypeSlots = new int[4];

    public int PlayerId => playerId;
    public IReadOnlyList<Treasure> Stock => stock;
    public IReadOnlyList<Treasure> DisplayedTreasures => displayedTreasures;
    public bool HandVisible { get; private set; } = true;
    public bool CanScrollHandBackward => handScrollIndex > 0.001f;
    public bool CanScrollHandForward => handScrollIndex < MaxHandScroll - 0.001f;
    private float handScrollIndex;
    private int MaxHandScroll => Mathf.Max(0, stock.Count - Mathf.Max(1, handVisibleCardCount));

    public void Configure(int id, Transform hand, Transform display)
    {
        playerId = id; handAnchor = hand; displayAnchor = display;
        stock.Clear(); displayedTreasures.Clear();
        displayTypeSlots.Clear();
        System.Array.Clear(nextDisplayTypeSlots, 0, nextDisplayTypeSlots.Length);
        handScrollIndex = 0;
        HandVisible = playerId != 0;
    }

    public void AddToStock(Treasure card)
    {
        if (!stock.Contains(card)) stock.Add(card);
        displayedTreasures.Remove(card);
        displayTypeSlots.Remove(card);
        card.SetOwner(this);
    }

    public void AddToDisplay(Treasure card)
    {
        stock.Remove(card);
        if (!displayedTreasures.Contains(card))
        {
            displayedTreasures.Add(card);
            int type = (int)card.Type;
            displayTypeSlots[card] = nextDisplayTypeSlots[type]++;
        }
        card.SetOwner(this);
        card.SetLocation(TreasureLocation.Display);
        card.SetFaceUp(false);
    }

    public void RemoveDisplayed(Treasure card)
    {
        displayedTreasures.Remove(card);
        displayTypeSlots.Remove(card);
        // nextDisplayTypeSlotsは減らさない。盗まれた場所はターン終了まで空けておく。
    }

    public void CompactDisplaySlots()
    {
        displayTypeSlots.Clear();
        System.Array.Clear(nextDisplayTypeSlots, 0, nextDisplayTypeSlots.Length);
        foreach (Treasure card in displayedTreasures)
        {
            int type = (int)card.Type;
            displayTypeSlots[card] = nextDisplayTypeSlots[type]++;
        }
    }

    public void SwapDisplayedTreasures(Treasure first, Treasure second, float duration)
    {
        if (first == null || second == null || first == second ||
            first.Type != second.Type || !displayTypeSlots.ContainsKey(first) ||
            !displayTypeSlots.ContainsKey(second)) return;
        int firstSlot = displayTypeSlots[first];
        displayTypeSlots[first] = displayTypeSlots[second];
        displayTypeSlots[second] = firstSlot;
        int firstListIndex = displayedTreasures.IndexOf(first);
        int secondListIndex = displayedTreasures.IndexOf(second);
        if (firstListIndex >= 0 && secondListIndex >= 0)
        {
            displayedTreasures[firstListIndex] = second;
            displayedTreasures[secondListIndex] = first;
        }
        GetDisplayPose(first, out Vector3 firstPosition, out Quaternion firstRotation);
        GetDisplayPose(second, out Vector3 secondPosition, out Quaternion secondRotation);
        first.AnimateToKeepingCurrentFace(firstPosition, firstRotation, duration);
        second.AnimateToKeepingCurrentFace(secondPosition, secondRotation, duration);
    }

    public int[] GetDisplayedTreasureNetworkOrder()
    {
        return displayedTreasures.ConvertAll(card =>
            card != null ? card.NetworkId : -1).ToArray();
    }

    public void ApplyDisplayedTreasureNetworkOrder(int[] networkIds)
    {
        if (networkIds == null || networkIds.Length == 0) return;
        var byId = new Dictionary<int, Treasure>();
        foreach (Treasure card in displayedTreasures)
            if (card != null) byId[card.NetworkId] = card;
        var ordered = new List<Treasure>();
        foreach (int id in networkIds)
            if (byId.TryGetValue(id, out Treasure card))
            {
                ordered.Add(card);
                byId.Remove(id);
            }
        foreach (Treasure card in displayedTreasures)
            if (card != null && byId.ContainsKey(card.NetworkId))
                ordered.Add(card);
        displayedTreasures.Clear();
        displayedTreasures.AddRange(ordered);
        CompactDisplaySlots();
    }

    public void ShuffleDisplayedType(TreasureType type, float duration)
    {
        var cards = displayedTreasures.FindAll(card => card != null && card.Type == type);
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int other = Random.Range(0, i + 1);
            Treasure temporary = cards[i];
            cards[i] = cards[other];
            cards[other] = temporary;
        }
        var typeIndices = new List<int>();
        for (int i = 0; i < displayedTreasures.Count; i++)
            if (displayedTreasures[i] != null && displayedTreasures[i].Type == type)
                typeIndices.Add(i);
        for (int i = 0; i < cards.Count; i++)
        {
            displayedTreasures[typeIndices[i]] = cards[i];
            displayTypeSlots[cards[i]] = i;
        }
        // 座標はここでは動かさない。全員の並び替え完了後、展示場外から一斉再展示する。
    }

    public void SortHandForLayout()
    {
        stock.Sort(CompareTreasure);
        handScrollIndex = Mathf.Clamp(handScrollIndex, 0f, MaxHandScroll);
    }

    public void EnsureCardVisible(Treasure card)
    {
        int index = stock.IndexOf(card);
        int visibleCount = Mathf.Max(1, handVisibleCardCount);
        if (index < handScrollIndex) handScrollIndex = index;
        else if (index >= handScrollIndex + visibleCount) handScrollIndex = index - visibleCount + 1;
        handScrollIndex = Mathf.Clamp(handScrollIndex, 0f, MaxHandScroll);
    }

    public void ScrollHand(float amount, float duration)
    {
        handScrollIndex = Mathf.Clamp(handScrollIndex + amount, 0f, MaxHandScroll);
        AnimateHandLayout(duration);
    }

    public void DragHand(float amount)
    {
        handScrollIndex = Mathf.Clamp(handScrollIndex + amount, 0f, MaxHandScroll);
        foreach (Treasure card in stock)
        {
            GetHandPose(card, out Vector3 position, out Quaternion rotation);
            card.MoveTo(position, rotation);
        }
    }

    public void DragHandScreenPixels(float screenPixels, Camera camera)
    {
        if (camera == null)
        {
            DragHand(screenPixels / 100f);
            return;
        }
        Vector3 axis = handAnchor.rotation * Vector3.right * handCardSpacing;
        float pixelsPerCard = Mathf.Abs(
            camera.WorldToScreenPoint(handAnchor.position + axis).x -
            camera.WorldToScreenPoint(handAnchor.position).x);
        DragHand(screenPixels / Mathf.Max(20f, pixelsPerCard));
    }

    public void AnimateHandLayout(float duration)
    {
        foreach (Treasure card in stock)
        {
            GetHandPose(card, out Vector3 position, out Quaternion rotation);
            card.AnimateTo(position, rotation, duration);
        }
    }

    public void SetHandVisible(bool visible, float duration)
    {
        HandVisible = visible;
        AnimateHandLayout(duration);
    }

    public void LayoutCards()
    {
        for (int i = 0; i < stock.Count; i++)
        {
            GetHandPose(i, stock.Count, out Vector3 p, out Quaternion r);
            stock[i].SetLocation(TreasureLocation.Hand);
            stock[i].SetFaceUp(true);
            stock[i].MoveTo(p, r);
        }
        for (int i = 0; i < displayedTreasures.Count; i++)
        {
            GetDisplayPose(displayedTreasures[i], out Vector3 p, out Quaternion r);
            displayedTreasures[i].MoveTo(p, r);
        }
    }

    public void GetHandPose(Treasure card, out Vector3 position, out Quaternion rotation)
    {
        int index = Mathf.Max(0, stock.IndexOf(card));
        GetHandPose(index, stock.Count, out position, out rotation);
    }

    private void GetHandPose(int index, int count, out Vector3 position, out Quaternion rotation)
    {
        rotation = handAnchor.rotation * Quaternion.Euler(0, 0, 1);
        int visibleCount = Mathf.Min(Mathf.Max(1, handVisibleCardCount), count);
        float start = -handCardSpacing * (visibleCount - 1) * 0.5f;
        float visibleIndex = index - handScrollIndex;
        position = handAnchor.position + Vector3.up * 0.03f
            + handAnchor.rotation * Vector3.right * (start + handCardSpacing * visibleIndex);
        if (playerId == 0 && !HandVisible)
        {
            float overflowWidth = Mathf.Max(0, count - visibleCount) * handCardSpacing;
            position += handAnchor.rotation * Vector3.right * (handStorageDistance + overflowWidth);
        }
    }

    public void GetDisplayPose(Treasure card, out Vector3 position, out Quaternion rotation)
    {
        TreasureType[] order = { TreasureType.Gold, TreasureType.Jewel, TreasureType.Relic, TreasureType.Painting };
        int column = System.Array.IndexOf(order, card.Type);
        int type = (int)card.Type;
        int typeIndex = displayTypeSlots.TryGetValue(card, out int assignedSlot)
            ? assignedSlot
            : nextDisplayTypeSlots[type];
        int slotCount = Mathf.Max(1, nextDisplayTypeSlots[type] +
            (displayTypeSlots.ContainsKey(card) ? 0 : 1));
        float rowSpacing = slotCount <= 1 ? 0f
            : Mathf.Min(displayRowSpacing, displayMaxDepth / (slotCount - 1));
        rotation = displayAnchor.rotation * Quaternion.Euler(0, 0, 1);
        float start = -displayColumnSpacing * 1.5f;
        position = displayAnchor.position + Vector3.up * (0.03f + typeIndex * 0.002f)
            + displayAnchor.rotation * Vector3.right * (start + displayColumnSpacing * column)
            + displayAnchor.rotation * Vector3.forward * (rowSpacing * typeIndex);
    }

    private static int CompareTreasure(Treasure a, Treasure b)
    {
        int type = TypeOrder(a.Type).CompareTo(TypeOrder(b.Type));
        return type != 0 ? type : a.Authenticity.CompareTo(b.Authenticity);
    }

    private static int TypeOrder(TreasureType type)
    {
        switch (type)
        {
            // Player1のhandAnchorではローカル右が画面左を向くため、
            // 初期分配と同じ内部順にすると画面上は「遺物・宝石・絵画・金」になる。
            case TreasureType.Gold: return 0;
            case TreasureType.Painting: return 1;
            case TreasureType.Jewel: return 2;
            default: return 3; // 遺物
        }
    }
}
}
