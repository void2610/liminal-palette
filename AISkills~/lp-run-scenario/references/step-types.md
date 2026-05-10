# Scenario Step Types — 完全仕様

`POST /api/v1/scenarios/run` の `steps[]` で使える 5 種別の詳細。各ステップの必須/オプションフィールド、結果 JSON の形、失敗条件、ベストプラクティス。

---

## 共通フィールド

全ステップで使える:

| フィールド | 型 | 必須 | 説明 |
|---|---|---|---|
| `type` | string | ✅ | `command` / `wait_seconds` / `wait_frames` / `assert_equals` / `assert_not_equals` |
| `description` | string | — | 結果 JSON に含まれる説明文。複数 assert があるシナリオで「何を検証しているか」のメモに有用 |

---

## `command`

### リクエストフィールド

| フィールド | 型 | 必須 | 説明 |
|---|---|---|---|
| `path` | string | ✅ | `[LiminalCommand]` の path。`lp-list-commands` で発見 |
| `args` | object | ✅ | 引数。**全 value を string で送る**。引数 0 個でも `{}` を必ず付ける |

### 受理形式

`lp-execute` と完全に同じ。型変換ルール (Vector はカンマ区切り、enum は名前指定など) も同じ。`/lp-execute` の references/type-conversion.md を参照。

### 結果 JSON

```json
{
  "kind": "Command",
  "success": true,
  "durationMs": 1.2,
  "commandPath": "Enemy/Spawn",
  "args": {"type": "Goblin"},
  "commandResult": {
    "success": true,
    "value": null,
    "error": null,
    "exceptionType": null,
    "stackTrace": null,
    "durationMs": 1.0,
    "logs": []
  }
}
```

`commandResult` は `lp-execute` のレスポンスと完全同形。

### 失敗条件

- `commandResult.success == false` の時、ステップ自体も失敗扱い → fail-fast でシナリオ打ち切り
- 引数バインド失敗 / 例外 / インスタンス未解決 等は全て command 経由で検出される

### 例

```json
{"type":"command","path":"Enemy/Spawn","args":{"type":"Goblin","level":"5"}}
{"type":"command","path":"Editor/Console/Clear","args":{},"description":"クリーンスレートで開始"}
```

---

## `wait_seconds`

### リクエストフィールド

| フィールド | 型 | 必須 | 説明 |
|---|---|---|---|
| `seconds` | number | ✅ | 待機時間 (秒、float)。0 / 負数は即時実行扱い |

### 挙動

- 内部で `Task.Delay(TimeSpan.FromSeconds(seconds))`
- メインスレッドはブロックしない (他のコマンドは並行で走れる)
- 精度は OS のタイマ精度 (Windows で 15ms、Mac/Linux で数 ms)

### 結果 JSON

```json
{
  "kind": "WaitSeconds",
  "success": true,
  "durationMs": 1004.5,
  "seconds": 1.0
}
```

`durationMs` は実測値。`seconds * 1000` より僅かに大きい。

### 用途

- 物理シミュレーションを進めたい
- アニメーション再生を待ちたい
- 非同期処理 (network I/O 等) の完了を待ちたい

### 例

```json
{"type":"wait_seconds","seconds":0.5}
{"type":"wait_seconds","seconds":2.0,"description":"ローディング完了待ち"}
```

⚠️ 過剰な wait はシナリオ全体を遅くする。`wait_frames` で代替できるなら frames のほうが安定 (CPU 負荷で wait_seconds は揺らぐ)。

---

## `wait_frames`

### リクエストフィールド

| フィールド | 型 | 必須 | 説明 |
|---|---|---|---|
| `frames` | integer | ✅ | 待機フレーム数。0 / 負数は即時実行扱い |

### 環境別の挙動

| 環境 | 1 frame の意味 |
|---|---|
| Edit Mode | `EditorApplication.update` tick (≒ 1/60〜1/30 秒、設定による) |
| Play Mode | `Time.frameCount` の増分 (通常 60 fps なら ~16ms) |
| Player build (Development) | `Time.frameCount` の増分 |

LP の `EditorFrameWaiter` クラスが Edit Mode 用、Play Mode は `WaitForEndOfFrame` 系の coroutine ベース。

### 結果 JSON

```json
{
  "kind": "WaitFrames",
  "success": true,
  "durationMs": 16.7,
  "frames": 1
}
```

### 用途 (典型)

- 物理 / アニメ / Rigidbody が `Update` で反映されるのを待つ
- UI の Layout 再計算を待つ
- 1 frame の間に副作用が起きるイベント (`OnTriggerEnter` 等) の処理待ち

### 例

```json
{"type":"wait_frames","frames":1}
{"type":"wait_frames","frames":3,"description":"3 frame 物理を進める"}
```

### `wait_seconds` との使い分け

| ケース | 推奨 |
|---|---|
| 物理 / Rigidbody / Update 駆動の状態反映 | `wait_frames` |
| 実時間ベースの遅延 (network / `Task.Delay`) | `wait_seconds` |
| Play Mode で Time.timeScale = 0 | `wait_frames` (時間が止まっていてもフレームは進む) |
| Edit Mode で長時間待ちたい | `wait_seconds` (Edit Mode の frame は不安定) |

---

## `assert_equals`

### リクエストフィールド

| フィールド | 型 | 必須 | 説明 |
|---|---|---|---|
| `path` | string | ✅ | `[LiminalObservableField]` の path。`lp-get-state` で確認できる |
| `expected` | string \| number \| bool \| null | ✅ | 期待値 |

### 挙動

1. `ObservableFieldRegistry` から `path` のフィールドを取得
2. フィールドが見つからなければ失敗 ("ObservableField not found")
3. インスタンス未解決なら失敗 ("Instance not resolved")
4. `ReactiveProperty.Value` を取得し、`expected` と比較

### `expected` の型解決

| `expected` の JSON 型 | 比較ルール |
|---|---|
| **string** | フィールドの `Value` を `ToDisplayString` で string 化して string 比較 |
| **number** (int/float) | フィールドが int/float なら直接比較。違う型ならまず string 化して `expected` を string 化したものと比較 |
| **bool** | フィールドが bool なら直接比較。違う型なら同上 |
| **null** | フィールドの `Value` が null か |

⚠️ HTTP 経由は JSON 往復で型が落ちる (`100` を送っても int でなく Int64 / float になる場合あり)。**string 推奨**:

```json
{"path":"Player/Hp","expected":"100"}    // ✓ 確実
{"path":"Player/Hp","expected":100}      // 動くが型解決のクセに依存
```

### Vector / Color の expected

`ToDisplayString` 形式で書く必要あり:

```json
{"path":"Player/Position","expected":"(1.50, 2.00, 3.00)"}    // Vector3
{"path":"UI/Background/Color","expected":"#FF8800FF"}         // Color (HEX 8桁)
```

不安なら **`lp-get-state` で実際の `value` を取得して、それをそのまま `expected` に貼る** のが確実。

### 結果 JSON

```json
{
  "kind": "AssertEquals",
  "success": true,
  "durationMs": 0.1,
  "observableFieldPath": "Enemy/Hp",
  "expected": "100",
  "actualValue": "100"
}
```

失敗時:

```json
{
  "kind": "AssertEquals",
  "success": false,
  "durationMs": 0.1,
  "observableFieldPath": "Enemy/Hp",
  "expected": "100",
  "actualValue": "65",
  "error": "expected '100' but got '65'"
}
```

### 失敗条件

- 値が `expected` と異なる
- フィールドが registry に未登録
- インスタンス未解決
- `Observable<T>` 単体 (現在値を保持しないため null と比較されて失敗)

---

## `assert_not_equals`

### リクエストフィールド

`assert_equals` と同じ (`path` + `expected`)。

### 挙動

`assert_equals` の論理否定。`actualValue == expected` なら失敗、違えば成功。

### 結果 JSON

```json
{
  "kind": "AssertNotEquals",
  "success": true,
  "durationMs": 0.1,
  "observableFieldPath": "Enemy/Count",
  "expected": "0",
  "actualValue": "3"
}
```

### 用途

- spawn したら count が 0 でないことを確認
- ダメージ後に HP が初期値と違うことを確認
- 状態遷移が起きたことの簡易検証

### 例

```json
{"type":"assert_not_equals","path":"Enemy/Count","expected":"0","description":"spawn が成功していること"}
{"type":"assert_not_equals","path":"Game/State","expected":"Initial"}
```

---

## ステップ全体の挙動 (補足)

### fail-fast

最初の失敗で打ち切り。残りのステップは実行されない。`steps[]` には失敗ステップを含むそこまでが入る。

### 並列性

LP は **`SemaphoreSlim(1, 1)` で 1 シナリオずつ実行**。同時に 2 つのシナリオは走らない (409 Conflict)。ただし `/execute` は並列可。

### メインスレッド marshal

HTTP リクエストはワーカースレッドで受け付けられ、ステップ実行は **`MainThreadDispatcher`** でメインスレッドへ marshal される。Time.frameCount や Rigidbody 等メインスレッド限定 API も安全に触れる。

### 副作用付きステップ生成 (named のみ)

named シナリオは `IEnumerable<ScenarioStep>` を返す yield return メソッド。LP は `lp-list-scenarios` でも step 列を 1 度 enumerate する (stepCount のため)。**yield return 行間に副作用 (Debug.Log・状態変更) を書かない**。

---

## ステップ種別早見表

| やりたいこと | step type | 例 |
|---|---|---|
| `[LiminalCommand]` を呼ぶ | `command` | spawn, set, damage |
| 実時間で待つ | `wait_seconds` | network, Task.Delay |
| フレームで待つ | `wait_frames` | 物理 / Rigidbody / Update |
| 状態が期待値と一致 | `assert_equals` | HP=100, position=(0,0,0) |
| 状態が期待値と違う | `assert_not_equals` | count!=0, state!=Initial |
