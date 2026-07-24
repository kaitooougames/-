using UnityEngine;

public class NumberButton : MonoBehaviour
{
    public int number;  // 1～6 の値（Inspector で設定）
    private Vector3 originalPosition; // 初期位置を保存
    private float hoverHeight = 0.1f; // ホバー時の上昇量

    private CardInteraction assignedPhantomThiefCard;  // **関連付けられた怪盗カード**

    void Start()
    {
        originalPosition = transform.position; // 初期位置を保存
    }

    void OnMouseEnter()
    {
        // カーソルを合わせたら少し上に移動
        transform.position = originalPosition + new Vector3(0, hoverHeight, 0);
    }

    void OnMouseExit()
    {
        // カーソルが外れたら元の位置に戻る
        transform.position = originalPosition;
    }

    void OnMouseDown()
    {
        if (assignedPhantomThiefCard != null)
        {
            assignedPhantomThiefCard.OnNumberSelected(number);
        }
        else
        {
            Debug.LogWarning("NumberButton: 関連付けられた怪盗カードがありません！");
        }
    }

    // **怪盗カードをセットする関数**
    public void SetPhantomThiefCard(CardInteraction card)
    {
        if (card != null && card.isPhantomThief)
        {
            assignedPhantomThiefCard = card;
        }
        else
        {
            assignedPhantomThiefCard = null;
            Debug.LogWarning("怪盗カードではないものが設定されようとしました: " + card?.name);
        }
    }

}


