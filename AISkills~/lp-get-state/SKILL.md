---
name: lp-get-state
description: "Read current values of `[ConsoleObservableField]` reactive snapshots via LiminalPalette HTTP API. Use when you need to: (1) Observe game state before/after an `lp-execute` call (HP, mana, count, etc.), (2) Iterate all reactive fields with `?path=` omitted, (3) Verify VContainer instance resolution via `instanceResolved`, (4) Check why a field returns null."
---

# lp-get-state

`[ConsoleObservableField]` で公開された `ReactiveProperty<T>` / `Observable<T>` の現在値スナップショットを取得する。AI Agent が「現在の状態を観測してから次のコマンドを決める」用途の中核。

---

## Prerequisites

```bash
export LP_TOKEN=$(cat ~/.liminal-palette/token)
for port in 7610 7611 7612 7613 7614 7615; do
  if curl -s -m 1 "http://127.0.0.1:$port/api/v1/health" > /dev/null 2>&1; then
    export LP_PORT=$port; break
  fi
done
export LP_BASE="http://127.0.0.1:$LP_PORT"
```

---

## Usage

### 単一フィールド

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" \
     "$LP_BASE/api/v1/state?path=Player/Health"
```

### 全件

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/state"
```

---

## Examples

```bash
# 単一値だけ抽出
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/state?path=Player/Health" \
  | jq -r '.value'

# 全件のうち、解決済みかつ非 null だけ
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/state" \
  | jq '.fields[] | select(.value != null)'

# Player/ 配下だけ絞り込み
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/state" \
  | jq '.fields[] | select(.path | startswith("Player/"))'

# 未解決フィールドの一覧 (VContainer 未登録の検出)
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/state" \
  | jq '.fields[] | select(.instanceResolved == false) | .path'

# 「Player/Health が 100 か」を判定するパターン (シェル条件)
hp=$(curl -s -H "Authorization: Bearer $LP_TOKEN" \
       "$LP_BASE/api/v1/state?path=Player/Health" | jq -r '.value')
if [ "$hp" = "100" ]; then echo "OK"; else echo "got=$hp"; fi
```

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

### 全件 (`?path=` 省略)

```json
{
  "fields": [
    { "path": "Player/Health", "value": "75", "type": "Int32", "instanceResolved": true },
    { "path": "Player/Mana",   "value": "30", "type": "Int32", "instanceResolved": true },
    { "path": "Enemy/Count",   "value": null, "type": "Int32", "instanceResolved": false }
  ]
}
```

| フィールド | 説明 |
|---|---|
| `path` | `[ConsoleObservableField("...")]` で指定された path |
| `value` | `ReactiveProperty.Value` を `TypeConverterRegistry.ToDisplayString` で string 化したもの。null になる条件は下記 |
| `type` | `T` の `Type.Name` |
| `instanceResolved` | VContainer でインスタンス解決できたかどうか (全件版のみ。単一版は解決失敗時 500 を返す) |

### `value` が null になる 3 ケース

1. **インスタンス未解決**: VContainer に登録されていないクラスに属するフィールド。`instanceResolved: false` になる
2. **`Observable<T>` 単体**: 現在値を保持しないため null。`ReactiveProperty<T>` か `IReadOnlyReactiveProperty<T>` で公開すること
3. **`ReactiveProperty.Value` 自体が null**: 参照型のフィールドで初期化されていない場合

---

## Error Handling

| Status | 状況 | 対処 |
|---|---|---|
| 401 Unauthorized | Token 不一致 | `~/.liminal-palette/token` を再読み込み / Editor 再起動 |
| 404 Not Found | `?path=...` 指定 + その path が未登録 | typo 確認。全件版で実在 path を一覧して照合 |
| 500 Internal Server Error | 単一指定でインスタンス未解決 | 利用側で対象クラスを VContainer に `Register` + `RegisterEntryPoint<LiminalPaletteEntryPoint>` |

---

## Notes

### `lp-execute` と組み合わせた検証パターン

```bash
# 1. 実行前の値を取得
before=$(curl -s -H "Authorization: Bearer $LP_TOKEN" \
           "$LP_BASE/api/v1/state?path=Player/Health" | jq -r '.value')

# 2. コマンドを実行
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
     -X POST "$LP_BASE/api/v1/execute" \
     -d '{"path":"Player/Health/Damage","args":{"amount":"30"}}'

# 3. 実行後の値を取得して比較
after=$(curl -s -H "Authorization: Bearer $LP_TOKEN" \
          "$LP_BASE/api/v1/state?path=Player/Health" | jq -r '.value')

echo "before=$before after=$after"
```

ad-hoc にスクリプトで組むより、**`lp-run-scenario` の ad-hoc 経路の `assert_equals` ステップ**を使うほうが整理しやすい (1 リクエストで完結 / fail-fast / レートリミット消費少)。

### `Observable<T>` 単体は使わない

`ReactiveProperty<T>` は現在値保持 + プッシュの両方をサポートするが、`Observable<T>` 単体はプッシュのみ。`/state` は **現在値スナップショット用途のため、`Observable<T>` 単体は常に null を返す**。AI Agent が状態を観測するためには利用側で `ReactiveProperty<T>` で公開する設計が必要。

### タイミング問題

`[ConsoleCommand]` 内で `ReactiveProperty.Value = X` した直後に `/state` を叩けば新値が読める (R3 は同期的に内部値を更新する)。ただし「物理 / アニメーション / Rigidbody 経由」の状態は `Update` を 1 フレーム待たないと反映されない:

```bash
# シナリオ ad-hoc で wait_frames を挟む方が確実
# (lp-run-scenario の ad-hoc 経路に切り替え推奨)
```

### Editor / Play Mode で値が違うことがある

Editor (port 7610) と Play Mode (port 7611) で別の VContainer スコープが立っている場合、`/state` の結果も異なる。AI Agent はどちらに送っているか文脈で判断する。

---

## 関連スキル

- `lp-execute` — コマンドを実行して副作用を起こす
- `lp-run-scenario` — assert_equals ステップで状態検証を 1 リクエストにまとめる
- `lp-list-commands` — 状態を変更するコマンドの発見
