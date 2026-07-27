using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ArrestHandler : MonoBehaviour
{
    public ArrestEffect arrestEffectPrefab; // 逮捕エフェクトのプレハブ
    private List<CardInteraction> fieldCards = new List<CardInteraction>(); // 場のカード
    private List<CardInteraction> phantomThieves = new List<CardInteraction>(); // 怪盗カード
    private List<CardInteraction> cages = new List<CardInteraction>(); // 檻カード
    public List<ArrestEffect> currentBattingEffects = new List<ArrestEffect>();
    private readonly List<int> successfulCagePlayerIds = new List<int>();
    private readonly Dictionary<CardInteraction, int> fieldCardOwners = new Dictionary<CardInteraction, int>();
    [SerializeField] ArrestHandler arrestHandler; // ArrestHandlerを参照


    private void Start()
    {
        CardInteraction.OnAllCardsFlipped -= ResolveArrests; // 二重登録を防ぐために先に解除
        // カードのめくりイベントに登録
        CardInteraction.OnAllCardsFlipped += ResolveArrests;
    }

    private void OnDestroy()
    {
        // イベントの登録解除（メモリリーク防止）
        CardInteraction.OnAllCardsFlipped -= ResolveArrests;
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

        // **各プレイヤーのカードを取得**
        Player player = FindObjectOfType<Player>();
        Player2 player2 = FindObjectOfType<Player2>();
        Player3 player3 = FindObjectOfType<Player3>();
        Player4 player4 = FindObjectOfType<Player4>();

        if (player != null && !player.isEliminated && player.SelectedCard != null)
        {
            player.SelectedCard.SelectedNumber = player.SelectedNumber;
            Debug.Log($"[DEBUG] {player.SelectedCard.name} の SelectedNumber = {player.SelectedCard.SelectedNumber}");
            fieldCards.Add(player.SelectedCard);
            fieldCardOwners[player.SelectedCard] = 0;
        }

        if (player2 != null && !player2.isEliminated && player2.SelectedCard != null)
        {
            player2.SelectedCard.SelectedNumber = player2.SelectedNumber;
            Debug.Log($"[DEBUG] {player2.SelectedCard.name} の SelectedNumber = {player2.SelectedCard.SelectedNumber}");
            fieldCards.Add(player2.SelectedCard);
            fieldCardOwners[player2.SelectedCard] = 1;
        }

        if (player3 != null && !player3.isEliminated && player3.SelectedCard != null)
        {
            player3.SelectedCard.SelectedNumber = player3.SelectedNumber;
            Debug.Log($"[DEBUG] {player3.SelectedCard.name} の SelectedNumber = {player3.SelectedCard.SelectedNumber}");
            fieldCards.Add(player3.SelectedCard);
            fieldCardOwners[player3.SelectedCard] = 2;
        }

        if (player4 != null && !player4.isEliminated && player4.SelectedCard != null)
        {
            player4.SelectedCard.SelectedNumber = player4.SelectedNumber;
            Debug.Log($"[DEBUG] {player4.SelectedCard.name} の SelectedNumber = {player4.SelectedCard.SelectedNumber}");
            fieldCards.Add(player4.SelectedCard);
            fieldCardOwners[player4.SelectedCard] = 3;
        }


        // **取得した場のカードをデバッグ表示**
        Debug.Log($"場のカードの数: {fieldCards.Count}");
        foreach (var card in fieldCards)
        {
            Debug.Log($"カード名: {card.name}, 怪盗: {card.isPhantomThief}, 檻: {card.isCage}, 選択数: {card.SelectedNumber}");
        }

        // **怪盗カードを分類**
        foreach (var card in fieldCards)
        {
            if (card.isPhantomThief)
            {
                phantomThieves.Add(card);
            }
            else if (card.isCage)
            {
                cages.Add(card);
            }
        }

        // **怪盗と檻の数をデバッグ表示**
        Debug.Log($"怪盗の数: {phantomThieves.Count}, 檻の数: {cages.Count}");
        foreach (var thief in phantomThieves)
        {
            Debug.Log($"怪盗カード: {thief.name}, 選択数: {thief.SelectedNumber}");
        }

        // **怪盗の競合処理**
        HandleThiefConflict(); // 🔹 檻がいなくても競合チェックを実行
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
        ArrestThieves();

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


    private void ArrestThieves()
    {
        // 警備員ですでに逮捕済みでも、檻の対応対象なら檻は成功・報酬あり。
        // 逮捕ペナルティそのものはArrest内で二重発動を防ぐ。
        phantomThieves = phantomThieves.OrderBy(t => t.SelectedNumber).ToList();

        for (int i = 0; i < cages.Count && i < phantomThieves.Count; i++)
        {
            Debug.Log($"檻が {phantomThieves[i].name} を逮捕！");
            Arrest(phantomThieves[i]);
            int cagePlayerId = FindPlayerIdByCard(cages[i]);
            if (cagePlayerId >= 0 && !successfulCagePlayerIds.Contains(cagePlayerId))
                successfulCagePlayerIds.Add(cagePlayerId);
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

    private int FindPlayerIdByCard(CardInteraction card)
    {
        return card != null && fieldCardOwners.TryGetValue(card, out int playerId)
            ? playerId
            : -1;
    }

    private bool Arrest(CardInteraction card)
    {
        Debug.Log($"{card.name} が逮捕されました！");
        bool arrested = false;

        // Player を検索
        Player player = FindPlayerByCard(card);
        if (player != null && !player.HasBeenArrested)
        {
            player.ShowArrestEffect();
            arrested = true;
        }

        // Player2 を検索
        Player2 player2 = FindPlayer2ByCard(card);
        if (player2 != null && !player2.HasBeenArrested)
        {
            player2.ShowArrestEffect();
            arrested = true;
        }

        // Player3 を検索
        Player3 player3 = FindPlayer3ByCard(card);
        if (player3 != null && !player3.HasBeenArrested)
        {
            player3.ShowArrestEffect();
            arrested = true;
        }

        // Player4 を検索
        Player4 player4 = FindPlayer4ByCard(card);
        if (player4 != null && !player4.HasBeenArrested)
        {
            player4.ShowArrestEffect();
            arrested = true;
        }
        return arrested;
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
