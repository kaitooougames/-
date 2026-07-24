using UnityEngine;

public class EffectManager : MonoBehaviour
{
    public static EffectManager Instance { get; private set; }

    public StealNumberEffect effectPrefab;
    private StealNumberEffect currentEffect;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void ShowStealNumber(int number)
    {
        
        ClearStealNumber(); // 先に削除
        currentEffect = Instantiate(effectPrefab, new Vector3(0.7f, 0f, -1.17f), Quaternion.identity);
        currentEffect.ShowNumber(number);
    }

    public void ClearStealNumber()
    {
        if (currentEffect != null)
        {
            Destroy(currentEffect.gameObject);
            currentEffect = null;
        }
        
    }
}
