using System.Collections.Generic;
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
            KaitouOnlineSession.Instance != null && KaitouOnlineSession.Instance.IsOnline;

        private readonly Dictionary<int, ActionCardChoice> hostChoices =
            new Dictionary<int, ActionCardChoice>();
        private KaitouOnlineSession session;
        private bool revealStarted;
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
            Instance.revealStarted = false;
            WaitingMessage = "";
            Debug.Log("<color=#70E8FF>【オンライン】次の日の行動選択待ちへ移行</color>");
        }

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
            foreach (TreasureDisplayChoice choice in Instance.displayChoices)
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
            foreach (TreasureStealChoice choice in Instance.stealChoices)
                if (choice.day == day)
                    controller.ApplyOnlineStealChoice(
                        choice.actorSeat, choice.treasureId);
            foreach (TreasureDisplayChoice choice in Instance.robberDisplayChoices)
                if (choice.day == day)
                    controller.ApplyOnlineRobberDisplayChoice(
                        choice.actorSeat, choice.treasureId);
            foreach (TreasureStealChoice choice in Instance.analysisChoices)
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

        public static int ToLocalSeat(int networkSeat) =>
            Instance != null ? Instance.LocalIndexForNetworkSeat(networkSeat) : networkSeat;

        public static void SubmitDetectiveChoice(int day,
            int localDetectiveSeat, int localTargetSeat)
        {
            SubmitOnlineSeatChoice("detective_choice", day,
                ToNetworkSeat(localDetectiveSeat), ToNetworkSeat(localTargetSeat));
        }

        public static void SubmitFrameUpChoice(int day,
            int localSourceSeat, int localTargetSeat)
        {
            SubmitOnlineSeatChoice("frame_up_choice", day,
                ToNetworkSeat(localSourceSeat), ToNetworkSeat(localTargetSeat));
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
                actorSeat = ToNetworkSeat(localPlayerIndex),
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
                hostChoices[choice.seat] = choice;
                Debug.Log($"【オンライン選択受信】P{choice.seat + 1}：{CardLabel(choice)}");
                if (hostChoices.Count >= session.RoomPlayerCount) BroadcastReady();
                return;
            }

            if (envelope.type == MessageType.StateSnapshot)
            {
                ActionSelectionState state =
                    Protocol.Parse<ActionSelectionState>(envelope.payload);
                if (state.choices == null || state.choices.Length == 0) return;
                ApplyReadyChoices(state.choices);
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
                    SpecialActionCardSystem.GrantUnverifiedSpecialCardsToSeat(
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
                        $"ハニートラップにより、行動カードをPlayer{status.robberSeat + 1}に見せてしまいました。";
                else
                    PriorityMessage =
                        $"Player{status.robberSeat + 1}がPlayer{status.victimSeat + 1}の行動カードを確認中です。";
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
            int[] prisonUntilDays = new int[count];
            for (int seat = 0; seat < count; seat++)
            {
                ReadParticipantState(LocalIndexForNetworkSeat(seat),
                    out arrested[seat], out eliminated[seat]);
                criminalRecords[seat] = ReadCriminalRecord(
                    LocalIndexForNetworkSeat(seat));
                prisonUntilDays[seat] = SpecialActionCardSystem.GetPrisonUntilDay(
                    LocalIndexForNetworkSeat(seat));
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
            int localSeat = session.LocalSeat;
            if (networkSeat == localSeat) return 0;
            if (networkSeat == 0) return localSeat;
            return networkSeat;
        }

        private int NetworkSeatForLocalIndex(int localIndex)
        {
            int localSeat = session.LocalSeat;
            if (localIndex == 0) return localSeat;
            if (localIndex == localSeat) return 0;
            return localIndex;
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

        private static void WriteCriminalRecord(int localIndex, bool value)
        {
            if (localIndex == 0)
            { Player p = Object.FindFirstObjectByType<Player>(); if (p != null) p.hasCriminalRecord = value; }
            else if (localIndex == 1)
            { Player2 p = Object.FindFirstObjectByType<Player2>(); if (p != null) p.hasCriminalRecord = value; }
            else if (localIndex == 2)
            { Player3 p = Object.FindFirstObjectByType<Player3>(); if (p != null) p.hasCriminalRecord = value; }
            else
            { Player4 p = Object.FindFirstObjectByType<Player4>(); if (p != null) p.hasCriminalRecord = value; }
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
            var ordered = new ActionCardChoice[session.RoomPlayerCount];
            for (int seat = 0; seat < ordered.Length; seat++)
                if (!hostChoices.TryGetValue(seat, out ordered[seat])) return;
            session.Send(MessageType.StateSnapshot, -1,
                Protocol.Json(new ActionSelectionState { choices = ordered }));
        }

        private void ApplyReadyChoices(ActionCardChoice[] choices)
        {
            if (revealStarted) return;
            foreach (ActionCardChoice choice in choices)
            {
                if (choice.seat == session.LocalSeat) continue;
                // 2人オンラインでは、相手を既存盤のPlayer2として表示する。
                if (session.RoomPlayerCount == 2)
                {
                    Player2 opponent = FindFirstObjectByType<Player2>();
                    bool isPass = choice.specialEffect == 0 && !choice.isExhibit &&
                                  !choice.isThief && !choice.isCage;
                    if (isPass) opponent?.SelectOnlinePass();
                    else if (choice.specialEffect == (int)SpecialActionEffect.AdvanceNotice &&
                             SpecialActionCardSystem.TryGetActiveAdvanceNotice(
                                 1, out CardInteraction heldNotice))
                        opponent?.RestoreAdvanceNotice(heldNotice);
                    else opponent?.SelectOnlineCard(choice.specialEffect, choice.isExhibit,
                            choice.isThief, choice.isCage, choice.declaredNumber);
                }
            }

            revealStarted = true;
            WaitingMessage = "";
            StartCoroutine(RevealActionsAfterPlacement());
            Debug.Log("<color=#70E8FF>【オンライン】全員の行動選択が揃いました。一斉開示します。</color>");
        }

        private System.Collections.IEnumerator RevealActionsAfterPlacement()
        {
            // 通信で選ばれた相手カードが卓上へ移動し終わるまで待ち、
            // 3日目以降も両端末で同じタイミングに開示する。
            yield return new WaitForSeconds(0.85f);
            CameraController cameraController =
                Camera.main != null ? Camera.main.GetComponent<CameraController>() : null;
            cameraController?.MoveCamera();
        }

        private static ActionCardChoice Describe(CardInteraction card, int seat, int number) =>
            new ActionCardChoice
            {
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
