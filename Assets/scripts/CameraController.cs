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
    private bool detectiveChoiceActive;
    private int detectiveChosenSeat = -1;
    private readonly List<int> detectiveTargetSeats = new List<int>();
    private int prisonRollSeat = -1;
    private string prisonRollMessage = "";
    private readonly Dictionary<int, StealNumberEffect> prisonFloorMarkers =
        new Dictionary<int, StealNumberEffect>();
    private string detectiveAnnouncement = "";
    private AudioSource detectiveAudioSource;
    private AudioClip detectiveWrongClip;
    private bool appraiserTypeChoiceActive;
    private int appraiserChosenType = -1;
    private bool appraiserConfirmActive;
    private bool appraiserConfirmed;
    private string appraiserMessage = "";
    private string appraiserButtonLabel = "把握OK";
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
        detectiveAudioSource = GetComponent<AudioSource>();
        if (detectiveAudioSource == null)
            detectiveAudioSource = gameObject.AddComponent<AudioSource>();
        detectiveWrongClip = CreateDetectiveWrongClip();
    }
    private void Update()
    {
        SyncPrisonFloorMarkers();
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

        yield return StartCoroutine(ResolveDetectivesBeforeReveal());
        FlipAllCards(); // 普通に呼び出す

        yield return new WaitForSeconds(2f);
        while (ArrestHandler.Instance != null && ArrestHandler.Instance.HasPendingFrameUpChoice)
            yield return null;
        yield return StartCoroutine(MoveCameraCoroutine(secondTargetPosition, secondTargetRotation));

        // 行動カードを確認してカメラが通常位置へ戻ってから、お宝の展示を始める。
        BeginTreasureDisplaysFromActionCards();
        yield return StartCoroutine(WaitForTreasureDisplays());

        yield return StartCoroutine(ResolveAppraisersBeforeSecurityDice());

        yield return new WaitForSeconds(1f);
        TriggerSecurityDice();

        isCameraMoving = false;
    }

    private IEnumerator ResolveDetectivesBeforeReveal()
    {
        List<int> detectiveSeats = new List<int>();
        Dictionary<int, int> choicesByDetective = new Dictionary<int, int>();

        // 名探偵は全員先に公開する。指名結果は全員が選び終わるまで判定しない。
        for (int seat = 0; seat < 4; seat++)
        {
            CardInteraction detectiveCard = GetSelectedActionCard(seat);
            if (!IsActionSeatActive(seat) || detectiveCard == null ||
                detectiveCard.specialEffect != SpecialActionEffect.Detective)
                continue;

            detectiveCard.RevealBeforeAllCards();
            detectiveSeats.Add(seat);
        }
        if (detectiveSeats.Count == 0) yield break;

        yield return new WaitForSeconds(0.8f);

        // 指名段階。前の名探偵の選択や成否は、後の名探偵には公開されない。
        foreach (int detectiveSeat in detectiveSeats)
        {
            detectiveTargetSeats.Clear();
            for (int targetSeat = 0; targetSeat < 4; targetSeat++)
                if (targetSeat != detectiveSeat && IsActionSeatActive(targetSeat) &&
                    !SpecialActionCardSystem.IsAdvanceNoticeActiveToday(targetSeat) &&
                    GetSelectedActionCard(targetSeat) != null)
                    detectiveTargetSeats.Add(targetSeat);
            if (detectiveTargetSeats.Count == 0) continue;

            int chosenSeat;
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            {
                int day = handManager != null ? handManager.CurrentDay : 1;
                int networkDetective =
                    KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(detectiveSeat);
                if (detectiveSeat == 0)
                {
                    detectiveChosenSeat = -1;
                    detectiveChoiceActive = true;
                    while (detectiveChosenSeat < 0) yield return null;
                    detectiveChoiceActive = false;
                    KaitouOnline.KaitouOnlineGameBridge.SubmitDetectiveChoice(
                        day, detectiveSeat, detectiveChosenSeat);
                }
                else if (KaitouOnline.KaitouOnlineGameBridge.IsHostCpuLocalSeat(
                             detectiveSeat))
                {
                    int cpuTarget = detectiveTargetSeats[
                        Random.Range(0, detectiveTargetSeats.Count)];
                    KaitouOnline.KaitouOnlineGameBridge.SubmitDetectiveChoice(
                        day, detectiveSeat, cpuTarget);
                }
                int networkTarget = -1;
                while (!KaitouOnline.KaitouOnlineGameBridge.TryGetDetectiveChoice(
                           day, networkDetective, out networkTarget))
                    yield return null;
                chosenSeat =
                    KaitouOnline.KaitouOnlineGameBridge.ToLocalSeat(networkTarget);
            }
            else if (detectiveSeat == 0)
            {
                detectiveChosenSeat = -1;
                detectiveChoiceActive = true;
                while (detectiveChosenSeat < 0) yield return null;
                detectiveChoiceActive = false;
                chosenSeat = detectiveChosenSeat;
            }
            else
            {
                chosenSeat = detectiveTargetSeats[Random.Range(0, detectiveTargetSeats.Count)];
            }
            choicesByDetective[detectiveSeat] = chosenSeat;
            Debug.Log($"【名探偵指名】Player{detectiveSeat + 1} → Player{chosenSeat + 1}");
        }

        // 正解した指名だけを、指名先ごとにまとめる。
        Dictionary<int, List<int>> successfulDetectivesByTarget =
            new Dictionary<int, List<int>>();
        foreach (KeyValuePair<int, int> choice in choicesByDetective)
        {
            CardInteraction targetCard = GetSelectedActionCard(choice.Value);
            detectiveAnnouncement =
                $"Player{choice.Key + 1}の名探偵 → Player{choice.Value + 1}を指名";
            if (targetCard != null)
            {
                yield return StartCoroutine(targetCard.BlinkAsDetectiveTarget());
            }
            if (targetCard == null || !targetCard.isPhantomThief)
            {
                Debug.Log($"【名探偵失敗】Player{choice.Key + 1}の指名先Player{choice.Value + 1}は怪盗ではありません。");
                PlayDetectiveWrongSound();
                yield return new WaitForSeconds(0.85f);
                continue;
            }

            targetCard.RevealBeforeAllCards();
            yield return new WaitForSeconds(0.55f);
            if (!successfulDetectivesByTarget.TryGetValue(
                    choice.Value, out List<int> successfulDetectives))
            {
                successfulDetectives = new List<int>();
                successfulDetectivesByTarget.Add(choice.Value, successfulDetectives);
            }
            successfulDetectives.Add(choice.Key);
        }

        // 一斉判定。怪盗の逮捕は1回、報酬は単独正解なら2枚、同じ怪盗への複数正解なら各1枚。
        List<int> successfulTargets = new List<int>(successfulDetectivesByTarget.Keys);
        successfulTargets.Sort((left, right) =>
            KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(left).CompareTo(
                KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(right)));
        foreach (int successfulTarget in successfulTargets)
        {
            List<int> successfulDetectives = successfulDetectivesByTarget[successfulTarget];
            successfulDetectives.Sort((left, right) =>
                KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(left).CompareTo(
                    KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(right)));
            CardInteraction targetCard = GetSelectedActionCard(successfulTarget);
            if (targetCard == null) continue;

            SpecialActionCardSystem.MarkDetectiveExcluded(targetCard);
            ArrestHandler.Instance?.ArrestFromExternalEffect(targetCard);

            int rewardCount = successfulDetectives.Count >= 2 ? 1 : 2;
            foreach (int detectiveSeat in successfulDetectives)
            {
                KaitouOnline.KaitouOnlineGameBridge.PrepareDetectiveRewardRandom(
                    handManager != null ? handManager.CurrentDay : 1,
                    detectiveSeat);
                SpecialActionCardSystem.GrantDetectiveReward(
                    detectiveSeat, rewardCount, handManager);
            }

            Debug.Log(
                $"【名探偵成功】Player{successfulTarget + 1}の怪盗を逮捕・当日除外。" +
                $"正解者{successfulDetectives.Count}人、各自の特殊カード報酬{rewardCount}枚");
        }
        if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
        {
            int checkpointDay = handManager != null ? handManager.CurrentDay : 1;
            KaitouOnline.KaitouOnlineGameBridge.BeginStateCheckpoint(
                checkpointDay, "detective");
            while (KaitouOnline.KaitouOnlineGameBridge.IsWaitingForStateCheckpoint(
                       checkpointDay, "detective"))
                yield return null;
        }
        detectiveAnnouncement = "まもなく全員の行動カードを公開します";
        yield return new WaitForSeconds(1.25f);
        detectiveAnnouncement = "";
    }

    private AudioClip CreateDetectiveWrongClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.48f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float time = i / (float)sampleRate;
            float frequency = time < 0.22f ? 185f : 125f;
            float envelope = Mathf.Clamp01((duration - time) * 7f);
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * time) * 0.34f * envelope;
        }
        AudioClip clip = AudioClip.Create(
            "DetectiveWrong", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void PlayDetectiveWrongSound()
    {
        if (detectiveAudioSource != null && detectiveWrongClip != null)
            detectiveAudioSource.PlayOneShot(detectiveWrongClip);
    }

    private CardInteraction GetSelectedActionCard(int seat)
    {
        if (seat == 0) return Player != null ? Player.SelectedCard : null;
        if (seat == 1) return Player2 != null ? Player2.SelectedCard : null;
        if (seat == 2) return Player3 != null ? Player3.SelectedCard : null;
        return Player4 != null ? Player4.SelectedCard : null;
    }

    private bool IsActionSeatActive(int seat)
    {
        if (SpecialActionCardSystem.CannotActToday(seat)) return false;
        if (seat == 0) return Player != null && !Player.isEliminated;
        if (seat == 1) return Player2 != null && Player2.gameObject.activeInHierarchy && !Player2.isEliminated;
        if (seat == 2) return Player3 != null && Player3.gameObject.activeInHierarchy && !Player3.isEliminated;
        return Player4 != null && Player4.gameObject.activeInHierarchy && !Player4.isEliminated;
    }

    private void OnGUI()
    {
        KaitouGuiFont.Apply();
        DrawPrisonRollMessage();
        DrawDetectiveAnnouncement();
        DrawAppraiserControls();
        DrawAdvanceNoticeMessage();
        if (!detectiveChoiceActive) return;
        GUIStyle boxStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
        };
        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
        };
        GUI.Box(new Rect(Screen.width * 0.5f - 380f, 28f, 760f, 105f),
            "名探偵：怪盗だと思うプレイヤーを指名してください", boxStyle);
        float width = 190f;
        float startX = Screen.width * 0.5f - detectiveTargetSeats.Count * width * 0.5f;
        for (int i = 0; i < detectiveTargetSeats.Count; i++)
        {
            int seat = detectiveTargetSeats[i];
            if (GUI.Button(new Rect(startX + i * width, 145f, width - 12f, 72f),
                    $"Player {seat + 1}", buttonStyle))
            {
                detectiveChosenSeat = seat;
                break;
            }
        }
    }

    private void DrawAdvanceNoticeMessage()
    {
        if (isFlipping || !SpecialActionCardSystem.HasActiveAdvanceNoticeToday()) return;
        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 27,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = new Color(1f, 0.78f, 0.72f);
        GUI.Box(new Rect(Screen.width * 0.5f - 430f, 25f, 860f, 82f),
            "予告状が公開されています。行動カード開示まで会話禁止です。", style);
    }

    private void DrawAppraiserControls()
    {
        if (!appraiserTypeChoiceActive && !appraiserConfirmActive &&
            string.IsNullOrEmpty(appraiserMessage)) return;
        GUIStyle boxStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
        };
        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 27, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
        };
        GUI.Box(new Rect(Screen.width * 0.5f - 430f, 25f, 860f, 92f),
            appraiserMessage, boxStyle);
        if (appraiserTypeChoiceActive)
        {
            string[] labels = { "遺物", "宝石", "絵画" };
            int[] values =
            {
                (int)TreasureGame.TreasureType.Relic,
                (int)TreasureGame.TreasureType.Jewel,
                (int)TreasureGame.TreasureType.Painting
            };
            float startX = Screen.width * 0.5f - 315f;
            for (int i = 0; i < labels.Length; i++)
                if (GUI.Button(new Rect(startX + i * 210f, 130f, 195f, 68f), labels[i], buttonStyle))
                    appraiserChosenType = values[i];
        }
        if (appraiserConfirmActive &&
            GUI.Button(new Rect(Screen.width * 0.5f - 150f, 130f, 300f, 68f),
                appraiserButtonLabel, buttonStyle))
            appraiserConfirmed = true;
    }

    private IEnumerator ResolveAppraisersBeforeSecurityDice()
    {
        TreasureGame.TreasureController treasureController =
            FindFirstObjectByType<TreasureGame.TreasureController>();
        if (treasureController == null) yield break;

        var appraiserChoices = new List<KeyValuePair<int, TreasureGame.TreasureType>>();
        for (int seat = 0; seat < 4; seat++)
        {
            CardInteraction card = GetSelectedActionCard(seat);
            if (!IsActionSeatActive(seat) || card == null ||
                card.specialEffect != SpecialActionEffect.Appraiser)
                continue;

            TreasureGame.TreasureType chosenType;
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            {
                int day = handManager != null ? handManager.CurrentDay : 1;
                int networkAppraiser =
                    KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(seat);
                if (seat == 0)
                {
                    appraiserChosenType = -1;
                    appraiserMessage = "鑑定士：公開する宝の種類を選んでください。";
                    appraiserTypeChoiceActive = true;
                    while (appraiserChosenType < 0) yield return null;
                    appraiserTypeChoiceActive = false;
                    KaitouOnline.KaitouOnlineGameBridge.SubmitAppraiserTypeChoice(
                        day, seat, appraiserChosenType);
                }
                else if (KaitouOnline.KaitouOnlineGameBridge.IsHostCpuLocalSeat(seat))
                {
                    TreasureGame.TreasureType[] cpuTypes =
                    {
                        TreasureGame.TreasureType.Relic,
                        TreasureGame.TreasureType.Jewel,
                        TreasureGame.TreasureType.Painting
                    };
                    KaitouOnline.KaitouOnlineGameBridge.SubmitAppraiserTypeChoice(
                        day, seat, (int)cpuTypes[Random.Range(0, cpuTypes.Length)]);
                }
                int typeValue = -1;
                while (!KaitouOnline.KaitouOnlineGameBridge.TryGetAppraiserTypeChoice(
                           day, networkAppraiser, out typeValue))
                    yield return null;
                chosenType = (TreasureGame.TreasureType)typeValue;
            }
            else if (seat == 0)
            {
                appraiserChosenType = -1;
                appraiserMessage = "鑑定士：公開する宝の種類を選んでください。";
                appraiserTypeChoiceActive = true;
                while (appraiserChosenType < 0) yield return null;
                appraiserTypeChoiceActive = false;
                chosenType = (TreasureGame.TreasureType)appraiserChosenType;
            }
            else
            {
                TreasureGame.TreasureType[] types =
                {
                    TreasureGame.TreasureType.Relic,
                    TreasureGame.TreasureType.Jewel,
                    TreasureGame.TreasureType.Painting
                };
                chosenType = types[Random.Range(0, types.Length)];
            }

            appraiserChoices.Add(
                new KeyValuePair<int, TreasureGame.TreasureType>(seat, chosenType));
        }

        if (appraiserChoices.Count == 0) yield break;

        // 全鑑定士の選択が終わってから、対象をまとめて一斉公開する。
        var revealedSet = new HashSet<TreasureGame.Treasure>();
        var choiceLabels = new List<string>();
        foreach (KeyValuePair<int, TreasureGame.TreasureType> choice in appraiserChoices)
        {
            int treasurePlayerId = ToTreasurePlayerId(choice.Key);
            foreach (TreasureGame.Treasure treasure in
                     treasureController.RevealDisplayedTypeExceptPlayer(
                         treasurePlayerId, choice.Value))
                revealedSet.Add(treasure);
            choiceLabels.Add($"P{choice.Key + 1}:{TreasureTypeLabel(choice.Value)}");
        }

        appraiserMessage = $"鑑定士を一斉公開中（{string.Join(" / ", choiceLabels)}）";
        appraiserButtonLabel = "把握OK";
        yield return new WaitForSeconds(0.8f);
        appraiserConfirmed = false;
        appraiserConfirmActive = true;
        while (!appraiserConfirmed) yield return null;
        appraiserConfirmActive = false;
        treasureController.HideRevealedTreasures(revealedSet);
        yield return new WaitForSeconds(0.55f);

        // 公開対象になった各プレイヤーが、公開された種類だけを秘密裏に並び替える。
        var rearrangeTypesByPlayer = new Dictionary<int, HashSet<TreasureGame.TreasureType>>();
        foreach (TreasureGame.Treasure treasure in revealedSet)
        {
            if (treasure == null || treasure.Owner == null) continue;
            int playerId = treasure.Owner.PlayerId;
            if (!rearrangeTypesByPlayer.TryGetValue(playerId, out HashSet<TreasureGame.TreasureType> types))
            {
                types = new HashSet<TreasureGame.TreasureType>();
                rearrangeTypesByPlayer.Add(playerId, types);
            }
            types.Add(treasure.Type);
        }
        // 全対象者の並び替えを同時に開始する。CPUは画面を動かさず内部順だけ決める。
        int cpuRearrangerCount = 0;
        foreach (KeyValuePair<int, HashSet<TreasureGame.TreasureType>> entry in rearrangeTypesByPlayer)
        {
            if (entry.Key == 0) continue;
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            {
                if (KaitouOnline.KaitouOnlineGameBridge.IsHostCpuTreasureSeat(entry.Key))
                {
                    treasureController.ShuffleAppraiserDisplay(entry.Key, entry.Value);
                    KaitouOnline.KaitouOnlineGameBridge.SubmitAppraiserOrder(
                        handManager != null ? handManager.CurrentDay : 1,
                        entry.Key,
                        treasureController.GetDisplayedTreasureNetworkOrder(entry.Key));
                }
                continue;
            }
            treasureController.ShuffleAppraiserDisplay(entry.Key, entry.Value);
            cpuRearrangerCount++;
        }

        bool playerOneRearranging = rearrangeTypesByPlayer.TryGetValue(
            0, out HashSet<TreasureGame.TreasureType> playerOneTypes) &&
            treasureController.BeginAppraiserRearrangement(0, playerOneTypes);
        bool playerOneDone = !playerOneRearranging;
        appraiserConfirmed = false;
        if (playerOneRearranging)
        {
            appraiserMessage = "鑑定士：同じ種類の宝を2枚ずつ選んで並び替えてください。";
            appraiserButtonLabel = "並び替え完了";
            appraiserConfirmActive = true;
        }

        float cpuFinishTime = Time.time + (cpuRearrangerCount > 0 ? 1.2f : 0f);
        bool onlineAppraiser =
            KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession;
        int appraiserDay = handManager != null ? handManager.CurrentDay : 1;
        bool localOrderSent = false;
        while (!playerOneDone || Time.time < cpuFinishTime ||
               onlineAppraiser && !AllOnlineAppraiserOrdersReady(
                   appraiserDay, rearrangeTypesByPlayer))
        {
            if (!playerOneDone && appraiserConfirmed)
            {
                playerOneDone = true;
                appraiserConfirmActive = false;
                treasureController.FinishAppraiserRearrangement();
                if (onlineAppraiser)
                {
                    KaitouOnline.KaitouOnlineGameBridge.SubmitAppraiserOrder(
                        appraiserDay, 0,
                        treasureController.GetDisplayedTreasureNetworkOrder(0));
                    localOrderSent = true;
                }
                if (Time.time < cpuFinishTime)
                    appraiserMessage = "ほかのPlayerが並び替え中です。";
            }
            else if (playerOneDone && (Time.time < cpuFinishTime ||
                     onlineAppraiser && !AllOnlineAppraiserOrdersReady(
                         appraiserDay, rearrangeTypesByPlayer)))
            {
                appraiserMessage = "ほかのPlayerが並び替え中です。";
            }
            if (onlineAppraiser && playerOneDone && !localOrderSent &&
                rearrangeTypesByPlayer.ContainsKey(0))
            {
                KaitouOnline.KaitouOnlineGameBridge.SubmitAppraiserOrder(
                    appraiserDay, 0,
                    treasureController.GetDisplayedTreasureNetworkOrder(0));
                localOrderSent = true;
            }
            yield return null;
        }
        if (onlineAppraiser)
        {
            foreach (int localPlayerId in rearrangeTypesByPlayer.Keys)
            {
                int networkSeat =
                    KaitouOnline.KaitouOnlineGameBridge.ToNetworkTreasureSeat(localPlayerId);
                if (KaitouOnline.KaitouOnlineGameBridge.TryGetAppraiserOrder(
                        appraiserDay, networkSeat, out int[] order))
                    treasureController.ApplyOnlineAppraiserOrder(
                        networkSeat, order);
            }
        }
        appraiserConfirmActive = false;
        appraiserMessage = "全員の並び替えが完了しました。再展示します。";
        yield return StartCoroutine(treasureController.RedisplayAppraisedTreasures(revealedSet));
        appraiserMessage = "";
    }

    private static bool AllOnlineAppraiserOrdersReady(int day,
        Dictionary<int, HashSet<TreasureGame.TreasureType>> rearrangeTypesByPlayer)
    {
        foreach (int localPlayerId in rearrangeTypesByPlayer.Keys)
        {
            int networkSeat =
                KaitouOnline.KaitouOnlineGameBridge.ToNetworkTreasureSeat(localPlayerId);
            if (!KaitouOnline.KaitouOnlineGameBridge.TryGetAppraiserOrder(
                    day, networkSeat, out _))
                return false;
        }
        return true;
    }

    private static string TreasureTypeLabel(TreasureGame.TreasureType type)
    {
        if (type == TreasureGame.TreasureType.Relic) return "遺物";
        if (type == TreasureGame.TreasureType.Jewel) return "宝石";
        return "絵画";
    }

    private void DrawDetectiveAnnouncement()
    {
        if (string.IsNullOrEmpty(detectiveAnnouncement)) return;
        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = Color.white;
        GUI.Box(new Rect(Screen.width * 0.5f - 390f, 28f, 780f, 88f),
            detectiveAnnouncement, style);
    }

    private void DrawPrisonRollMessage()
    {
        if (prisonRollSeat < 0) return;
        GUIStyle messageStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 30,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        messageStyle.normal.textColor = Color.white;
        GUI.Box(new Rect(Screen.width * 0.5f - 390f, 28f, 780f, 90f),
            prisonRollMessage, messageStyle);
    }

    private void SyncPrisonFloorMarkers()
    {
        Vector3[] markerPositions =
        {
            new Vector3(0.7f, 0.025f, -1.17f),
            new Vector3(-0.7f, 0.025f, 1.17f),
            new Vector3(-1.8f, 0.025f, 0f),
            new Vector3(1.8f, 0.025f, 0f)
        };

        for (int seat = 0; seat < markerPositions.Length; seat++)
        {
            bool shouldShow = SpecialActionCardSystem.IsImprisoned(seat) &&
                              IsActionSeatConfigured(seat);
            bool hasMarker = prisonFloorMarkers.TryGetValue(
                seat, out StealNumberEffect marker) && marker != null;
            if (!shouldShow)
            {
                if (hasMarker) Destroy(marker.gameObject);
                prisonFloorMarkers.Remove(seat);
                continue;
            }
            if (hasMarker) continue;

            StealNumberEffect template = GetNumberEffectPrefab(seat);
            if (template == null) continue;
            marker = Instantiate(template, markerPositions[seat], Quaternion.identity);
            marker.ShowPrisonStatus();
            prisonFloorMarkers[seat] = marker;
        }
    }

    private StealNumberEffect GetNumberEffectPrefab(int seat)
    {
        if (seat == 0) return Player != null ? Player.effectPrefab : null;
        if (seat == 1) return Player2 != null ? Player2.effectPrefab : null;
        if (seat == 2) return Player3 != null ? Player3.effectPrefab : null;
        return Player4 != null ? Player4.effectPrefab : null;
    }

    private bool IsActionSeatConfigured(int seat)
    {
        if (seat == 0) return Player != null;
        if (seat == 1) return Player2 != null && Player2.gameObject.activeInHierarchy;
        if (seat == 2) return Player3 != null && Player3.gameObject.activeInHierarchy;
        return Player4 != null && Player4.gameObject.activeInHierarchy;
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

        // オンラインでは各人が自分の画面のPlayer1を選び、相手の選択は通信で届く。
        // ローカル対戦時だけPlayer2以降をCPUとして自動選択する。
        if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession) return;

        // Player1は手動選択。CPUプレイヤーは選べる手札から自動選択する。
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

        float nextCpuChoiceTime = 0f;
        while (treasureController.Phase == TreasureGame.TreasurePhase.SelectingDisplays ||
               treasureController.Phase == TreasureGame.TreasurePhase.Displaying)
        {
            if (Time.time >= nextCpuChoiceTime &&
                KaitouOnline.KaitouOnlineGameBridge.TryDriveHostCpuTreasureChoice(
                    treasureController))
                nextCpuChoiceTime = Time.time + 0.55f;
            yield return null;
        }
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
        var selectedCards = new List<CardInteraction>();
        for (int seat = 0; seat < 4; seat++)
        {
            CardInteraction selected = GetSelectedActionCard(seat);
            bool isHeldAdvanceNotice = selected != null &&
                selected.specialEffect == SpecialActionEffect.AdvanceNotice &&
                SpecialActionCardSystem.IsAdvanceNoticePendingCard(seat, selected) &&
                SpecialActionCardSystem.IsAdvanceNoticeActiveToday(seat);
            if (isHeldAdvanceNotice) continue;
            if (selected != null && IsActionSeatConfigured(seat) &&
                !selectedCards.Contains(selected))
                selectedCards.Add(selected);
        }
        if (selectedCards.Count == 0)
        {
            Debug.LogWarning("公開する行動カードがありません。");
            isFlipping = false;
            return;
        }

        CardInteraction.PrepareFlipCount(selectedCards.Count);
        foreach (CardInteraction card in selectedCards)
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
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            {
                int day = handManager != null ? handManager.CurrentDay : 1;
                KaitouOnline.KaitouOnlineGameBridge.BeginSecurityDice(
                    securityDice, players, day);
            }
            else
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
        if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
        {
            int day = handManager != null ? handManager.CurrentDay : 1;
            while (KaitouOnline.KaitouOnlineGameBridge.IsWaitingForSecurityDice(day))
                yield return null;
        }
        // 警備サイコロが停止して結果が確定するまで、怪盗は盗み始めない。
        DiceEffectController diceEffect = securityDice != null
            ? securityDice.diceEffectController
            : null;
        while (diceEffect != null && diceEffect.IsRolling)
            yield return null;
        // 出目が静止・表示された状態を少し見せてから逮捕判定へ進む。
        if (diceEffect != null)
            yield return new WaitForSeconds(0.35f);
        bool hasPendingDiceArrest = securityDice != null &&
                                    securityDice.PendingDiceArrestCount > 0;
        securityDice?.ResolvePendingDiceArrests();
        // 警備サイコロで濡れ衣が発動した場合も、移し替え先の選択完了を待つ。
        bool frameUpChoiceShown = ArrestHandler.Instance != null &&
                                  ArrestHandler.Instance.HasPendingFrameUpChoice;
        while (ArrestHandler.Instance != null && ArrestHandler.Instance.HasPendingFrameUpChoice)
            yield return null;
        // 移し替え先の逮捕エフェクトを確認してから、手札回収・次ターン処理へ進む。
        if (frameUpChoiceShown)
            yield return new WaitForSeconds(1.25f);
        else if (hasPendingDiceArrest)
            // 少しためて表示する逮捕文字と落下演出を確認してから怪盗処理へ進む。
            yield return new WaitForSeconds(0.85f);

        if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
        {
            int day = handManager != null ? handManager.CurrentDay : 1;
            KaitouOnline.KaitouOnlineGameBridge.SynchronizeArrestResolution(day);
            while (KaitouOnline.KaitouOnlineGameBridge.IsWaitingForArrestResolution(day))
                yield return null;
        }

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

        AddRobberyDeclaration(0, Player, ToTreasurePlayerId(0), Player != null ? Player.SelectedCard : null,
            Player != null ? Player.SelectedNumber : 0,
            Player != null && Player.HasBeenArrested, Player != null && Player.isEliminated,
            robberPlayers, robberyCounts, robberyEffects);
        if (Player2 != null && Player2.gameObject.activeInHierarchy)
            AddRobberyDeclaration(1, Player2, ToTreasurePlayerId(1), Player2.SelectedCard, Player2.SelectedNumber,
                Player2.HasBeenArrested, Player2.isEliminated, robberPlayers, robberyCounts, robberyEffects);
        if (Player3 != null && Player3.gameObject.activeInHierarchy)
            AddRobberyDeclaration(2, Player3, ToTreasurePlayerId(2), Player3.SelectedCard, Player3.SelectedNumber,
                Player3.HasBeenArrested, Player3.isEliminated, robberPlayers, robberyCounts, robberyEffects);
        if (Player4 != null && Player4.gameObject.activeInHierarchy)
            AddRobberyDeclaration(3, Player4, ToTreasurePlayerId(3), Player4.SelectedCard, Player4.SelectedNumber,
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
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            {
                if (KaitouOnline.KaitouOnlineGameBridge.TryDriveHostCpuTreasureChoice(
                        treasureController))
                    yield return new WaitForSeconds(0.55f);
                else
                yield return null;
                continue;
            }
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
        int actionSeat,
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
        if (selectedCard.specialEffect == SpecialActionEffect.AdvanceNotice &&
            !SpecialActionCardSystem.IsAdvanceNoticeActiveToday(actionSeat)) return;

        int count = selectedCard.specialEffect == SpecialActionEffect.AdvanceNotice
            ? 10 : Mathf.Clamp(declaredCount, 1, 6);
        robberPlayers.Add(playerId);
        robberyCounts.Add(count);
        robberyEffects.Add((int)selectedCard.specialEffect);
        Debug.Log($"【怪盗宣言】Player{playerId + 1}：{count}枚");
    }

    private int[] DetermineSuccessfulCageRewardPlayers()
    {
        if (ArrestHandler.Instance != null)
        {
            int[] seats = ArrestHandler.Instance.GetSuccessfulCagePlayerIds();
            int[] result = new int[seats.Length];
            for (int i = 0; i < seats.Length; i++)
                result[i] = ToTreasurePlayerId(seats[i]);
            return result;
        }

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
        StartCoroutine(FinishActionTurn());
    }

    private IEnumerator FinishActionTurn()
    {
        yield return StartCoroutine(RollPrisonReleaseDice());
        if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
        {
            int cleanupDay = handManager != null ? handManager.CurrentDay : 1;
            KaitouOnline.KaitouOnlineGameBridge.BeginTurnCleanup(cleanupDay);
            while (KaitouOnline.KaitouOnlineGameBridge.IsWaitingForTurnCleanup(cleanupDay))
                yield return null;
        }
        handManager.MoveCardsAfterThiefPhase();

        cardInteraction.MoveCardsAfterThiefPhase();
        int penaltyDay = handManager != null ? handManager.CurrentDay - 1 : 1;
        KaitouOnline.KaitouOnlineGameBridge.PreparePenaltyRandom(penaltyDay, 0);
        Player.MoveCardsAfterThiefPhase();
        if (Player2 != null && Player2.gameObject.activeInHierarchy)
        {
            KaitouOnline.KaitouOnlineGameBridge.PreparePenaltyRandom(penaltyDay, 1);
            Player2.MoveCardsAfterThiefPhase();
        }
        if (Player3 != null && Player3.gameObject.activeInHierarchy)
        {
            KaitouOnline.KaitouOnlineGameBridge.PreparePenaltyRandom(penaltyDay, 2);
            Player3.MoveCardsAfterThiefPhase();
        }
        if (Player4 != null && Player4.gameObject.activeInHierarchy)
        {
            KaitouOnline.KaitouOnlineGameBridge.PreparePenaltyRandom(penaltyDay, 3);
            Player4.MoveCardsAfterThiefPhase();
        }
        ReevaluateActionCardEliminations();
        if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
        {
            int token = 100000 +
                (handManager != null ? handManager.CurrentDay : 1);
            KaitouOnline.KaitouOnlineGameBridge.SynchronizeArrestResolution(token);
            while (KaitouOnline.KaitouOnlineGameBridge
                       .IsWaitingForArrestResolution(token))
                yield return null;
        }
        arrestEffect.MoveCardsAfterThiefPhase();

        StartCoroutine(Wait());

    }

    private void ReevaluateActionCardEliminations()
    {
        if (Player != null &&
            SpecialActionCardSystem.IsEliminatedByNormalCards(Player.playerCards))
            Player.isEliminated = true;
        if (Player2 != null && Player2.gameObject.activeInHierarchy &&
            SpecialActionCardSystem.IsEliminatedByNormalCards(Player2.player2Cards))
            Player2.isEliminated = true;
        if (Player3 != null && Player3.gameObject.activeInHierarchy &&
            SpecialActionCardSystem.IsEliminatedByNormalCards(Player3.player3Cards))
            Player3.isEliminated = true;
        if (Player4 != null && Player4.gameObject.activeInHierarchy &&
            SpecialActionCardSystem.IsEliminatedByNormalCards(Player4.player4Cards))
            Player4.isEliminated = true;
    }

    private IEnumerator RollPrisonReleaseDice()
    {
        if (handManager == null) yield break;
        List<int> prisoners =
            SpecialActionCardSystem.GetPrisonersEligibleForRelease(handManager.CurrentDay);
        if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            prisoners.Sort((a, b) =>
                KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(a).CompareTo(
                    KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(b)));
        DiceEffectController dice = securityDice != null
            ? securityDice.diceEffectController
            : null;

        foreach (int seat in prisoners)
        {
            int result;
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            {
                int networkSeat = KaitouOnline.KaitouOnlineGameBridge.ToNetworkSeat(seat);
                int token = 300000 + handManager.CurrentDay * 10 + networkSeat;
                KaitouOnline.KaitouOnlineGameBridge.BeginPrisonRoll(token);
                while (!KaitouOnline.KaitouOnlineGameBridge.TryGetPrisonRoll(
                           token, out result))
                    yield return null;
            }
            else result = Random.Range(1, 7);
            prisonRollSeat = seat;
            prisonRollMessage =
                $"Player{seat + 1}が監獄の釈放サイコロを振っています";
            Debug.Log($"<color=#BFA8FF>【監獄】Player{seat + 1}が釈放サイコロを振ります。</color>");
            if (dice != null)
            {
                while (dice.IsRolling) yield return null;
                dice.StartDiceRoll(result);
                // StartCoroutine内でIsRollingが立つ次フレームまで待つ。
                yield return null;
                while (dice.IsRolling) yield return null;
                yield return new WaitForSeconds(0.65f);
            }
            bool released =
                SpecialActionCardSystem.ResolvePrisonReleaseRoll(seat, result);
            if (released)
                ClearFirstOffenseRestriction(seat);
            prisonRollMessage = released
                ? $"Player{seat + 1}：{result}が出たため脱獄成功しました"
                : $"Player{seat + 1}：{result}が出たため脱獄失敗しました";
            yield return new WaitForSeconds(1.8f);
            prisonRollSeat = -1;
            prisonRollMessage = "";
            yield return new WaitForSeconds(0.65f);
        }
        prisonRollSeat = -1;
        prisonRollMessage = "";
    }

    private static void ClearFirstOffenseRestriction(int seat)
    {
        switch (seat)
        {
            case 0:
                Object.FindFirstObjectByType<Player>()?.ClearFirstOffenseRestriction();
                break;
            case 1:
                Object.FindFirstObjectByType<Player2>()?.ClearFirstOffenseRestriction();
                break;
            case 2:
                Object.FindFirstObjectByType<Player3>()?.ClearFirstOffenseRestriction();
                break;
            case 3:
                Object.FindFirstObjectByType<Player4>()?.ClearFirstOffenseRestriction();
                break;
        }
    }

    private IEnumerator Wait()
    {
        TreasureGame.TreasureController treasureController =
            FindFirstObjectByType<TreasureGame.TreasureController>();
        if (treasureController != null)
        {
            while (treasureController.Phase != TreasureGame.TreasurePhase.Waiting &&
                   treasureController.Phase != TreasureGame.TreasurePhase.GameOver)
                yield return null;
            if (treasureController.Phase == TreasureGame.TreasurePhase.GameOver)
                yield break;

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

                if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
                {
                    int token = 200000 +
                        (handManager != null ? handManager.CurrentDay : 1);
                    KaitouOnline.VictoryResolutionState state =
                        KaitouOnline.KaitouOnlineSession.Instance != null &&
                        KaitouOnline.KaitouOnlineSession.Instance.IsHost
                            ? treasureController.BuildOnlineVictoryResolution(token)
                            : new KaitouOnline.VictoryResolutionState { day = token };
                    KaitouOnline.KaitouOnlineGameBridge.BeginVictoryResolution(state);
                    while (KaitouOnline.KaitouOnlineGameBridge
                               .IsWaitingForVictoryResolution(token))
                        yield return null;
                }
                else
                    treasureController.EvaluateVictoryNow();

                if (treasureController.Phase == TreasureGame.TreasurePhase.GameOver)
                {
                    handManager?.EndGame();
                    yield break;
                }
            }
        }

        // 行動カードの回収アニメーションが終わる分だけ待つ。
        yield return new WaitForSeconds(2f);
        cardInteraction.EnableCardClicks(); // カードクリック再開
        if (handManager != null) handManager.RefreshPlayerOneCardAvailability();
        if (SpecialActionCardSystem.TryGetActiveAdvanceNotice(0, out CardInteraction advanceNotice))
        {
            Player.RestoreAdvanceNotice(advanceNotice);
            handManager?.ForceAdvanceNoticeSelection(advanceNotice);
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            {
                KaitouOnline.KaitouOnlineGameBridge.SubmitLocalAction(
                    advanceNotice, 10);
                Debug.Log("<color=#FF7A7A>【予告状実行日】固定した予告状を同期し、相手の選択を待ちます。</color>");
                isFlipping = false;
                yield break;
            }
            Player2?.SelectRandomCard();
            Player3?.SelectRandomCard();
            Player4?.SelectRandomCard();
            Debug.Log("<color=#FF7A7A>【予告状実行日】Player1は予告状のまま怪盗を開始します。</color>");
            MoveCamera();
            isFlipping = false;
            yield break;
        }
        if (SpecialActionCardSystem.CannotActToday(0))
        {
            Debug.Log("<color=#BFA8FF>【行動休止】Player1はこの日の行動を休みます。</color>");
            if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
            {
                KaitouOnline.KaitouOnlineGameBridge.SubmitLocalPass();
                isFlipping = false;
                yield break;
            }
            Player2?.SelectRandomCard();
            Player3?.SelectRandomCard();
            Player4?.SelectRandomCard();
            MoveCamera();
        }
        isFlipping = false;
    }

    private int ToTreasurePlayerId(int actionSeatId)
    {
        return handManager != null ? handManager.ToTreasurePlayerId(actionSeatId) : actionSeatId;
    }
}
