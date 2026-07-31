using UnityEngine;
using System.Collections.Generic;

public class Player : MonoBehaviour
{
    public CardInteraction SelectedCard { get; private set; } // 選んだカード
    public int SelectedNumber { get; private set; } // 選択した数字
    public ArrestEffect arrestEffectPrefab; // 逮捕エフェクトのプレハブ

    private List<ActionCard> actionCards = new List<ActionCard>(); // 行動カードリスト
    public bool hasCriminalRecord = false; // 前科ありフラグ
   
    public bool HasBeenArrested { get; set; } // プレイヤーが逮捕されたかどうか
    private bool hasAppliedPenalty = false;  // ペナルティが適用されたかどうかをチェックするフラグ
    public GameObject arrestPenaltyCardPrefab;  // ペナルティカードのプレハブをInspectorで設定
    private bool isFirstOffense = false; // 初犯フラグ
    private GameObject penaltyCardObj;  // クラスのメンバ変数として宣言
    private ArrestEffect activeArrestEffect; // 現在の逮捕エフェクト
    private List<ArrestPenaltyCard> PenaltyCards1 = new List<ArrestPenaltyCard>(); // 🔹 このクラスで生成したペナルティカードを管理
    private List<CardInteraction> selectableCards = new List<CardInteraction>();
    public List<CardInteraction> playerCards; // 相手のカードリスト
    public bool isEliminated = false; // 🔸 脱落フラグを追加
    private StealNumberEffect activeStealEffect; // 現在の怪盗宣言エフェクト
    public StealNumberEffect effectPrefab; // **数字表示用のプレハブをセット**

    public bool IsEliminated => isEliminated; // 外部からも確認できるようにする

    void Start()
    {
        // 🔹 ゲーム開始時に場のカードを取得
        selectableCards.AddRange(FindObjectsOfType<CardInteraction>());
    }

    public void SelectCard(CardInteraction card, int selectedNumber)
    {
        List<CardInteraction> selectableCards = new List<CardInteraction>(playerCards);

        SelectedCard = card;
        SelectedNumber = selectedNumber; 
        Debug.Log($"Player が {card.name} を選択し、{selectedNumber} を宣言しました！");

        if (isFirstOffense)
        {        
            // **怪盗カードのクリックを無効化**
            foreach (var c in selectableCards)
            {
                if (c.isPhantomThief)
                {
                    c.isClickable = false; // **怪盗カードをクリック不可にする**
                                           // Debug.Log($"{name} は初犯のため怪盗カードを出せません。");

                }
            }

            isFirstOffense = false;
        }           
              
    }

    public void OnCardsRevealed()
    {
        if (SpecialActionCardSystem.IsDetectiveExcluded(SelectedCard)) return;
        if (SelectedNumber > 0)
        {
            ShowStealNumber(SelectedNumber);
        }
        else
        {
            Debug.Log($"{SelectedCard?.name ?? "カードなし"} は数字を持たないため表示しません");
        }
    }


    private void ShowStealNumber(int number)
    {
        if (activeStealEffect != null)
        {
            activeStealEffect.ShowNumber(number);
            return;
        }

        activeStealEffect = Instantiate(effectPrefab, new Vector3(0.7f, 0f, -1.17f), Quaternion.identity);
        activeStealEffect.ShowNumber(number);
    }

    // 逮捕エフェクトの表示
    public void ShowArrestEffect(bool immediate = false)
    {
        // 既にペナルティが適用されていれば何もしない
        if (hasAppliedPenalty)
        {
            Debug.Log($"{name} はすでにペナルティが適用されています。");
            return; // ペナルティ処理をスキップ
        }

         HasBeenArrested = true;
       
        hasAppliedPenalty = true;

        if (arrestEffectPrefab != null)
        {
            activeArrestEffect = Instantiate(arrestEffectPrefab, transform.position + new Vector3(0, 0.1f, -1.55f), Quaternion.identity);
            if (immediate) activeArrestEffect.ShowArrestForSecurityDice();
            else activeArrestEffect.ShowArrest();
        }
    }

    public void RestoreAdvanceNotice(CardInteraction card)
    {
        SelectedCard = card;
        SelectedNumber = 10;
        card.SelectedNumber = 10;
    }

    public void EndActionWithoutArrestPenalty()
    {
        HasBeenArrested = true;
        hasAppliedPenalty = false;
    }


    // 逮捕ペナルティ処理
    public void ApplyPenalty()
    {

        if (!hasCriminalRecord)
        {
            hasCriminalRecord = true;
            isFirstOffense = true;
            Debug.Log($"{name} は初犯になりました。次のターン怪盗を出せません。");
            ShowPenaltyCard();

            // 次のカード選択画面を待たず、怪盗カードをその場で暗くして
            // 「初犯中は選択できない」ことを見た目でも分かるようにする。
            if (HandManager.Instance != null)
                HandManager.Instance.RefreshPlayerOneCardAvailability();
        }
        else
        {
            if (SpecialActionCardSystem.NormalCards(playerCards).Count > 0)
            {
                RemoveRandomPlayerCard();
                Debug.Log($"{name} は前科ありのため行動カードを1枚没収されました。残り {playerCards.Count} 枚。");
            }

            if (SpecialActionCardSystem.IsEliminatedByNormalCards(playerCards))
            {
                Debug.Log($"{name} は脱落しました！");
                isEliminated = true;
            }

        }
    }
    private void RemoveRandomPlayerCard()
    {
        if (playerCards == null || playerCards.Count == 0) return;

        List<CardInteraction> normalCards = SpecialActionCardSystem.NormalCards(playerCards);
        if (normalCards.Count == 0) return;
        CardInteraction cardToRemove = normalCards[Random.Range(0, normalCards.Count)];
        playerCards.Remove(cardToRemove);
        HandManager.Instance?.RemoveConfiscatedPlayerOneCard(cardToRemove);

        // 🔸 GameObject を破壊せず非表示にする
        cardToRemove.gameObject.SetActive(false);

        Debug.Log($"{name} の {cardToRemove.name} が没収され、非表示になりました。");

        if (playerCards.Count == 1 && playerCards[0].IsCageCard())
        {
            Debug.Log($"{name} は檻カード1枚のみになったため脱落しました！");
        }
        else if (playerCards.Count == 0)
        {
            Debug.Log($"{name} は行動カードがなくなったため脱落しました！");
        }
    }


    private void ShowPenaltyCard()
    {
        if (isFirstOffense && arrestPenaltyCardPrefab != null)
        {
            Vector3 spawnPosition = new Vector3(2.5f, 0f, -2.5f); // 生成位置
            Quaternion spawnRotation = Quaternion.Euler(0f, 180f, 0f);

            Vector3 startPosition = spawnPosition + spawnRotation * Vector3.forward * 2.5f;
            ArrestPenaltyCard penaltyCard = Instantiate(arrestPenaltyCardPrefab, startPosition, spawnRotation).GetComponent<ArrestPenaltyCard>();

            PenaltyCards1.Add(penaltyCard); // 🔹 生成したペナルティカードをリストに追加
            penaltyCard.SetPenaltyState(true);
            penaltyCard.AnimateTo(spawnPosition);

            Debug.Log($"{name} に初犯のペナルティカードを表示しました。");
        }
    }

    public bool IsFirstOffense()
    {
        return isFirstOffense;
    }

    public void ClearFirstOffenseRestriction()
    {
        isFirstOffense = false;
    }

    public void ResetPenaltyCards()
    {
        foreach (ArrestPenaltyCard penaltyCard in PenaltyCards1)
            if (penaltyCard != null) Destroy(penaltyCard.gameObject);
        PenaltyCards1.Clear();
        if (activeArrestEffect != null) Destroy(activeArrestEffect.gameObject);
        activeArrestEffect = null;
        hasCriminalRecord = false;
        isFirstOffense = false;
        hasAppliedPenalty = false;
        HasBeenArrested = false;
    }

    public void ApplyOnlineCriminalRecord(bool value)
    {
        hasCriminalRecord = value;
        if (value && PenaltyCards1.Count == 0)
        { isFirstOffense = true; ShowPenaltyCard(); }
    }

    public void MoveCardsAfterThiefPhase()
    {
        bool keepAdvanceNotice = SpecialActionCardSystem.IsAdvanceNoticePendingCard(0, SelectedCard);
   
        if (hasCriminalRecord)
        {
            foreach (var penaltyCard in PenaltyCards1) // 🔹 生成したペナルティカードだけ裏返す
            {
                if (penaltyCard != null)
                {
                    penaltyCard.FlipCard();
                }
            }
        }

        if (hasAppliedPenalty)
        { ApplyPenalty(); }

        hasAppliedPenalty = false;
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

        HasBeenArrested = false;
        if (!keepAdvanceNotice)
        {
            SelectedCard = null;
            SelectedNumber = 0;
        }
    }

}
