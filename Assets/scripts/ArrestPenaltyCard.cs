using UnityEngine;

public class ArrestPenaltyCard : MonoBehaviour
{
    private bool isFirstOffense = true;  // 初犯フラグ
    private Quaternion seatRotation = Quaternion.identity;
    [SerializeField] private float moveDuration = 0.55f;
    [SerializeField] private float flipDuration = 0.4f;

    // 初犯か前科ありかを設定し、カードの向きを決定する
    public void SetPenaltyState(bool isFirstOffense)
    {
        seatRotation = transform.rotation;
        this.isFirstOffense = isFirstOffense;
        UpdateCardRotation();
    }

    // カードを裏返す
    public void FlipCard()
    {
        isFirstOffense = false; // 一度裏返したら前科ありの状態に変更
        StopCoroutine(nameof(AnimateFlip));
        StartCoroutine(nameof(AnimateFlip));
    }

    public void AnimateTo(Vector3 targetPosition)
    {
        StopCoroutine(nameof(AnimateMove));
        StartCoroutine(AnimateMove(targetPosition));
    }

    private System.Collections.IEnumerator AnimateMove(Vector3 targetPosition)
    {
        Vector3 startPosition = transform.position;
        float elapsed = 0f;
        while (elapsed < moveDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / moveDuration);
            transform.position = Vector3.Lerp(startPosition, targetPosition, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.position = targetPosition;
    }

    private System.Collections.IEnumerator AnimateFlip()
    {
        Quaternion startRotation = transform.rotation;
        Quaternion targetRotation = seatRotation * Quaternion.Euler(0f, 0f, 180f);
        float elapsed = 0f;
        while (elapsed < flipDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / flipDuration);
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.rotation = targetRotation;
    }

    // カードの向きを更新
    private void UpdateCardRotation()
    {
        transform.rotation = seatRotation *
            (isFirstOffense ? Quaternion.identity : Quaternion.Euler(0f, 0f, 180f));
    }
}
