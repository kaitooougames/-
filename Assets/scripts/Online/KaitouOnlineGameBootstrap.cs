using UnityEngine;
using UnityEngine.SceneManagement;

namespace KaitouOnline
{
    // MainMenuからオンライン本編へ入った時に、部屋人数をローカル盤へ反映する入口。
    // ゲーム操作の送受信は、このBootstrapから段階的にオンラインBridgeへ移す。
    public static class KaitouOnlineGameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplyOnlineRoom()
        {
            KaitouOnlineSession session = KaitouOnlineSession.Instance;
            if (session == null || !session.IsOnline) return;
            if (SceneManager.GetActiveScene().name != "IntegratedGameScene") return;
            ApplyOnlineRoomNow(session);
        }

        public static void ApplyOnlineRoomNow(KaitouOnlineSession session)
        {
            if (session == null || !session.IsOnline) return;
            if (SceneManager.GetActiveScene().name != "IntegratedGameScene") return;

            if (session.GameSeed != 0) Random.InitState(session.GameSeed);

            // 宝配布にも同じ席変換を使うため、人数反映より先にBridgeを用意する。
            if (Object.FindFirstObjectByType<KaitouOnlineGameBridge>() == null)
                new GameObject("KaitouOnlineGameBridge").AddComponent<KaitouOnlineGameBridge>();

            HandManager.SetPlayerCountGlobally(session.RoomPlayerCount);
            TreasureGame.TreasureController treasure =
                Object.FindFirstObjectByType<TreasureGame.TreasureController>();
            treasure?.PreparePlayerCount(session.RoomPlayerCount);

            Debug.Log($"<color=#70E8FF>【オンライン盤】P{session.LocalSeat + 1}として参加。"+
                      $"部屋人数：{session.RoomPlayerCount}</color>");
        }
    }
}
