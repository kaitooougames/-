using UnityEngine;

public class NumberButton : MonoBehaviour
{
    public int number;  // 1〜6 の値（Inspector で設定）
    private Vector3 originalLocalPosition; // パネル内の初期位置を保存
    private float hoverHeight = 0.1f; // ホバー時の上昇量

    private CardInteraction assignedPhantomThiefCard;  // **関連付けられた怪盗カード**

    void Awake()
    {
        originalLocalPosition = transform.localPosition;
    }

    void OnEnable()
    {
        transform.localPosition = originalLocalPosition;
    }

    void OnMouseEnter()
    {
        // 元と同じワールドY方向へ0.1上げる。ただし座標保存はローカルなので飛ばない。
        Vector3 localHoverOffset = transform.parent != null
            ? transform.parent.InverseTransformVector(Vector3.up * hoverHeight)
            : Vector3.up * hoverHeight;
        transform.localPosition = originalLocalPosition + localHoverOffset;
    }

    void OnMouseExit()
    {
        // カーソルが外れたら元の位置に戻る
        transform.localPosition = originalLocalPosition;
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


