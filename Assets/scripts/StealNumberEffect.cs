using UnityEngine;
using TMPro;

public class StealNumberEffect : MonoBehaviour
{
    public TextMeshProUGUI numberText;

    public void ShowNumber(int number)
    {
        gameObject.SetActive(true);
        numberText.gameObject.SetActive(true);
        numberText.enabled = true;
        numberText.text = number.ToString();
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvas.enabled = true;
        numberText.ForceMeshUpdate();
    }

    public void ShowPrisonStatus()
    {
        numberText.text = "IN PRISON";
        numberText.color = Color.white;
        numberText.fontSize = 14f;
        numberText.textWrappingMode = TextWrappingModes.NoWrap;
        numberText.overflowMode = TextOverflowModes.Overflow;
        numberText.alignment = TextAlignmentOptions.Center;

        RectTransform textRect = numberText.rectTransform;
        textRect.sizeDelta = new Vector2(95f, 24f);
        textRect.anchoredPosition = Vector2.zero;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 95;
        }
    }
}
