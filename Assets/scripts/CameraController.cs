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
        yield return StartCoroutine(MoveCameraCoroutine(secondTargetPosition, secondTargetRotation));

        yield return new WaitForSeconds(1f);
        TriggerSecurityDice();

        isCameraMoving = false;
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
        yield return new WaitForSeconds(4f); // ⏳ 3秒待つ
        kaitou();
    }

    public void kaitou()
    {
        Debug.Log("怪盗フェーズ終了！");
        handManager.MoveCardsAfterThiefPhase();
       
        cardInteraction.MoveCardsAfterThiefPhase();
        Player.MoveCardsAfterThiefPhase();
        Player2.MoveCardsAfterThiefPhase();
        Player3.MoveCardsAfterThiefPhase();
        Player4.MoveCardsAfterThiefPhase();
        arrestEffect.MoveCardsAfterThiefPhase();

        StartCoroutine(Wait());
       
    }
    private IEnumerator Wait()
    {
        yield return new WaitForSeconds(2f); // ⏳ 3秒待つ
        cardInteraction.EnableCardClicks(); // カードクリック再開
        isFlipping = false;
    }
}
