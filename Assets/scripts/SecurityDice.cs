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
        player = FindFirstObjectByType<Player>(FindObjectsInactive.Include);
        player2 = FindFirstObjectByType<Player2>(FindObjectsInactive.Include);
        player3 = FindFirstObjectByType<Player3>(FindObjectsInactive.Include);
        player4 = FindFirstObjectByType<Player4>(FindObjectsInactive.Include);
    }


    // 逮捕されていない怪盗がいるかをチェックするメソッド
    private bool HasUnarrestedPhantomThief(List<Player> players)
    {
        foreach (var player in players)
        {
            Debug.Log($"[DEBUG] {player.name} - 選択カード: {player.SelectedCard?.GetType().Name}, isPhantomThief: {player.SelectedCard?.isPhantomThief}, HasBeenArrested: {player.HasBeenArrested}");
            if (IsParticipating(player2))
                Debug.Log($"[DEBUG] {player2.name} - 選択カード: {player2.SelectedCard?.GetType().Name}, isPhantomThief: {player2.SelectedCard?.isPhantomThief}, HasBeenArrested: {player2.HasBeenArrested}");
            if (IsParticipating(player3))
                Debug.Log($"[DEBUG] {player3.name} - 選択カード: {player3.SelectedCard?.GetType().Name}, isPhantomThief: {player3.SelectedCard?.isPhantomThief}, HasBeenArrested: {player3.HasBeenArrested}");
            if (IsParticipating(player4))
                Debug.Log($"[DEBUG] {player4.name} - 選択カード: {player4.SelectedCard?.GetType().Name}, isPhantomThief: {player4.SelectedCard?.isPhantomThief}, HasBeenArrested: {player4.HasBeenArrested}");

            if (!player.isEliminated && player.SelectedCard != null && player.SelectedCard.isPhantomThief && !player.HasBeenArrested)
            {
                return true; // 逮捕されていない怪盗がいる
            }

            if (IsParticipating(player2) && !player2.isEliminated && player2.SelectedCard != null && player2.SelectedCard.isPhantomThief && !player2.HasBeenArrested)
            {
                return true; // 逮捕されていない怪盗がいる
            }

            if (IsParticipating(player3) && !player3.isEliminated && player3.SelectedCard != null && player3.SelectedCard.isPhantomThief && !player3.HasBeenArrested)
            {
                return true; // 逮捕されていない怪盗がいる
            }

            if (IsParticipating(player4) && !player4.isEliminated && player4.SelectedCard != null && player4.SelectedCard.isPhantomThief && !player4.HasBeenArrested)
            {
                return true; // 逮捕されていない怪盗がいる
            }


        }
          return false; // すべて逮捕済み
    }

    private static bool IsParticipating(MonoBehaviour participant)
    {
        return participant != null && participant.gameObject.activeInHierarchy;
    }


    public void RollDice(List<Player> players)
    {
        bool fakeCopPlayed = HasSelectedEffect(SpecialActionEffect.FakeCop);
        // 逮捕されていない怪盗がいるかどうかを確認
        if (!HasUnarrestedPhantomThief(players) && !fakeCopPlayed)
        {
            Debug.Log("すべての怪盗が逮捕されました。警備サイコロを振りません。");
            return; // 逮捕されていない怪盗がいなければサイコロを振らない
        }

        rolledNumber = Random.Range(diceMin, diceMax + 1);
        // エフェクト開始！
        diceEffectController.StartDiceRoll(rolledNumber);
        Debug.Log($"警備サイコロの目: {rolledNumber}");

        // 偽警官が出ていれば怪盗がいなくても振り、1で使用者自身が逮捕される。
        if (fakeCopPlayed && rolledNumber == 1)
            ArrestFakeCopPlayers();

        // プレイヤーごとに処理を行う
        foreach (var player in players)
        {
            if (player.SelectedCard != null && player.SelectedCard.isPhantomThief && player.HasBeenArrested==false)
            {
                int chosenNumber = player.SelectedCard.SelectedNumber;
                Debug.Log($"{player.name} の怪盗が選んだ数: {chosenNumber}");

                bool wireBeltSafe = player.SelectedCard.specialEffect == SpecialActionEffect.WireBelt &&
                                    rolledNumber <= 2;
                if (chosenNumber > rolledNumber && !wireBeltSafe)
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
            if (IsParticipating(player2) && player2.SelectedCard != null && player2.SelectedCard.isPhantomThief && player2.HasBeenArrested == false)
            {
                int chosenNumber = player2.SelectedCard.SelectedNumber;
                Debug.Log($"{player2.name} の怪盗が選んだ数: {chosenNumber}");

                bool wireBeltSafe = player2.SelectedCard.specialEffect == SpecialActionEffect.WireBelt &&
                                    rolledNumber <= 2;
                if (chosenNumber > rolledNumber && !wireBeltSafe)
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
            if (IsParticipating(player3) && player3.SelectedCard != null && player3.SelectedCard.isPhantomThief && player3.HasBeenArrested == false)
            {
                int chosenNumber = player3.SelectedCard.SelectedNumber;
                Debug.Log($"{player3.name} の怪盗が選んだ数: {chosenNumber}");

                bool wireBeltSafe = player3.SelectedCard.specialEffect == SpecialActionEffect.WireBelt &&
                                    rolledNumber <= 2;
                if (chosenNumber > rolledNumber && !wireBeltSafe)
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
            if (IsParticipating(player4) && player4.SelectedCard != null && player4.SelectedCard.isPhantomThief && player4.HasBeenArrested == false)
            {
                int chosenNumber = player4.SelectedCard.SelectedNumber;
                Debug.Log($"{player4.name} の怪盗が選んだ数: {chosenNumber}");

                bool wireBeltSafe = player4.SelectedCard.specialEffect == SpecialActionEffect.WireBelt &&
                                    rolledNumber <= 2;
                if (chosenNumber > rolledNumber && !wireBeltSafe)
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

    private bool HasSelectedEffect(SpecialActionEffect effect)
    {
        return (player != null && !player.isEliminated && player.SelectedCard != null &&
                player.SelectedCard.specialEffect == effect) ||
               (IsParticipating(player2) && !player2.isEliminated && player2.SelectedCard != null &&
                player2.SelectedCard.specialEffect == effect) ||
               (IsParticipating(player3) && !player3.isEliminated && player3.SelectedCard != null &&
                player3.SelectedCard.specialEffect == effect) ||
               (IsParticipating(player4) && !player4.isEliminated && player4.SelectedCard != null &&
                player4.SelectedCard.specialEffect == effect);
    }

    private void ArrestFakeCopPlayers()
    {
        Debug.Log("【偽警官】警備サイコロが1のため、偽警官の使用者を逮捕！");
        if (player != null && !player.isEliminated && !player.HasBeenArrested &&
            player.SelectedCard != null && player.SelectedCard.specialEffect == SpecialActionEffect.FakeCop)
            player.ShowArrestEffect();
        if (IsParticipating(player2) && !player2.isEliminated && !player2.HasBeenArrested &&
            player2.SelectedCard != null && player2.SelectedCard.specialEffect == SpecialActionEffect.FakeCop)
            player2.ShowArrestEffect();
        if (IsParticipating(player3) && !player3.isEliminated && !player3.HasBeenArrested &&
            player3.SelectedCard != null && player3.SelectedCard.specialEffect == SpecialActionEffect.FakeCop)
            player3.ShowArrestEffect();
        if (IsParticipating(player4) && !player4.isEliminated && !player4.HasBeenArrested &&
            player4.SelectedCard != null && player4.SelectedCard.specialEffect == SpecialActionEffect.FakeCop)
            player4.ShowArrestEffect();
    }
}
