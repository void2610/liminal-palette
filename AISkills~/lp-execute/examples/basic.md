# lp-execute — 基本パターン例集

各型の引数を持つコマンドを実行する curl の典型形。`$LP_TOKEN` と `$LP_BASE` がセット済みの前提。

## 1. 引数なし (副作用 only)

### Editor の Console をクリア

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Editor/Console/Clear","args":{}}'
```

### Player の HP を満タンに (引数 0 個のファサードコマンド)

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Player/Health/FullHeal","args":{}}'
```

⚠️ `"args": {}` を省略すると 400 BadRequest。0 個でも必ず空オブジェクト。

---

## 2. 単一の primitive 引数

### int

```bash
-d '{"path":"Player/Health/Set","args":{"value":"100"}}'
```

### float

```bash
-d '{"path":"Player/Speed/Set","args":{"value":"3.14"}}'
```

⚠️ 小数点は `.` 固定。`"3,14"` は失敗。

### string

```bash
-d '{"path":"Game/SetTitle","args":{"title":"Hello World"}}'
```

### bool

```bash
-d '{"path":"Game/SetGodMode","args":{"enabled":"true"}}'
-d '{"path":"Game/SetGodMode","args":{"enabled":"false"}}'
```

⚠️ `"yes"`, `"1"`, `"0"` は不可。bool は `"true"` / `"false"` (大小無視)。

---

## 3. 複数の primitive 引数

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Math/Add","args":{"a":"3","b":"4"}}'
# → {"success":true,"value":"7","durationMs":0.5,"logs":[]}
```

---

## 4. Vector 系

### Vector3 (3 要素)

```bash
# カンマ区切り
-d '{"path":"Player/Position/Teleport","args":{"pos":"1,2,3"}}'

# 空白区切り (寛容に解釈される)
-d '{"path":"Player/Position/Teleport","args":{"pos":"1 2 3"}}'

# 括弧付き
-d '{"path":"Player/Position/Teleport","args":{"pos":"(1, 2, 3)"}}'
```

### Vector2

```bash
-d '{"path":"UI/Anchor/Set","args":{"anchor":"0.5, 0.5"}}'
```

### Vector3Int

```bash
-d '{"path":"Tile/Place","args":{"cell":"10,20,0"}}'
```

⚠️ 小数を含むと失敗 (`"1.5,2,3"` は NG)。

---

## 5. Color

### HEX 表記 (推奨)

```bash
-d '{"path":"UI/Background/SetColor","args":{"c":"#FF8800"}}'
-d '{"path":"UI/Background/SetColor","args":{"c":"#FF8800CC"}}'  # alpha 付き
```

⚠️ Unity 標準色名 (`"red"`, `"blue"`) は `#` 付きでないと弾かれる。

### 数値表記

```bash
# Color (0..1 範囲)
-d '{"path":"UI/Background/SetColor","args":{"c":"1.0, 0.53, 0, 1.0"}}'

# Color32 (0..255 範囲)
-d '{"path":"Sprite/Tint","args":{"c":"255, 136, 0, 255"}}'
```

---

## 6. Enum

### 名前指定 (大小無視)

```bash
-d '{"path":"Player/Move","args":{"dir":"Up"}}'
-d '{"path":"Player/Move","args":{"dir":"up"}}'
-d '{"path":"Player/Move","args":{"dir":"DOWN"}}'
```

### 数値指定

```bash
-d '{"path":"Player/Move","args":{"dir":"0"}}'
```

### `[Flags]` Enum

```bash
-d '{"path":"File/SetPermission","args":{"perm":"Read,Write"}}'
-d '{"path":"File/SetPermission","args":{"perm":"3"}}'  # Read=1, Write=2 → 3
```

### choices 制約付き

`lp-list-commands` で choices を確認:

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq '.commands[] | select(.path == "Enemy/Spawn") | .parameters[] | {name, choices}'
# → {"name":"type","choices":["Goblin","Orc","Dragon"]}
```

```bash
-d '{"path":"Enemy/Spawn","args":{"type":"Goblin"}}'    # ✓
-d '{"path":"Enemy/Spawn","args":{"type":"Slime"}}'     # ✗ choices 外
```

---

## 7. デフォルト値ありの引数

`hasDefault: true` の引数は `args` から省略可能 (デフォルト値が使われる):

```bash
# spawn コマンドが count: int = 1, level: int = 5 をデフォルトに持つ場合
-d '{"path":"Enemy/Spawn","args":{"type":"Goblin"}}'                          # count=1, level=5
-d '{"path":"Enemy/Spawn","args":{"type":"Goblin","count":"3"}}'              # count=3, level=5
-d '{"path":"Enemy/Spawn","args":{"type":"Goblin","count":"3","level":"10"}}' # count=3, level=10
```

---

## 8. 結果の取り出し

### 成功時の value

```bash
RESP=$(curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Player/Position/Get","args":{}}')

echo "$RESP" | jq -r '.value'
# → "(1.50, 2.00, 3.00)"
```

### success / durationMs

```bash
echo "$RESP" | jq '{success, ms: .durationMs}'
```

### 実行中の Debug.Log

```bash
echo "$RESP" | jq '.logs[] | {type, message}'
```

### 失敗時のエラー

```bash
if [ "$(echo "$RESP" | jq -r '.success')" = "false" ]; then
  echo "$RESP" | jq '{error, exceptionType}'
fi
```

---

## 9. HEREDOC で長い JSON

bash の引用エスケープを避けるパターン:

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d @- <<'EOF'
{
  "path": "Inventory/AddItem",
  "args": {
    "itemId": "IronSword",
    "count": "5",
    "metadata": "Crafted by Player"
  }
}
EOF
```

`<<'EOF'` (シングルクォート) は body 内の `$` 等を展開しない。`<<EOF` は展開する。状況に応じて選ぶ。
