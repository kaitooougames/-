using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KaitouOnline
{
    // まず行動カード選択をホスト権威で同期する。
    // 宝・逮捕・サイコロなども同じBridgeへ順番に追加する。
    public sealed class KaitouOnlineGameBridge : MonoBehaviour
    {
        public static KaitouOnlineGameBridge Instance { get; private set; }
        public static bool IsActive => Instance != null && Instance.session != null &&
                                       Instance.session.IsOnline;
        public static bool IsOnlineSession =>
            KaitouOnlineSession.Instance != null &&
            (KaitouOnlineSession.Instance.IsOnline ||
             KaitouOnlineSession.Instance.HasStartedOnlineGame);

        private readonly Dictionary<int, ActionCardChoice> hostChoices =
            new Dictionary<int, ActionCardChoice>();
        private KaitouOnlineSession session;
        // boolだと前日の開示終了が遅れた端末で、翌日のSnapshotまで破棄してしまう。
        // 開示済みかどうかは日ごとに管理する。
        private int revealStartedDay = -1;
        public static string WaitingMessage { get; private set; } = "";
        public static string PriorityMessage { get; private set; } = "";
        private readonly Dictionary<int, int> securityDiceResults =
            new Dictionary<int, int>();
        private readonly HashSet<int> securityDiceBroadcastDays = new HashSet<int>();
        private readonly HashSet<int> securityDiceRequestedDays = new HashSet<int>();
        private readonly HashSet<int> securityDiceAppliedDays = new HashSet<int>();
        private readonly List<TreasureDisplayChoice> displayChoices =
            new List<TreasureDisplayChoice>();
        private readonly List<TreasureStealChoice> stealChoices =
            new List<TreasureStealChoice>();
        private readonly List<TreasureDisplayChoice> robberDisplayChoices =
            new List<TreasureDisplayChoice>();
        private readonly List<TreasureStealChoice> analysisChoices =
            new List<TreasureStealChoice>();
        private readonly HashSet<int> arrestResolutionBroadcastDays = new HashSet<int>();
        private readonly HashSet<int> arrestResolutionAppliedDays = new HashSet<int>();
        private readonly Dictionary<int, int> penaltySeeds =
            new Dictionary<int, int>();
        private readonly HashSet<int> victoryResolutionAppliedDays = new HashSet<int>();
        private readonly HashSet<int> victoryResolutionRequestedDays = new HashSet<int>();
        private readonly Dictionary<int, VictoryResolutionState> victoryStates =
            new Dictionary<int, VictoryResolutionState>();
        private readonly Dictionary<string, int> detectiveChoices =
            new Dictionary<string, int>();
        private readonly Dictionary<string, int> appraiserChoices =
            new Dictionary<string, int>();
        private readonly Dictionary<string, int[]> appraiserOrders =
            new Dictionary<string, int[]>();
        private readonly Dictionary<string, int> frameUpChoices =
            new Dictionary<string, int>();
        private readonly HashSet<int> turnCleanupReleasedDays = new HashSet<int>();
        private readonly Dictionary<string, Dictionary<int, string>> checkpointSignatures =
            new Dictionary<string, Dictionary<int, string>>();
        private readonly HashSet<string> completedCheckpoints = new HashSet<string>();
        private bool desyncAbortStarted;

        private void Awake()
        {
            Instance = this;
            WaitingMessage = "";
            PriorityMessage = "";
            session = KaitouOnlineSession.Instance;
            if (session != null) session.MessageReceived += OnMessage;
        }

        private void OnDestroy()
        {
            if (session != null) session.MessageReceived -= OnMessage;
            if (Instance == this) Instance = null;
        }

        public static void MarkConnectionLost(string message)
        {
            WaitingMessage = string.IsNullOrEmpty(message)
                ? "オンライン接続が切れました。ゲームを停止しています。"
                : message;
            PriorityMessage = WaitingMessage;
            Debug.LogError("【オンライン進行停止】" + WaitingMessage);
        }

        public static void BeginStateCheckpoint(int day, string checkpoint)
        {
            if (!IsOnlineSession || Instance == null) return;
            string signature = Instance.BuildActionStateSignature();
            Instance.session.SendAction(new ActionRequest
            {
                action = "state_checkpoint",
                actorSeat = Instance.session.LocalSeat,
                data = Protocol.Json(new StateCheckpoint
                {
                    day = day,
                    checkpoint = checkpoint,
                    actorSeat = Instance.session.LocalSeat,
                    signature = signature
                })
            });
        }

        public static bool IsWaitingForStateCheckpoint(int day, string checkpoint) =>
            IsOnlineSession && Instance != null &&
            !Instance.completedCheckpoints.Contains(CheckpointKey(day, checkpoint));

        public static bool SubmitLocalAction(CardInteraction card, int declaredNumber)
        {
            if (!IsOnlineSession || card == null) return false;
            EnsureInstance();
            if (!IsActive)
            {
                Debug.LogError("【オンライン】同期役を復元できないため、ローカル進行を停止しました。");
                return true;
            }
            Instance.Submit(card, declaredNumber);
            WaitingMessage = "相手が行動カードを選ぶのを待っています。";
            return true;
        }

        public static void BeginSecurityDice(SecurityDice dice,
            List<Player> players, int day)
        {
            if (!IsOnlineSession || dice == null) return;
            EnsureInstance();
            Instance.BeginSecurityDiceInternal(dice, players, day);
        }

        public static bool IsWaitingForSecurityDice(int day) =>
            IsOnlineSession && (Instance == null ||
                                !Instance.securityDiceAppliedDays.Contains(day));

        public static void PrepareNextActionDay()
        {
            if (!IsOnlineSession) return;
            EnsureInstance();
            Instance.hostChoices.Clear();
            WaitingMessage = "";
            Debug.Log("<color=#70E8FF>【オンライン】次の日の行動選択待ちへ移行</color>");
        }

        public static void BeginTurnCleanup(int day)
        {
            if (!IsOnlineSession) return;
            EnsureInstance();
            if (Instance.session.IsHost &&
                !Instance.turnCleanupReleasedDays.Contains(day))
            {
                OnlineSeatChoice state = new OnlineSeatChoice { day = day };
                Instance.session.Send(MessageType.TurnCleanup, -1, Protocol.Json(state));
            }
        }

        public static bool IsWaitingForTurnCleanup(int day) =>
            IsOnlineSession && (Instance == null ||
                !Instance.turnCleanupReleasedDays.Contains(day));

        public static void SubmitDisplayTreasure(TreasureGame.Treasure treasure)
        {
            if (!IsOnlineSession || treasure == null) return;
            EnsureInstance();
            int day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1;
            ActionRequest request = new ActionRequest
            {
                action = "select_display_treasure",
                actorSeat = Instance.session.LocalSeat,
                cardId = treasure.NetworkId,
                targetCardId = -1,
                number = 0,
                data = ""
            };
            Instance.session.SendAction(request);
            Debug.Log($"<color=#70E8FF>【オンライン展示選択送信】" +
                      $"P{Instance.session.LocalSeat + 1} 宝ID:{treasure.NetworkId}</color>");
        }

        public static void ReplayDisplayChoices()
        {
            if (!IsOnlineSession || Instance == null) return;
            TreasureGame.TreasureController controller =
                Object.FindFirstObjectByType<TreasureGame.TreasureController>();
            if (controller == null) return;
            int day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1;
            // 最後の選択を適用するとResolveDisplaysがキューをClearする。
            // 元Listを直接列挙するとゲストだけCollection modifiedで進行が止まるため、
            // 受信時点のスナップショットを再生する。
            var replayChoices = new List<TreasureDisplayChoice>(
                Instance.displayChoices);
            foreach (TreasureDisplayChoice choice in replayChoices)
                if (choice.day == day)
                    controller.ApplyOnlineDisplayChoice(choice.treasureId);
        }

        public static void ClearDisplayChoiceQueue()
        {
            if (Instance != null) Instance.displayChoices.Clear();
        }

        public static void ClearStealChoiceQueue()
        {
            if (Instance != null) Instance.stealChoices.Clear();
        }

        public static void ClearRobberDisplayChoiceQueue()
        {
            if (Instance != null) Instance.robberDisplayChoices.Clear();
        }

        // オンラインCPUの宝操作はホストだけが決定し、通常の通信経路へ流す。
        // 各端末が個別にCPUを動かすと展示完了時刻と回収処理がずれるため、
        // CPUも人間と同じ「選択メッセージ」を全端末へ配信する。
        public static bool TryDriveHostCpuTreasureChoice(
            TreasureGame.TreasureController controller)
        {
            if (!IsOnlineSession || Instance == null || controller == null ||
                !Instance.session.IsHost || Instance.session.CpuPlayers <= 0)
                return false;

            int firstCpuSeat = Instance.session.ConfirmedHumanPlayers;
            TreasureGame.Treasure[] treasures =
                Object.FindObjectsByType<TreasureGame.Treasure>(FindObjectsSortMode.None);

            if (controller.Phase == TreasureGame.TreasurePhase.SelectingDisplays)
            {
                foreach (TreasureGame.Treasure treasure in treasures)
                {
                    if (treasure == null || treasure.NetworkOwnerSeat < firstCpuSeat ||
                        !controller.CanInteract(treasure)) continue;
                    Instance.BroadcastCpuDisplayChoice(treasure);
                    return true;
                }
            }
            else if (controller.Phase == TreasureGame.TreasurePhase.Robbing &&
                     controller.ActiveRobber != null)
            {
                int robberSeat = ToNetworkTreasureSeat(controller.ActiveRobber.PlayerId);
                if (robberSeat < firstCpuSeat) return false;
                foreach (TreasureGame.Treasure treasure in treasures)
                {
                    if (!controller.CanOnlineCpuSteal(treasure)) continue;
                    Instance.BroadcastCpuStealChoice(robberSeat, treasure);
                    return true;
                }
            }
            else if (controller.Phase == TreasureGame.TreasurePhase.Inspecting &&
                     controller.ActiveRobber != null)
            {
                int robberSeat = ToNetworkTreasureSeat(controller.ActiveRobber.PlayerId);
                if (robberSeat < firstCpuSeat) return false;
                foreach (TreasureGame.Treasure treasure in treasures)
                {
                    if (!controller.CanOnlineCpuAnalyze(treasure)) continue;
                    Instance.BroadcastCpuAnalysisChoice(robberSeat, treasure);
                    return true;
                }
            }
            else if (controller.Phase == TreasureGame.TreasurePhase.RobberDisplay)
            {
                foreach (TreasureGame.Treasure treasure in treasures)
                {
                    if (treasure == null || treasure.NetworkOwnerSeat < firstCpuSeat ||
                        !controller.CanInteract(treasure)) continue;
                    Instance.BroadcastCpuRobberDisplayChoice(treasure);
                    return true;
                }
            }
            return false;
        }

        private void BroadcastCpuDisplayChoice(TreasureGame.Treasure treasure)
        {
            TreasureDisplayChoice choice = new TreasureDisplayChoice
            {
                day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1,
                actorSeat = treasure.NetworkOwnerSeat,
                treasureId = treasure.NetworkId
            };
            session.Send(MessageType.TreasureDisplayChoice, -1, Protocol.Json(choice));
        }

        private void BroadcastCpuStealChoice(int robberSeat, TreasureGame.Treasure treasure)
        {
            TreasureStealChoice choice = new TreasureStealChoice
            {
                day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1,
                actorSeat = robberSeat,
                treasureId = treasure.NetworkId
            };
            session.Send(MessageType.TreasureStealChoice, -1, Protocol.Json(choice));
        }

        private void BroadcastCpuRobberDisplayChoice(TreasureGame.Treasure treasure)
        {
            TreasureDisplayChoice choice = new TreasureDisplayChoice
            {
                day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1,
                actorSeat = treasure.NetworkOwnerSeat,
                treasureId = treasure.NetworkId
            };
            session.Send(MessageType.TreasureRobberDisplayChoice, -1, Protocol.Json(choice));
        }

        private void BroadcastCpuAnalysisChoice(int robberSeat, TreasureGame.Treasure treasure)
        {
            TreasureStealChoice choice = new TreasureStealChoice
            {
                day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1,
                actorSeat = robberSeat,
                treasureId = treasure.NetworkId
            };
            session.Send(MessageType.AnalysisTreasureChoice, -1, Protocol.Json(choice));
        }

        public static void SubmitStealTreasure(TreasureGame.Treasure treasure)
        {
            if (!IsOnlineSession || treasure == null) return;
            EnsureInstance();
            ActionRequest request = new ActionRequest
            {
                action = "steal_treasure",
                actorSeat = Instance.session.LocalSeat,
                cardId = treasure.NetworkId,
                targetCardId = -1,
                number = 0,
                data = ""
            };
            Instance.session.SendAction(request);
            Debug.Log($"<color=#70E8FF>【オンライン怪盗選択送信】" +
                      $"P{Instance.session.LocalSeat + 1} 宝ID:{treasure.NetworkId}</color>");
        }

        public static void SubmitRobberDisplayTreasure(TreasureGame.Treasure treasure)
        {
            if (!IsOnlineSession || treasure == null) return;
            EnsureInstance();
            ActionRequest request = new ActionRequest
            {
                action = "display_stolen_treasure",
                actorSeat = Instance.session.LocalSeat,
                cardId = treasure.NetworkId,
                targetCardId = -1,
                number = 0,
                data = ""
            };
            Instance.session.SendAction(request);
            Debug.Log($"<color=#70E8FF>【オンライン怪盗展示送信】" +
                      $"P{Instance.session.LocalSeat + 1} 宝ID:{treasure.NetworkId}</color>");
        }

        public static void SubmitAnalysisTreasure(TreasureGame.Treasure treasure)
        {
            if (!IsOnlineSession || treasure == null) return;
            EnsureInstance();
            Instance.session.SendAction(new ActionRequest
            {
                action = "analyze_treasure",
                actorSeat = Instance.session.LocalSeat,
                cardId = treasure.NetworkId
            });
        }

        public static void ReplayTreasureActionChoices()
        {
            if (!IsOnlineSession || Instance == null) return;
            TreasureGame.TreasureController controller =
                Object.FindFirstObjectByType<TreasureGame.TreasureController>();
            if (controller == null) return;
            int day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1;
            var replaySteals = new List<TreasureStealChoice>(Instance.stealChoices);
            var replayDisplays = new List<TreasureDisplayChoice>(
                Instance.robberDisplayChoices);
            var replayAnalysis = new List<TreasureStealChoice>(
                Instance.analysisChoices);
            foreach (TreasureStealChoice choice in replaySteals)
                if (choice.day == day)
                    controller.ApplyOnlineStealChoice(
                        choice.actorSeat, choice.treasureId);
            foreach (TreasureDisplayChoice choice in replayDisplays)
                if (choice.day == day)
                    controller.ApplyOnlineRobberDisplayChoice(
                        choice.actorSeat, choice.treasureId);
            foreach (TreasureStealChoice choice in replayAnalysis)
                if (choice.day == day)
                    controller.ApplyOnlineAnalysisChoice(
                        choice.actorSeat, choice.treasureId);
        }

        public static void SynchronizeArrestResolution(int day)
        {
            if (!IsOnlineSession) return;
            EnsureInstance();
            if (!Instance.session.IsHost ||
                !Instance.arrestResolutionBroadcastDays.Add(day)) return;
            ArrestResolutionState state = Instance.CaptureArrestResolution(day);
            Instance.session.Send(MessageType.ArrestResolutionState, -1,
                Protocol.Json(state));
            Debug.Log($"<color=#70E8FF>【オンライン逮捕結果送信】{day}日目</color>");
        }

        public static bool IsWaitingForArrestResolution(int day) =>
            IsOnlineSession && (Instance == null ||
                                !Instance.arrestResolutionAppliedDays.Contains(day));

        public static void PreparePenaltyRandom(int day, int localPlayerIndex)
        {
            if (!IsOnlineSession || Instance == null ||
                !Instance.penaltySeeds.TryGetValue(day, out int seed)) return;
            int networkSeat = Instance.NetworkSeatForLocalIndex(localPlayerIndex);
            Random.InitState(unchecked(seed + networkSeat * 7919));
        }

        public static void PrepareCageRewardRandom(int day, int localPlayerIndex)
        {
            if (!IsOnlineSession || Instance == null) return;
            int networkSeat = Instance.NetworkSeatForLocalIndex(localPlayerIndex);
            Random.InitState(unchecked(Instance.session.GameSeed ^
                (day * 104729) ^ (networkSeat * 15485863)));
        }

        public static void PrepareInitialSpecialRandom(int localPlayerIndex)
        {
            if (!IsOnlineSession || Instance == null) return;
            int networkSeat = Instance.NetworkSeatForLocalIndex(localPlayerIndex);
            Random.InitState(unchecked(Instance.session.GameSeed ^
                0x2F6E2B1 ^ (networkSeat * 32452843)));
        }

        public static void PrepareDetectiveRewardRandom(
            int day, int localPlayerIndex)
        {
            if (!IsOnlineSession || Instance == null) return;
            int networkSeat = Instance.NetworkSeatForLocalIndex(localPlayerIndex);
            Random.InitState(unchecked(Instance.session.GameSeed ^
                (day * 49999) ^ (networkSeat * 67867967)));
        }

        public static void BeginVictoryResolution(VictoryResolutionState state)
        {
            if (!IsOnlineSession || Instance == null) return;
            Instance.victoryResolutionRequestedDays.Add(state.day);
            if (Instance.victoryStates.TryGetValue(state.day, out VictoryResolutionState cached))
            {
                Instance.ApplyVictoryResolution(cached);
                return;
            }
            if (Instance.session.IsHost)
                Instance.session.Send(MessageType.VictoryResolutionState, -1,
                    Protocol.Json(state));
        }

        public static bool IsWaitingForVictoryResolution(int day) =>
            IsOnlineSession && (Instance == null ||
                                !Instance.victoryResolutionAppliedDays.Contains(day));

        public static void RequestRestartGame()
        {
            if (!IsOnlineSession || Instance == null || !Instance.session.IsHost) return;
            StartGameState state = new StartGameState
            {
                sceneName = "IntegratedGameScene",
                playerCount = Instance.session.RoomPlayerCount,
                humanPlayerCount = Instance.session.ConfirmedHumanPlayers,
                cpuPlayers = Instance.session.CpuPlayers,
                randomSeed = Random.Range(1, int.MaxValue)
            };
            Instance.session.Send(MessageType.RestartGame, -1, Protocol.Json(state));
        }

        public static void RequestGrantAllSpecialCards()
        {
            if (!IsOnlineSession || Instance == null) return;
            ActionRequest request = new ActionRequest
            {
                action = "test_grant_all_special",
                actorSeat = Instance.session.LocalSeat
            };
            Instance.session.SendAction(request);
        }

        public static void NotifyHoneyTrapInspection(
            int localVictimSeat, bool active)
        {
            if (!IsOnlineSession || Instance == null) return;
            HoneyTrapStatus status = new HoneyTrapStatus
            {
                active = active,
                robberSeat = Instance.session.LocalSeat,
                victimSeat = Instance.NetworkSeatForLocalIndex(localVictimSeat)
            };
            ActionRequest request = new ActionRequest
            {
                action = "honey_trap_status",
                actorSeat = Instance.session.LocalSeat,
                data = Protocol.Json(status)
            };
            Instance.session.SendAction(request);
        }

        public static int ToNetworkSeat(int localIndex) =>
            Instance != null ? Instance.NetworkSeatForLocalIndex(localIndex) : localIndex;

        public static string PlayerNameForNetworkSeat(int networkSeat)
        {
            KaitouOnlineSession activeSession = KaitouOnlineSession.Instance;
            return activeSession != null
                ? activeSession.GetPlayerName(networkSeat)
                : $"Player{networkSeat + 1}";
        }

        public static string PlayerNameForLocalSeat(int localSeat) =>
            PlayerNameForNetworkSeat(ToNetworkSeat(localSeat));

        public static int ToLocalSeat(int networkSeat) =>
            Instance != null ? Instance.LocalIndexForNetworkSeat(networkSeat) : networkSeat;

        // 宝Controllerは3人時も内部Playerを0,1,2の連続番号で保持する。
        // 行動カードの物理席（0,2,3）とは分けて変換する。
        public static int ToLocalTreasureSeat(int networkSeat) =>
            Instance != null ? Instance.NetworkSeatOrder(networkSeat) : networkSeat;

        public static int ToNetworkTreasureSeat(int localTreasureSeat) =>
            Instance != null ? Instance.NetworkSeatAtOrder(localTreasureSeat) : localTreasureSeat;

        public static bool IsHostCpuLocalSeat(int localSeat)
        {
            if (!IsOnlineSession || Instance == null || !Instance.session.IsHost) return false;
            int networkSeat = Instance.NetworkSeatForLocalIndex(localSeat);
            return networkSeat >= Instance.session.ConfirmedHumanPlayers &&
                   networkSeat < Instance.session.RoomPlayerCount;
        }

        public static bool IsHostCpuTreasureSeat(int localTreasureSeat)
        {
            if (!IsOnlineSession || Instance == null || !Instance.session.IsHost) return false;
            int networkSeat = Instance.NetworkSeatAtOrder(localTreasureSeat);
            return networkSeat >= Instance.session.ConfirmedHumanPlayers &&
                   networkSeat < Instance.session.RoomPlayerCount;
        }

        public static bool ValidateSeatMappings()
        {
            if (!IsOnlineSession || Instance == null) return true;
            var actionSeats = new HashSet<int>();
            var treasureSeats = new HashSet<int>();
            for (int networkSeat = 0;
                 networkSeat < Instance.session.RoomPlayerCount; networkSeat++)
            {
                int actionSeat = Instance.LocalIndexForNetworkSeat(networkSeat);
                int treasureSeat = Instance.NetworkSeatOrder(networkSeat);
                if (actionSeat < 0 || treasureSeat < 0 ||
                    !actionSeats.Add(actionSeat) || !treasureSeats.Add(treasureSeat) ||
                    Instance.NetworkSeatForLocalIndex(actionSeat) != networkSeat ||
                    Instance.NetworkSeatAtOrder(treasureSeat) != networkSeat)
                {
                    Debug.LogError($"【オンライン席変換エラー】network={networkSeat} " +
                                   $"action={actionSeat} treasure={treasureSeat}");
                    return false;
                }
            }
            Debug.Log($"<color=#70E8FF>【オンライン席検査OK】" +
                      $"local=P{Instance.session.LocalSeat + 1} players={Instance.session.RoomPlayerCount} " +
                      $"action=[{string.Join(",", actionSeats)}] " +
                      $"treasure=[{string.Join(",", treasureSeats)}]</color>");
            return true;
        }

        public static void SubmitDetectiveChoice(int day,
            int localDetectiveSeat, int localTargetSeat)
        {
            SubmitOnlineSeatChoice("detective_choice", day,
                ToNetworkSeat(localDetectiveSeat), ToNetworkSeat(localTargetSeat));
        }

        public static void SubmitFrameUpChoice(int day,
            int localSourceSeat, int localTargetSeat)
        {
            int source = ToNetworkSeat(localSourceSeat);
            int target = ToNetworkSeat(localTargetSeat);
            if (Instance != null && Instance.session.IsHost)
            {
                OnlineSeatChoice choice = new OnlineSeatChoice
                { day = day, actorSeat = source, value = target };
                Instance.frameUpChoices[ChoiceKey(day, source)] = target;
                Instance.session.Send(MessageType.FrameUpChoice, -1,
                    Protocol.Json(choice));
                return;
            }
            SubmitOnlineSeatChoice("frame_up_choice", day, source, target);
        }

        public static bool TryGetFrameUpChoice(int day,
            int networkSourceSeat, out int networkTargetSeat)
        {
            networkTargetSeat = -1;
            return Instance != null && Instance.frameUpChoices.TryGetValue(
                ChoiceKey(day, networkSourceSeat), out networkTargetSeat);
        }

        public static bool SubmitLocalPass()
        {
            if (!IsOnlineSession) return false;
            EnsureInstance();
            if (!IsActive) return true;
            Instance.SubmitPass();
            WaitingMessage = "相手が行動カードを選ぶのを待っています。";
            return true;
        }

        public static void BeginPrisonRoll(int token)
        {
            if (!IsOnlineSession || Instance == null || !Instance.session.IsHost ||
                Instance.securityDiceResults.ContainsKey(token)) return;
            int result = Random.Range(1, 7);
            Instance.securityDiceResults[token] = result;
            Instance.session.Send(MessageType.SecurityDiceState, -1,
                Protocol.Json(new SecurityDiceState { day = token, result = result }));
        }

        public static bool TryGetPrisonRoll(int token, out int result)
        {
            result = 0;
            return Instance != null &&
                   Instance.securityDiceResults.TryGetValue(token, out result);
        }

        public static bool TryGetDetectiveChoice(int day,
            int networkDetectiveSeat, out int networkTargetSeat)
        {
            networkTargetSeat = -1;
            return Instance != null && Instance.detectiveChoices.TryGetValue(
                ChoiceKey(day, networkDetectiveSeat), out networkTargetSeat);
        }

        public static void SubmitAppraiserTypeChoice(int day,
            int localAppraiserSeat, int treasureType)
        {
            SubmitOnlineSeatChoice("appraiser_type_choice", day,
                ToNetworkSeat(localAppraiserSeat), treasureType);
        }

        public static bool TryGetAppraiserTypeChoice(int day,
            int networkAppraiserSeat, out int treasureType)
        {
            treasureType = -1;
            return Instance != null && Instance.appraiserChoices.TryGetValue(
                ChoiceKey(day, networkAppraiserSeat), out treasureType);
        }

        public static void SubmitAppraiserOrder(int day, int localPlayerIndex,
            int[] treasureIds)
        {
            if (!IsOnlineSession || Instance == null) return;
            AppraiserOrderState state = new AppraiserOrderState
            {
                day = day,
                actorSeat = ToNetworkTreasureSeat(localPlayerIndex),
                treasureIds = treasureIds
            };
            Instance.session.SendAction(new ActionRequest
            {
                action = "appraiser_order",
                actorSeat = state.actorSeat,
                data = Protocol.Json(state)
            });
        }

        public static bool TryGetAppraiserOrder(int day,
            int networkSeat, out int[] treasureIds)
        {
            treasureIds = null;
            return Instance != null && Instance.appraiserOrders.TryGetValue(
                ChoiceKey(day, networkSeat), out treasureIds);
        }

        private static void SubmitOnlineSeatChoice(string action, int day,
            int actorSeat, int value)
        {
            if (!IsOnlineSession || Instance == null) return;
            OnlineSeatChoice choice = new OnlineSeatChoice
            {
                day = day, actorSeat = actorSeat, value = value
            };
            Instance.session.SendAction(new ActionRequest
            {
                action = action,
                actorSeat = actorSeat,
                data = Protocol.Json(choice)
            });
        }

        private static string ChoiceKey(int day, int actorSeat) =>
            day + ":" + actorSeat;

        private static void EnsureInstance()
        {
            if (Instance != null) return;
            KaitouOnlineGameBridge existing =
                Object.FindFirstObjectByType<KaitouOnlineGameBridge>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }
            new GameObject("KaitouOnlineGameBridge").AddComponent<KaitouOnlineGameBridge>();
        }

        private void Submit(CardInteraction card, int declaredNumber)
        {
            ActionCardChoice choice = Describe(card, session.LocalSeat, declaredNumber);
            ActionRequest request = new ActionRequest
            {
                action = "select_action_card",
                actorSeat = session.LocalSeat,
                cardId = -1,
                targetCardId = -1,
                number = declaredNumber,
                data = Protocol.Json(choice)
            };
            session.SendAction(request);
            Debug.Log($"<color=#70E8FF>【オンライン選択送信】P{choice.seat + 1} " +
                      $"{CardLabel(choice)} 宣言:{declaredNumber}</color>");
        }

        private void SubmitPass()
        {
            ActionCardChoice choice = new ActionCardChoice
            {
                day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1,
                seat = session.LocalSeat,
                specialEffect = 0,
                isExhibit = false,
                isThief = false,
                isCage = false,
                declaredNumber = 0
            };
            session.SendAction(new ActionRequest
            {
                action = "select_action_card",
                actorSeat = session.LocalSeat,
                data = Protocol.Json(choice)
            });
            Debug.Log($"<color=#BFA8FF>【オンライン行動休止】P{session.LocalSeat + 1}</color>");
        }

        private void OnMessage(Envelope envelope)
        {
            if (envelope.type == MessageType.ActionRequest && session.IsHost)
            {
                ActionRequest request = Protocol.Parse<ActionRequest>(envelope.payload);
                if (request.action == "select_display_treasure")
                {
                    TreasureDisplayChoice displayChoice = new TreasureDisplayChoice
                    {
                        day = HandManager.Instance != null
                            ? HandManager.Instance.CurrentDay : 1,
                        actorSeat = envelope.senderSeat,
                        treasureId = request.cardId
                    };
                    session.Send(MessageType.TreasureDisplayChoice, -1,
                        Protocol.Json(displayChoice));
                    return;
                }
                if (request.action == "steal_treasure")
                {
                    TreasureStealChoice stealChoice = new TreasureStealChoice
                    {
                        day = HandManager.Instance != null
                            ? HandManager.Instance.CurrentDay : 1,
                        actorSeat = envelope.senderSeat,
                        treasureId = request.cardId
                    };
                    session.Send(MessageType.TreasureStealChoice, -1,
                        Protocol.Json(stealChoice));
                    return;
                }
                if (request.action == "display_stolen_treasure")
                {
                    TreasureDisplayChoice robberDisplay = new TreasureDisplayChoice
                    {
                        day = HandManager.Instance != null
                            ? HandManager.Instance.CurrentDay : 1,
                        actorSeat = envelope.senderSeat,
                        treasureId = request.cardId
                    };
                    session.Send(MessageType.TreasureRobberDisplayChoice, -1,
                        Protocol.Json(robberDisplay));
                    return;
                }
                if (request.action == "analyze_treasure")
                {
                    TreasureStealChoice analysisChoice = new TreasureStealChoice
                    {
                        day = HandManager.Instance != null
                            ? HandManager.Instance.CurrentDay : 1,
                        actorSeat = envelope.senderSeat,
                        treasureId = request.cardId
                    };
                    session.Send(MessageType.AnalysisTreasureChoice, -1,
                        Protocol.Json(analysisChoice));
                    return;
                }
                if (request.action == "test_grant_all_special")
                {
                    OnlineSeatChoice grantTarget = new OnlineSeatChoice
                    {
                        actorSeat = envelope.senderSeat
                    };
                    session.Send(MessageType.TestGrantAllSpecial, -1,
                        Protocol.Json(grantTarget));
                    return;
                }
                if (request.action == "honey_trap_status")
                {
                    session.Send(MessageType.HoneyTrapStatus, -1, request.data);
                    return;
                }
                if (request.action == "detective_choice")
                {
                    session.Send(MessageType.DetectiveChoice, -1, request.data);
                    return;
                }
                if (request.action == "state_checkpoint")
                {
                    StateCheckpoint checkpoint =
                        Protocol.Parse<StateCheckpoint>(request.data);
                    checkpoint.actorSeat = envelope.senderSeat;
                    Instance.ReceiveStateCheckpoint(checkpoint);
                    return;
                }
                if (request.action == "desync_report")
                {
                    StateCheckpoint report = Protocol.Parse<StateCheckpoint>(request.data);
                    if (string.IsNullOrWhiteSpace(report.message))
                        report.message = "行動カードの同期に失敗したため、接続を終了しました。";
                    session.Send(MessageType.DesyncDetected, -1, Protocol.Json(report));
                    return;
                }
                if (request.action == "appraiser_type_choice")
                {
                    session.Send(MessageType.AppraiserTypeChoice, -1, request.data);
                    return;
                }
                if (request.action == "appraiser_order")
                {
                    session.Send(MessageType.AppraiserOrder, -1, request.data);
                    return;
                }
                if (request.action == "frame_up_choice")
                {
                    session.Send(MessageType.FrameUpChoice, -1, request.data);
                    return;
                }
                if (request.action != "select_action_card") return;
                ActionCardChoice choice = Protocol.Parse<ActionCardChoice>(request.data);
                choice.seat = envelope.senderSeat;
                if (choice.seat < 0 || choice.seat >= session.RoomPlayerCount) return;
                int currentDay = HandManager.Instance != null
                    ? HandManager.Instance.CurrentDay : 1;
                if (choice.day != currentDay)
                {
                    Debug.LogWarning($"【オンライン選択破棄】受信:{choice.day}日目 / 現在:{currentDay}日目");
                    return;
                }
                hostChoices[choice.seat] = choice;
                Debug.Log($"【オンライン選択受信】P{choice.seat + 1}：{CardLabel(choice)}");
                EnsureHostCpuChoices(currentDay);
                if (hostChoices.Count >= session.RoomPlayerCount) BroadcastReady();
                return;
            }

            if (envelope.type == MessageType.StateSnapshot)
            {
                ActionSelectionState state =
                    Protocol.Parse<ActionSelectionState>(envelope.payload);
                if (state.choices == null || state.choices.Length == 0) return;
                ApplyReadyChoices(state.day, state.revealAtServerTime, state.choices,
                    state.inventory);
                return;
            }

            if (envelope.type == MessageType.TurnCleanup)
            {
                OnlineSeatChoice state = Protocol.Parse<OnlineSeatChoice>(envelope.payload);
                turnCleanupReleasedDays.Add(state.day);
                Debug.Log($"<color=#70E8FF>【オンライン一斉回収】{state.day}日目</color>");
                return;
            }

            if (envelope.type == MessageType.SecurityDiceState)
            {
                SecurityDiceState state =
                    Protocol.Parse<SecurityDiceState>(envelope.payload);
                securityDiceResults[state.day] = state.result;
                if (securityDiceRequestedDays.Contains(state.day))
                    ApplySecurityDice(state.day, state.result);
                return;
            }

            if (envelope.type == MessageType.TreasureDisplayChoice)
            {
                TreasureDisplayChoice choice =
                    Protocol.Parse<TreasureDisplayChoice>(envelope.payload);
                displayChoices.Add(choice);
                TreasureGame.TreasureController controller =
                    Object.FindFirstObjectByType<TreasureGame.TreasureController>();
                controller?.ApplyOnlineDisplayChoice(choice.treasureId);
                return;
            }

            if (envelope.type == MessageType.TreasureStealChoice)
            {
                TreasureStealChoice choice =
                    Protocol.Parse<TreasureStealChoice>(envelope.payload);
                stealChoices.Add(choice);
                TreasureGame.TreasureController controller =
                    Object.FindFirstObjectByType<TreasureGame.TreasureController>();
                controller?.ApplyOnlineStealChoice(choice.actorSeat, choice.treasureId);
                return;
            }

            if (envelope.type == MessageType.TreasureRobberDisplayChoice)
            {
                TreasureDisplayChoice choice =
                    Protocol.Parse<TreasureDisplayChoice>(envelope.payload);
                robberDisplayChoices.Add(choice);
                TreasureGame.TreasureController controller =
                    Object.FindFirstObjectByType<TreasureGame.TreasureController>();
                controller?.ApplyOnlineRobberDisplayChoice(
                    choice.actorSeat, choice.treasureId);
                return;
            }

            if (envelope.type == MessageType.AnalysisTreasureChoice)
            {
                TreasureStealChoice choice =
                    Protocol.Parse<TreasureStealChoice>(envelope.payload);
                analysisChoices.Add(choice);
                TreasureGame.TreasureController controller =
                    Object.FindFirstObjectByType<TreasureGame.TreasureController>();
                controller?.ApplyOnlineAnalysisChoice(
                    choice.actorSeat, choice.treasureId);
                return;
            }

            if (envelope.type == MessageType.ArrestResolutionState)
            {
                ArrestResolutionState state =
                    Protocol.Parse<ArrestResolutionState>(envelope.payload);
                ApplyArrestResolution(state);
                return;
            }

            if (envelope.type == MessageType.VictoryResolutionState)
            {
                VictoryResolutionState state =
                    Protocol.Parse<VictoryResolutionState>(envelope.payload);
                victoryStates[state.day] = state;
                if (victoryResolutionRequestedDays.Contains(state.day))
                    ApplyVictoryResolution(state);
                return;
            }

            if (envelope.type == MessageType.TestGrantAllSpecial)
            {
                if (HandManager.Instance != null)
                {
                    OnlineSeatChoice grantTarget =
                        Protocol.Parse<OnlineSeatChoice>(envelope.payload);
                    int localSeat = LocalIndexForNetworkSeat(grantTarget.actorSeat);
                    SpecialActionCardSystem.GrantAllSpecialCardsToSeat(
                        localSeat, HandManager.Instance);
                }
                return;
            }

            if (envelope.type == MessageType.RestartGame)
            {
                StartGameState restart =
                    Protocol.Parse<StartGameState>(envelope.payload);
                Random.InitState(restart.randomSeed);
                SceneManager.LoadScene(string.IsNullOrEmpty(restart.sceneName)
                    ? "IntegratedGameScene" : restart.sceneName);
                return;
            }

            if (envelope.type == MessageType.HoneyTrapStatus)
            {
                HoneyTrapStatus status =
                    Protocol.Parse<HoneyTrapStatus>(envelope.payload);
                if (!status.active)
                    PriorityMessage = "";
                else if (session.LocalSeat == status.victimSeat)
                    PriorityMessage =
                        $"ハニートラップにより、行動カードを{PlayerNameForNetworkSeat(status.robberSeat)}に見せてしまいました。";
                else
                    PriorityMessage =
                        $"{PlayerNameForNetworkSeat(status.robberSeat)}が" +
                        $"{PlayerNameForNetworkSeat(status.victimSeat)}の行動カードを確認中です。";
                return;
            }

            if (envelope.type == MessageType.DetectiveChoice)
            {
                OnlineSeatChoice choice =
                    Protocol.Parse<OnlineSeatChoice>(envelope.payload);
                detectiveChoices[ChoiceKey(choice.day, choice.actorSeat)] =
                    choice.value;
                return;
            }

            if (envelope.type == MessageType.StateCheckpointResult)
            {
                StateCheckpoint result =
                    Protocol.Parse<StateCheckpoint>(envelope.payload);
                if (result.success)
                    completedCheckpoints.Add(CheckpointKey(result.day, result.checkpoint));
                return;
            }

            if (envelope.type == MessageType.DesyncDetected)
            {
                StateCheckpoint result =
                    Protocol.Parse<StateCheckpoint>(envelope.payload);
                if (!desyncAbortStarted)
                    StartCoroutine(DisconnectAfterDesync(result.message));
                return;
            }

            if (envelope.type == MessageType.AppraiserTypeChoice)
            {
                OnlineSeatChoice choice =
                    Protocol.Parse<OnlineSeatChoice>(envelope.payload);
                appraiserChoices[ChoiceKey(choice.day, choice.actorSeat)] =
                    choice.value;
                return;
            }

            if (envelope.type == MessageType.AppraiserOrder)
            {
                AppraiserOrderState state =
                    Protocol.Parse<AppraiserOrderState>(envelope.payload);
                appraiserOrders[ChoiceKey(state.day, state.actorSeat)] =
                    state.treasureIds ?? System.Array.Empty<int>();
                return;
            }

            if (envelope.type == MessageType.FrameUpChoice)
            {
                OnlineSeatChoice choice =
                    Protocol.Parse<OnlineSeatChoice>(envelope.payload);
                frameUpChoices[ChoiceKey(choice.day, choice.actorSeat)] =
                    choice.value;
                ArrestHandler handler = Object.FindFirstObjectByType<ArrestHandler>();
                handler?.ApplyOnlineFrameUpChoice(choice.actorSeat, choice.value);
            }
        }


        private string BuildActionStateSignature()
        {
            List<string> seats = new List<string>();
            for (int networkSeat = 0; networkSeat < session.RoomPlayerCount; networkSeat++)
            {
                int localSeat = LocalIndexForNetworkSeat(networkSeat);
                seats.Add(networkSeat + "=" +
                          SpecialActionCardSystem.BuildActionHandSignature(localSeat));
            }
            return string.Join("|", seats);
        }

        private void ReceiveStateCheckpoint(StateCheckpoint checkpoint)
        {
            string key = CheckpointKey(checkpoint.day, checkpoint.checkpoint);
            if (!checkpointSignatures.TryGetValue(key, out Dictionary<int, string> values))
            {
                values = new Dictionary<int, string>();
                checkpointSignatures[key] = values;
            }
            values[checkpoint.actorSeat] = checkpoint.signature ?? "";
            int expected = Mathf.Max(1, session.ConfirmedHumanPlayers);
            if (values.Count < expected) return;

            string first = null;
            bool matches = true;
            foreach (KeyValuePair<int, string> value in values)
            {
                if (first == null) first = value.Value;
                else if (first != value.Value) matches = false;
            }
            if (!matches)
            {
                var details = new List<string>();
                foreach (KeyValuePair<int, string> value in values)
                    details.Add($"端末P{value.Key + 1}=[{value.Value}]");
                Debug.LogError("【行動手札同期差分】" + string.Join(" / ", details));
            }
            StateCheckpoint result = checkpoint;
            result.success = matches;
            string phaseName = checkpoint.checkpoint == "action_ready"
                ? "行動カードの手札構成"
                : checkpoint.checkpoint == "detective"
                    ? "名探偵の処理結果"
                    : "ゲーム状態";
            result.message = matches ? "" :
                phaseName + "が端末間で一致しなかったため、接続を終了しました。";
            session.Send(matches ? MessageType.StateCheckpointResult :
                MessageType.DesyncDetected, -1, Protocol.Json(result));
        }

        private System.Collections.IEnumerator DisconnectAfterDesync(string message)
        {
            desyncAbortStarted = true;
            MarkConnectionLost(message);
            yield return new WaitForSecondsRealtime(0.35f);
            session?.Disconnect();
            SceneManager.LoadScene("MainMenu");
        }

        private static string CheckpointKey(int day, string checkpoint) =>
            day + ":" + (checkpoint ?? "");

        private void BeginSecurityDiceInternal(SecurityDice dice,
            List<Player> players, int day)
        {
            securityDiceRequestedDays.Add(day);
            if (securityDiceResults.TryGetValue(day, out int received))
            {
                ApplySecurityDice(day, received, dice, players);
                return;
            }

            if (!session.IsHost || !securityDiceBroadcastDays.Add(day)) return;
            SecurityDiceState state = new SecurityDiceState
            {
                day = day,
                result = Random.Range(1, 7)
            };
            session.Send(MessageType.SecurityDiceState, -1, Protocol.Json(state));
            Debug.Log($"<color=#70E8FF>【オンライン警備サイコロ送信】" +
                      $"{day}日目：{state.result}</color>");
        }

        private void ApplySecurityDice(int day, int result,
            SecurityDice preferredDice = null, List<Player> preferredPlayers = null)
        {
            if (!securityDiceAppliedDays.Add(day)) return;
            SecurityDice dice = preferredDice != null
                ? preferredDice : Object.FindFirstObjectByType<SecurityDice>();
            List<Player> players = preferredPlayers;
            if (players == null)
            {
                players = new List<Player>();
                Player player = Object.FindFirstObjectByType<Player>();
                if (player != null) players.Add(player);
            }
            if (dice == null)
            {
                securityDiceAppliedDays.Remove(day);
                return;
            }
            dice.RollDice(players, result);
            Debug.Log($"<color=#70E8FF>【オンライン警備サイコロ適用】" +
                      $"{day}日目：{result}</color>");
        }

        private ArrestResolutionState CaptureArrestResolution(int day)
        {
            int count = session.RoomPlayerCount;
            bool[] arrested = new bool[count];
            bool[] eliminated = new bool[count];
            bool[] criminalRecords = new bool[count];
            bool[] penaltyPending = new bool[count];
            int[] prisonUntilDays = new int[count];
            for (int seat = 0; seat < count; seat++)
            {
                int localIndex = LocalIndexForNetworkSeat(seat);
                ReadParticipantState(localIndex,
                    out arrested[seat], out eliminated[seat]);
                criminalRecords[seat] = ReadCriminalRecord(localIndex);
                penaltyPending[seat] = ReadPenaltyPending(localIndex);
                prisonUntilDays[seat] = SpecialActionCardSystem.GetPrisonUntilDay(
                    localIndex);
            }

            int[] localRewards = ArrestHandler.Instance != null
                ? ArrestHandler.Instance.GetSuccessfulCagePlayerIds()
                : System.Array.Empty<int>();
            int[] networkRewards = new int[localRewards.Length];
            for (int i = 0; i < localRewards.Length; i++)
                networkRewards[i] = NetworkSeatForLocalIndex(localRewards[i]);
            return new ArrestResolutionState
            {
                day = day,
                arrested = arrested,
                eliminated = eliminated,
                criminalRecords = criminalRecords,
                penaltyPending = penaltyPending,
                prisonUntilDays = prisonUntilDays,
                successfulCageSeats = networkRewards,
                penaltySeed = unchecked(session.GameSeed ^ (day * 486187739))
            };
        }

        private void ApplyArrestResolution(ArrestResolutionState state)
        {
            int count = Mathf.Min(session.RoomPlayerCount,
                state.arrested != null ? state.arrested.Length : 0);
            for (int seat = 0; seat < count; seat++)
            {
                int localIndex = LocalIndexForNetworkSeat(seat);
                bool eliminated = state.eliminated != null &&
                                  seat < state.eliminated.Length &&
                                  state.eliminated[seat];
                WriteParticipantState(localIndex, state.arrested[seat], eliminated);
                bool penaltyPending = state.penaltyPending != null &&
                                      seat < state.penaltyPending.Length &&
                                      state.penaltyPending[seat];
                WritePenaltyPending(localIndex, penaltyPending);
                if (state.criminalRecords != null &&
                    seat < state.criminalRecords.Length)
                    WriteCriminalRecord(localIndex, state.criminalRecords[seat]);
                int prisonUntil = state.prisonUntilDays != null &&
                                  seat < state.prisonUntilDays.Length
                    ? state.prisonUntilDays[seat]
                    : -1;
                SpecialActionCardSystem.ApplyOnlinePrisonState(
                    localIndex, prisonUntil);
            }

            if (ArrestHandler.Instance != null)
            {
                int[] rewards = state.successfulCageSeats ??
                                System.Array.Empty<int>();
                int[] localRewards = new int[rewards.Length];
                for (int i = 0; i < rewards.Length; i++)
                    localRewards[i] = LocalIndexForNetworkSeat(rewards[i]);
                ArrestHandler.Instance.SetSuccessfulCagePlayerIds(localRewards);
            }
            penaltySeeds[state.day] = state.penaltySeed;
            arrestResolutionAppliedDays.Add(state.day);
            Debug.Log($"<color=#70E8FF>【オンライン逮捕結果適用】{state.day}日目</color>");
        }

        private void ApplyVictoryResolution(VictoryResolutionState state)
        {
            if (!victoryResolutionAppliedDays.Add(state.day)) return;
            TreasureGame.TreasureController controller =
                Object.FindFirstObjectByType<TreasureGame.TreasureController>();
            if (controller == null)
            {
                victoryResolutionAppliedDays.Remove(state.day);
                return;
            }
            controller.ApplyOnlineVictoryResolution(state);
        }

        private int LocalIndexForNetworkSeat(int networkSeat)
        {
            int order = NetworkSeatOrder(networkSeat);
            if (session.RoomPlayerCount == 3)
                return order == 0 ? 0 : order == 1 ? 2 : 3;
            return order;
        }

        private int NetworkSeatForLocalIndex(int localIndex)
        {
            int order = session.RoomPlayerCount == 3
                ? localIndex == 0 ? 0 : localIndex == 2 ? 1 : 2
                : localIndex;
            return NetworkSeatAtOrder(order);
        }

        private int NetworkSeatOrder(int networkSeat)
        {
            if (networkSeat == session.LocalSeat) return 0;
            int order = 1;
            for (int seat = 0; seat < session.RoomPlayerCount; seat++)
            {
                if (seat == session.LocalSeat) continue;
                if (seat == networkSeat) return order;
                order++;
            }
            return -1;
        }

        private int NetworkSeatAtOrder(int targetOrder)
        {
            if (targetOrder == 0) return session.LocalSeat;
            int order = 1;
            for (int seat = 0; seat < session.RoomPlayerCount; seat++)
            {
                if (seat == session.LocalSeat) continue;
                if (order == targetOrder) return seat;
                order++;
            }
            return -1;
        }

        private static void ReadParticipantState(int localIndex,
            out bool arrested, out bool eliminated)
        {
            arrested = false;
            eliminated = false;
            if (localIndex == 0)
            {
                Player p = Object.FindFirstObjectByType<Player>();
                if (p != null) { arrested = p.HasBeenArrested; eliminated = p.isEliminated; }
            }
            else if (localIndex == 1)
            {
                Player2 p = Object.FindFirstObjectByType<Player2>();
                if (p != null) { arrested = p.HasBeenArrested; eliminated = p.isEliminated; }
            }
            else if (localIndex == 2)
            {
                Player3 p = Object.FindFirstObjectByType<Player3>();
                if (p != null) { arrested = p.HasBeenArrested; eliminated = p.isEliminated; }
            }
            else
            {
                Player4 p = Object.FindFirstObjectByType<Player4>();
                if (p != null) { arrested = p.HasBeenArrested; eliminated = p.isEliminated; }
            }
        }

        private static bool ReadCriminalRecord(int localIndex)
        {
            if (localIndex == 0)
                return Object.FindFirstObjectByType<Player>()?.hasCriminalRecord ?? false;
            if (localIndex == 1)
                return Object.FindFirstObjectByType<Player2>()?.hasCriminalRecord ?? false;
            if (localIndex == 2)
                return Object.FindFirstObjectByType<Player3>()?.hasCriminalRecord ?? false;
            return Object.FindFirstObjectByType<Player4>()?.hasCriminalRecord ?? false;
        }

        private static bool ReadPenaltyPending(int localIndex)
        {
            if (localIndex == 0)
                return Object.FindFirstObjectByType<Player>()?.HasPendingArrestPenalty ?? false;
            if (localIndex == 1)
                return Object.FindFirstObjectByType<Player2>()?.HasPendingArrestPenalty ?? false;
            if (localIndex == 2)
                return Object.FindFirstObjectByType<Player3>()?.HasPendingArrestPenalty ?? false;
            return Object.FindFirstObjectByType<Player4>()?.HasPendingArrestPenalty ?? false;
        }

        private static void WritePenaltyPending(int localIndex, bool value)
        {
            if (localIndex == 0)
                Object.FindFirstObjectByType<Player>()?.ApplyOnlinePenaltyPending(value);
            else if (localIndex == 1)
                Object.FindFirstObjectByType<Player2>()?.ApplyOnlinePenaltyPending(value);
            else if (localIndex == 2)
                Object.FindFirstObjectByType<Player3>()?.ApplyOnlinePenaltyPending(value);
            else
                Object.FindFirstObjectByType<Player4>()?.ApplyOnlinePenaltyPending(value);
        }

        private static void WriteCriminalRecord(int localIndex, bool value)
        {
            if (localIndex == 0)
            { Player p = Object.FindFirstObjectByType<Player>(); if (p != null) p.ApplyOnlineCriminalRecord(value); }
            else if (localIndex == 1)
            { Player2 p = Object.FindFirstObjectByType<Player2>(); if (p != null) p.ApplyOnlineCriminalRecord(value); }
            else if (localIndex == 2)
            { Player3 p = Object.FindFirstObjectByType<Player3>(); if (p != null) p.ApplyOnlineCriminalRecord(value); }
            else
            { Player4 p = Object.FindFirstObjectByType<Player4>(); if (p != null) p.ApplyOnlineCriminalRecord(value); }
        }

        private static void WriteParticipantState(int localIndex,
            bool arrested, bool eliminated)
        {
            if (localIndex == 0)
            {
                Player p = Object.FindFirstObjectByType<Player>();
                if (p != null) { p.HasBeenArrested = arrested; p.isEliminated = eliminated; }
            }
            else if (localIndex == 1)
            {
                Player2 p = Object.FindFirstObjectByType<Player2>();
                if (p != null) { p.HasBeenArrested = arrested; p.isEliminated = eliminated; }
            }
            else if (localIndex == 2)
            {
                Player3 p = Object.FindFirstObjectByType<Player3>();
                if (p != null) { p.HasBeenArrested = arrested; p.isEliminated = eliminated; }
            }
            else
            {
                Player4 p = Object.FindFirstObjectByType<Player4>();
                if (p != null) { p.HasBeenArrested = arrested; p.isEliminated = eliminated; }
            }
        }

        private void BroadcastReady()
        {
            int day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1;
            var ordered = new ActionCardChoice[session.RoomPlayerCount];
            for (int seat = 0; seat < ordered.Length; seat++)
                if (!hostChoices.TryGetValue(seat, out ordered[seat]) ||
                    ordered[seat].day != day) return;
            session.Send(MessageType.StateSnapshot, -1,
                Protocol.Json(new ActionSelectionState
                {
                    day = day,
                    revealAtServerTime = NetworkManager.Singleton != null
                        ? NetworkManager.Singleton.ServerTime.Time + 0.8 : 0d,
                    choices = ordered,
                    inventory = CaptureAuthoritativeActionInventory()
                }));
        }

        private ActionCardInventoryEntry[] CaptureAuthoritativeActionInventory()
        {
            var result = new List<ActionCardInventoryEntry>();
            for (int networkSeat = 0; networkSeat < session.RoomPlayerCount; networkSeat++)
            {
                int localIndex = LocalIndexForNetworkSeat(networkSeat);
                List<CardInteraction> cards = GetActionCardsAtLocalIndex(localIndex);
                var counts = new Dictionary<string, ActionCardInventoryEntry>();
                if (cards != null)
                {
                    foreach (CardInteraction card in cards)
                    {
                        if (card == null) continue;
                        string key = $"{(int)card.specialEffect}:" +
                                     $"{(card.isExhibit ? 1 : 0)}" +
                                     $"{(card.isPhantomThief ? 1 : 0)}" +
                                     $"{(card.isCage ? 1 : 0)}";
                        if (!counts.TryGetValue(key, out ActionCardInventoryEntry entry))
                        {
                            entry = new ActionCardInventoryEntry
                            {
                                seat = networkSeat,
                                specialEffect = (int)card.specialEffect,
                                isExhibit = card.isExhibit,
                                isThief = card.isPhantomThief,
                                isCage = card.isCage,
                                count = 0
                            };
                        }
                        entry.count++;
                        counts[key] = entry;
                    }
                }
                result.AddRange(counts.Values);
            }
            return result.ToArray();
        }

        private void ApplyAuthoritativeActionInventory(
            ActionCardInventoryEntry[] networkInventory)
        {
            if (networkInventory == null) return;
            var localInventory = new ActionCardInventoryEntry[networkInventory.Length];
            for (int i = 0; i < networkInventory.Length; i++)
            {
                localInventory[i] = networkInventory[i];
                localInventory[i].seat = LocalIndexForNetworkSeat(networkInventory[i].seat);
            }
            for (int localIndex = 0; localIndex < 4; localIndex++)
            {
                if (!IsConfiguredLocalSeat(localIndex)) continue;
                SpecialActionCardSystem.SynchronizeOnlineHand(localIndex,
                    localInventory, HandManager.Instance);
            }
        }

        private static List<CardInteraction> GetActionCardsAtLocalIndex(int localIndex)
        {
            if (localIndex == 0)
                return Object.FindFirstObjectByType<Player>(
                    FindObjectsInactive.Include)?.playerCards;
            if (localIndex == 1)
                return Object.FindFirstObjectByType<Player2>(
                    FindObjectsInactive.Include)?.player2Cards;
            if (localIndex == 2)
                return Object.FindFirstObjectByType<Player3>(
                    FindObjectsInactive.Include)?.player3Cards;
            if (localIndex == 3)
                return Object.FindFirstObjectByType<Player4>(
                    FindObjectsInactive.Include)?.player4Cards;
            return null;
        }

        private void EnsureHostCpuChoices(int day)
        {
            if (!session.IsHost || hostChoices.Count < session.ConfirmedHumanPlayers) return;
            for (int networkSeat = session.ConfirmedHumanPlayers;
                 networkSeat < session.RoomPlayerCount; networkSeat++)
            {
                if (hostChoices.ContainsKey(networkSeat)) continue;
                int localIndex = LocalIndexForNetworkSeat(networkSeat);
                CardInteraction selected = null;
                int declaredNumber = 0;
                if (localIndex == 2)
                {
                    Player3 cpu = FindFirstObjectByType<Player3>();
                    cpu?.SelectRandomCard();
                    selected = cpu?.SelectedCard;
                    declaredNumber = selected != null && selected.isPhantomThief
                        ? selected.RandomDeclaredNumber() : 0;
                    cpu?.SetCpuDeclaredNumber(declaredNumber);
                }
                else if (localIndex == 3)
                {
                    Player4 cpu = FindFirstObjectByType<Player4>();
                    cpu?.SelectRandomCard();
                    selected = cpu?.SelectedCard;
                    declaredNumber = selected != null && selected.isPhantomThief
                        ? selected.RandomDeclaredNumber() : 0;
                    cpu?.SetCpuDeclaredNumber(declaredNumber);
                }
                if (selected == null && IsCpuActionRequired(localIndex))
                {
                    AbortForActionMismatch(day,
                        $"CPUのP{networkSeat + 1}が行動可能なのにカードを選べませんでした。");
                    return;
                }
                hostChoices[networkSeat] = selected != null
                    ? Describe(selected, networkSeat, declaredNumber)
                    : new ActionCardChoice { day = day, seat = networkSeat };
                Debug.Log($"【オンラインCPU選択】P{networkSeat + 1}：" +
                          CardLabel(hostChoices[networkSeat]));
            }
        }

        private static bool IsCpuActionRequired(int localIndex)
        {
            if (SpecialActionCardSystem.CannotActToday(localIndex)) return false;
            if (localIndex == 2)
            {
                Player3 player = Object.FindFirstObjectByType<Player3>();
                return player != null && player.gameObject.activeInHierarchy &&
                       !player.isEliminated;
            }
            if (localIndex == 3)
            {
                Player4 player = Object.FindFirstObjectByType<Player4>();
                return player != null && player.gameObject.activeInHierarchy &&
                       !player.isEliminated;
            }
            return false;
        }

        private void ApplyReadyChoices(
            int snapshotDay, double revealAtServerTime, ActionCardChoice[] choices,
            ActionCardInventoryEntry[] inventory)
        {
            if (revealStartedDay == snapshotDay) return;
            int currentDay = HandManager.Instance != null
                ? HandManager.Instance.CurrentDay : 1;
            if (snapshotDay != currentDay || choices == null ||
                choices.Length != session.RoomPlayerCount)
            {
                Debug.LogWarning($"【オンライン開示破棄】受信:{snapshotDay}日目 / " +
                                 $"現在:{currentDay}日目 / 選択数:{choices?.Length ?? 0} / " +
                                 $"必要数:{session.RoomPlayerCount}");
                return;
            }
            ApplyAuthoritativeActionInventory(inventory);
            var receivedSeats = new HashSet<int>();
            foreach (ActionCardChoice choice in choices)
                if (choice.day != snapshotDay || choice.seat < 0 ||
                    choice.seat >= session.RoomPlayerCount ||
                    !receivedSeats.Add(choice.seat))
                {
                    AbortForActionMismatch(snapshotDay,
                        "4人分の行動選択データに欠落または重複がありました。");
                    return;
                }
            foreach (ActionCardChoice choice in choices)
            {
                if (choice.seat == session.LocalSeat) continue;
                // ホストのCPUはEnsureHostCpuChoicesで既に実カードを選択・移動済み。
                // Snapshotを重ねて適用すると同種の別カードまで卓上へ出て二重表示になる。
                if (session.IsHost && choice.seat >= session.ConfirmedHumanPlayers)
                    continue;
                int localIndex = LocalIndexForNetworkSeat(choice.seat);
                bool isPass = choice.specialEffect == 0 && !choice.isExhibit &&
                              !choice.isThief && !choice.isCage;
                if (localIndex == 1)
                {
                    Player2 opponent = FindFirstObjectByType<Player2>();
                    if (isPass) opponent?.SelectOnlinePass();
                    else if (choice.specialEffect == (int)SpecialActionEffect.AdvanceNotice &&
                             SpecialActionCardSystem.TryGetActiveAdvanceNotice(
                                 1, out CardInteraction heldNotice))
                        opponent?.RestoreAdvanceNotice(heldNotice);
                    else opponent?.SelectOnlineCard(choice.specialEffect, choice.isExhibit,
                            choice.isThief, choice.isCage, choice.declaredNumber);
                }
                else if (localIndex == 2)
                {
                    Player3 participant = FindFirstObjectByType<Player3>();
                    if (isPass) participant?.SelectOnlinePass();
                    else if (choice.specialEffect == (int)SpecialActionEffect.AdvanceNotice &&
                             SpecialActionCardSystem.TryGetActiveAdvanceNotice(
                                 2, out CardInteraction heldNotice))
                        participant?.RestoreAdvanceNotice(heldNotice);
                    else participant?.SelectOnlineCard(choice.specialEffect, choice.isExhibit,
                        choice.isThief, choice.isCage, choice.declaredNumber);
                }
                else if (localIndex == 3)
                {
                    Player4 participant = FindFirstObjectByType<Player4>();
                    if (isPass) participant?.SelectOnlinePass();
                    else if (choice.specialEffect == (int)SpecialActionEffect.AdvanceNotice &&
                             SpecialActionCardSystem.TryGetActiveAdvanceNotice(
                                 3, out CardInteraction heldNotice))
                        participant?.RestoreAdvanceNotice(heldNotice);
                    else participant?.SelectOnlineCard(choice.specialEffect, choice.isExhibit,
                        choice.isThief, choice.isCage, choice.declaredNumber);
                }
            }

            // 選択スナップショットを受け取っただけでは進めない。
            // 全席で対応する実カードが確定していることを検査し、1枚でも欠けた状態で
            // 競合・檻・宝フェーズへ入ることを防ぐ。
            if (!ValidateAppliedChoices(snapshotDay, choices, out string mismatch))
            {
                AbortForActionMismatch(snapshotDay, mismatch);
                return;
            }

            // 両者の選択が揃った通知を共通の起点にする。
            // 相手カードの伏せ移動開始直後、ローカルと同じカメラ・開示シーケンスへ入る。
            revealStartedDay = snapshotDay;
            WaitingMessage = "";
            BeginStateCheckpoint(snapshotDay, "action_ready");
            StartCoroutine(BeginSynchronizedReveal(snapshotDay, revealAtServerTime));
        }

        private bool ValidateAppliedChoices(int day, ActionCardChoice[] choices,
            out string mismatch)
        {
            foreach (ActionCardChoice choice in choices)
            {
                int localIndex = LocalIndexForNetworkSeat(choice.seat);
                CardInteraction selected = GetSelectedCardAtLocalIndex(localIndex);
                bool isPass = choice.specialEffect == 0 && !choice.isExhibit &&
                              !choice.isThief && !choice.isCage;
                if (isPass)
                {
                    if (selected != null)
                    {
                        mismatch = $"{day}日目 P{choice.seat + 1}は休みですが、" +
                                   $"{selected.name}が選択状態です。";
                        return false;
                    }
                    Debug.Log($"【4人同期検査】{day}日目 P{choice.seat + 1}：休み");
                    continue;
                }

                if (selected == null || (int)selected.specialEffect != choice.specialEffect ||
                    selected.isExhibit != choice.isExhibit ||
                    selected.isPhantomThief != choice.isThief ||
                    selected.isCage != choice.isCage ||
                    (choice.isThief && selected.SelectedNumber != choice.declaredNumber))
                {
                    mismatch = $"{day}日目 P{choice.seat + 1}の行動カードを" +
                               "この端末の手札へ正しく反映できませんでした。" +
                               $" 期待=特殊:{choice.specialEffect}/展示:{choice.isExhibit}/" +
                               $"怪盗:{choice.isThief}/檻:{choice.isCage}/宣言:{choice.declaredNumber}" +
                               (selected == null ? " 実際=カードなし" :
                                $" 実際={selected.name}/特殊:{(int)selected.specialEffect}/" +
                                $"展示:{selected.isExhibit}/怪盗:{selected.isPhantomThief}/" +
                                $"檻:{selected.isCage}/宣言:{selected.SelectedNumber}");
                    return false;
                }
                Debug.Log($"<color=#70E8FF>【4人同期検査OK】{day}日目 " +
                          $"P{choice.seat + 1}：{CardLabel(choice)} " +
                          $"宣言:{choice.declaredNumber}</color>");
            }
            mismatch = "";
            return true;
        }

        private static CardInteraction GetSelectedCardAtLocalIndex(int localIndex)
        {
            if (localIndex == 0)
                return Object.FindFirstObjectByType<Player>()?.SelectedCard;
            if (localIndex == 1)
                return Object.FindFirstObjectByType<Player2>()?.SelectedCard;
            if (localIndex == 2)
                return Object.FindFirstObjectByType<Player3>()?.SelectedCard;
            if (localIndex == 3)
                return Object.FindFirstObjectByType<Player4>()?.SelectedCard;
            return null;
        }

        private void AbortForActionMismatch(int day, string message)
        {
            string fullMessage = string.IsNullOrWhiteSpace(message)
                ? "行動カードの同期に失敗したため、接続を終了しました。"
                : message + " 接続を終了します。";
            Debug.LogError("【4人同期検査NG】" + fullMessage);
            StateCheckpoint report = new StateCheckpoint
            {
                day = day,
                checkpoint = "action_reveal",
                actorSeat = session.LocalSeat,
                success = false,
                message = fullMessage
            };
            if (session.IsHost)
                session.Send(MessageType.DesyncDetected, -1, Protocol.Json(report));
            else
                session.SendAction(new ActionRequest
                {
                    action = "desync_report",
                    actorSeat = session.LocalSeat,
                    data = Protocol.Json(report)
                });
        }

        private System.Collections.IEnumerator BeginSynchronizedReveal(
            int day, double revealAtServerTime)
        {
            // 全端末の4席分の行動手札が一致してからだけ開示する。
            // 不一致のまま進めると競合・檻・展示数が連鎖的に壊れる。
            while (IsActive && IsWaitingForStateCheckpoint(day, "action_ready"))
                yield return null;
            if (!IsActive) yield break;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && revealAtServerTime > 0d)
                while (manager.IsListening && manager.ServerTime.Time < revealAtServerTime)
                    yield return null;
            CameraController cameraController =
                Camera.main != null ? Camera.main.GetComponent<CameraController>() : null;
            cameraController?.PrepareSynchronizedActionReveal(day);
            // ローカル版と同じく、カメラが中央へ寄り始めるこの瞬間に
            // 4席の伏せカードを一斉に卓上へ移動させる。
            MoveAllSelectedCardsToTable(day);
            StartCoroutine(EnsureSelectedCardsReachedTable(day));
            cameraController?.MoveCamera();
            Debug.Log($"<color=#70E8FF>【オンライン時刻同期開示】{day}日目 " +
                      $"serverTime={revealAtServerTime:F3}</color>");
        }

        private void MoveAllSelectedCardsToTable(int day)
        {
            Vector3[] positions =
            {
                new Vector3(0f, 0f, -1f),
                new Vector3(0f, 0f, 1f),
                new Vector3(-1f, 0f, 0f),
                new Vector3(1f, 0f, 0f)
            };
            for (int localIndex = 0; localIndex < positions.Length; localIndex++)
            {
                // 自分のカードは従来どおり選択時のMoveToCenterに任せる。
                if (!IsConfiguredLocalSeat(localIndex)) continue;
                CardInteraction selected = GetSelectedCardAtLocalIndex(localIndex);
                if (selected == null) continue;
                selected.EnsureVisibleForTable();
                selected.DisableClick(false);
                // 予告状は1日目に表向きで卓上へ残る。実行日の再同期でも
                // MoveToによって元の裏向き角度へ戻さず、その姿勢を維持する。
                if (SpecialActionCardSystem.IsAdvanceNoticePendingCard(localIndex, selected) &&
                    SpecialActionCardSystem.IsAdvanceNoticeActiveToday(localIndex))
                    continue;
                if (localIndex == 0) continue;
                selected.MoveTo(positions[localIndex], 2.5f);
                Debug.Log($"<color=#70E8FF>【オンライン一斉卓上移動】{day}日目 " +
                          $"local P{localIndex + 1}：{selected.name}</color>");
            }
        }

        private static bool IsConfiguredLocalSeat(int localIndex)
        {
            HandManager manager = HandManager.Instance;
            if (manager == null) return false;
            if (manager.ActionPlayerCount == 3)
                return localIndex == 0 || localIndex == 2 || localIndex == 3;
            return localIndex >= 0 && localIndex < manager.ActionPlayerCount;
        }

        private System.Collections.IEnumerator EnsureSelectedCardsReachedTable(int day)
        {
            // カメラ移動と一緒にスライドを見せ、その終盤で必ず目標座標へ確定する。
            yield return new WaitForSeconds(0.8f);
            for (int localIndex = 0; localIndex < 4; localIndex++)
            {
                if (!IsConfiguredLocalSeat(localIndex)) continue;
                CardInteraction selected = GetSelectedCardAtLocalIndex(localIndex);
                if (selected == null) continue;
                selected.EnsureVisibleForTable();
                selected.DisableClick(false);
                if (SpecialActionCardSystem.IsAdvanceNoticePendingCard(localIndex, selected) &&
                    SpecialActionCardSystem.IsAdvanceNoticeActiveToday(localIndex))
                    continue;
                if (localIndex == 0) continue;
                selected.CompleteCurrentMoveImmediately();
                Debug.Log($"<color=#70E8FF>【オンライン卓上到着確認】{day}日目 " +
                          $"local P{localIndex + 1}：{selected.name} " +
                          $"座標:{selected.transform.position} Scale:{selected.VisualScale}</color>");
            }
        }

        private static ActionCardChoice Describe(CardInteraction card, int seat, int number) =>
            new ActionCardChoice
            {
                day = HandManager.Instance != null ? HandManager.Instance.CurrentDay : 1,
                seat = seat,
                specialEffect = (int)card.specialEffect,
                isExhibit = card.isExhibit,
                isThief = card.isPhantomThief,
                isCage = card.isCage,
                declaredNumber = number
            };

        private static string CardLabel(ActionCardChoice choice) =>
            choice.specialEffect != 0
                ? ((SpecialActionEffect)choice.specialEffect).ToString()
                : choice.isThief ? "通常怪盗" : choice.isCage ? "通常檻" : "通常展示";
    }
}
