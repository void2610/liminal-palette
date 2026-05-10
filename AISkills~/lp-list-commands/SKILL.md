---
name: lp-list-commands
description: 'List all [ConsoleCommand] registered in a running Unity project via `lp commands`. Use to discover available commands, filter by category prefix (Player/, Enemy/, etc.), inspect parameter schemas (type, hasDefault, choices), find async commands, or verify path spelling before invoking lp-execute.'
when_to_use: 'Trigger phrases: "コマンド一覧", "何が呼べる", "Player カテゴリのコマンド", "list LP commands", "what can I run", "show schema for X", "fuzzy search command", "before lp-execute".'
allowed-tools: Bash(lp *), Bash(jq *)
---

# lp-list-commands

LiminalPalette に `[ConsoleCommand]` で登録されたコマンドの **スキーマ一覧**を取得する。AI Agent が「何を呼べるか」を発見し、`lp-execute` の引数を組み立てる前の必須ステップ。

---

## 基本

```bash
# 装飾された一覧 (path / description / 引数シグネチャ)
lp commands

# prefix で絞り込み
lp commands --filter Player/
```

`lp commands` の出力例:

```
  Player/HP/Heal               HPを回復する (amount:Int32)
  Player/StatusEffect/Add      プレイヤーに状態異常を付与する (type:StatusEffectType, stacks:Int32)
  ...
  total: 12
```

---

## より細かいクエリは `--json | jq`

`--filter` は単純な prefix 一致のみ。複雑な条件は `--json` で生 JSON を取って `jq` に投げる。

### 全コマンドの path だけ列挙

```bash
lp commands --json | jq -r '.commands[].path' | sort
```

### 特定コマンドのスキーマ詳細

```bash
lp commands --json | jq '.commands[] | select(.path == "Player/HP/Heal")'
```

### 引数の `choices` (enum / `[Choices]` で限定されている値)

```bash
lp commands --json \
  | jq '.commands[] | select(.path == "Enemy/Spawn") | .parameters[] | {name, type, choices}'
```

### キーワード検索 (description / name 含む)

```bash
KW="spawn"
lp commands --json \
  | jq --arg kw "$KW" '.commands[] | select((.name + " " + .description) | ascii_downcase | contains($kw|ascii_downcase))'
```

### Async コマンドだけ

```bash
lp commands --json | jq '.commands[] | select(.isAsync == true) | .path'
```

より多くのレシピは [examples/jq-recipes.md](examples/jq-recipes.md)。

---

## Output (`--json` で取れる JSON)

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
| `path` | "/" 区切りの一意識別子 | `lp exec <path> ...` の path に |
| `name` | path の末尾セグメント | (UI 表示用) |
| `category` | path の前段 | (UI のグループ化用) |
| `description` | `[ConsoleCommand(Description = ...)]` の値 | 引数を組み立てる時の意図把握 |
| `isAsync` | `Task<T>`/`ValueTask<T>` 戻り値で true | 実行に時間がかかる可能性 |
| `returnType` | 戻り値の `Type.Name` (短縮名) | `result.value` の解釈に使う |
| `aliases` | `[ConsoleCommand(Aliases = ...)]` の別名配列 | path 同様に invoke 可 |
| `parameters[]` | 引数のスキーマ (下記) | `lp exec` の `key=value` を組み立てる |

### parameters[] の中身

| フィールド | 説明 |
|---|---|
| `name` | 引数名。`lp exec` の `name=...` に使う (大文字小文字区別) |
| `type` | `Type.Name` (例: `Int32`, `Vector3`, `Color`, 自作 enum 名) |
| `position` | 0-origin の引数位置 |
| `hasDefault` | デフォルト値があるか。`false` なら必ず指定 |
| `default` | デフォルト値の文字列化。`hasDefault: false` なら null |
| `description` | `[Description]` 属性の説明文 |
| `choices` | enum / `[Choices(...)]` で valid 値が限定されている場合の候補配列。空でない時は **必ずここから選ぶ** |

---

## 大量コマンド時のノウハウ

`commandCount` が数百を超えるプロジェクトで全件をそのまま AI コンテキストに入れると重い。**段階的に絞る**:

1. **まずカテゴリ prefix を出して全体像を把握**
   ```bash
   lp commands --json | jq -r '.commands[].category' | sort -u
   ```

2. **興味のあるカテゴリだけ詳細展開**
   ```bash
   lp commands --filter Combat/
   ```

3. **特定コマンドだけ scheme を読む**
   ```bash
   lp commands --json | jq '.commands[] | select(.path == "Combat/Attack")'
   ```

---

## Notes

### path の typo 対策

`lp exec` で 404 が返ったら、**まず本スキルで正確な綴りを確認**する。LP の path は **大文字小文字を区別する**。

### choices が空でない引数

```json
{"name": "type", "type": "EnemyType", "choices": ["Goblin", "Orc", "Dragon"]}
```

この場合 `lp exec Enemy/Spawn type=Goblin` のように choices の値を渡す。範囲外 (`type=Slime`) は `TypeConverter` で弾かれて exit code 2 (`success: false`)。

### scenarios はここに出ない

`[ConsoleScenario]` は別 endpoint。`lp scenarios` (`/lp-list-scenarios`) を使う。

### Editor / Runtime で違うコマンド一覧

両稼働時、ポートごとに `commandCount` が違う:
- Editor 側 (7610) は Editor 限定 `[ConsoleCommand]` (例: `Editor/Console/Clear`) を含む
- Runtime 側 (7611) は Runtime 専用コマンドのみ

両方を見たいなら両ポートに対して本スキルを実行する:

```bash
for base in http://127.0.0.1:7610 http://127.0.0.1:7611; do
  echo "=== $base ==="
  lp --base-url "$base" commands --json | jq -r '.commands[].path' | sort
done
```

---

## Error Handling

| 状況 | 対処 |
|---|---|
| `Liminal Palette サーバーが見つかりません` | `lp health` でまず疎通確認。`/lp-find-port` 参照 |
| HTTP 401 | Token が期限切れ/不一致。`~/.liminal-palette/token` を再生成 (Editor 再起動) |

---

## See also

- `/lp-execute` — ここで発見した `path` を実行
- `/lp-list-scenarios` — scenarios 一覧 (別 endpoint)
- examples: [jq-recipes.md](examples/jq-recipes.md) — 検索 / フィルタ / 集計の jq パターン集
