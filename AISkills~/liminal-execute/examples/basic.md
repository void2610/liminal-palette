# liminal-execute — 基本パターン例集

各型の引数を持つコマンドを `liminal exec` で実行する典型形。

## 1. 引数なし (副作用 only)

### Editor の Console をクリア

```bash
liminal exec Editor/Console/Clear
```

### Player の HP を満タンに (引数 0 個のファサードコマンド)

```bash
liminal exec Player/Health/FullHeal
```

`liminal exec` では引数 0 個の場合 `args={}` を勝手に送ってくれるので、CLI 側で何も書かなくて良い。

---

## 2. 単一の primitive 引数

### int

```bash
liminal exec Player/Health/Set value=100
```

### float

```bash
liminal exec Player/Speed/Set value=3.14
```

⚠️ 小数点は `.` 固定。`value=3,14` は失敗。

### string

シェルが空白で引数分割しないようクォート:

```bash
liminal exec Game/SetTitle 'title=Hello World'
```

### bool

```bash
liminal exec Game/SetGodMode enabled=true
liminal exec Game/SetGodMode enabled=false
```

⚠️ `yes`, `1`, `0` は不可。bool は `true` / `false` (大小無視)。

---

## 3. 複数の primitive 引数

```bash
liminal exec Math/Add a=3 b=4
# → success  (0.5 ms)
#     value : 7
```

---

## 4. Vector 系

### Vector3 (3 要素)

```bash
# カンマ区切り
liminal exec Player/Position/Teleport pos=1,2,3

# 空白区切り (寛容に解釈される) — シェルクォート必須
liminal exec Player/Position/Teleport 'pos=1 2 3'

# 括弧付き
liminal exec Player/Position/Teleport 'pos=(1, 2, 3)'
```

### Vector2

```bash
liminal exec UI/Anchor/Set 'anchor=0.5, 0.5'
```

### Vector3Int

```bash
liminal exec Tile/Place cell=10,20,0
```

⚠️ 小数を含むと失敗 (`cell=1.5,2,3` は NG)。

---

## 5. Color

### HEX 表記 (推奨)

```bash
liminal exec UI/Background/SetColor c=#FF8800
liminal exec UI/Background/SetColor c=#FF8800CC   # alpha 付き
```

⚠️ Unity 標準色名 (`red`, `blue`) は `#` 付きでないと弾かれる。

### 数値表記

```bash
# Color (0..1 範囲)
liminal exec UI/Background/SetColor 'c=1.0, 0.53, 0, 1.0'

# Color32 (0..255 範囲)
liminal exec Sprite/Tint 'c=255, 136, 0, 255'
```

---

## 6. Enum

### 名前指定 (大小無視)

```bash
liminal exec Player/Move dir=Up
liminal exec Player/Move dir=up
liminal exec Player/Move dir=DOWN
```

### 数値指定

```bash
liminal exec Player/Move dir=0
```

### `[Flags]` Enum

```bash
liminal exec File/SetPermission perm=Read,Write
liminal exec File/SetPermission perm=3   # Read=1, Write=2 → 3
```

### choices 制約付き

`liminal commands` で choices を確認:

```bash
liminal commands --json \
  | jq '.commands[] | select(.path == "Enemy/Spawn") | .parameters[] | {name, choices}'
# → {"name":"type","choices":["Goblin","Orc","Dragon"]}
```

```bash
liminal exec Enemy/Spawn type=Goblin    # ✓
liminal exec Enemy/Spawn type=Slime     # ✗ choices 外 (exit code 2)
```

---

## 7. デフォルト値ありの引数

`hasDefault: true` の引数は省略可能 (デフォルト値が使われる):

```bash
# spawn コマンドが count: int = 1, level: int = 5 をデフォルトに持つ場合
liminal exec Enemy/Spawn type=Goblin                     # count=1, level=5
liminal exec Enemy/Spawn type=Goblin count=3             # count=3, level=5
liminal exec Enemy/Spawn type=Goblin count=3 level=10    # count=3, level=10
```

---

## 8. 結果の取り出し

### 成功時の value

```bash
RESP=$(liminal exec Player/Position/Get --json)
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
if ! liminal exec Player/Health/Set value=abc; then
  echo "exit code: $?"   # 2 = success:false, 1 = 通信失敗
fi

# JSON で詳細を見る
RESP=$(liminal exec Player/Health/Set value=abc --json)
echo "$RESP" | jq '{error, exceptionType}'
```

---

## 9. シェル変数を埋め込む

```bash
NEW_HP=75
liminal exec Player/Health/Set "value=$NEW_HP"

# クォート位置に注意 — value= までを 1 引数にする
ITEM="Iron Sword"
liminal exec Inventory/Add "itemId=$ITEM"
```

`liminal` は `key=value` をそのまま JSON の `args` に詰めるだけなので、シェル展開で組み立てて渡せば良い。

### 複数の動的引数を組み立てる

```bash
ARGS=(
  "type=Goblin"
  "count=$N"
  "position=$X,$Y,$Z"
)
liminal exec Enemy/Spawn "${ARGS[@]}"
```
