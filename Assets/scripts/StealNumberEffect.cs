using UnityEngine;
using TMPro;

public class StealNumberEffect : MonoBehaviour
{
    public TextMeshProUGUI numberText;

    public void ShowNumber(int number)
    {
        numberText.text = number.ToString();
    }
}
