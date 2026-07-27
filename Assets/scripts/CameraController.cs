using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class CameraController : MonoBehaviour
{
    public Vector3 firstTargetPosition = new Vector3(0, 3.2f, 0);
    public Quaternion firstTargetRotation = Quaternion.Euler(90, 0, 0);
    public Vector3 secondTargetPosition = new Vector3(0, 4, -3);
    public Quaternion secondTargetRotation = Quaternion.Euler(60, 0, 0);

    public float moveSpeed = 0.8f;
    private bool isCameraMoving = false;
    private bool playerDisplayViewActive;
    private Quaternion displayViewReturnRotation;
    public SecurityDice securityDice; // Unity Inspector で設定
    private List<Player> players; // プレイヤーリスト
    private bool isFlipping = false;
    public CardInteraction cardInteraction;
    public ArrestHandler arrestHandler;
    public HandManager handManager;
    public Player Player;
    public Player2 Player2;
    public Player3 Player3;
    public Player4 Player4;
    public ArrestEffect arrestEffect; // ← インスペクターでアタッチする
    public bool PlayerDisplayViewActive => playerDisplayViewActive;
    public bool IsCameraMoving => isCameraMoving;


    private void Start()
    {
        // **SecurityDice が未設定なら探す**
        if (securityDice == null)
        {
            securityDice = FindObjectOfType<SecurityDice>();
            if (securityDice == null)
                Debug.LogError("SecurityDice がシーン内に見つかりません！");
        }

        // **プレイヤーを自動取得**
        players = new List<Player>(FindObjectsOfType<Player>());
        if (players.Count == 0)
        {
            Debug.LogError("プレイヤーがシーン内に見つかりません！");
        }
    }
    public void MoveCamera()
    {
        if (isCameraMoving)
        {
            Debug.Log("MoveCamera() が呼ばれたが、カメラ移動中のため無視: " + Time.frameCount);
            return;
        }

        Debug.Log("MoveCamera() を実行: " + Time.frameCount);
        isCameraMoving = true;
        StartCoroutine(MoveCameraSequence());
    }


    private IEnumerator MoveCameraSequence()
    { 
        Debug.Log("MoveCameraSequence が開始された" + Time.frameCount);

        yield return StartCoroutine(MoveCameraCoroutine(firstTargetPosition, firstTargetRotation));

        yield return new WaitForSeconds(1f);

        FlipAllCards(); // 普通に呼び出す

        yield return new WaitForSeconds(2f);
        while (ArrestHandler.Instance != null && ArrestHandler.Instance.HasPendingFrameUpChoice)
            yield return null;
        yield return StartCoroutine(MoveCameraCoroutine(secondTargetPosition, secondTargetRotation));

        // 行動カードを確認してカメラが通常位置へ戻ってから、お宝の展示を始める。
        BeginTreasureDisplaysFromActionCards();
        yield return StartCoroutine(WaitForTreasureDisplays());

        yield return new WaitForSeconds(1f);
        TriggerSecurityDice();

        isCameraMoving = false;
    }

    public void SetPlayerDisplayView(bool active)
    {
        if (isCameraMoving || playerDisplayViewActive == active) return;
        if (active) displayViewReturnRotation = transform.rotation;
        StartCoroutine(MovePlayerDisplayView(active));
    }

    private IEnumerator MovePlayerDisplayView(bool active)
    {
        isCameraMoving = true;
        Quaternion startRotation = transform.rotation;
        Quaternion targetRotation = active
            ? Quaternion.Euler(73f, transform.rotation.eulerAngles.y, transform.rotation.eulerAngles.z)
            : displayViewReturnRotation;
        float duration = 0.55f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.rotation = targetRotation;
        playerDisplayViewActive = active;
        isCameraMoving = false;
    }

    private void BeginTreasureDisplaysFromActionCards()
    {
        // 展示選択中に行動カードが戻って見えないよう、基本は閉じた状態にする。
        if (handManager != null) handManager.SetActionHandVisible(false);
        TreasureGame.TreasureController treasureController =
            FindFirstObjectByType<TreasureGame.TreasureController>();
        if (treasureController == null || treasureController.Phase != TreasureGame.TreasurePhase.Waiting)
            return;

        var displayPlayers = new List<int>();
        var displayCounts = new List<int>();
        var displayTypeRestrictions = new List<int>();
        AddDisplayDeclaration(Player != null ? Player.SelectedCard : null,
            Player != null && Player.isEliminated, ToTreasurePlayerId(0), treasureController,
            displayPlayers, displayCounts, displayTypeRestrictions);
        if (Player2 != null && Player2.gameObject.activeInHierarchy && !Player2.isEliminated &&
            Player2.SelectedCard != null && Player2.SelectedCard.isExhibit)
        {
            displayPlayers.Add(ToTreasurePlayerId(1));
            AddDisplayCountAndRestriction(Player2.SelectedCard, ToTreasurePlayerId(1), treasureController,
                displayCounts, displayTypeRestrictions);
        }
        if (Player3 != null && Player3.gameObject.activeInHierarchy && !Player3.isEliminated &&
            Player3.SelectedCard != null && Player3.SelectedCard.isExhibit)
        {
            displayPlayers.Add(ToTreasurePlayerId(2));
            AddDisplayCountAndRestriction(Player3.SelectedCard, ToTreasurePlayerId(2), treasureController,
                displayCounts, displayTypeRestrictions);
        }
        if (Player4 != null && Player4.gameObject.activeInHierarchy && !Player4.isEliminated &&
            Player4.SelectedCard != null && Player4.SelectedCard.isExhibit)
        {
            displayPlayers.Add(ToTreasurePlayerId(3));
            AddDisplayCountAndRestriction(Player4.SelectedCard, ToTreasurePlayerId(3), treasureController,
                displayCounts, displayTypeRestrictions);
        }

        if (displayPlayers.Count == 0) return;

        treasureController.BeginDisplayPhase(displayPlayers.ToArray(), displayCounts.ToArray(),
            displayTypeRestrictions.ToArray());
        TreasureGame.Treasure[] treasures =
            FindObjectsByType<TreasureGame.Treasure>(FindObjectsSortMode.None);

        // Player1は手動選択。CPUプレイヤーは選べる手札から1枚を自動選択する。
        foreach (int playerId in displayPlayers)
        {
            if (playerId == ToTreasurePlayerId(0)) continue;
            int declarationIndex = displayPlayers.IndexOf(playerId);
            int remaining = declarationIndex >= 0 ? displayCounts[declarationIndex] : 1;
            foreach (TreasureGame.Treasure treasure in treasures)
            {
                if (treasure.Owner == null || treasure.Owner.PlayerId != playerId ||
                    !treasureController.CanInteract(treasure)) continue;
                treasureController.HandleTreasureClick(treasure);
                remaining--;
                if (remaining <= 0) break;
            }
        }
    }

    private static void AddDisplayDeclaration(CardInteraction card, bool eliminated, int playerId,
        TreasureGame.TreasureController treasureController,
        List<int> playerIds, List<int> counts, List<int> typeRestrictions)
    {
        if (eliminated || card == null || !card.isExhibit) return;
        playerIds.Add(playerId);
        AddDisplayCountAndRestriction(card, playerId, treasureController, counts, typeRestrictions);
    }

    private static void AddDisplayCountAndRestriction(CardInteraction card, int playerId,
        TreasureGame.TreasureController treasureController,
        List<int> counts, List<int> typeRestrictions)
    {
        if (card.specialEffect == SpecialActionEffect.EerieGuard)
        {
            int relics = treasureController.GetHandTypeCount(playerId, TreasureGame.TreasureType.Relic);
            if (relics >= 2)
            {
                counts.Add(2);
                // 1枚目は種類自由。遺物を選んだ場合だけ2枚目も遺物。
                typeRestrictions.Add(-2);
                return;
            }
        }
        counts.Add(card.DisplayCount);
        typeRestrictions.Add(-1);
    }

    private IEnumerator WaitForTreasureDisplays()
    {
        TreasureGame.TreasureController treasureController =
            FindFirstObjectByType<TreasureGame.TreasureController>();
        if (treasureController == null) yield break;

        SyncEliminatedPlayers(treasureController);

        while (treasureController.Phase == TreasureGame.TreasurePhase.SelectingDisplays ||
               treasureController.Phase == TreasureGame.TreasurePhase.Displaying)
            yield return null;
    }

    private IEnumerator MoveCameraCoroutine(Vector3 position, Quaternion rotation)
    {
        float elapsedTime = 0f;
        float duration = 1f / moveSpeed;

        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;

        while (elapsedTime < duration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsedTime / duration);
            transform.position = Vector3.Lerp(startPos, position, t);
            transform.rotation = Quaternion.Lerp(startRot, rotation, t);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.position = position;
        transform.rotation = rotation;
    }

    public void FlipAllCards()
    {
        if (isFlipping)
        {
            Debug.Log("FlipAllCards() がすでに実行中のためキャンセル: " + Time.frameCount);
            return; // すでに実行中なら処理を中断
        }

        isFlipping = true;
       
        Debug.Log("FlipAllCards() が呼ばれた" + Time.frameCount);
        CardInteraction[] allCards = FindObjectsOfType<CardInteraction>();
        CardInteraction.PrepareFlipCount(allCards.Length);
        foreach (var card in allCards)
        {
            Debug.Log($"カード {card.name} をめくる処理を実行");
            card.FlipCard();
        }
     
     
    }

    private void TriggerSecurityDice()
    {
        if (securityDice != null && players.Count > 0)
        {
            Debug.Log("サイコロ警備を発動！");
            securityDice.RollDice(players);
        }
        else
        {
            Debug.LogError("SecurityDice または players が設定されていません！");
        }

        StartCoroutine(WaitAndThiefPhase()); // 3秒待ってから怪盗フェーズ！
    }

    private IEnumerator WaitAndThiefPhase()
    {
        // 警備サイコロが停止して結果が確定するまで、怪盗は盗み始めない。
        DiceEffectController diceEffect = securityDice != null
            ? securityDice.diceEffectController
            : null;
        while (diceEffect != null && diceEffect.IsRolling)
            yield return null;
        // 出目が静止・表示された状態を少し見せてから逮捕判定へ進む。
        if (diceEffect != null)
            yield return new WaitForSeconds(0.35f);
        securityDice?.ResolvePendingDiceArrests();
        // 警備サイコロで濡れ衣が発動した場合も、移し替え先の選択完了を待つ。
        while (ArrestHandler.Instance != null && ArrestHandler.Instance.HasPendingFrameUpChoice)
            yield return null;

        // サイコロと檻のどちらかで逮捕された怪盗は、ここで除外される。
        yield return StartCoroutine(RunTreasureRobberiesFromActionCards());
        TreasureGame.TreasureController treasureController =
            FindFirstObjectByType<TreasureGame.TreasureController>();
        if (treasureController != null && treasureController.Phase == TreasureGame.TreasurePhase.GameOver)
        {
            if (handManager != null) handManager.EndGame();
            isFlipping = false;
            yield break;
        }
        kaitou();
    }

    private IEnumerator RunTreasureRobberiesFromActionCards()
    {
        TreasureGame.TreasureController treasureController =
            FindFirstObjectByType<TreasureGame.TreasureController>();
        if (treasureController == null) yield break;

        var robberPlayers = new List<int>();
        var robberyCounts = new List<int>();
        var robberyEffects = new List<int>();

        AddRobberyDeclaration(Player, ToTreasurePlayerId(0), Player != null ? Player.SelectedCard : null,
            Player != null ? Player.SelectedNumber : 0,
            Player != null && Player.HasBeenArrested, Player != null && Player.isEliminated,
            robberPlayers, robberyCounts, robberyEffects);
        if (Player2 != null && Player2.gameObject.activeInHierarchy)
            AddRobberyDeclaration(Player2, ToTreasurePlayerId(1), Player2.SelectedCard, Player2.SelectedNumber,
                Player2.HasBeenArrested, Player2.isEliminated, robberPlayers, robberyCounts, robberyEffects);
        if (Player3 != null && Player3.gameObject.activeInHierarchy)
            AddRobberyDeclaration(Player3, ToTreasurePlayerId(2), Player3.SelectedCard, Player3.SelectedNumber,
                Player3.HasBeenArrested, Player3.isEliminated, robberPlayers, robberyCounts, robberyEffects);
        if (Player4 != null && Player4.gameObject.activeInHierarchy)
            AddRobberyDeclaration(Player4, ToTreasurePlayerId(3), Player4.SelectedCard, Player4.SelectedNumber,
                Player4.HasBeenArrested, Player4.isEliminated, robberPlayers, robberyCounts, robberyEffects);

        Debug.Log($"<color=#FF9F70>【行動カード→怪盗】逮捕されていない怪盗 {robberPlayers.Count}人</color>");
        int[] rewardPlayers = DetermineSuccessfulCageRewardPlayers();
        treasureController.QueueArrestRewardDisplays(rewardPlayers);
        Debug.Log($"<color=#FFD966>【檻報酬連携】展示報酬 {rewardPlayers.Length}人</color>");
        treasureController.BeginRobberyPhase(robberPlayers.ToArray(), robberyCounts.ToArray(),
            robberyEffects.ToArray());

        while (treasureController.Phase != TreasureGame.TreasurePhase.Waiting &&
               treasureController.Phase != TreasureGame.TreasurePhase.GameOver)
        {
            if (treasureController.Phase == TreasureGame.TreasurePhase.Robbing &&
                treasureController.ActiveRobber != null &&
                treasureController.ActiveRobber.PlayerId != 0)
            {
                if (TrySelectCpuTreasure(treasureController))
                    yield return new WaitForSeconds(0.75f);
                else
                    yield return null;
            }
            else if (treasureController.Phase == TreasureGame.TreasurePhase.RobberDisplay)
            {
                if (TrySelectCpuTreasure(treasureController))
                    yield return new WaitForSeconds(0.75f);
                else
                    yield return null;
            }
            else if (treasureController.Phase == TreasureGame.TreasurePhase.SelectingDisplays)
            {
                // 檻の逮捕成功報酬。Player1は手動、CPUは1枚を自動選択する。
                if (TrySelectCpuTreasure(treasureController))
                    yield return new WaitForSeconds(0.75f);
                else
                    yield return null;
            }
            else
            {
                yield return null;
            }
        }

        // 檻報酬の宝展示が完了してから、特殊行動カードを引く。
        if (treasureController.Phase != TreasureGame.TreasurePhase.GameOver)
            SpecialActionCardSystem.GrantCageRewards(rewardPlayers, handManager);
    }

    private static void AddRobberyDeclaration(
        MonoBehaviour participant,
        int playerId,
        CardInteraction selectedCard,
        int declaredCount,
        bool arrested,
        bool eliminated,
        List<int> robberPlayers,
        List<int> robberyCounts,
        List<int> robberyEffects)
    {
        if (participant == null || !participant.gameObject.activeInHierarchy || arrested || eliminated ||
            selectedCard == null || !selectedCard.isPhantomThief)
            return;

        int count = Mathf.Clamp(declaredCount, 1, 6);
        robberPlayers.Add(playerId);
        robberyCounts.Add(count);
        robberyEffects.Add((int)selectedCard.specialEffect);
        Debug.Log($"【怪盗宣言】Player{playerId + 1}：{count}枚");
    }

    private int[] DetermineSuccessfulCageRewardPlayers()
    {
        var cagePlayers = new List<int>();
        int thiefCount = 0;
        CountActionForCageReward(ToTreasurePlayerId(0), Player != null ? Player.SelectedCard : null,
            Player != null && Player.isEliminated, cagePlayers, ref thiefCount);
        if (Player2 != null && Player2.gameObject.activeInHierarchy)
            CountActionForCageReward(ToTreasurePlayerId(1), Player2.SelectedCard, Player2.isEliminated, cagePlayers, ref thiefCount);
        if (Player3 != null && Player3.gameObject.activeInHierarchy)
            CountActionForCageReward(ToTreasurePlayerId(2), Player3.SelectedCard, Player3.isEliminated, cagePlayers, ref thiefCount);
        if (Player4 != null && Player4.gameObject.activeInHierarchy)
            CountActionForCageReward(ToTreasurePlayerId(3), Player4.SelectedCard, Player4.isEliminated, cagePlayers, ref thiefCount);

        bool cagesSucceeded = cagePlayers.Count > 0 && thiefCount >= cagePlayers.Count;
        Debug.Log($"【檻報酬判定】怪盗{thiefCount}人 / 檻{cagePlayers.Count}人 → " +
            (cagesSucceeded ? "逮捕成功" : "報酬なし"));
        return cagesSucceeded ? cagePlayers.ToArray() : System.Array.Empty<int>();
    }

    private static void CountActionForCageReward(int playerId, CardInteraction card, bool eliminated,
        List<int> cagePlayers, ref int thiefCount)
    {
        if (eliminated || card == null) return;
        if (card.isPhantomThief) thiefCount++;
        else if (card.isCage) cagePlayers.Add(playerId);
    }

    private void SyncEliminatedPlayers(TreasureGame.TreasureController treasureController)
    {
        if (Player != null && Player.isEliminated) treasureController.SetPlayerEliminated(ToTreasurePlayerId(0));
        if (Player2 != null && Player2.gameObject.activeInHierarchy && Player2.isEliminated)
            treasureController.SetPlayerEliminated(ToTreasurePlayerId(1));
        if (Player3 != null && Player3.gameObject.activeInHierarchy && Player3.isEliminated)
            treasureController.SetPlayerEliminated(ToTreasurePlayerId(2));
        if (Player4 != null && Player4.gameObject.activeInHierarchy && Player4.isEliminated)
            treasureController.SetPlayerEliminated(ToTreasurePlayerId(3));
    }

    private static bool TrySelectCpuTreasure(TreasureGame.TreasureController treasureController)
    {
        TreasureGame.Treasure[] treasures =
            FindObjectsByType<TreasureGame.Treasure>(FindObjectsSortMode.None);
        foreach (TreasureGame.Treasure treasure in treasures)
        {
            if (!treasureController.CanInteract(treasure)) continue;

            // 展示選択はPlayer1だけを手動のまま残す。
            if ((treasureController.Phase == TreasureGame.TreasurePhase.RobberDisplay ||
                 treasureController.Phase == TreasureGame.TreasurePhase.SelectingDisplays) &&
                treasure.Owner != null && treasure.Owner.PlayerId == 0)
                continue;

            treasureController.HandleTreasureClick(treasure);
            return true;
        }
        return false;
    }

    public void kaitou()
    {
        Debug.Log("怪盗フェーズ終了！");
        handManager.MoveCardsAfterThiefPhase();
       
        cardInteraction.MoveCardsAfterThiefPhase();
        Player.MoveCardsAfterThiefPhase();
        if (Player2 != null && Player2.gameObject.activeInHierarchy) Player2.MoveCardsAfterThiefPhase();
        if (Player3 != null && Player3.gameObject.activeInHierarchy) Player3.MoveCardsAfterThiefPhase();
        if (Player4 != null && Player4.gameObject.activeInHierarchy) Player4.MoveCardsAfterThiefPhase();
        arrestEffect.MoveCardsAfterThiefPhase();

        StartCoroutine(Wait());
       
    }
    private IEnumerator Wait()
    {
        TreasureGame.TreasureController treasureController =
            FindFirstObjectByType<TreasureGame.TreasureController>();
        if (treasureController != null && treasureController.Phase == TreasureGame.TreasurePhase.Waiting)
        {
            var eliminatedPlayers = new List<int>();
            if (Player != null && Player.isEliminated) eliminatedPlayers.Add(ToTreasurePlayerId(0));
            if (Player2 != null && Player2.gameObject.activeInHierarchy && Player2.isEliminated) eliminatedPlayers.Add(ToTreasurePlayerId(1));
            if (Player3 != null && Player3.gameObject.activeInHierarchy && Player3.isEliminated) eliminatedPlayers.Add(ToTreasurePlayerId(2));
            if (Player4 != null && Player4.gameObject.activeInHierarchy && Player4.isEliminated) eliminatedPlayers.Add(ToTreasurePlayerId(3));

            if (eliminatedPlayers.Count > 0)
            {
                foreach (int playerId in eliminatedPlayers)
                    treasureController.SetPlayerEliminated(playerId);
                treasureController.DisplayAllTreasures(eliminatedPlayers.ToArray());
                yield return null;
                while (treasureController.Phase == TreasureGame.TreasurePhase.Displaying ||
                       treasureController.Phase == TreasureGame.TreasurePhase.SelectingDisplays)
                    yield return null;
            }
        }

        // 行動カードの回収アニメーションが終わる分だけ待つ。
        yield return new WaitForSeconds(2f);
        cardInteraction.EnableCardClicks(); // カードクリック再開
        if (handManager != null) handManager.RefreshPlayerOneCardAvailability();
        isFlipping = false;
    }

    private int ToTreasurePlayerId(int actionSeatId)
    {
        return handManager != null ? handManager.ToTreasurePlayerId(actionSeatId) : actionSeatId;
    }
}
