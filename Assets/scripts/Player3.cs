using UnityEngine;
using System.Collections.Generic;


public class Player3 : MonoBehaviour
{
    public List<CardInteraction> player3Cards; // 相手のカードリスト
    public StealNumberEffect effectPrefab; // **数字表示用のプレハブをセット**
    private CardInteraction selectedCard; // **ランダムに選ばれたカードを保持**
    public CardInteraction SelectedCard => selectedCard; // **外部から取得できるプロパティ**
    public int SelectedNumber { get; private set; }
    public ArrestEffect arrestEffectPrefab; // 逮捕エフェクトのプレハブ
    public bool hasCriminalRecord = false; // 前科ありフラグ

    public bool HasBeenArrested { get; set; } // プレイヤーが逮捕されたかどうか
    private bool hasAppliedPenalty = false;  // ペナルティが適用されたかどうかをチェックするフラグ
    public GameObject arrestPenaltyCardPrefab;  // ペナルティカードのプレハブをInspectorで設定
    private bool isFirstOffense = false; // 初犯フラグ

    private StealNumberEffect activeStealEffect; // 現在の怪盗宣言エフェクト
    private ArrestEffect activeArrestEffect; // 現在の逮捕エフェクト
    private List<ArrestPenaltyCard> PenaltyCards3 = new List<ArrestPenaltyCard>(); // 🔹 このクラスで生成したペナルティカードを管理
    private bool penaltyCardCreatedThisTurn;
    public bool isEliminated = false; // 🔸 脱落フラグを追加

    public bool IsEliminated => isEliminated; // 外部からも確認できるようにする

    public void SelectRandomCard()
    {
        if (SpecialActionCardSystem.TryGetActiveAdvanceNotice(2, out CardInteraction notice))
        {
            RestoreAdvanceNotice(notice);
            return;
        }
        if (isEliminated)
        {
            Debug.Log($"{name} はすでに脱落しています。ペナルティ適用なし。");
            return;
        }
        if (player3Cards.Count == 0) return;

        // 🔥 怪盗が初犯の場合はランダム選択から除外
        List<CardInteraction> selectableCards = new List<CardInteraction>(player3Cards);
        selectableCards.RemoveAll(card => !SpecialActionCardSystem.CanSelect(2, card));

        if (isFirstOffense)
        {
            selectableCards.RemoveAll(card => card.isPhantomThief); // 怪盗カードを除外
            Debug.Log($"{name} は初犯のため怪盗カードを出せません。");
            isFirstOffense = false;
            hasCriminalRecord = true; // 前科ありに設定
        }

        if (selectableCards.Count == 0)
        {
            Debug.Log($"{name} が選べるカードがありません。");
            return;
        }

        // ランダムに選択
        int randomIndex = Random.Range(0, selectableCards.Count);
        selectedCard = selectableCards[randomIndex];
        selectedCard.SetVisualVisible(true);
        SpecialActionCardSystem.NotifySelected(2, selectedCard);

        Vector3 player3Position = new Vector3(-1, 0, 0);
        selectedCard.MoveTo(player3Position, 2.5f);
    }

    public void RestoreAdvanceNotice(CardInteraction card)
    {
        selectedCard = card;
        SelectedNumber = 10;
        card.SelectedNumber = 10;
    }

    public void SelectOnlineCard(int specialEffect, bool exhibit, bool thief, bool cage,
        int declaredNumber)
    {
        CardInteraction match = player3Cards.Find(card => card != null &&
            (int)card.specialEffect == specialEffect && card.isExhibit == exhibit &&
            card.isPhantomThief == thief && card.isCage == cage);
        if (match == null) return;
        selectedCard = match;
        match.SetVisualVisible(true);
        SelectedNumber = thief ? declaredNumber : 0;
        match.SelectedNumber = SelectedNumber;
        SpecialActionCardSystem.NotifySelected(2, match);
        match.MoveTo(new Vector3(-1f, 0f, 0f), 2.5f);
    }

    public void SelectOnlinePass()
    {
        selectedCard = null;
        SelectedNumber = 0;
    }

    public void SetCpuDeclaredNumber(int number)
    {
        SelectedNumber = number;
        if (selectedCard != null) selectedCard.SelectedNumber = number;
    }

    private void OnEnable()
    {
        CardInteraction.OnAllCardsFlipped -= OnCardsRevealed; // 一度解除してから登録
        CardInteraction.OnAllCardsFlipped += OnCardsRevealed;
    }

    private void OnDisable()
    {
        CardInteraction.OnAllCardsFlipped -= OnCardsRevealed;
    }

    public void OnCardsRevealed()
    {
        if (SpecialActionCardSystem.IsDetectiveExcluded(selectedCard)) return;
        if (isEliminated)
        {
            Debug.Log($"{name} はすでに脱落しています。ペナルティ適用なし。");
            return;
        }
        if (selectedCard == null)
        {
            Debug.Log("選ばれたカードがありません。");
            return;
        }

        if (selectedCard.isPhantomThief)
        {
            // 🔹 すでに選ばれていたら処理をスキップ
            if (SelectedNumber != 0)
            {
                Debug.Log($"Player3 はすでに怪盗の数字を決めています。（{SelectedNumber}）");
                ShowStealNumber(SelectedNumber);
                return;
            }
            SelectedNumber = selectedCard.RandomDeclaredNumber();
            Debug.Log($"Player3 が怪盗を選んだ！盗む宝の数: {SelectedNumber}");

            ShowStealNumber(SelectedNumber);
        }
    }

    private void ShowStealNumber(int number)
    {
        if (activeStealEffect != null)
        {
            activeStealEffect.ShowNumber(number);
            return;
        }

        activeStealEffect = Instantiate(effectPrefab, new Vector3(-1.8f, 0f, 0f), Quaternion.identity);
        activeStealEffect.ShowNumber(number);
    }
    
    public void ShowArrestEffect(bool immediate = false)
    {
        if (isEliminated)
        {
            Debug.Log($"{name} は脱落しているため行動できません。");
            return;
        }

        if (hasAppliedPenalty)
        {
            Debug.Log($"{name} はすでにペナルティが適用されています。");
            return;
        }

        Debug.Log($"{name} にペナルティを適用: 逮捕フラグを true にします。");

        HasBeenArrested = true;
      
        hasAppliedPenalty = true;

        if (arrestEffectPrefab != null)
        {
            activeArrestEffect = Instantiate(arrestEffectPrefab, transform.position + new Vector3(-1.5f, 0.1f, -0.5f), Quaternion.identity);
            if (immediate) activeArrestEffect.ShowArrestForSecurityDice();
            else activeArrestEffect.ShowArrest();
        }
      
    }

    public void EndActionWithoutArrestPenalty()
    {
        HasBeenArrested = true;
        hasAppliedPenalty = false;
    }

    public void ApplyPenalty()
    {
        if (isEliminated)
        {
            Debug.Log($"{name} はすでに脱落しています。ペナルティ適用なし。");
            return;
        }
        if (!hasCriminalRecord)
        {
            hasCriminalRecord = true;
            isFirstOffense = true;
            Debug.Log($"{name} は初犯になりました。次のターン怪盗を出せません。");
            ShowPenaltyCard();
        }
        else
        {
            if (SpecialActionCardSystem.NormalCards(player3Cards).Count > 0)
            {
                RemoveRandomPlayerCard();
                Debug.Log($"{name} は前科ありのため行動カードを1枚没収されました。残り {player3Cards.Count} 枚。");
            }

            // 🟡 残り1枚が檻カードなら脱落＋檻カード削除
            if (SpecialActionCardSystem.IsEliminatedByNormalCards(player3Cards))
            {
                Debug.Log($"{name} は檻カード1枚のみになったため脱落しました！");
                player3Cards.RemoveAt(0); // 檻カード削除
                isEliminated = true;
                return;
            }
            else if (player3Cards.Count == 0)
            {
                Debug.Log($"{name} は行動カードがすべて没収され、脱落しました！");
                isEliminated = true;
                return;
            }
        }
    }

    private void RemoveRandomPlayerCard()
    {
        if (player3Cards == null || player3Cards.Count == 0)
        {
            Debug.LogWarning($"{name} の player3Cards が空です！");
            return;
        }

        List<CardInteraction> normalCards = SpecialActionCardSystem.NormalCards(player3Cards);
        if (normalCards.Count == 0) return;
        CardInteraction cardToRemove = normalCards[Random.Range(0, normalCards.Count)];
        player3Cards.Remove(cardToRemove);
        Debug.Log($"{name} の {cardToRemove.name} が没収されました。");
    }

  
    private void ShowPenaltyCard()
    {
        if (isFirstOffense && arrestPenaltyCardPrefab != null)
        {
            Vector3 spawnPosition = new Vector3(-2.5f, 0f, -2.5f); // 生成位置
            Quaternion spawnRotation = Quaternion.Euler(0f, -90f, 0f);

            Vector3 startPosition = spawnPosition + spawnRotation * Vector3.forward * 2.5f;
            ArrestPenaltyCard penaltyCard = Instantiate(arrestPenaltyCardPrefab, startPosition, spawnRotation).GetComponent<ArrestPenaltyCard>();

            PenaltyCards3.Add(penaltyCard); // 🔹 生成したペナルティカードをリストに追加
            penaltyCardCreatedThisTurn = true;
            penaltyCard.SetPenaltyState(true);
            penaltyCard.AnimateTo(spawnPosition);

            Debug.Log($"{name} に初犯のペナルティカードを表示しました。");
        }
    }


    public void ClearFirstOffenseRestriction()
    {
        isFirstOffense = false;
    }

    public void ResetPenaltyCards()
    {
        foreach (ArrestPenaltyCard penaltyCard in PenaltyCards3)
            if (penaltyCard != null) Destroy(penaltyCard.gameObject);
        PenaltyCards3.Clear();
        if (activeArrestEffect != null) Destroy(activeArrestEffect.gameObject);
        activeArrestEffect = null;
        hasCriminalRecord = false;
        isFirstOffense = false;
        hasAppliedPenalty = false;
        penaltyCardCreatedThisTurn = false;
        HasBeenArrested = false;
    }

    public void ApplyOnlineCriminalRecord(bool value)
    {
        hasCriminalRecord = value;
        if (value && PenaltyCards3.Count == 0)
        { isFirstOffense = true; ShowPenaltyCard(); }
    }

    public void MoveCardsAfterThiefPhase()
    {
        bool keepAdvanceNotice = SpecialActionCardSystem.IsAdvanceNoticePendingCard(2, selectedCard);
      
        if (hasCriminalRecord && !penaltyCardCreatedThisTurn)
        {
            foreach (var penaltyCard in PenaltyCards3) // 🔹 生成したペナルティカードだけ裏返す
            {
                if (penaltyCard != null)
                {
                    penaltyCard.FlipCard();
                }
            }
        }

        foreach (var card in player3Cards)
        {
            if (SpecialActionCardSystem.IsAdvanceNoticePendingCard(2, card)) continue;
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
                card.MoveToHidden(new Vector3(-5, 2, 0), 5);
            else
                card.MoveTo(new Vector3(-5, 2, 0), 5);
        }
        if (!keepAdvanceNotice && activeStealEffect != null)
        {
            Destroy(activeStealEffect.gameObject);
            activeStealEffect = null;
        }
        if (activeArrestEffect != null)
        {
            Destroy(activeArrestEffect.gameObject);
            activeArrestEffect = null;
        } 

        if (hasAppliedPenalty)
        { ApplyPenalty(); }

        hasAppliedPenalty = false;
        penaltyCardCreatedThisTurn = false;

        HasBeenArrested = false;

        if (!keepAdvanceNotice)
        {
            SelectedNumber = 0;
            selectedCard = null;
        }


    }
}
