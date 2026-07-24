# TreasureGame 移植状況

## 現在の状態

`Treasuretest` で作成したお宝カード機能を、行動カード側の既存コードと衝突しない形で移植しています。

- お宝コードは `TreasureGame` 名前空間に隔離
- 行動カード側の既存 `Player`、`Treasure`、`TreasureController` は未変更
- カード画像、マテリアル、プレハブを専用フォルダへ移植
- 独立確認用シーンを `Scenes/TreasureScene.unity` として移植
- 行動カードの環境を土台にした統合シーンを `Assets/Scenes/IntegratedGameScene.unity` として作成
- 行動カード側とお宝側を同時にコンパイルできることを確認済み

## 本番用の統合シーン

`Assets/Scenes/IntegratedGameScene.unity` を今後の本番シーンとして使用します。

このシーンでは、次の環境は行動カード側のものを維持しています。

- テーブル・床
- Main Camera
- Directional Light
- Global Volume
- GameManager・BGM
- 行動カード、逮捕、警備サイコロ、既存UI

お宝側から追加したものは次のとおりです。

- `TreasureGame Controller`
- `TreasurePlayer1`～`TreasurePlayer4`
- 宝の手札位置と展示位置
- 宝カードのシーンテンプレート

`TreasureTurnPrototype` は無効化済みです。
元の `Assets/Scenes/SampleScene.unity` と、独立確認用の `TreasureScene.unity` は変更せず残しています。

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

1. `ActionTreasureBridge` を作成
2. 行動カード公開後に展示・怪盗・逮捕報酬情報をお宝側へ渡す
3. Player 2～4の自動選択をお宝カード選択にも接続
4. 2人・3人プレイ時の行動カード側参加者を制限

怪盗配列には、逮捕判定と警備サイコロ判定を通過した
`HasBeenArrested == false` のプレイヤーだけを含めます。
逮捕された怪盗は盗み処理へ進ませません。

詳しい呼び出し順は `Docs/ACTION_CARD_INTEGRATION.md` を参照してください。
