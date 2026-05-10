# liminal-get-state — 検証パターン集

`liminal state` を `liminal exec` と組み合わせて状態の変化を検証するシェルパターン。**多くは `liminal run --steps -` の `assert_equals` で代替可能だが、シェルでやるケースの参考**。

## 1. 単純な before / after

```bash
# 取り出しヘルパ
get_state() {
  liminal state "$1" --json | jq -r '.value'
}

before=$(get_state "Player/Health")
echo "before HP=$before"

liminal exec Player/Health/Damage amount=30 >/dev/null

after=$(get_state "Player/Health")
echo "after HP=$after"
echo "diff=$((before - after))"
```

## 2. 数値の閾値判定

```bash
hp=$(liminal state Player/Health --json | jq -r '.value')

if (( hp < 30 )); then
  echo "Critical: HP=$hp"
  liminal exec Player/Health/Heal amount=50
fi
```

## 3. float の閾値判定

```bash
speed=$(liminal state Player/Speed --json | jq -r '.value')

# bash の `(( ))` は float を扱えない。awk か bc を使う
if awk -v v="$speed" 'BEGIN { exit !(v > 5.0) }'; then
  echo "Speed too high: $speed"
fi
```

## 4. Vector3 の値をパース

```bash
pos=$(liminal state Player/Position --json | jq -r '.value')
# pos="(1.50, 2.00, 3.00)"

# カッコと空白を剥がして配列に
read -r x y z <<<"$(echo "$pos" | sed -E 's/[()]//g; s/,/ /g')"
echo "x=$x y=$y z=$z"

# x が 0 に近いか判定
if awk -v v="$x" 'BEGIN { exit !(v >= -0.1 && v <= 0.1) }'; then
  echo "x is near zero"
fi
```

## 5. Color の比較 (HEX)

```bash
color=$(liminal state UI/Background/Color --json | jq -r '.value')
# color="#FF8800FF"

# 大小無視で比較
if [ "${color^^}" = "#FF8800FF" ]; then
  echo "Color matches"
fi
```

## 6. Enum の名前比較

```bash
state=$(liminal state Game/State --json | jq -r '.value')
# state="Playing"

case "$state" in
  Playing)  echo "ゲーム進行中" ;;
  Paused)   echo "ポーズ中" ;;
  GameOver) echo "ゲームオーバー" ;;
  *)        echo "未知の状態: $state" ;;
esac
```

## 7. ポーリング (条件達成まで待つ)

```bash
# 最大 30 秒、Player/Position の x が 10 を超えるまで待つ
end=$(($(date +%s) + 30))
while [ "$(date +%s)" -lt "$end" ]; do
  pos=$(liminal state Player/Position --json | jq -r '.value')
  read -r x _ _ <<<"$(echo "$pos" | sed -E 's/[()]//g; s/,/ /g')"

  if awk -v v="$x" 'BEGIN { exit !(v > 10) }'; then
    echo "Done: x=$x"
    break
  fi

  sleep 0.2
done
```

⚠️ ポーリングは LP のメインスレッドに負荷をかける。可能なら scenarios の `wait_frames` のほうが正確。

## 8. 複数フィールドの一括チェック

```bash
RESP=$(liminal state --json)

# Player/* が全部 != null
all_resolved=$(echo "$RESP" | jq '
  [.fields[] | select(.path | startswith("Player/"))]
  | all(.value != null)
')

if [ "$all_resolved" = "true" ]; then
  echo "Player の全フィールドが解決済み"
else
  echo "未解決のフィールドあり:"
  echo "$RESP" | jq -r '.fields[] | select(.path | startswith("Player/")) | select(.value == null) | .path'
fi
```

## 9. 期待値辞書との一致チェック

```bash
declare -A EXPECTED=(
  ["Player/Health"]="100"
  ["Player/Mana"]="50"
  ["Game/Stage"]="1"
)

failures=0
for path in "${!EXPECTED[@]}"; do
  actual=$(liminal state "$path" --json | jq -r '.value')
  expected="${EXPECTED[$path]}"
  if [ "$actual" != "$expected" ]; then
    echo "FAIL: $path expected=$expected actual=$actual"
    failures=$((failures + 1))
  fi
done

[ "$failures" -eq 0 ] && echo "All passed" || echo "$failures field(s) mismatched"
```

## 10. `assert_equals` ベースに置き換える (推奨)

上記 9 は scenarios で 1 リクエストに:

```bash
cat <<'EOF' | liminal run --steps - --json \
  | jq '{success, failedAtStep, failed: [.steps[] | select(.success == false)]}'
[
  {"type":"assert_equals","path":"Player/Health","expected":"100"},
  {"type":"assert_equals","path":"Player/Mana","expected":"50"},
  {"type":"assert_equals","path":"Game/Stage","expected":"1"}
]
EOF
```

詳細: `/liminal-run-scenario`。

---

## シェルでの値比較の落とし穴まとめ

| 型 | 比較方法 | 例 |
|---|---|---|
| `Int32` / `Int64` | `(( a < b ))` または `[ "$a" -eq "$b" ]` | `(( hp < 30 ))` |
| `Single` / `Double` | `awk -v` または `bc` | `awk -v a="$f" 'BEGIN { exit !(a > 5.0) }'` |
| `String` | `[ "$a" = "$b" ]` | `[ "$state" = "Playing" ]` |
| Enum | string 比較 | 同上 |
| `Vector3` | sed でパース後個別比較 | パターン 4 を参照 |
| `Color` | HEX 大小揃えて string 比較 | `[ "${a^^}" = "${b^^}" ]` |
| `bool` | `[ "$v" = "True" ]` | LP の `ToDisplayString` は `"True"` / `"False"` |

シェルの数値比較が複雑なケースは **scenarios の `assert_equals` に逃がす**のが楽。LP 側で型解決してから比較するためシェル側のパースが不要。
