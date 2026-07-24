using UnityEngine;

public class Treasure : MonoBehaviour
{
    public string Type { get; private set; }       // 宝の種類（金、絵画、宝石、遺物）
    public string Authenticity { get; private set; } // 本物か偽物か

    // Treasureの初期化
    public void Initialize(string type, string authenticity)
    {
        Type = type;
        Authenticity = authenticity;

        // ここで、例えばカードの外観を変更する処理を追加できます
        // 例えば、Typeによって異なるテクスチャを設定するなど
    }
}
