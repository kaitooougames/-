using System.Collections;
using UnityEngine;
using TMPro;

public class DiceEffectController : MonoBehaviour
{
    public Transform diceModel;                     // 3Dサイコロ
    public TextMeshProUGUI resultText;              // 出目表示用のUI
    public float rollDuration = 1f;                 // 転がる秒数
    public GameObject effectPrefab;                 // 出目後の演出エフェクト（optional）

    private bool isRolling = false;
    public bool IsRolling => isRolling;

    public void StartDiceRoll(int result)
    {
        if (!isRolling)
            StartCoroutine(RollDice(result));
    }

    private IEnumerator RollDice(int result)
    {
        isRolling = true;

        float timer = 0f;
        while (timer < rollDuration)
        {
            diceModel.Rotate(
                Random.Range(180, 360),
                Random.Range(180, 360),
                Random.Range(180, 360)
            );
            timer += Time.deltaTime;
            yield return null;
        }

        // サイコロの出目で静止
        SetDiceRotation(result);

        // 出目の表示とエフェクト
    

        isRolling = false;
    }

    private void SetDiceRotation(int number)
    {
        switch (number)
        {
            case 1: diceModel.rotation = Quaternion.Euler(-90, 0, 0); break;
            case 2: diceModel.rotation = Quaternion.Euler(0, 0, 0); break;
            case 3: diceModel.rotation = Quaternion.Euler(0, 0, -90); break;
            case 4: diceModel.rotation = Quaternion.Euler(0, 0, 90); break;
            case 5: diceModel.rotation = Quaternion.Euler(180, 0, 0); break;
            case 6: diceModel.rotation = Quaternion.Euler(90, 0, 0); break;
        }
    }

    private void ShowResult(int number)
    {
        if (resultText != null)
        {
            resultText.text = number.ToString();
            resultText.gameObject.SetActive(true);
        }

        if (effectPrefab != null)
        {
            Instantiate(effectPrefab, diceModel.position + new Vector3(0, 1, 0), Quaternion.identity);
        }
    }
}
