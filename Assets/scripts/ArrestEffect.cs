using UnityEngine;
using TMPro;
using System.Collections;

public class ArrestEffect : MonoBehaviour
{
    public TextMeshPro textMesh;
    public float dropHeight = 1f;
    public float dropSpeed = 10f;
    public float scaleMultiplier = 1.5f;
    public float scaleDuration = 0.1f;

    private Vector3 originalPosition;
    private Vector3 originalScale;

    private void Start()
    {
        originalPosition = transform.position;
        originalScale = transform.localScale;
    }

    public void ShowArrest()
    {
        ShowEffect("Arrest!", 1f);
    }
    public void ShowArrestForSecurityDice()
    {
        ShowEffect("Arrest!", 0.35f);
    }
    public void ShowBatting()
    {
        textMesh.gameObject.SetActive(false);
        textMesh.text = "Batting!";
        textMesh.color = new Color(0.22f, 0.28f, 0.93f); // 青

        StartCoroutine(EffectAnimation());

    }

    public void ShowCageDisabled()
    {
        textMesh.gameObject.SetActive(false);
        textMesh.text = "BLOCKED!";
        textMesh.color = new Color(1f, 0.28f, 0.08f);
        StartCoroutine(EffectAnimation());
    }

    private void ShowEffect(string message, float delay)
    {
        // **最初はテキストを非表示**
        textMesh.gameObject.SetActive(false);

        textMesh.text = message;
        StartCoroutine(EffectAnimation(delay));
    }

    private IEnumerator EffectAnimation(float delay = 1f)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        // **テキストを表示**
        textMesh.gameObject.SetActive(true);

        // **初期位置を上に設定**
        transform.position = originalPosition + Vector3.up * dropHeight;

        // **一瞬大きくして迫力を出す**
        transform.localScale = originalScale * scaleMultiplier;
        yield return new WaitForSeconds(scaleDuration);
        transform.localScale = originalScale;

        // **落下アニメーション**
        while (transform.position.y > originalPosition.y)
        {
            transform.position = Vector3.MoveTowards(transform.position, originalPosition, dropSpeed * Time.deltaTime);
            yield return null;
        }
    }

    public void MoveCardsAfterThiefPhase()
    {
        ArrestEffect[] allEffects = GameObject.FindObjectsOfType<ArrestEffect>();
        foreach (ArrestEffect effect in allEffects)
        {
            if (effect.gameObject.name.Contains("Clone"))
            {
                Destroy(effect.gameObject);
            }
        }

    }


}

