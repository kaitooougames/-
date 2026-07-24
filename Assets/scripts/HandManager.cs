using UnityEngine;
using System.Collections.Generic;

public class HandManager : MonoBehaviour
{
    public List<CardInteraction> cards; // 手札のカードリスト
    private bool cardSelected = false;  // すでにカードを選んだか
   


    void Start()
    {
        SetInitialPositions(); // 初期位置を設定
        Invoke("ArrangeHand", 1.0f); // 1秒後に手札を並べる
    }

    // **カードの初期位置を (0,3,-4) に設定**
    void SetInitialPositions()
    {
        foreach (var card in cards)
        {
            card.transform.position = new Vector3(0, 3, -4);
        }
    }

    void ArrangeHand()
    {
        float[] basePositions = { -0.96f, -0.32f, 0.32f, 0.96f };

        for (int i = 0; i < cards.Count; i++)
        {
            float xPos = basePositions[i % basePositions.Length];
            Vector3 position = new Vector3(xPos, 2f, -2.5f);
            cards[i].MoveTo(position);
            cards[i].SetHandManager(this);

            // **最初の怪盗カードを見つけたら isPhantomThief を設定**
            if (cards[i].isPhantomThief)
            {
                Debug.Log("怪盗カードがセットされました: " + cards[i].name);
            }
        }
    }
    public void SelectCard(CardInteraction selectedCard)
    {
        if (cardSelected)
        {
            Debug.Log("すでにカードが選択されています: " + selectedCard.name);
            return; // すでにカードを選んでいたら無視
        }
        cardSelected = true;
        Debug.Log("カード選択: " + selectedCard.name);

        // **怪盗カードでなければカメラを移動**
        if (!selectedCard.isPhantomThief)
        {
            Camera.main.GetComponent<CameraController>().MoveCamera();
        }


        // **他のカードを初期位置に戻す**
        foreach (var card in cards)
        {
            if (card != selectedCard)
            {
                Debug.Log(card.name + " を初期位置に戻す");
                card.MoveTo(new Vector3(0, 3, -4)); // 初期位置に戻す
                card.DisableClick(); // クリックを無効化

            }
        }

    }

    public void MoveCardsAfterThiefPhase()
    {

        Start();
        cardSelected = false;
        Debug.Log("2日目開始！");
    }

}

