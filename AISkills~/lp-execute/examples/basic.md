# lp-execute — 基本パターン例集

各型の引数を持つコマンドを `lp exec` で実行する典型形。

## 1. 引数なし (副作用 only)

### Editor の Console をクリア

```bash
lp exec Editor/Console/Clear
```

### Player の HP を満タンに (引数 0 個のファサードコマンド)

```bash
lp exec Player/Health/FullHeal
```

`lp exec` では引数 0 個の場合 `args={}` を勝手に送ってくれるので、CLI 側で何も書かなくて良い。

---

## 2. 単一の primitive 引数

### int

```bash
lp exec Player/Health/Set value=100
```

### float

```bash
lp exec Player/Speed/Set value=3.14
```

⚠️ 小数点は `.` 固定。`value=3,14` は失敗。

### string

シェルが空白で引数分割しないようクォート:

```bash
lp exec Game/SetTitle 'title=Hello World'
```

### bool

```bash
lp exec Game/SetGodMode enabled=true
lp exec Game/SetGodMode enabled=false
```

⚠️ `yes`, `1`, `0` は不可。bool は `true` / `false` (大小無視)。

---

## 3. 複数の primitive 引数

```bash
lp exec Math/Add a=3 b=4
# → success  (0.5 ms)
#     value : 7
```

---

## 4. Vector 系

### Vector3 (3 要素)

```bash
# カンマ区切り
lp exec Player/Position/Teleport pos=1,2,3

# 空白区切り (寛容に解釈される) — シェルクォート必須
lp exec Player/Position/Teleport 'pos=1 2 3'

# 括弧付き
lp exec Player/Position/Teleport 'pos=(1, 2, 3)'
```

### Vector2

```bash
lp exec UI/Anchor/Set 'anchor=0.5, 0.5'
```

### Vector3Int

```bash
lp exec Tile/Place cell=10,20,0
```

⚠️ 小数を含むと失敗 (`cell=1.5,2,3` は NG)。

---

## 5. Color

### HEX 表記 (推奨)

```bash
lp exec UI/Background/SetColor c=#FF8800
lp exec UI/Background/SetColor c=#FF8800CC   # alpha 付き
```

⚠️ Unity 標準色名 (`red`, `blue`) は `#` 付きでないと弾かれる。

### 数値表記

```bash
# Color (0..1 範囲)
lp exec UI/Background/SetColor 'c=1.0, 0.53, 0, 1.0'

# Color32 (0..255 範囲)
lp exec Sprite/Tint 'c=255, 136, 0, 255'
```

---

## 6. Enum

### 名前指定 (大小無視)

```bash
lp exec Player/Move dir=Up
lp exec Player/Move dir=up
lp exec Player/Move dir=DOWN
```

### 数値指定

```bash
lp exec Player/Move dir=0
```

### `[Flags]` Enum

```bash
lp exec File/SetPermission perm=Read,Write
lp exec File/SetPermission perm=3   # Read=1, Write=2 → 3
```

### choices 制約付き

`lp commands` で choices を確認:

```bash
lp commands --json \
  | jq '.commands[] | select(.path == "Enemy/Spawn") | .parameters[] | {name, choices}'
# → {"name":"type","choices":["Goblin","Orc","Dragon"]}
```

```bash
lp exec Enemy/Spawn type=Goblin    # ✓
lp exec Enemy/Spawn type=Slime     # ✗ choices 外 (exit code 2)
```

---

## 7. デフォルト値ありの引数

`hasDefault: true` の引数は省略可能 (デフォルト値が使われる):

```bash
# spawn コマンドが count: int = 1, level: int = 5 をデフォルトに持つ場合
lp exec Enemy/Spawn type=Goblin                     # count=1, level=5
lp exec Enemy/Spawn type=Goblin count=3             # count=3, level=5
lp exec Enemy/Spawn type=Goblin count=3 level=10    # count=3, level=10
```

---

## 8. 結果の取り出し

### 成功時の value

```bash
RESP=$(lp exec Player/Position/Get --json)
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
if ! lp exec Player/Health/Set value=abc; then
  echo "exit code: $?"   # 2 = success:false, 1 = 通信失敗
fi

# JSON で詳細を見る
RESP=$(lp exec Player/Health/Set value=abc --json)
echo "$RESP" | jq '{error, exceptionType}'
```

---

## 9. シェル変数を埋め込む

```bash
NEW_HP=75
lp exec Player/Health/Set "value=$NEW_HP"

# クォート位置に注意 — value= までを 1 引数にする
ITEM="Iron Sword"
lp exec Inventory/Add "itemId=$ITEM"
```

`lp` は `key=value` をそのまま JSON の `args` に詰めるだけなので、シェル展開で組み立てて渡せば良い。

### 複数の動的引数を組み立てる

```bash
ARGS=(
  "type=Goblin"
  "count=$N"
  "position=$X,$Y,$Z"
)
lp exec Enemy/Spawn "${ARGS[@]}"
```
