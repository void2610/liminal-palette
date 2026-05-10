---
name: lp-get-state
description: 'Read current values of [LiminalObservableField] reactive snapshots (HP, mana, count, position, ...) via `lp state`. Use to observe game state before/after lp-execute calls, iterate all reactive fields, or detect VContainer instance resolution failures via instanceResolved=false.'
when_to_use: 'Trigger phrases: "現在のHP", "Player の状態", "観測する", "値を読む", "ReactiveProperty の現在値", "what''s the current X", "read state", "before/after check".'
allowed-tools: Bash(lp *), Bash(jq *)
---

# lp-get-state

`[LiminalObservableField]` で公開された `ReactiveProperty<T>` / `IReadOnlyReactiveProperty<T>` の現在値スナップショットを `lp state` で取得する。AI Agent が「現在の状態を観測してから次のコマンドを決める」用途、および `lp exec` の前後検証で使う。

---

## 単一フィールド取得

```bash
lp state Player/HP
```

出力:

```
  Player/HP
    value : 75
    type  : Int32
```

---

## 全件取得

```bash
lp state
```

出力 (装飾付き、`●` は instance 解決済み、`○` は未解決):

```
  ● Player/HP        100 (Int32)
  ● Player/MaxHP     100 (Int32)
  ● Player/Coins     100 (Int32)
  ○ Enemy/Count      null (Int32)
```

---

## `--json` で取って `jq` で絞る

### 値だけ抽出

```bash
HP=$(lp state Player/HP --json | jq -r '.value')
echo "HP=$HP"
```

### 全件のうち、解決済み + 非 null だけ

```bash
lp state --json | jq '.fields[] | select(.value != null)'
```

### prefix で絞り込み

```bash
lp state --json | jq '.fields[] | select(.path | startswith("Player/"))'
```

### 未解決フィールドの一覧 (VContainer 設定漏れ検出)

```bash
lp state --json | jq -r '.fields[] | select(.instanceResolved == false) | .path'
```

### 値を条件判定

```bash
hp=$(lp state Player/HP --json | jq -r '.value')
if (( hp < 30 )); then
  echo "Critical health: $hp"
fi
```

⚠️ `value` は string で返るので bash の数値比較は `(( ))` が安全。Vector / Color のような複合型は parse 必須。

より多くの検証パターンは [examples/verify-patterns.md](examples/verify-patterns.md) を参照。

---

## Output (`--json`)

### 単一指定

```json
{
  "path": "Player/Health",
  "value": "75",
  "type": "Int32"
}
```

| フィールド | 説明 |
|---|---|
| `path` | `[LiminalObservableField("...")]` で指定された path |
| `value` | `ReactiveProperty.Value` を `TypeConverterRegistry.ToDisplayString` で string 化 |
| `type` | T の `Type.Name` |

### 全件

```json
{
  "fields": [
    {"path":"Player/Health","value":"75","type":"Int32","instanceResolved":true},
    ...
  ]
}
```

全件版のみ `instanceResolved` が含まれる (単一版は解決失敗なら 500 を返すため不要)。

---

## `value` が null になる 3 ケース

| 条件 | `instanceResolved` | 単一指定の挙動 |
|---|---|---|
| **インスタンス未解決** (VContainer に登録なし) | `false` | HTTP 500 → `lp` は赤エラー出力 |
| **`Observable<T>` 単体** (現在値を保持しない) | `true` | 200 + value: null |
| **`ReactiveProperty.Value` 自体が null** (参照型で初期化前) | `true` | 200 + value: null |

null と「実際の value 文字列が "null"」は別物。`type` と組み合わせて判別する。

---

## `lp exec` との組み合わせ (検証パターン)

### before / after 比較

```bash
before=$(lp state Player/HP --json | jq -r '.value')
lp exec Player/Health/Damage amount=30
after=$(lp state Player/HP --json | jq -r '.value')
echo "before=$before after=$after"
```

### より良い: scenarios の assert_equals を使う

複数の `lp exec` + 検証を 1 リクエストにまとめると race condition 回避 + rate limit 消費 1:

```bash
cat <<'EOF' | lp run --steps -
[
  {"type":"command","path":"Player/Health/Damage","args":{"amount":"30"}},
  {"type":"assert_equals","path":"Player/HP","expected":"70"}
]
EOF
```

詳細: `/lp-run-scenario`。

---

## 物理 / アニメ / Rigidbody のタイミング問題

`[LiminalCommand]` 内で `ReactiveProperty.Value = X` した直後に `lp state` を叩けば**新値が読める** (R3 は同期更新)。

ただし「物理 / アニメ / Rigidbody 経由で間接的に変わる」状態は `Update` を 1 フレーム待つ必要がある:

```csharp
[LiminalCommand("Player/Position/Teleport")]
public void Teleport(Vector2 pos) {
    _rb.MovePosition(pos);  // Rigidbody 経由 → 1 frame 待たないと反映されない
}
```

```bash
# Teleport 直後の lp state は古い値を返す可能性。scenarios の wait_frames を挟む。
cat <<'EOF' | lp run --steps -
[
  {"type":"command","path":"Player/Position/Teleport","args":{"pos":"0,0"}},
  {"type":"wait_frames","frames":1},
  {"type":"assert_equals","path":"Player/Position","expected":"(0.00, 0.00)"}
]
EOF
```

---

## Notes

### `Observable<T>` 単体は使えない

```csharp
[LiminalObservableField("Player/HitStream")]
public Observable<int> HitStream { get; }   // ← lp state では常に null
```

`Observable<T>` はプッシュのみで現在値保持しない。`lp state` は **現在値スナップショット用**なので常に null を返す。AI Agent から状態観測したいなら **`ReactiveProperty<T>` で公開する**設計が必要。

### Editor / Play Mode で値が違う

両稼働時、Editor (7610) と Play Mode (7611) で別の VContainer スコープが立っているケースがある。`lp state` の結果も別。AI Agent はどちらに送っているか文脈で判断する:

```bash
echo "=== Editor ==="
lp --port 7610 state --json | jq '.fields[] | select(.value != null)'

echo "=== Runtime ==="
lp --port 7611 state --json | jq '.fields[] | select(.value != null)'
```

### Vector / Color の value 形式

`type` が複合型 (Vector3, Color 等) の場合、`value` は `ToDisplayString` 結果:

| type | value 例 |
|---|---|
| `Int32` / `Single` | `"75"` / `"3.14"` |
| `Vector3` | `"(1.50, 2.00, 3.00)"` |
| `Color` | `"#FF8800FF"` (HEX 8桁) |
| Enum | `"Up"` (名前) |

`assert_equals` で比較する時は **同じ ToDisplayString 形式**で書く必要あり。

---

## Error Handling

| 症状 | 状況 | 対処 |
|---|---|---|
| HTTP 401 | Token 不一致 | `~/.liminal-palette/token` 再生成 |
| HTTP 404 | 単一指定で path 未登録 | `lp state` (全件) で実在 path を確認 |
| HTTP 500 | 単一指定でインスタンス未解決 | 利用側で `builder.Register<T>()` + `RegisterEntryPoint<LiminalPaletteEntryPoint>()` |

---

## See also

- `/lp-execute` — 状態を変える
- `/lp-run-scenario` — execute + assert_equals を 1 リクエストに
- examples: [verify-patterns.md](examples/verify-patterns.md) — bash での検証パターン集
- LP 本体: `Documentation~/commands.md` の `[LiminalObservableField]` セクション
