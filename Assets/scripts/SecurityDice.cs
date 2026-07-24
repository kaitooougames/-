using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class SecurityDice : MonoBehaviour
{
    public int diceMin = 1; // サイコロの最小値
    public int diceMax = 6; // サイコロの最大値
    private int rolledNumber; // 出目
    public DiceEffectController diceEffectController;

    private Player player;
    private Player2 player2;
    private Player3 player3;
    private Player4 player4;

    private void Awake()
    {
        // Awake で FindObjectOfType を呼び出す
        player = FindObjectOfType<Player>();
        player2 = FindObjectOfType<Player2>();
        player3 = FindObjectOfType<Player3>();
        player4 = FindObjectOfType<Player4>();
    }


    // 逮捕されていない怪盗がいるかをチェックするメソッド
    private bool HasUnarrestedPhantomThief(List<Player> players)
    {
        foreach (var player in players)
        {
            Debug.Log($"[DEBUG] {player.name} - 選択カード: {player.SelectedCard?.GetType().Name}, isPhantomThief: {player.SelectedCard?.isPhantomThief}, HasBeenArrested: {player.HasBeenArrested}");
            Debug.Log($"[DEBUG] {player2.name} - 選択カード: {player2.SelectedCard?.GetType().Name}, isPhantomThief: {player2.SelectedCard?.isPhantomThief}, HasBeenArrested: {player2.HasBeenArrested}");
            Debug.Log($"[DEBUG] {player3.name} - 選択カード: {player3.SelectedCard?.GetType().Name}, isPhantomThief: {player3.SelectedCard?.isPhantomThief}, HasBeenArrested: {player3.HasBeenArrested}");
            Debug.Log($"[DEBUG] {player4.name} - 選択カード: {player4.SelectedCard?.GetType().Name}, isPhantomThief: {player4.SelectedCard?.isPhantomThief}, HasBeenArrested: {player4.HasBeenArrested}");

            if (player.SelectedCard != null && player.SelectedCard.isPhantomThief && !player.HasBeenArrested)
            {
                return true; // 逮捕されていない怪盗がいる
            }

            if (player2.SelectedCard != null && player2.SelectedCard.isPhantomThief && !player2.HasBeenArrested)
            {
                return true; // 逮捕されていない怪盗がいる
            }

            if (player3.SelectedCard != null && player3.SelectedCard.isPhantomThief && !player3.HasBeenArrested)
            {
                return true; // 逮捕されていない怪盗がいる
            }

            if (player4.SelectedCard != null && player4.SelectedCard.isPhantomThief && !player4.HasBeenArrested)
            {
                return true; // 逮捕されていない怪盗がいる
            }

           
        }
          return false; // すべて逮捕済み
    }


    public void RollDice(List<Player> players)
    {
        // 逮捕されていない怪盗がいるかどうかを確認
        if (!HasUnarrestedPhantomThief(players))
        {
            Debug.Log("すべての怪盗が逮捕されました。警備サイコロを振りません。");
            return; // 逮捕されていない怪盗がいなければサイコロを振らない
        }

        rolledNumber = Random.Range(diceMin, diceMax + 1);
        // エフェクト開始！
        diceEffectController.StartDiceRoll(rolledNumber);
        Debug.Log($"警備サイコロの目: {rolledNumber}");

        // プレイヤーごとに処理を行う
        foreach (var player in players)
        {
            if (player.SelectedCard != null && player.SelectedCard.isPhantomThief && player.HasBeenArrested==false)
            {
                int chosenNumber = player.SelectedCard.SelectedNumber;
                Debug.Log($"{player.name} の怪盗が選んだ数: {chosenNumber}");

                if (chosenNumber > rolledNumber)
                {
                    Debug.Log($"{player.name} の怪盗が逮捕されました！");
                    player.ShowArrestEffect(); // ペナルティを適用
                }
                else
                {
                    Debug.Log($"{player.name} の怪盗は逮捕されませんでした！");
                }
            }

            // Player2の処理
            Player2 player2 = FindObjectOfType<Player2>();
            if (player2?.SelectedCard != null && player2.SelectedCard.isPhantomThief && player2.HasBeenArrested == false)
            {
                int chosenNumber = player2.SelectedCard.SelectedNumber;
                Debug.Log($"{player2.name} の怪盗が選んだ数: {chosenNumber}");

                if (chosenNumber > rolledNumber)
                {
                    Debug.Log($"{player2.name} の怪盗が逮捕されました！");
                    player2.ShowArrestEffect(); // ペナルティを適用
                }
                else
                {
                    Debug.Log($"{player2.name} の怪盗は逮捕されませんでした！");
                }
            }

            // Player3の処理
            Player3 player3 = FindObjectOfType<Player3>();
            if (player3?.SelectedCard != null && player3.SelectedCard.isPhantomThief && player3.HasBeenArrested == false)
            {
                int chosenNumber = player3.SelectedCard.SelectedNumber;
                Debug.Log($"{player3.name} の怪盗が選んだ数: {chosenNumber}");

                if (chosenNumber > rolledNumber)
                {
                    Debug.Log($"{player3.name} の怪盗が逮捕されました！");
                    player3.ShowArrestEffect(); // ペナルティを適用
                }
                else
                {
                    Debug.Log($"{player3.name} の怪盗は逮捕されませんでした！");
                }
            }

            // Player4の処理
            Player4 player4 = FindObjectOfType<Player4>();
            if (player4?.SelectedCard != null && player4.SelectedCard.isPhantomThief && player4.HasBeenArrested == false)
            {
                int chosenNumber = player4.SelectedCard.SelectedNumber;
                Debug.Log($"{player4.name} の怪盗が選んだ数: {chosenNumber}");

                if (chosenNumber > rolledNumber)
                {
                    Debug.Log($"{player4.name} の怪盗が逮捕されました！");
                    player4.ShowArrestEffect(); // ペナルティを適用
                }
                else
                {
                    Debug.Log($"{player4.name} の怪盗は逮捕されませんでした！");
                }
            }
        }
    }
}
