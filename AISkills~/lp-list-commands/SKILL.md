---
name: lp-list-commands
description: "List all `[ConsoleCommand]` registered in the running Unity project via LiminalPalette HTTP API. Use when you need to: (1) Discover available commands and their parameter schemas before invoking, (2) Filter by category prefix (e.g. `Player/`, `Enemy/`), (3) Inspect `parameters[].choices` / `isAsync` / `returnType` / `aliases` for a specific command."
---

# lp-list-commands

LiminalPalette に `[ConsoleCommand]` で登録されたコマンド一覧をスキーマ付きで取得する。AI Agent が「何を呼べるか」を発見し、引数を組み立てるための一次情報源。

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

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands"
```

---

## Examples

```bash
# 全コマンドの path だけ列挙
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq -r '.commands[].path' | sort

# 特定カテゴリだけ (prefix match)
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq '.commands[] | select(.path | startswith("Player/"))'

# 特定コマンドのスキーマだけ取り出す (引数を組み立てる前に必読)
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq '.commands[] | select(.path == "Player/Health/Set")'

# parameters の choices (enum で valid 値が決まっているコマンドの確認)
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq '.commands[] | select(.parameters[] | .choices | length > 0) | {path, parameters}'

# async コマンドだけ抽出
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq '.commands[] | select(.isAsync == true) | .path'

# fuzzy 風: name と description に "spawn" を含むものを検索
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq '.commands[] | select((.name + .description) | ascii_downcase | contains("spawn"))'
```

---

## Output

```json
{
  "commands": [
    {
      "path": "Player/Health/Set",
      "name": "Set",
      "category": "Player/Health",
      "description": "プレイヤーの HP を設定",
      "isAsync": false,
      "returnType": "Void",
      "aliases": [],
      "parameters": [
        {
          "name": "value",
          "type": "Int32",
          "position": 0,
          "hasDefault": false,
          "default": null,
          "description": "",
          "choices": []
        }
      ]
    }
  ]
}
```

| フィールド | 説明 |
|---|---|
| `path` | "/" 区切りの一意識別子。`lp-execute` の `path` に渡す値 |
| `name` | path の末尾セグメント (UI 表示用) |
| `category` | path の前段 (UI のグループ化用) |
| `description` | `[ConsoleCommand(Description = ...)]` の値 |
| `isAsync` | `Task<T>` / `ValueTask<T>` 戻り値で true。実行に時間がかかる可能性あり |
| `returnType` | 戻り値の `Type.Name` (短縮名) |
| `aliases` | `[ConsoleCommand(Aliases = ...)]` で追加された別名 |
| `parameters[]` | 引数のスキーマ (下記) |

### parameters[] の中身

| フィールド | 説明 |
|---|---|
| `name` | 引数名 (`lp-execute` の `args` キーに使う) |
| `type` | `Type.Name` (例: `Int32`, `String`, `Vector3`, `Color`, 自作 enum 名) |
| `position` | 0-origin の引数位置 |
| `hasDefault` | デフォルト値があるか (`false` なら `args` で必ず指定が必要) |
| `default` | デフォルト値の文字列化。`hasDefault: false` なら null |
| `description` | `[Description]` 属性で付けた引数説明 |
| `choices` | enum や `[Choices(...)]` で valid 値が限定されている場合の候補配列 |

---

## Error Handling

| Status | 状況 | 対処 |
|---|---|---|
| 401 | Token 不一致 | `~/.liminal-palette/token` 再読み込み / Editor 再起動 |
| (応答なし) | LP 未起動 / ポート違い | `lp-find-port` でポート確認 |

---

## Notes

### 大量コマンド時のパース

`commandCount` が数百を超えるプロジェクトでは、全件一気に AI コンテキストに入れると重い。**必ず `jq` で絞り込む**:

- カテゴリ prefix で絞る (`startswith("Player/")`)
- description / name の部分一致で絞る
- パラメータ詳細は対象コマンドだけ抽出する

### path の typo 対策

`lp-execute` で 404 が返ったら、**まず `lp-list-commands` で path の正確な綴りを確認する**。LP の path は大文字小文字を区別する。

### choices の活用

`parameters[].choices` が空でない場合、`lp-execute` の引数値はそこから選ぶこと。enum / `[Choices]` 限定の引数で範囲外の値を送ると、`TypeConverter` レベルで弾かれる。

### scenario との違い

`[ConsoleScenario]` の一覧はここには出ない。シナリオは `lp-list-scenarios` で別取得。

---

## 関連スキル

- `lp-find-port` — 事前にポート確認
- `lp-execute` — ここで発見した `path` を実行
- `lp-list-scenarios` — シナリオ一覧 (別 endpoint)
