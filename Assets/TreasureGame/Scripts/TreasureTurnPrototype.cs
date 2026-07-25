using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TreasureGame
{
// 行動カード側が完成するまで、お宝ターン全体を試すための仮クラス。
public class TreasureTurnPrototype : MonoBehaviour
{
    [SerializeField] private TreasureController treasureController;
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private float startDelay = 1f;
    [Header("試作ボタン")]
    [SerializeField] private bool showStartButton = true;
    [SerializeField] private float buttonMargin = 14f;
    [SerializeField, Range(1f, 10f)] private float handInertiaFriction = 3.2f;
    [SerializeField] private float handMaxFlickSpeed = 2400f;
    [Header("画面UI調整")]
    [SerializeField, Range(1f, 3f)] private float uiScale = 2f;
    [SerializeField] private Vector2 instructionOffset = new Vector2(0f, 14f);
    [SerializeField] private Vector2 treasureButtonOffset = Vector2.zero;

    private bool turnRunning;
    private bool draggingHand;
    private float lastDragX;
    private float handSlideVelocity;

    private void Update()
    {
        if (draggingHand || Mathf.Abs(handSlideVelocity) < 10f) return;
        EnsureController();
        if (treasureController == null || !treasureController.PlayerOneHandVisible)
        {
            handSlideVelocity = 0f;
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        treasureController.DragPlayerOneHand(handSlideVelocity * deltaTime);
        handSlideVelocity *= Mathf.Exp(-handInertiaFriction * deltaTime);
    }

    private void OnGUI()
    {
        if (!showStartButton) return;
        EnsureController();

        if (treasureController != null && !string.IsNullOrEmpty(treasureController.InstructionText))
        {
            GUIStyle instructionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(20f * uiScale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            float instructionWidth = Mathf.Min(900f * uiScale, Screen.width - 40f * uiScale);
            Rect instructionRect = new Rect((Screen.width - instructionWidth) * 0.5f +
                instructionOffset.x * uiScale, instructionOffset.y * uiScale,
                instructionWidth, 58f * uiScale);
            GUI.Label(instructionRect, treasureController.InstructionText, instructionStyle);
        }

        bool canStart = !turnRunning &&
            (treasureController == null || treasureController.Phase == TreasurePhase.Waiting);
        GUI.enabled = canStart;

        GUIStyle style = new GUIStyle(GUI.skin.button)
        {
            fontSize = Mathf.RoundToInt(14f * uiScale),
            fontStyle = FontStyle.Bold
        };
        string label = canStart ? "ランダムお宝ターン開始" : "お宝ターン進行中…";
        float width = 210f * uiScale;
        float height = 42f * uiScale;
        Rect buttonRect = new Rect(buttonMargin * uiScale,
            Screen.height - height - buttonMargin * uiScale, width, height);
        if (GUI.Button(buttonRect, label, style)) RunRandomTurn();

        if (treasureController != null)
        {
            GUI.enabled = true;
            GUIStyle treasureStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(15f * uiScale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            string treasureLabel = treasureController.PlayerOneHandVisible
                ? "閉\nじ\nる\n◀"
                : "宝\nを\n見\nる\n▶";
            float treasureHeight = 155f * uiScale;
            Rect treasureButton = new Rect(treasureButtonOffset.x * uiScale,
                (Screen.height - treasureHeight) * 0.5f + treasureButtonOffset.y * uiScale,
                38f * uiScale, treasureHeight);
            global::CameraController cameraController = Camera.main != null
                ? Camera.main.GetComponent<global::CameraController>()
                : null;
            bool displayViewLocked = cameraController != null &&
                (cameraController.PlayerDisplayViewActive || cameraController.IsCameraMoving);
            GUI.enabled = !displayViewLocked;
            if (GUI.Button(treasureButton, treasureLabel, treasureStyle))
                treasureController.TogglePlayerOneHand();

            if (treasureController.PlayerOneHandVisible) HandleHandScrollInput();

            GUI.enabled = true;
            GUI.enabled = treasureController.Phase == TreasurePhase.Waiting;
            string freeLabel = treasureController.FreeInteractionMode ? "FREE：ON" : "FREE：OFF";
            if (GUI.Button(new Rect(Screen.width - 118f * uiScale, 14f * uiScale,
                104f * uiScale, 38f * uiScale), freeLabel, style))
                treasureController.ToggleFreeInteractionMode();
            if (GUI.Button(new Rect(Screen.width - 158f * uiScale, 60f * uiScale,
                144f * uiScale, 38f * uiScale), "P1 手札を全部展示", style))
                treasureController.DisplayAllTreasures(0);

            GUI.enabled = !turnRunning && (treasureController.Phase == TreasurePhase.Waiting ||
                treasureController.Phase == TreasurePhase.GameOver);
            float playerButtonY = 106f * uiScale;
            for (int count = 2; count <= 4; count++)
            {
                GUIStyle countStyle = new GUIStyle(style);
                if (treasureController.PlayerCount == count) countStyle.fontStyle = FontStyle.Bold;
                if (GUI.Button(new Rect(Screen.width - 158f * uiScale + (count - 2) * 48f * uiScale,
                    playerButtonY, 44f * uiScale, 34f * uiScale),
                    $"{count}人", countStyle))
                {
                    treasureController.RestartWithPlayerCount(count);
                    global::HandManager.SetPlayerCountGlobally(count);
                }
            }

            GUI.enabled = true;
            if (GUI.Button(new Rect(Screen.width - 158f * uiScale, 148f * uiScale,
                144f * uiScale, 38f * uiScale),
                "全カードをめくる", style))
                treasureController.TestRevealAllDisplayedTreasures();
        }

        GUI.enabled = true;
    }

    private void HandleHandScrollInput()
    {
        Event current = Event.current;
        Rect handArea = new Rect(40f, Screen.height * 0.43f, Screen.width - 55f, Screen.height * 0.42f);
        bool inside = handArea.Contains(current.mousePosition);

        if (inside && current.type == EventType.ScrollWheel)
        {
            float scroll = Mathf.Abs(current.delta.x) > Mathf.Abs(current.delta.y)
                ? current.delta.x : current.delta.y;
            treasureController.DragPlayerOneHand(scroll * 32f);
            handSlideVelocity = Mathf.Clamp(handSlideVelocity + scroll * 240f,
                -handMaxFlickSpeed, handMaxFlickSpeed);
            current.Use();
        }
        else if (inside && current.type == EventType.MouseDown && current.button == 0)
        {
            draggingHand = true;
            lastDragX = current.mousePosition.x;
            handSlideVelocity = 0f;
        }
        else if (draggingHand && current.type == EventType.MouseDrag && current.button == 0)
        {
            float deltaX = current.mousePosition.x - lastDragX;
            lastDragX = current.mousePosition.x;
            treasureController.DragPlayerOneHand(deltaX);
            float frameTime = Mathf.Max(0.005f, Time.unscaledDeltaTime);
            float instantVelocity = deltaX / frameTime;
            handSlideVelocity = Mathf.Clamp(
                Mathf.Lerp(handSlideVelocity, instantVelocity, 0.55f),
                -handMaxFlickSpeed, handMaxFlickSpeed);
            current.Use();
        }
        else if (current.rawType == EventType.MouseUp)
        {
            draggingHand = false;
        }
    }

    private void EnsureController()
    {
        if (treasureController == null)
            treasureController = FindFirstObjectByType<TreasureController>();
    }

    private IEnumerator Start()
    {
        if (!playOnStart) yield break;
        yield return new WaitForSeconds(startDelay);
        RunRandomTurn();
    }

    [ContextMenu("ランダムお宝ターン開始")]
    public void RunRandomTurn()
    {
        if (treasureController == null)
        {
            EnsureController();
            if (treasureController == null)
            {
                Debug.LogError("【お宝試作】TreasureControllerが見つかりません。");
                return;
            }
        }

        if (treasureController.Phase != TreasurePhase.Waiting)
        {
            Debug.LogWarning($"【お宝試作】現在は {treasureController.Phase} 中なので開始できません。");
            return;
        }
        if (treasureController.FreeInteractionMode) treasureController.ToggleFreeInteractionMode();

        var displayPlayers = new List<int>();
        var displayCounts = new List<int>();
        var robberPlayers = new List<int>();
        var robberyCounts = new List<int>();

        Debug.Log("<color=#70E8FF>========== ランダムお宝ターン ==========</color>");
        Debug.Log("【試作】P1も参加します。P1は手動操作、P2以降は自動操作です。");
        for (int playerId = 0; playerId < treasureController.PlayerCount; playerId++)
        {
            // 試作では約3分の1が怪盗、約3分の2が展示。
            bool robber = Random.value < (1f / 3f);
            if (!robber)
            {
                displayPlayers.Add(playerId);
                int displayCount = Random.Range(1, 4);
                displayCounts.Add(displayCount);
                Debug.Log($"【行動決定】プレイヤー{playerId + 1}：{displayCount}枚展示");
            }
            else
            {
                int count = Random.Range(1, 3);
                robberPlayers.Add(playerId);
                robberyCounts.Add(count);
                Debug.Log($"【行動決定】プレイヤー{playerId + 1}：怪盗（宣言 {count}枚）");
            }
        }

        turnRunning = true;
        StartCoroutine(RunPhases(displayPlayers, displayCounts, robberPlayers, robberyCounts));
    }

    private IEnumerator RunPhases(List<int> displays, List<int> displayCounts, List<int> robbers, List<int> counts)
    {
        if (displays.Count > 0)
        {
            treasureController.BeginDisplayPhase(displays.ToArray(), displayCounts.ToArray());
            yield return AutoPlayUntilPhaseChanges(TreasurePhase.SelectingDisplays);
            yield return new WaitUntil(() => treasureController.Phase == TreasurePhase.Waiting);
        }
        else
        {
            Debug.Log("【展示フェーズ】展示プレイヤーはいません。");
        }

        if (robbers.Count > 0)
        {
            treasureController.BeginRobberyPhase(robbers.ToArray(), counts.ToArray());
            while (treasureController.Phase != TreasurePhase.Waiting)
            {
                if (treasureController.Phase == TreasurePhase.Robbing ||
                    treasureController.Phase == TreasurePhase.RobberDisplay)
                {
                    if (treasureController.Phase == TreasurePhase.Robbing &&
                        treasureController.ActiveRobber != null &&
                        treasureController.ActiveRobber.PlayerId == 0)
                        yield return null;
                    else
                        yield return AutoPlayOneCard();
                }
                else
                    yield return null;
            }
        }
        else
        {
            Debug.Log("【怪盗フェーズ】怪盗プレイヤーはいません。展示場を整理します。");
            treasureController.BeginRobberyPhase(new int[0], new int[0]);
            yield return new WaitUntil(() => treasureController.Phase == TreasurePhase.Waiting);
        }

        Debug.Log("<color=#93E66C>========== お宝ターン完了 ==========</color>");
        turnRunning = false;
    }

    private IEnumerator AutoPlayUntilPhaseChanges(TreasurePhase phase)
    {
        while (treasureController.Phase == phase)
            yield return AutoPlayOneCard();
    }

    private IEnumerator AutoPlayOneCard()
    {
        var candidates = new List<Treasure>();
        foreach (Treasure card in FindObjectsByType<Treasure>(FindObjectsSortMode.None))
            if (card.Owner != null && treasureController.CanInteract(card) &&
                (treasureController.Phase == TreasurePhase.Robbing || card.Owner.PlayerId != 0))
                candidates.Add(card);

        if (candidates.Count > 0)
        {
            Treasure choice = candidates[Random.Range(0, candidates.Count)];
            Debug.Log($"【自動選択】P{choice.Owner.PlayerId + 1}：{choice.name}");
            treasureController.HandleTreasureClick(choice);
            yield return new WaitForSeconds(0.15f);
        }
        else
        {
            yield return null;
        }
    }
}
}
