using UnityEngine;
using System.Collections.Generic;

public class Player2 : MonoBehaviour
{
    public List<CardInteraction> player2Cards; // 相手のカードリスト
    public StealNumberEffect effectPrefab; // **数字表示用のプレハブをセット**
    private CardInteraction selectedCard; // **ランダムに選ばれたカードを保持**
    public CardInteraction SelectedCard => selectedCard; // **外部から取得できるプロパティ**
    public int SelectedNumber { get; private set; }
    public ArrestEffect arrestEffectPrefab; // 逮捕エフェクトのプレハブ
    public bool hasCriminalRecord = false; // 前科ありフラグ

    public bool HasBeenArrested { get; set; } // プレイヤーが逮捕されたかどうか
    private bool hasAppliedPenalty = false;  // ペナルティが適用されたかどうかをチェックするフラグ
    public bool HasPendingArrestPenalty => hasAppliedPenalty;

    public void ApplyOnlinePenaltyPending(bool value)
    {
        hasAppliedPenalty = value;
    }
    public GameObject arrestPenaltyCardPrefab;  // ペナルティカードのプレハブをInspectorで設定
    private bool isFirstOffense = false; // 初犯フラグ

    private StealNumberEffect activeStealEffect; // 現在の怪盗宣言エフェクト
    private ArrestEffect activeArrestEffect; // 現在の逮捕エフェクト

    private List<ArrestPenaltyCard> PenaltyCards2 = new List<ArrestPenaltyCard>(); // 🔹 このクラスで生成したペナルティカードを管理
    private bool penaltyCardCreatedThisTurn;
    public bool isEliminated = false; // 🔸 脱落フラグを追加

    public bool IsEliminated => isEliminated; // 外部からも確認できるようにする
    public void SelectRandomCard()
    {
        if (SpecialActionCardSystem.TryGetActiveAdvanceNotice(1, out CardInteraction notice))
        {
            RestoreAdvanceNotice(notice);
            return;
        }
        if (isEliminated)
        {
            Debug.Log($"{name} は脱落しているためカードを選びません。");
            return;
        }
        if (player2Cards.Count == 0) return;

        // 🔥 怪盗が初犯の場合はランダム選択から除外
        List<CardInteraction> selectableCards = new List<CardInteraction>(player2Cards);
        selectableCards.RemoveAll(card => !SpecialActionCardSystem.CanSelect(1, card));

        if (isFirstOffense)
        {
            selectableCards.RemoveAll(card => card.isPhantomThief); // 怪盗カードを除外
            Debug.Log($"{name} は初犯のため怪盗カードを出せません。");
            isFirstOffense = false;
        }

        if (selectableCards.Count == 0)
        {
            Debug.Log($"{name} が選べるカードがありません。");
            return;
        }

        // ランダムに選択
        int randomIndex = Random.Range(0, selectableCards.Count);
        selectedCard = selectableCards[randomIndex];
        selectedCard.EnsureVisibleForTable();
        SpecialActionCardSystem.NotifySelected(1, selectedCard);

        Vector3 player2Position = new Vector3(0, 0, 1);
        if (!KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            selectedCard.MoveTo(player2Position, 2.5f);
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
        CardInteraction match = player2Cards.Find(card => card != null &&
            (int)card.specialEffect == specialEffect &&
            card.isExhibit == exhibit &&
            card.isPhantomThief == thief &&
            card.isCage == cage);
        if (match == null)
            match = SpecialActionCardSystem.EnsureOnlineCardInHand(1, specialEffect,
                exhibit, thief, cage, HandManager.Instance);
        if (match == null)
        {
            Debug.LogError($"【オンライン】Player2の対応カードがありません。特殊:{specialEffect}");
            return;
        }
        selectedCard = match;
        match.EnsureVisibleForTable();
        SelectedNumber = thief ? declaredNumber : 0;
        match.SelectedNumber = SelectedNumber;
        SpecialActionCardSystem.NotifySelected(1, match);
        // ローカルCPUのPlayer2と同じ伏せ移動速度・軌道を使う。
        Debug.Log($"【オンライン卓上待機】local P2：{match.name} 宣言:{SelectedNumber}");
    }

    public void SelectOnlinePass()
    {
        selectedCard = null;
        SelectedNumber = 0;
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
            Debug.Log($"{name} は脱落しているため行動できません。");
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
                Debug.Log($"Player2 はすでに怪盗の数字を決めています。（{SelectedNumber}）");
                ShowStealNumber(SelectedNumber);
                return;
            }

            SelectedNumber = selectedCard.RandomDeclaredNumber();
            Debug.Log($"Player2 が怪盗を選んだ！盗む宝の数: {SelectedNumber}");
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

        activeStealEffect = Instantiate(effectPrefab, new Vector3(-0.7f, 0f, 1.17f), Quaternion.identity);
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
            activeArrestEffect = Instantiate(arrestEffectPrefab, transform.position + new Vector3(0f, 0.1f, 1.6f), Quaternion.identity);
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
            bool noFirstOffenseRule = HandManager.Instance != null &&
                                      HandManager.Instance.ActionPlayerCount == 2;
            hasCriminalRecord = true;
            isFirstOffense = !noFirstOffenseRule;
            ShowPenaltyCard(!noFirstOffenseRule);
            if (noFirstOffenseRule)
            {
                RemoveRandomPlayerCard();
                Debug.Log($"{name} は2人用ルールにより、最初の逮捕から行動カードを1枚没収されました。");
                if (SpecialActionCardSystem.IsEliminatedByNormalCards(player2Cards))
                    isEliminated = true;
                return;
            }
            Debug.Log($"{name} は初犯になりました。次のターン怪盗を出せません。");
        }
        else
        {
            if (SpecialActionCardSystem.NormalCards(player2Cards).Count > 0)
            {
                RemoveRandomPlayerCard();
                Debug.Log($"{name} は前科ありのため行動カードを1枚没収されました。残り {player2Cards.Count} 枚。");
            }

            // 通常の檻1枚だけでも脱落。カード自体は同期用に手札へ残す。
            if (SpecialActionCardSystem.IsEliminatedByNormalCards(player2Cards))
            {
                List<CardInteraction> normalCards = SpecialActionCardSystem.NormalCards(player2Cards);
                Debug.Log(normalCards.Count == 1 && normalCards[0].IsCageCard()
                    ? $"{name} は通常の檻1枚になったため脱落しました！"
                    : $"{name} は通常カードがすべて没収されたため脱落しました！");
                isEliminated = true;
                return;
            }
            else if (player2Cards.Count == 0)
            {
                Debug.Log($"{name} は行動カードがすべて没収され、脱落しました！");
                isEliminated = true;
                return;
            }

        }
    }

    private void RemoveRandomPlayerCard()
    {
        if (player2Cards == null || player2Cards.Count == 0) return;

        List<CardInteraction> normalCards = SpecialActionCardSystem.NormalCards(player2Cards);
        if (normalCards.Count == 0) return;
        CardInteraction cardToRemove = normalCards[Random.Range(0, normalCards.Count)];
        player2Cards.Remove(cardToRemove);
        Debug.Log($"{name} の {cardToRemove.name} が没収されました。");
    }

    private void ShowPenaltyCard(bool showFirstOffenseFace = true)
    {
        if (arrestPenaltyCardPrefab != null)
        {
            Vector3 spawnPosition = new Vector3(-2.5f, 0f, 2.5f);
            Quaternion spawnRotation = Quaternion.identity;
            Vector3 startPosition = spawnPosition + spawnRotation * Vector3.forward * 2.5f;
            ArrestPenaltyCard penaltyCard = Instantiate(arrestPenaltyCardPrefab, startPosition, spawnRotation).GetComponent<ArrestPenaltyCard>();

            PenaltyCards2.Add(penaltyCard); // 🔹 生成したペナルティカードをリストに追加
            penaltyCardCreatedThisTurn = true;

            penaltyCard.SetPenaltyState(showFirstOffenseFace);
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
        foreach (ArrestPenaltyCard penaltyCard in PenaltyCards2)
            if (penaltyCard != null) Destroy(penaltyCard.gameObject);
        PenaltyCards2.Clear();
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
        if (value && PenaltyCards2.Count == 0)
        {
            bool firstFace = HandManager.Instance == null ||
                             HandManager.Instance.ActionPlayerCount != 2;
            isFirstOffense = firstFace;
            ShowPenaltyCard(firstFace);
        }
    }

    public void MoveCardsAfterThiefPhase()
    {
        bool keepAdvanceNotice = SpecialActionCardSystem.IsAdvanceNoticePendingCard(1, selectedCard);
      
        if (hasCriminalRecord && !penaltyCardCreatedThisTurn)
        {
            foreach (var penaltyCard in PenaltyCards2) // 🔹 生成したペナルティカードだけ裏返す
            {
                if (penaltyCard != null)
                {
                    penaltyCard.FlipCard();
                }
            }
        }

        foreach (var card in player2Cards)
        {
            if (SpecialActionCardSystem.IsAdvanceNoticePendingCard(1, card)) continue;
            card.ResetForNextActionSelection();
            card.MoveTo(new Vector3(0f, 2f, 2.5f), 2.5f);
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
