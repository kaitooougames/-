using UnityEngine;

namespace TreasureGame
{
// 旧シーンとの参照互換用。ゲーム進行はTreasureControllerへ一本化した。
public class GameController : MonoBehaviour
{
    private void Awake()
    {
        enabled = false;
    }
}
}
