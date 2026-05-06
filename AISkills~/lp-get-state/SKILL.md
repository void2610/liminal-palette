---
name: lp-get-state
description: 'Read current values of [ConsoleObservableField] reactive snapshots (HP, mana, count, position, ...) via LiminalPalette HTTP API. Use to observe game state before/after lp-execute calls, iterate all reactive fields, or detect VContainer instance resolution failures via instanceResolved=false.'
when_to_use: 'Trigger phrases: "現在のHP", "Player の状態", "観測する", "値を読む", "ReactiveProperty の現在値", "what''s the current X", "read state", "before/after check".'
allowed-tools: Bash(curl *), Bash(jq *), Bash(cat *)
---

# lp-get-state

`[ConsoleObservableField]` で公開された `ReactiveProperty<T>` / `IReadOnlyReactiveProperty<T>` の現在値スナップショットを取得する。AI Agent が「現在の状態を観測してから次のコマンドを決める」用途、および `lp-execute` の前後検証で使う。

---

## Setup

```bash
[ -z "${LP_TOKEN:-}" ] && export LP_TOKEN=$(cat ~/.liminal-palette/token)
[ -z "${LP_BASE:-}" ] && {
  for p in 7610 7611 7612 7613 7614 7615; do
    curl -s -m 1 "http://127.0.0.1:$p/api/v1/health" >/dev/null 2>&1 && export LP_PORT=$p && break
  done
  export LP_BASE="http://127.0.0.1:$LP_PORT"
}
```

---

## 単一フィールド取得

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" \
     "$LP_BASE/api/v1/state?path=Player/Health"
```

レスポンス:

```json
{"path":"Player/Health","value":"75","type":"Int32"}
```

---

## 全件取得

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/state"
```

レスポンス:

```json
{
  "fields": [
    {"path":"Player/Health","value":"75","type":"Int32","instanceResolved":true},
    {"path":"Player/Mana","value":"30","type":"Int32","instanceResolved":true},
    {"path":"Enemy/Count","value":null,"type":"Int32","instanceResolved":false}
  ]
}
```

---

## よく使うパターン

### 値だけ抽出

```bash
HP=$(curl -s -H "Authorization: Bearer $LP_TOKEN" \
       "$LP_BASE/api/v1/state?path=Player/Health" | jq -r '.value')
echo "HP=$HP"
```

### 全件のうち、解決済み + 非 null だけ

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/state" \
  | jq '.fields[] | select(.value != null)'
```

### prefix で絞り込み

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/state" \
  | jq '.fields[] | select(.path | startswith("Player/"))'
```

### 未解決フィールドの一覧 (VContainer 設定漏れ検出)

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/state" \
  | jq '.fields[] | select(.instanceResolved == false) | .path'
```

### 値を条件判定

```bash
hp=$(curl -s -H "Authorization: Bearer $LP_TOKEN" \
       "$LP_BASE/api/v1/state?path=Player/Health" | jq -r '.value')

if [ "$hp" -lt 30 ] 2>/dev/null; then
  echo "Critical health: $hp"
fi
```

⚠️ `value` は string で返るので bash の数値比較は `[` の代わりに `[[ ]]` または `(( ))` を使う。型が int / float なら直接比較可能だが、Vector / Color のような複合型は parse 必須。

より多くの検証パターンは [examples/verify-patterns.md](examples/verify-patterns.md) を参照。

---

## Output

### 単一指定 (`?path=...`)

```json
{
  "path": "Player/Health",
  "value": "75",
  "type": "Int32"
}
```

| フィールド | 説明 |
|---|---|
| `path` | `[ConsoleObservableField("...")]` で指定された path |
| `value` | `ReactiveProperty.Value` を `TypeConverterRegistry.ToDisplayString` で string 化 |
| `type` | T の `Type.Name` |

### 全件 (`?path=` 省略)

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
| **インスタンス未解決** (VContainer に登録なし) | `false` | 500 Internal Server Error |
| **`Observable<T>` 単体** (現在値を保持しない) | `true` | 200 + value: null |
| **`ReactiveProperty.Value` 自体が null** (参照型で初期化前) | `true` | 200 + value: null |

null と "実際に value が "null" という文字列" は別物。`type` と組み合わせて判別する。

---

## `lp-execute` との組み合わせ (検証パターン)

### before / after 比較

```bash
# 実行前
before=$(curl -s -H "Authorization: Bearer $LP_TOKEN" \
           "$LP_BASE/api/v1/state?path=Player/Health" | jq -r '.value')

# コマンド実行
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
     -X POST "$LP_BASE/api/v1/execute" \
     -d '{"path":"Player/Health/Damage","args":{"amount":"30"}}' >/dev/null

# 実行後
after=$(curl -s -H "Authorization: Bearer $LP_TOKEN" \
          "$LP_BASE/api/v1/state?path=Player/Health" | jq -r '.value')

echo "before=$before after=$after"
```

### より良い: scenarios の assert_equals を使う

複数の execute + 検証を 1 リクエストにまとめると race condition 回避 + rate limit 消費 1:

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d '{"steps":[
    {"type":"command","path":"Player/Health/Damage","args":{"amount":"30"}},
    {"type":"assert_equals","path":"Player/Health","expected":"70"}
  ]}'
```

詳細: `/lp-run-scenario`。

---

## 物理 / アニメ / Rigidbody のタイミング問題

`[ConsoleCommand]` 内で `ReactiveProperty.Value = X` した直後に `/state` を叩けば**新値が読める** (R3 は同期更新)。

ただし「物理 / アニメ / Rigidbody 経由で間接的に変わる」状態は `Update` を 1 フレーム待つ必要がある:

```csharp
[ConsoleCommand("Player/Position/Teleport")]
public void Teleport(Vector2 pos) {
    _rb.MovePosition(pos);  // Rigidbody 経由 → 1 frame 待たないと反映されない
}
```

```bash
# Teleport 直後の /state は古い値を返す可能性
# scenarios の wait_frames を挟む
curl ... -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d '{"steps":[
    {"type":"command","path":"Player/Position/Teleport","args":{"pos":"0,0"}},
    {"type":"wait_frames","frames":1},
    {"type":"assert_equals","path":"Player/Position","expected":"(0.00, 0.00)"}
  ]}'
```

---

## Notes

### `Observable<T>` 単体は使えない

```csharp
[ConsoleObservableField("Player/HitStream")]
public Observable<int> HitStream { get; }   // ← /state では常に null
```

`Observable<T>` はプッシュのみで現在値保持しない。`/state` は **現在値スナップショット用**なので常に null を返す。AI Agent から状態観測したいなら **`ReactiveProperty<T>` で公開する**設計が必要。

### Editor / Play Mode で値が違う

両稼働時、Editor (7610) と Play Mode (7611) で別の VContainer スコープが立っているケースがある。`/state` の結果も別。AI Agent はどちらに送っているか文脈で判断する。

```bash
echo "=== Editor ==="
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE_EDITOR/api/v1/state" \
  | jq '.fields[] | select(.value != null)'

echo "=== Runtime ==="
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE_RUNTIME/api/v1/state" \
  | jq '.fields[] | select(.value != null)'
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

| Status | 状況 | 対処 |
|---|---|---|
| 401 | Token 不一致 | `~/.liminal-palette/token` 再読み込み |
| 404 | `?path=...` 指定で path 未登録 | 全件版で実在 path を確認 |
| 500 | 単一指定でインスタンス未解決 | 利用側で `builder.Register<T>()` + `RegisterEntryPoint<LiminalPaletteEntryPoint>()` |

---

## See also

- `/lp-execute` — 状態を変える
- `/lp-run-scenario` — execute + assert_equals を 1 リクエストに
- examples: [verify-patterns.md](examples/verify-patterns.md) — bash での検証パターン集
- LP 本体: `Documentation~/commands.md` の `[ConsoleObservableField]` セクション
