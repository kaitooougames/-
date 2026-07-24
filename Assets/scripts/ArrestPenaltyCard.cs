using UnityEngine;

public class ArrestPenaltyCard : MonoBehaviour
{
    private bool isFirstOffense = true;  // 初犯フラグ

    // 初犯か前科ありかを設定し、カードの向きを決定する
    public void SetPenaltyState(bool isFirstOffense)
    {
        this.isFirstOffense = isFirstOffense;
        UpdateCardRotation();
    }

    // カードを裏返す
    public void FlipCard()
    {
        isFirstOffense = false; // 一度裏返したら前科ありの状態に変更
        UpdateCardRotation();
    }

    // カードの向きを更新
    private void UpdateCardRotation()
    {
        transform.rotation = isFirstOffense ? Quaternion.Euler(0, 0, 0) : Quaternion.Euler(0, 0, 180);
    }
}
