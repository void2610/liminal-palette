---
name: lp-list-commands
description: 'List all [ConsoleCommand] registered in a running Unity project via LiminalPalette HTTP API. Use to discover available commands, filter by category prefix (Player/, Enemy/, etc.), inspect parameter schemas (type, hasDefault, choices), find async commands, or verify path spelling before invoking lp-execute.'
when_to_use: 'Trigger phrases: "コマンド一覧", "何が呼べる", "Player カテゴリのコマンド", "list LP commands", "what can I run", "show schema for X", "fuzzy search command", "before lp-execute".'
allowed-tools: Bash(curl *), Bash(jq *), Bash(cat *)
---

# lp-list-commands

LiminalPalette に `[ConsoleCommand]` で登録されたコマンドの **スキーマ一覧**を取得する。AI Agent が「何を呼べるか」を発見し、`lp-execute` の引数を組み立てる前の必須ステップ。

---

## Setup (必要なら)

```bash
# Token + base URL がまだ無ければ
[ -z "${LP_TOKEN:-}" ] && export LP_TOKEN=$(cat ~/.liminal-palette/token)
[ -z "${LP_BASE:-}" ] && {
  for p in 7610 7611 7612 7613 7614 7615; do
    curl -s -m 1 "http://127.0.0.1:$p/api/v1/health" >/dev/null 2>&1 && export LP_PORT=$p && break
  done
  export LP_BASE="http://127.0.0.1:$LP_PORT"
}
```

---

## 基本

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands"
```

レスポンス全体は重い (数百コマンド × スキーマ) ので **必ず `jq` で絞り込む**。

---

## よく使うパターン

### 1. 全コマンドの path だけ列挙

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq -r '.commands[].path' | sort
```

### 2. カテゴリ prefix で絞り込み

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq '.commands[] | select(.path | startswith("Player/"))'
```

### 3. 特定コマンドのスキーマ詳細

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq '.commands[] | select(.path == "Player/Health/Set")'
```

### 4. 引数の choices (enum 等で valid 値が決まっているケース)

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq '.commands[] | select(.path == "Enemy/Spawn") | .parameters[] | {name, type, choices}'
```

### 5. キーワード検索 (description / name 含む)

```bash
KW="spawn"
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq --arg kw "$KW" '.commands[] | select((.name + " " + .description) | ascii_downcase | contains($kw|ascii_downcase))'
```

より多くのレシピは [examples/jq-recipes.md](examples/jq-recipes.md)。

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

### コマンドオブジェクトの主フィールド

| フィールド | 説明 | `lp-execute` での使い方 |
|---|---|---|
| `path` | "/" 区切りの一意識別子 | request body の `"path"` にそのまま |
| `name` | path の末尾セグメント | (UI 表示用) |
| `category` | path の前段 | (UI のグループ化用) |
| `description` | `[ConsoleCommand(Description = ...)]` の値 | 引数を組み立てる時の意図把握 |
| `isAsync` | `Task<T>`/`ValueTask<T>` 戻り値で true | 実行に時間がかかる可能性。curl `--max-time` を上げる検討 |
| `returnType` | 戻り値の `Type.Name` (短縮名) | `result.value` の解釈に使う |
| `aliases` | `[ConsoleCommand(Aliases = ...)]` の別名配列 | path 同様に invoke 可 |
| `parameters[]` | 引数のスキーマ (下記) | request body の `"args"` を組み立てる |

### parameters[] の中身

| フィールド | 説明 |
|---|---|
| `name` | 引数名。`lp-execute` の `args` キーに使う (大文字小文字区別) |
| `type` | `Type.Name` (例: `Int32`, `Vector3`, `Color`, 自作 enum 名) |
| `position` | 0-origin の引数位置 |
| `hasDefault` | デフォルト値があるか。`false` なら `args` で必ず指定 |
| `default` | デフォルト値の文字列化。`hasDefault: false` なら null |
| `description` | `[Description]` 属性の説明文 |
| `choices` | enum / `[Choices(...)]` で valid 値が限定されている場合の候補配列。空でない時は **必ずここから選ぶ** |

---

## 大量コマンド時のノウハウ

`commandCount` が数百を超えるプロジェクトで全件をそのまま AI コンテキストに入れると重い。**段階的に絞る**:

1. **まずカテゴリ prefix を出して全体像を把握**
   ```bash
   curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
     | jq -r '.commands[].category' | sort -u
   ```

2. **興味のあるカテゴリだけ詳細展開**
   ```bash
   curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
     | jq '.commands[] | select(.category | startswith("Combat/"))'
   ```

3. **特定コマンドだけ scheme を読む**
   ```bash
   curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
     | jq '.commands[] | select(.path == "Combat/Attack")'
   ```

---

## Notes

### path の typo 対策

`lp-execute` で 404 が返ったら、**まず本スキルで正確な綴りを確認**する。LP の path は **大文字小文字を区別する**。

### choices が空でない引数

```json
{"name": "type", "type": "EnemyType", "choices": ["Goblin", "Orc", "Dragon"]}
```

この場合 `"args": {"type": "Goblin"}` のように choices の値を渡す。範囲外の値 (`"args": {"type": "Slime"}`) は `TypeConverter` で弾かれて `success: false`。

### scenarios はここに出ない

`[ConsoleScenario]` は別 endpoint (`/api/v1/scenarios`)。`lp-list-scenarios` を使う。

### Editor / Runtime で違うコマンド一覧

両稼働時、ポートごとに `commandCount` が違う:
- Editor 側 (7610) は Editor 限定 `[ConsoleCommand]` (例: `Editor/Console/Clear`) を含む
- Runtime 側 (7611) は Runtime 専用コマンドのみ

両方を見たいなら両ポートに対して本スキルを実行する:

```bash
for base in "$LP_BASE_EDITOR" "$LP_BASE_RUNTIME"; do
  echo "=== $base ==="
  curl -s -H "Authorization: Bearer $LP_TOKEN" "$base/api/v1/commands" \
    | jq -r '.commands[].path' | sort
done
```

---

## Error Handling

| Status | 状況 | 対処 |
|---|---|---|
| 401 | Token 不一致 | `~/.liminal-palette/token` 再読み込み |
| (応答なし) | LP 未起動 / port 違い | `lp-find-port` でポート再確認 |

---

## See also

- `/lp-execute` — ここで発見した `path` を実行
- `/lp-list-scenarios` — scenarios 一覧 (別 endpoint)
- examples: [jq-recipes.md](examples/jq-recipes.md) — 検索 / フィルタ / 集計の jq パターン集
