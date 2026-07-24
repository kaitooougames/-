# 行動カードUnity・お宝カードUnity 統合ガイド

> `kaitooougame3` への移植版は `TreasureGame` 名前空間に入っています。
> 統合コードでは `TreasureGame.TreasureController` を参照してください。

## 1. この資料の目的

このプロジェクトのお宝カード処理を、別Unityプロジェクトで完成している行動カード処理から呼び出すための引き継ぎ資料です。

統合時は、行動カード側が「誰が何の行動を選んだか」を決定し、お宝側の `TreasureController` にプレイヤー番号と枚数を渡します。

## 2. 現在のお宝ターンの流れ

1. ターン開始
2. 通常展示・特殊展示を行うプレイヤーがカードを選択
3. 全展示プレイヤーの選択完了後、一斉に展示
4. 怪盗を宣言したプレイヤーが、宣言数の大きい順に盗む
5. 各怪盗プレイヤーは、盗んだカードから1枚選んで展示
6. 怪盗全員の選択完了後、一斉に展示
7. 檻の逮捕成功者がいる場合、報酬として1枚展示
8. 展示場の盗難跡を詰めて整理
9. 勝利判定
10. 勝者がいなければ次ターンへ

## 3. プレイヤー番号

コード上のプレイヤー番号は **0始まり** です。

| ゲーム上の表記 | コードへ渡す番号 |
|---|---:|
| Player 1 | `0` |
| Player 2 | `1` |
| Player 3 | `2` |
| Player 4 | `3` |

`TreasureController.PlayerCount` で現在の参加人数を取得できます。

## 4. 行動カード側から使用する主なAPI

### ターン開始

```csharp
treasureController.BeginTurn();
```

選択内容、怪盗情報、逮捕報酬予約など、前ターンの一時データを初期化します。

お宝側は勝者がいなかった場合、ターン終了処理の最後にも自動で `BeginTurn()` を呼びます。統合側で二重に呼ばないよう、最終的にどちらがターン開始を管理するか決めてください。

### 通常展示（基本1枚）

```csharp
int[] displayPlayers = { 0, 2 };
treasureController.BeginDisplayPhase(displayPlayers);
```

配列に含まれる各プレイヤーが1枚ずつ選択します。全員が選び終わると、自動で一斉展示されます。

手札が0枚のプレイヤーは自動でパスします。

### 特殊行動カードによる2枚・3枚展示

```csharp
int[] displayPlayers = { 0, 2 };
int[] displayCounts  = { 2, 3 };
treasureController.BeginDisplayPhase(displayPlayers, displayCounts);
```

同じ位置にある値が対応します。この例ではPlayer 1が2枚、Player 3が3枚展示します。

- 指定枚数が手札枚数を超えた場合は、残り手札枚数までに補正されます。
- 2枚以上を一度に展示したプレイヤーは、そのターンには勝利できません。
- 金を展示したプレイヤーも、そのターンには勝利できません。
- 特殊な2枚・3枚展示行動カードは、行動カード側ではまだ未実装です。

### 怪盗

```csharp
int[] robberPlayers = { 0, 1, 3 };
int[] robberyCounts = { 2, 6, 3 };
treasureController.BeginRobberyPhase(robberPlayers, robberyCounts);
```

怪盗数は1～6です。お宝側で範囲内に補正されます。

**逮捕された怪盗は盗むことができません。**
行動カード側で逮捕判定と警備サイコロ判定がすべて完了した後、
`HasBeenArrested == false` の怪盗だけを `robberPlayers` と `robberyCounts` に入れてください。
逮捕された怪盗をお宝側の `BeginRobberyPhase()` へ渡してはいけません。

実行順はお宝側で自動的に並べ替えられます。

1. 宣言数が大きいプレイヤー
2. 宣言数が同じ場合はPlayer番号が小さいプレイヤー

各怪盗プレイヤーが宣言数分を盗み終わってから、次の怪盗プレイヤーへ進みます。盗める展示品がない場合は自動でパスします。

怪盗全員の盗みが終わると、実際に盗品を得た怪盗プレイヤーは、盗品の中から1枚を選んで展示します。

### 怪盗が0人のターン

```csharp
treasureController.BeginRobberyPhase(
    System.Array.Empty<int>(),
    System.Array.Empty<int>()
);
```

怪盗フェーズを空で開始すると、逮捕報酬があれば逮捕報酬展示へ、なければターン終了処理へ進みます。

### 檻の逮捕成功報酬

```csharp
int[] arrestedRewardPlayers = { 1, 3 };
treasureController.QueueArrestRewardDisplays(arrestedRewardPlayers);
```

指定プレイヤーが、怪盗全員の盗品展示後に1枚ずつ展示します。

**この予約は `BeginRobberyPhase()` より前に行ってください。**

怪盗が0人でも、空の `BeginRobberyPhase()` を呼ぶことで逮捕報酬展示へ進めます。

### 脱落者の全展示

```csharp
treasureController.DisplayAllTreasures(playerId);
```

指定プレイヤーの残り手札を、選択なしですべて展示します。現在は `Waiting` フェーズ中のみ実行できます。

## 5. 推奨する1ターンの呼び出し例

```csharp
// 必要な場合のみ。ターン開始を行動カード側が管理するときに呼ぶ。
treasureController.BeginTurn();

// 行動カードの開示結果
int[] displayPlayers = { 0, 2 };
int[] displayCounts = { 1, 1 };

int[] robberPlayers = { 1, 3 };
int[] robberyCounts = { 4, 2 };

int[] arrestRewardPlayers = { 0 };

// 1. 展示選択を開始
treasureController.BeginDisplayPhase(displayPlayers, displayCounts);

// 2. 全員の展示完了を待つ
// TreasureController.Phase == TreasurePhase.Waiting になったら次へ

// 3. 怪盗開始前に逮捕報酬を予約
treasureController.QueueArrestRewardDisplays(arrestRewardPlayers);

// 4. 逮捕されていない怪盗だけで怪盗フェーズ開始
treasureController.BeginRobberyPhase(robberPlayers, robberyCounts);

// 以降はお宝側が以下を自動進行する
// 怪盗 → 盗品展示 → 逮捕報酬展示 → 展示整理 → 勝利判定
```

実際にはコルーチン、イベント、またはステートマシンで `TreasureController.Phase` を監視して次へ進めてください。固定秒数だけ待って次のメソッドを呼ぶ方法は、アニメーション時間の変更で壊れるため非推奨です。

## 6. カード選択の確定方法

現在は、クリック可能な `Treasure` をクリックすると次の共通入口が呼ばれます。

```csharp
treasureController.HandleTreasureClick(treasure);
```

`TreasureController.Phase` に応じて意味が変わります。

| Phase | クリックの意味 |
|---|---|
| `SelectingDisplays` | 自分の手札から展示カードを選択 |
| `Robbing` | 現在の怪盗が他人の展示品を盗む |
| `RobberDisplay` | 今回盗んだカードから展示する1枚を選択 |

クリック可能かどうかは次で確認できます。

```csharp
bool canSelect = treasureController.CanInteract(treasure);
```

Player 2以降をCPU操作にする場合も、候補カードから `CanInteract()` が `true` のカードを選び、`HandleTreasureClick()` に渡す構成が安全です。

## 7. フェーズ一覧

```csharp
public enum TreasurePhase
{
    Waiting,
    SelectingDisplays,
    Displaying,
    Robbing,
    RobberDisplay,
    EndingTurn,
    GameOver
}
```

| Phase | 状態 |
|---|---|
| `Waiting` | 次の行動開始待ち |
| `SelectingDisplays` | 展示するカードを選択中 |
| `Displaying` | カード移動アニメーション中 |
| `Robbing` | 現在の怪盗が盗むカードを選択中 |
| `RobberDisplay` | 怪盗が盗品から展示カードを選択中 |
| `EndingTurn` | 展示整理と勝利判定中 |
| `GameOver` | 勝者決定済み |

案内文は `TreasureController.InstructionText`、勝敗結果は `TreasureController.GameResultText` から取得できます。

## 8. 勝利判定

- 本物の宝を7点以上展示すると勝利候補
- 本物の金は1枚につき2点
- その他の本物は1枚につき1点
- 金を展示したターンは勝利不可
- 2枚以上を一度に展示したターンも勝利不可
- 判定はターン終了直前に実行

同時に条件を満たした場合の比較順：

1. 金の枚数
2. 本物の遺物の枚数
3. 本物の宝石の枚数
4. 本物の絵画の枚数
5. すべて同数なら引き分け勝利

勝利条件到達者の展示カードは、金 → 宝石 → 遺物 → 絵画の順に開示されます。同じ種類は同時にめくられます。

## 9. 展示場と手札の現在の仕様

- 展示列は金・宝石・遺物・絵画に分かれる
- 展示カードは裏向き
- 同じ種類では展示した順番を維持
- 盗まれた穴はターン中は残る
- 新しく展示したカードは同種類の最後尾へ追加
- ターン終了時だけ、順番を変えずに穴を詰める
- 展示場では本物・偽物による並べ替えをしない
- 手札は種類順に整理する
- Player 1以外の手札は非表示
- 2～4人プレイに対応
- 3人プレイではPlayer 2位置を空席にし、Player 1・3・4位置を使用

## 10. 統合時に外す・残すもの

### 外す、または無効化するもの

`TreasureTurnPrototype` は行動カード側が完成するまでの試作用クラスです。

統合後は次のいずれかにしてください。

- GameObjectから `TreasureTurnPrototype` を削除
- `TreasureTurnPrototype` コンポーネントを無効化
- `Play On Start` と `Show Start Button` をオフ

試作クラスと行動カード側が同時にターンを開始すると、フェーズが競合します。

### 残すもの

- `TreasureController`
- 参加人数分の `Player`
- Player位置
- 展示位置
- 宝カードのテンプレート
- お宝表示に必要なカメラ・マテリアル・画像

## 11. 現時点で統合側に必要な判断

1. ターン全体の親ステートを行動カード側とお宝側のどちらが管理するか
2. Player 2以降をCPU操作にするか、通信・別入力にするか
3. 同数の怪盗宣言時にPlayer番号順でよいか
4. 逮捕成功者が複数いる場合、全員同時に1枚展示でよいか
5. 脱落者の全展示を通常ターンのどの時点で実行するか
6. 行動カード側の「2枚展示」「3枚展示」カードの正式なカード名と発動条件

## 12. 今後追加すると統合しやすいもの

現在は `Phase` の監視で進行できますが、統合作業では次のイベントを追加するとより安全です。

```csharp
public event System.Action DisplayPhaseCompleted;
public event System.Action RobberyPhaseCompleted;
public event System.Action ArrestRewardPhaseCompleted;
public event System.Action TurnCompleted;
public event System.Action<string> GameEnded;
```

イベント化すれば、行動カード側が毎フレーム `Phase` を監視せずに済みます。この部分はまだ未実装です。
