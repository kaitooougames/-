# TreasureGame 移植状況

## 現在の状態

`Treasuretest` で作成したお宝カード機能を、行動カード側の既存コードと衝突しない形で移植しています。

- お宝コードは `TreasureGame` 名前空間に隔離
- 行動カード側の既存 `Player`、`Treasure`、`TreasureController` は未変更
- カード画像、マテリアル、プレハブを専用フォルダへ移植
- 独立確認用シーンを `Scenes/TreasureScene.unity` として移植
- 行動カード側とお宝側を同時にコンパイルできることを確認済み

## コードから参照する名前

行動カード側には同名クラスが存在するため、統合コードでは名前空間を明示します。

```csharp
using TreasureGame;

public class ActionTreasureBridge : MonoBehaviour
{
    [SerializeField] private TreasureGame.TreasureController treasureController;
}
```

または、すべて完全修飾名で記述します。

```csharp
TreasureGame.TreasureController
TreasureGame.Treasure
TreasureGame.Player
TreasureGame.TreasurePhase
```

## フォルダ構成

```text
Assets/TreasureGame/
├── Scripts/     お宝カードの処理
├── Images/      宝画像とマテリアル
├── Prefabs/     宝プレハブ
├── Scenes/      独立確認用のお宝シーン
├── Settings/    お宝シーン用Volume設定
└── Docs/        行動カードとの統合ガイド
```

## 次の統合作業

1. `TreasureScene` のお宝管理オブジェクト、Player位置、展示位置、宝テンプレートを行動カードの `SampleScene` へ統合
2. `TreasureTurnPrototype` を無効化
3. `ActionTreasureBridge` を作成
4. 行動カード公開後に展示・怪盗・逮捕報酬情報をお宝側へ渡す
5. Player 2～4の自動選択をお宝カード選択にも接続
6. 2人・3人プレイ時の行動カード側参加者を制限

詳しい呼び出し順は `Docs/ACTION_CARD_INTEGRATION.md` を参照してください。

