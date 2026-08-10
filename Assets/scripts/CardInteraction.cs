using UnityEngine;
using System.Collections;

public enum SpecialActionEffect
{
    None,
    Truck,
    LargeTruck,
    Collector,
    Guard,
    EerieGuard,
    FakeCop,
    FoolishGuard,
    TransportVehicle,
    DisguiseMask,
    WireBelt,
    Balloon,
    FrameUp,
    SoloStage,
    BlackoutModule,
    TearGas,
    Watchdog,
    Detective,
    Prison,
    ElectricBaton,
    AnalysisGlasses,
    Appraiser,
    HoneyTrap,
    AdvanceNotice
}

public class CardInteraction : MonoBehaviour
{
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private Vector3 originalScale;
    private const float HoverScale = 1.04f;
    private const float SelectedThiefScale = 1.08f;
    private bool isMoving = false;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private float moveSpeed = 5f;
    private float rotateSpeed = 5f;
    private bool hasMoved = false;
    private HandManager handManager;
  
    private bool clickable = true;
    private Renderer[] visualRenderers;
    private bool[] visibilityBeforePrivacyHide;
    private bool privacyHidden;
    private Coroutine pendingPrivacyHide;
    private MaterialPropertyBlock clickAppearanceBlock;
    public bool isClickable
    {
        get => clickable;
        set
        {
            clickable = value;
            RefreshClickAppearance();
        }
    }
    private bool isFlipping = false;
    private float flipSpeed = 5f;
    public static event System.Action OnAllCardsFlipped; // 全カードがめくられた後のイベント
    private static int flippedCardCount = 0;
    private static int totalCards = 16; // **カードの総数（適宜変更）**

    public static void PrepareFlipCount(int count)
    {
        flippedCardCount = 0;
        totalCards = Mathf.Max(1, count);
    }
    // **怪盗カード用**
    public bool isPhantomThief = false;  // 怪盗カードかどうか
    public bool isExhibit = false;       // 展示カードかどうか
    public bool isCage = false;          // 檻カードかどうか
    [Header("特殊行動カード")]
    public SpecialActionEffect specialEffect = SpecialActionEffect.None;
    public bool IsSpecialAction => specialEffect != SpecialActionEffect.None;
    public int DisplayCount => specialEffect == SpecialActionEffect.LargeTruck ? 3 :
        (specialEffect == SpecialActionEffect.Truck ||
         specialEffect == SpecialActionEffect.Collector ||
         specialEffect == SpecialActionEffect.TransportVehicle ? 2 : 1);

    public bool AllowsDeclaredNumber(int number)
    {
        if (specialEffect == SpecialActionEffect.SoloStage) return number == 3;
        if (specialEffect == SpecialActionEffect.AdvanceNotice) return number == 10;
        if (specialEffect == SpecialActionEffect.BlackoutModule) return number >= 1 && number <= 4;
        if (specialEffect == SpecialActionEffect.TearGas) return number >= 1 && number <= 3;
        return number >= 1 && number <= 6;
    }

    public int RandomDeclaredNumber()
    {
        if (specialEffect == SpecialActionEffect.SoloStage) return 3;
        if (specialEffect == SpecialActionEffect.AdvanceNotice) return 10;
        if (specialEffect == SpecialActionEffect.BlackoutModule) return Random.Range(1, 5);
        if (specialEffect == SpecialActionEffect.TearGas) return Random.Range(1, 4);
        return Random.Range(1, 7);
    }
    public GameObject numberSelectionPanel; // 数字選択用のUIパネル
    private int selectedStealNumber = 0; // **選択した数字を保存**
    public StealNumberEffect effectPrefab; // **数字表示用のエフェクトプレハブ**
    private bool numberSelected = false;
    private bool pointerDown;
    private bool draggingHand;
    private bool selectedHoverLocked;
    private bool handPoseInitialized;
    private bool earlyRevealed;
    private int earlyRevealedDay = -1;
    private bool revealedOnTable;
    private Vector3 pointerDownPosition;
    private Vector3 lastPointerPosition;
    private const float HandDragThreshold = 12f;
    public int SelectedNumber { get; set; } // 怪盗カードの選択した数字（1~6）
    public Quaternion HandPoseRotation => originalRotation;
    private StealNumberEffect activeStealEffect; // 現在の怪盗宣言エフェクト

    private ArrestEffect activeArrestEffect; // 現在の逮捕エフェクト

    private static bool IsUsableScale(Vector3 scale) =>
        Mathf.Abs(scale.x) > 0.0001f && Mathf.Abs(scale.y) > 0.0001f &&
        Mathf.Abs(scale.z) > 0.0001f;

    private void CaptureOriginalPoseIfNeeded()
    {
        if (!IsUsableScale(originalScale) && IsUsableScale(transform.localScale))
            originalScale = transform.localScale;
        if (!IsUsableScale(originalScale))
        {
            CardInteraction[] cards = FindObjectsByType<CardInteraction>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (CardInteraction card in cards)
            {
                if (card == null || card == this || !IsUsableScale(card.originalScale)) continue;
                originalScale = card.originalScale;
                break;
            }
        }
        // 全カードが壊れた状態からでも、シーンの行動カード標準サイズへ復旧する。
        if (!IsUsableScale(originalScale))
            originalScale = new Vector3(0.064f, 1f, 0.088f);
    }

    public void EnsureVisibleForTable()
    {
        Transform parent = transform.parent;
        if (parent != null && !parent.gameObject.activeSelf)
            parent.gameObject.SetActive(true);
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        enabled = true;
        CaptureOriginalPoseIfNeeded();
        transform.localScale = selectedHoverLocked
            ? originalScale * SelectedThiefScale : originalScale;
        SetVisualVisible(true);
        // 手札で選択不可だったときの暗転用PropertyBlockを卓上へ持ち越さない。
        // 選択カードは裏向きで移動中でも常に通常の明るさにする。
        SetClickBrightness(1f);
    }

    public Vector3 VisualScale => transform.localScale;

    public void SetVisualVisible(bool visible)
    {
        if (visible && pendingPrivacyHide != null)
        {
            StopCoroutine(pendingPrivacyHide);
            pendingPrivacyHide = null;
        }
        if (visualRenderers == null || visualRenderers.Length == 0)
            visualRenderers = GetComponentsInChildren<Renderer>(true);
        if (!visible)
        {
            if (privacyHidden) return;
            visibilityBeforePrivacyHide = new bool[visualRenderers.Length];
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                Renderer visualRenderer = visualRenderers[i];
                if (visualRenderer == null) continue;
                visibilityBeforePrivacyHide[i] = visualRenderer.enabled;
                visualRenderer.enabled = false;
            }
            privacyHidden = true;
            return;
        }

        // 旧オンライン非表示処理がfalseを記録している場合でも、
        // 表示要求では保存値を使わず全Rendererを必ず有効化する。
        foreach (Renderer visualRenderer in visualRenderers)
            if (visualRenderer != null) visualRenderer.enabled = true;
        privacyHidden = false;
    }

    public void MoveToHidden(Vector3 newPosition, float speed = 5f)
    {
        if (pendingPrivacyHide != null) StopCoroutine(pendingPrivacyHide);
        SetVisualVisible(true);
        MoveTo(newPosition, speed);
        pendingPrivacyHide = StartCoroutine(HideAfterMove());
    }

    private IEnumerator HideAfterMove()
    {
        while (isMoving) yield return null;
        pendingPrivacyHide = null;
        SetVisualVisible(false);
    }

    public bool IsCageCard()
    {
        return isCage; // isCage フィールドが true の場合、「檻」カード
    }


    private void Awake()
    {
        // Awakeは全GameObjectでStartより先に完了する。
        // HandManager.Startが相手カードを収納してもScaleを失わないよう、ここで必ず保存する。
        originalPosition = transform.position;
        originalRotation = transform.rotation;
        originalScale = transform.localScale;
        visualRenderers = GetComponentsInChildren<Renderer>(true);
        clickAppearanceBlock = new MaterialPropertyBlock();
        RefreshClickAppearance();
    }

    void Start()
    {
        // 動的生成カードはInitializeHandPoseで先に基準位置を設定済み。
        // 手札へ移動中の座標で上書きすると、最初のホバー時だけカードが飛んでしまう。
        if (!handPoseInitialized)
        {
            originalPosition = transform.position; // 初期位置を保持
            originalRotation = transform.rotation;
            originalScale = transform.localScale;
        }
        if (numberSelectionPanel) numberSelectionPanel.SetActive(false);
    }

    private void RefreshClickAppearance()
    {
        float brightness = clickable ? 1f : 0.32f;
        SetClickBrightness(brightness);
    }

    void Update()
    {
        if (selectedHoverLocked)
            transform.localScale = originalScale * SelectedThiefScale;

        if (isMoving)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * moveSpeed);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * rotateSpeed);

            if (Vector3.Distance(transform.position, targetPosition) < 0.01f &&
                Quaternion.Angle(transform.rotation, targetRotation) < 1f)
            {
                isMoving = false;
                transform.position = targetPosition;  // 位置を強制的に合わせる
            }
        }

        if (isFlipping)
        {
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * flipSpeed);
            if (Quaternion.Angle(transform.rotation, targetRotation) < 1f)
            {
                transform.rotation = targetRotation; // ぴったり合わせる
                isFlipping = false;
            }
        }
    }

    void OnMouseEnter()
    {
        if (!revealedOnTable && !isMoving && !hasMoved && isClickable)
        {
            // カードを少し浮かせるだけ
            transform.position = originalPosition + new Vector3(0, 0.2f, 0);
            transform.localScale = originalScale * HoverScale;
        }
    }

    void OnMouseExit()
    {
        if (!hasMoved) transform.position = originalPosition;
        transform.localScale = selectedHoverLocked ? originalScale * SelectedThiefScale : originalScale;
    }

    void OnMouseDown()
    {
        if (!isClickable || isMoving) return;
        if (handManager != null && handManager.CardSelected) return;
        pointerDown = true;
        handManager?.BeginActionCardGrip();
        draggingHand = false;
        pointerDownPosition = Input.mousePosition;
        lastPointerPosition = pointerDownPosition;
    }

    void OnMouseDrag()
    {
        if (!pointerDown || handManager == null) return;
        Vector3 current = Input.mousePosition;
        if (!draggingHand && Vector3.Distance(current, pointerDownPosition) >= HandDragThreshold)
            draggingHand = true;
        if (draggingHand)
            handManager.ScrollActionHandByPixels(current.x - lastPointerPosition.x);
        lastPointerPosition = current;
    }

    void OnMouseUp()
    {
        if (!pointerDown) return;
        pointerDown = false;
        handManager?.EndActionCardGrip();
        if (draggingHand)
        {
            draggingHand = false;
            return;
        }
        PerformClick();
    }

    private void PerformClick()
    {
        if (!isClickable || isMoving) return; // クリック不可・移動中なら無視
        if (handManager != null && handManager.CardSelected) return;

        if (SpecialActionCardSystem.TryGetActiveAdvanceNotice(0,
                out CardInteraction activeAdvanceNotice) && this != activeAdvanceNotice)
        {
            Debug.Log("【予告状実行日】Player1はほかの行動カードを選べません。");
            return;
        }

        Player player = FindObjectOfType<Player>();
        if (player == null || player.isEliminated) return;
        if (!SpecialActionCardSystem.CanSelect(0, this))
        {
            Debug.Log(isCage
                ? "2人用ルールにより、檻を4日連続で出すことはできません。"
                : "現在の効果により、この行動カードは選べません。");
            return;
        }

        if (isPhantomThief)
        {
            if (player.IsFirstOffense())  // ← 🔹 プレイヤーが初犯なら
            {
                Debug.Log("初犯のため怪盗カードをクリックできません。");
                return;
            }

            CardClickAudio.Play();

            if (isClickable)
            {
                selectedHoverLocked = true;
                LockSelectedThiefScale();
                if (specialEffect == SpecialActionEffect.AdvanceNotice)
                {
                    // 予告状は宣言数10固定なので数字選択を表示せず、そのまま確定する。
                    OnNumberSelected(10);
                    // 予告状を出した日から翌日の実行終了まで、ほかの行動手札は収納する。
                    handManager?.SetActionHandVisible(false);
                    if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession) return;
                    // 通常の怪盗は数字パネル確定後にここを通るが、予告状は即決定で
                    // PerformClickを抜けるため、ほかのプレイヤー選択を明示的に開始する。
                    FindObjectOfType<Player2>()?.SelectRandomCard();
                    FindObjectOfType<Player3>()?.SelectRandomCard();
                    FindObjectOfType<Player4>()?.SelectRandomCard();
                    return;
                }
                ShowNumberSelection();
                DisableClick(false);
            }
        }
        else // 展示カードや檻カードなど
        {
            CardClickAudio.Play();
            // 🔽 怪盗関連の選択値をリセット
            selectedStealNumber = 0;
            numberSelected = false;

            MoveToCenter(player.SelectedNumber);
            player.SelectCard(this, 0);
            SpecialActionCardSystem.NotifySelected(0, this);
            DisableClick(false);
        }

        handManager.SelectCard(this); // ハンドUIの選択管理

        if (KaitouOnline.KaitouOnlineGameBridge.IsOnlineSession)
        {
            if (!isPhantomThief)
                KaitouOnline.KaitouOnlineGameBridge.SubmitLocalAction(this, 0);
            return;
        }

        // 他のプレイヤーのランダムカード選択
        FindObjectOfType<Player2>()?.SelectRandomCard();
        FindObjectOfType<Player3>()?.SelectRandomCard();
        FindObjectOfType<Player4>()?.SelectRandomCard();
    }


    public void MoveTo(Vector3 newPosition, float speed = 5f) // デフォルト速度は5
    {
        CaptureOriginalPoseIfNeeded();
        transform.localScale = selectedHoverLocked ? originalScale * SelectedThiefScale : originalScale;
        Debug.Log(gameObject.name + " is moving to " + newPosition);
        targetPosition = newPosition;
        targetRotation = originalRotation;
        isMoving = true;
        moveSpeed = speed; // スピードを変更可能にする
                           // **新しい位置を originalPosition に更新**
        originalPosition = newPosition;
    }

    public void MoveToImmediate(Vector3 newPosition)
    {
        CaptureOriginalPoseIfNeeded();
        originalPosition = newPosition;
        targetPosition = newPosition;
        transform.position = newPosition;
        transform.rotation = originalRotation;
        transform.localScale = selectedHoverLocked ? originalScale * SelectedThiefScale : originalScale;
        isMoving = false;
    }

    public void CompleteCurrentMoveImmediately()
    {
        transform.position = targetPosition;
        transform.rotation = targetRotation;
        isMoving = false;
    }


    public void MoveToInspection(Vector3 newPosition, Quaternion newRotation, float speed = 7f)
    {
        transform.localScale = originalScale;
        targetPosition = newPosition;
        targetRotation = newRotation;
        isMoving = true;
        moveSpeed = speed;
    }


    public void MoveToCenter(int selectedNumber)
    {
        if (!isMoving && !hasMoved)
        {
            hasMoved = true;
            isMoving = true;
            targetPosition = new Vector3(0, 0, -1);
            targetRotation = Quaternion.Euler(0, 180, 180);
            handManager.SelectCard(this); // 🔹 選択した数字を渡す

            originalPosition = targetPosition;
        }
    }


    public void SetHandManager(HandManager manager)
    {
        handManager = manager;
    }

    public void InitializeHandPose(Vector3 position, Quaternion rotation)
    {
        handPoseInitialized = true;
        transform.position = position;
        transform.rotation = rotation;
        originalPosition = position;
        originalRotation = rotation;
        CaptureOriginalPoseIfNeeded();
        transform.localScale = originalScale;
        targetPosition = position;
        targetRotation = rotation;
        isMoving = false;
        isFlipping = false;
        hasMoved = false;
    }

    public void LockSelectedThiefScale()
    {
        if (!isPhantomThief) return;
        selectedHoverLocked = true;
        transform.localScale = originalScale * SelectedThiefScale;
    }

    public void DisableClick(bool dim = true)
    {
        isClickable = false;
        if (!dim) SetClickBrightness(1f);
    }

    public void EnableClick()
    {
        isClickable = true;
    }

    public void ResetForNextActionSelection()
    {
        if (pendingPrivacyHide != null)
        {
            StopCoroutine(pendingPrivacyHide);
            pendingPrivacyHide = null;
        }
        pointerDown = false;
        draggingHand = false;
        hasMoved = false;
        numberSelected = false;
        selectedStealNumber = 0;
        selectedHoverLocked = false;
        revealedOnTable = false;
        earlyRevealed = false;
        earlyRevealedDay = -1;
        transform.localScale = originalScale;
        SetVisualVisible(true);
        EnableClick();
    }


    // **数字がクリックされたときに呼ばれる関数**
    public void OnNumberSelected(int number)
    {
        if (numberSelected) return;

        if (!isPhantomThief)
        {
            Debug.LogWarning("怪盗カードでないのに数字が選ばれようとしました: " + gameObject.name);
            return;
        }

        Debug.Log("選択された数字: " + number);
        numberSelected = true;

        Player player = FindObjectOfType<Player>();
        if (player != null)
        {
            player.SelectCard(this, number);
        }
        SpecialActionCardSystem.NotifySelected(0, this);

        if (numberSelectionPanel) numberSelectionPanel.SetActive(false);

        selectedStealNumber = number;  // ✅ ここは怪盗カードだけが来るようになった
        SelectedNumber = number;
        MoveToCenter(player.SelectedNumber);

        if (KaitouOnline.KaitouOnlineGameBridge.SubmitLocalAction(this, number))
            return;

        CameraController cameraController = Camera.main.GetComponent<CameraController>();
        if (cameraController != null)
        {
            cameraController.MoveCamera();
        }
    }

    // 怪盗カードで選択した数字を設定
    public void SetSelectedNumber(int number)
    {
        if (isPhantomThief)
        {
            SelectedNumber = number;
        }
    }

    public void FlipCard()
    {
        EnsureVisibleForTable();
        revealedOnTable = true;
        selectedHoverLocked = false;
        transform.localScale = originalScale;
        Debug.Log("FlipCard() が呼ばれた: " + Time.frameCount);
        Debug.Log("isPhantomThief: " + isPhantomThief + ", selectedStealNumber: " + selectedStealNumber);

        // 選択不可の暗転は手札にある間だけ使用する。
        // 公開後の裏面まで暗くならないよう、反転開始時に見た目だけ通常へ戻す。
        int currentDay = HandManager.Instance != null
            ? HandManager.Instance.CurrentDay : -1;
        bool revealedEarlierToday = earlyRevealed && earlyRevealedDay == currentDay;
        if (!revealedEarlierToday)
        {
            earlyRevealed = false;
            earlyRevealedDay = -1;
            SetClickBrightness(1f);
            targetRotation = Quaternion.Euler(transform.rotation.eulerAngles.x + 180,
                transform.rotation.eulerAngles.y + 180, transform.rotation.eulerAngles.z);
            isFlipping = true;
        }

        flippedCardCount++;

        // **すべてのカードがめくられたらイベント発火**
      
        if (flippedCardCount >= totalCards)
        {
            Debug.Log("OnAllCardsFlipped.Invoke() を呼ぶ直前");
            OnAllCardsFlipped?.Invoke();
            flippedCardCount = 0; // **リセット**

            // ✅ プレイヤーに「カード全公開後」の処理を通知
            Player player = FindObjectOfType<Player>();
            if (player != null)
            {
                player.OnCardsRevealed();
            }
        }

    }

    public void RevealBeforeAllCards(bool dimAfterReveal = false)
    {
        if (earlyRevealed) return;
        revealedOnTable = true;
        selectedHoverLocked = false;
        transform.localScale = originalScale;
        earlyRevealed = true;
        earlyRevealedDay = HandManager.Instance != null
            ? HandManager.Instance.CurrentDay : -1;
        targetRotation = Quaternion.Euler(transform.rotation.eulerAngles.x + 180,
            transform.rotation.eulerAngles.y + 180, transform.rotation.eulerAngles.z);
        isFlipping = true;
        if (dimAfterReveal) SetClickBrightness(0.32f);
    }

    public IEnumerator BlinkAsDetectiveTarget(int blinkCount = 3)
    {
        for (int i = 0; i < blinkCount; i++)
        {
            SetClickBrightness(0.22f);
            yield return new WaitForSeconds(0.16f);
            SetClickBrightness(1f);
            yield return new WaitForSeconds(0.16f);
        }
    }

    private void SetClickBrightness(float brightness)
    {
        if (visualRenderers == null || clickAppearanceBlock == null) return;

        // 通常表示へ戻す場合は白色を上書きするのではなく、
        // 暗転用のPropertyBlock自体を外して元マテリアルを復元する。
        if (brightness >= 0.999f)
        {
            foreach (Renderer visualRenderer in visualRenderers)
            {
                if (visualRenderer == null) continue;
                int materialCount = Mathf.Max(1, visualRenderer.sharedMaterials.Length);
                for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
                    visualRenderer.SetPropertyBlock(null, materialIndex);
            }
            return;
        }

        Color tint = new Color(brightness, brightness, brightness, 1f);
        foreach (Renderer visualRenderer in visualRenderers)
        {
            if (visualRenderer == null) continue;
            int materialCount = Mathf.Max(1, visualRenderer.sharedMaterials.Length);
            for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
            {
                clickAppearanceBlock.Clear();
                visualRenderer.GetPropertyBlock(clickAppearanceBlock, materialIndex);
                clickAppearanceBlock.SetColor("_BaseColor", tint);
                clickAppearanceBlock.SetColor("_Color", tint);
                visualRenderer.SetPropertyBlock(clickAppearanceBlock, materialIndex);
            }
        }
    }


    public void ShowNumberSelection()
    {
        if (!isPhantomThief)
        {
            Debug.Log("怪盗カードではないので数字選択を表示しません: " + gameObject.name);
            return;
        }

        if (numberSelectionPanel)
        {
            numberSelectionPanel.SetActive(true);

            NumberButton[] buttons = numberSelectionPanel.GetComponentsInChildren<NumberButton>(true);
            foreach (var button in buttons)
            {
                button.gameObject.SetActive(AllowsDeclaredNumber(button.number));
                button.SetPhantomThiefCard(this);
            }
        }
    }


    private void ShowStealNumber()
    {
        if (selectedStealNumber == 0) return;
        EffectManager.Instance.ShowStealNumber(selectedStealNumber);
    }


    public void MoveCardsAfterThiefPhase()
    {
        EffectManager.Instance.ClearStealNumber();
        selectedStealNumber = 0; // 🔹 宣言された数字をリセット
        earlyRevealed = false;
        earlyRevealedDay = -1;
        revealedOnTable = false;
    }



    public void EnableCardClicks()
    {
        CardInteraction[] allCards = FindObjectsOfType<CardInteraction>();
        foreach (var card in allCards)
        {
            // 翌日実行待ちの予告状は、卓上の角度・位置・宣言表示をそのまま維持する。
            bool pendingAdvanceNotice = false;
            for (int seat = 0; seat < 4; seat++)
            {
                if (!SpecialActionCardSystem.IsAdvanceNoticePendingCard(seat, card)) continue;
                pendingAdvanceNotice = true;
                break;
            }
            if (pendingAdvanceNotice) continue;
            card.isClickable = true;  // クリックを再度有効化
            card.hasMoved = false;    // **移動フラグをリセット**
            card.numberSelected = false; // **数字選択フラグをリセット**
            card.selectedHoverLocked = false;
            card.transform.localScale = card.originalScale;
            card.transform.position = card.originalPosition; // **元の位置に戻す**
            card.transform.rotation = card.originalRotation; // **元の回転に戻す**
            Debug.Log(card.gameObject.name + " がクリックできるようになった！");
        }
    }

}
