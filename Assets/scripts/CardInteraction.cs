using UnityEngine;

public class CardInteraction : MonoBehaviour
{
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private bool isMoving = false;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private float moveSpeed = 5f;
    private float rotateSpeed = 5f;
    private bool hasMoved = false;
    private HandManager handManager;
  
    public bool isClickable { get; set; } = true; // 🔹 アクセス修正
    private bool isFlipping = false;
    private float flipSpeed = 5f;
    public static event System.Action OnAllCardsFlipped; // 全カードがめくられた後のイベント
    private static int flippedCardCount = 0;
    private static int totalCards = 16; // **カードの総数（適宜変更）**
    // **怪盗カード用**
    public bool isPhantomThief = false;  // 怪盗カードかどうか
    public bool isExhibit = false;       // 展示カードかどうか
    public bool isCage = false;          // 檻カードかどうか
    public GameObject numberSelectionPanel; // 数字選択用のUIパネル
    private int selectedStealNumber = 0; // **選択した数字を保存**
    public StealNumberEffect effectPrefab; // **数字表示用のエフェクトプレハブ**
    private bool numberSelected = false;
    public int SelectedNumber { get; set; } // 怪盗カードの選択した数字（1~6）
    private StealNumberEffect activeStealEffect; // 現在の怪盗宣言エフェクト

    private ArrestEffect activeArrestEffect; // 現在の逮捕エフェクト

    public bool IsCageCard()
    {
        return isCage; // isCage フィールドが true の場合、「檻」カード
    }


    void Start()
    {
        originalPosition = transform.position; // 初期位置を保持
        originalRotation = transform.rotation;
        if (numberSelectionPanel) numberSelectionPanel.SetActive(false);
    }

    void Update()
    {
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
        if (!isMoving && !hasMoved && isClickable)
        {
            // カードを少し浮かせるだけ
            transform.position = originalPosition + new Vector3(0, 0.2f, 0);
        }
    }

    void OnMouseExit()
    {
        if (!isMoving && !hasMoved && isClickable)
        {
            transform.position = originalPosition;
        }
    }

    void OnMouseDown()
    {
        if (!isClickable || isMoving) return; // クリック不可・移動中なら無視

        Player player = FindObjectOfType<Player>();
        if (player == null) return;

        if (isPhantomThief)
        {
            if (player.IsFirstOffense())  // ← 🔹 プレイヤーが初犯なら
            {
                Debug.Log("初犯のため怪盗カードをクリックできません。");
                return;
            }

            if (isClickable)
            {
                ShowNumberSelection();
                isClickable = false;
            }
        }
        else // 展示カードや檻カードなど
        {
            // 🔽 怪盗関連の選択値をリセット
            selectedStealNumber = 0;
            numberSelected = false;

            MoveToCenter(player.SelectedNumber);
            player.SelectCard(this, 0);
            isClickable = false;
        }

        handManager.SelectCard(this); // ハンドUIの選択管理

        // 他のプレイヤーのランダムカード選択
        FindObjectOfType<Player2>()?.SelectRandomCard();
        FindObjectOfType<Player3>()?.SelectRandomCard();
        FindObjectOfType<Player4>()?.SelectRandomCard();
    }


    public void MoveTo(Vector3 newPosition, float speed = 5f) // デフォルト速度は5
    {
        Debug.Log(gameObject.name + " is moving to " + newPosition);
        targetPosition = newPosition;
        targetRotation = originalRotation;
        isMoving = true;
        moveSpeed = speed; // スピードを変更可能にする
                           // **新しい位置を originalPosition に更新**
        originalPosition = newPosition;
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

    public void DisableClick()
    {
        isClickable = false;
    }

    public void EnableClick()
    {
        isClickable = true;
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

        if (numberSelectionPanel) numberSelectionPanel.SetActive(false);

        selectedStealNumber = number;  // ✅ ここは怪盗カードだけが来るようになった
        MoveToCenter(player.SelectedNumber);

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
        Debug.Log("FlipCard() が呼ばれた: " + Time.frameCount);
        Debug.Log("isPhantomThief: " + isPhantomThief + ", selectedStealNumber: " + selectedStealNumber);


        targetRotation = Quaternion.Euler(transform.rotation.eulerAngles.x + 180, transform.rotation.eulerAngles.y + 180, transform.rotation.eulerAngles.z);
        isFlipping = true;

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

            NumberButton[] buttons = numberSelectionPanel.GetComponentsInChildren<NumberButton>();
            foreach (var button in buttons)
            {
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
    }



    public void EnableCardClicks()
    {
        CardInteraction[] allCards = FindObjectsOfType<CardInteraction>();
        foreach (var card in allCards)
        {
            card.isClickable = true;  // クリックを再度有効化
            card.hasMoved = false;    // **移動フラグをリセット**
            card.numberSelected = false; // **数字選択フラグをリセット**
            card.transform.position = card.originalPosition; // **元の位置に戻す**
            card.transform.rotation = card.originalRotation; // **元の回転に戻す**
            Debug.Log(card.gameObject.name + " がクリックできるようになった！");
        }
    }

}