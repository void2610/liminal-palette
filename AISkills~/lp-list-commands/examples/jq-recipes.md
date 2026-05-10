# jq Recipes for `lp commands --json`

`lp commands --json` の出力を絞り込む jq パターン集。すべて `lp commands --json | jq '<expr>'` の `<expr>` 部分を示す。

## 概要把握

### カテゴリ一覧 (重複排除)

```jq
[.commands[].category] | unique | .[]
```

### コマンド総数 / カテゴリ別件数

```jq
{total: (.commands | length), byCategory: ([.commands[] | .category] | group_by(.) | map({key: .[0], count: length}))}
```

### path 一覧 (アルファベット順)

```jq
.commands | sort_by(.path) | .[].path
```

## 検索 / 絞り込み

### prefix 完全一致

```jq
.commands[] | select(.path | startswith("Player/"))
```

### 末尾一致 (例: 末尾が `/Set`)

```jq
.commands[] | select(.path | endswith("/Set"))
```

### path or description に部分一致 (case-insensitive)

```jq
.commands[] | select((.path + " " + .description) | ascii_downcase | contains("damage"))
```

### 複数 prefix (Player/ または Enemy/)

```jq
.commands[] | select(.path | startswith("Player/") or startswith("Enemy/"))
```

### 正規表現マッチ

```jq
.commands[] | select(.path | test("^Combat/.*Attack$"))
```

## スキーマ抽出

### 特定 path のスキーマ全体

```jq
.commands[] | select(.path == "Player/Health/Set")
```

### name + 引数の type/name のみ抜粋

```jq
.commands[] | {path, args: [.parameters[] | {name, type}]}
```

### 引数 0 個のコマンド (副作用 only)

```jq
.commands[] | select(.parameters | length == 0) | .path
```

### 引数 N 個以上のコマンド

```jq
.commands[] | select(.parameters | length >= 3) | {path, count: (.parameters | length)}
```

## 戻り値による絞り込み

### async コマンドだけ

```jq
.commands[] | select(.isAsync == true) | .path
```

### 値を返すコマンド (Void / Task 以外)

```jq
.commands[] | select(.returnType != "Void" and .returnType != "Task" and .returnType != "ValueTask") | {path, returnType}
```

### Vector3 を返すコマンド

```jq
.commands[] | select(.returnType == "Vector3") | .path
```

## choices / enum

### choices が定義されている引数を持つコマンド

```jq
.commands[] | select(any(.parameters[]; .choices | length > 0)) | {path, parameters: [.parameters[] | select(.choices | length > 0)]}
```

### 特定の choices を持つ引数

```jq
.commands[] | .parameters[] | select(.choices | contains(["Goblin"]))
```

## デフォルト値

### デフォルト値ありの引数を持つコマンド

```jq
.commands[] | select(any(.parameters[]; .hasDefault)) | {path, defaults: [.parameters[] | select(.hasDefault) | {name, default}]}
```

### **必須引数のみ** で実行可能なコマンド (全部デフォルトあり、または引数 0 個)

```jq
.commands[] | select(all(.parameters[]; .hasDefault) or (.parameters | length == 0)) | .path
```

## description / metadata

### description が空のコマンド (記述漏れ検出)

```jq
.commands[] | select(.description == "") | .path
```

### description が長い順

```jq
.commands | sort_by(-(.description | length)) | .[0:10] | .[] | {path, len: (.description | length)}
```

### aliases を持つコマンド

```jq
.commands[] | select(.aliases | length > 0) | {path, aliases}
```

## 集計

### カテゴリ別の async 比率

```jq
[.commands[] | {category, isAsync}] | group_by(.category)
  | map({category: .[0].category, total: length, async: (map(select(.isAsync)) | length)})
```

### 引数の type 別頻度 (どの型がよく使われるか)

```jq
[.commands[].parameters[].type] | group_by(.) | map({type: .[0], count: length}) | sort_by(-.count)
```

## AI Agent 向け実用パターン

### 「特定の機能を呼ぶための候補コマンドを絞る」

ユーザー指示 "プレイヤーの HP を回復したい" に対して:

```jq
[.commands[] | select(
  ((.path + " " + .description + " " + (.parameters | map(.name) | join(" "))) | ascii_downcase)
  | test("(player|hp|health|heal)")
)] | .[0:10]
```

上位 10 件まで絞って候補に。

### 「呼ぶ前に引数の choices を全部見て、AI が選ぶ」

```bash
PATH_TO_RUN="Enemy/Spawn"
lp commands --json \
  | jq --arg p "$PATH_TO_RUN" '
    .commands[] | select(.path == $p) | .parameters[]
    | {name, type, hasDefault, choices: (if (.choices | length) > 0 then .choices else null end)}
  '
```

→ AI が choices から値を選んで `lp exec` に渡す。

### 「path だけ取って Markdown のリストにする」

```jq
.commands | sort_by(.path) | map("- `" + .path + "`" + (if .description != "" then " — " + .description else "" end)) | .[]
```

→ そのまま Discord / GitHub Issue に貼れる。
