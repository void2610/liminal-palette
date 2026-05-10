# Ad-hoc Scenario — レシピ集

CLI 側で `steps[]` を組み立てて **その場限りの統合テスト**を回すパターン集。AI Agent が状況に応じて動的にステップ列を組む用途に。

`liminal run --steps -` は stdin から JSON を読む (配列直書きでも `{"steps":[...]}` でも OK)。`liminal run --steps FILE` でファイルからも読める。

## 基本: spawn → assert

```bash
cat <<'EOF' | liminal run --steps - --json | jq '{success, failedAtStep}'
[
  {"type":"command","path":"Enemy/Spawn","args":{"type":"Goblin"}},
  {"type":"assert_equals","path":"Enemy/Hp","expected":"100"}
]
EOF
```

## HEREDOC で長い JSON

シェルの引用エスケープを避ける:

```bash
cat <<'EOF' | liminal run --steps -
{
  "steps": [
    {"type":"command","path":"Player/Position/Teleport","args":{"pos":"0,0,0"}},
    {"type":"wait_frames","frames":1},
    {"type":"assert_equals","path":"Player/Position","expected":"(0.00, 0.00, 0.00)"},
    {"type":"command","path":"Enemy/Spawn","args":{"type":"Goblin"}},
    {"type":"wait_seconds","seconds":0.5},
    {"type":"assert_not_equals","path":"Enemy/Count","expected":"0","description":"spawn 成功"}
  ]
}
EOF
```

## レシピ 1: ループで動的に steps を生成 (10 体スポーン)

```bash
jq -n '
  [range(1; 11) | {
    type: "command",
    path: "Enemy/Spawn",
    args: { type: "Goblin", position: "\(.),0,0" }
  }] + [
    {type:"assert_equals", path:"Enemy/Count", expected:"10"}
  ]
' | liminal run --steps -
```

10 個の execute を 1 リクエストにまとめている → rate limit 消費 1。

## レシピ 2: jq で steps を組み立てる (型安全)

シェル文字列連結より jq のほうが安全:

```bash
jq -n '[
  {type:"command", path:"Player/Health/Set", args:{value:"100"}},
  {type:"command", path:"Player/Mana/Set",   args:{value:"50"}},
  {type:"assert_equals", path:"Player/Health", expected:"100"},
  {type:"assert_equals", path:"Player/Mana",   expected:"50"}
]' | liminal run --steps -
```

## レシピ 3: 戻り値を取得 (commandResult.value)

```bash
RESP=$(cat <<'EOF' | liminal run --steps - --json
[
  {"type":"command","path":"Player/Position/Get","args":{}},
  {"type":"command","path":"Enemy/Count/Get","args":{}}
]
EOF
)

# 各 command ステップの戻り値だけ抜き出す
echo "$RESP" | jq '.steps[] | select(.kind == "Command") | {path: .commandPath, value: .commandResult.value}'
```

## レシピ 4: 物理シミュレーションのテスト

```bash
cat <<'EOF' | liminal run --steps -
[
  {"type":"command","path":"Object/SpawnAt","args":{"prefab":"Ball","pos":"0,5,0"}},
  {"type":"command","path":"Time/Pause","args":{}},
  {"type":"assert_equals","path":"Ball/Position","expected":"(0.00, 5.00, 0.00)","description":"初期位置"},
  {"type":"command","path":"Time/Resume","args":{}},
  {"type":"wait_frames","frames":60,"description":"1 秒物理を進める"},
  {"type":"assert_not_equals","path":"Ball/Position","expected":"(0.00, 5.00, 0.00)","description":"重力で落下したはず"}
]
EOF
```

## レシピ 5: AB テスト (条件分岐は無いので 2 回叩く)

LP のシナリオには if/else が無い。条件分岐は外側のシェルで:

```bash
state=$(liminal state Game/State --json | jq -r '.value')

if [ "$state" = "InCombat" ]; then
  STEPS='[
    {"type":"command","path":"Combat/Flee","args":{}},
    {"type":"assert_equals","path":"Game/State","expected":"Field"}
  ]'
else
  STEPS='[
    {"type":"command","path":"Game/StartCombat","args":{}},
    {"type":"assert_equals","path":"Game/State","expected":"InCombat"}
  ]'
fi

echo "$STEPS" | liminal run --steps -
```

## レシピ 6: ファジング (ランダム引数で複数回実行)

```bash
for i in $(seq 1 20); do
  hp=$((RANDOM % 100 + 1))
  amount=$((RANDOM % 50 + 1))
  expected=$(( hp - amount > 0 ? hp - amount : 0 ))

  jq -n --arg hp "$hp" --arg amount "$amount" --arg expected "$expected" '[
    {type:"command", path:"Player/Health/Set",    args:{value:$hp}},
    {type:"command", path:"Player/Health/Damage", args:{amount:$amount}},
    {type:"assert_equals", path:"Player/Health", expected:$expected}
  ]' \
  | liminal run --steps - --json \
  | jq -r --arg i "$i" --arg hp "$hp" --arg amount "$amount" --arg expected "$expected" \
      '"[\($i)] hp=\($hp) dmg=\($amount) expected=\($expected) → success=\(.success)"'

  sleep 0.05
done
```

## レシピ 7: 既存の状態を保存して終了時に復元

```bash
# 1. 現状を取得
HP_BEFORE=$(liminal state Player/Health --json | jq -r '.value')

# 2. テスト実行
cat <<'EOF' | liminal run --steps -
[
  {"type":"command","path":"Player/Health/Set","args":{"value":"1"}},
  {"type":"command","path":"Player/Health/Damage","args":{"amount":"100"}},
  {"type":"assert_equals","path":"Player/Health","expected":"0"}
]
EOF

# 3. 復元
liminal exec Player/Health/Set "value=$HP_BEFORE"
```

## レシピ 8: 失敗ステップの詳細レポート

```bash
RESP=$(liminal run --steps tests/scenario.json --json)

if [ "$(echo "$RESP" | jq -r '.success')" = "true" ]; then
  echo "PASSED ($(echo "$RESP" | jq -r '.durationMs')ms)"
else
  failed_idx=$(echo "$RESP" | jq -r '.failedAtStep')
  echo "FAILED at step $failed_idx"
  echo "$RESP" | jq '.steps[] | select(.success == false) | .'
  echo "---"
  echo "Last passing step:"
  echo "$RESP" | jq --argjson i "$failed_idx" '.steps[$i - 1]?'
fi
```

## レシピ 9: 並列に見える複数シナリオ (実は逐次)

LP は scenario の 1 並列制限があるので、シェルで並列実行しても LP 側でシリアライズされる:

```bash
# どっちかが 409 Conflict で弾かれる
liminal run Test/A &
liminal run Test/B &
wait
```

並列を諦めて 1 シナリオに連結:

```bash
cat <<'EOF' | liminal run --steps -
[
  ... A の steps,
  ... B の steps
]
EOF
```

## レシピ 10: rate limit を意識した分割実行

100 step 以上の巨大な ad-hoc は (実装上問題ないが) 時間がかかるので、論理的単位で分けてレポートする:

```bash
SETUP_STEPS='[
  {"type":"command","path":"World/Reset","args":{}},
  {"type":"command","path":"Player/Spawn","args":{}},
  {"type":"wait_frames","frames":1}
]'
TEST_STEPS='[
  {"type":"command","path":"Player/Damage","args":{"amount":"30"}},
  {"type":"assert_equals","path":"Player/Health","expected":"70"}
]'

# Setup
echo "$SETUP_STEPS" | liminal run --steps - --json | jq '.success'

# Test (失敗してもセットアップは効いている状態)
echo "$TEST_STEPS" | liminal run --steps - --json | jq '.success'
```

ただし scenarios 同士は 1 並列 = 直列で走るので、間に時間が空くと別の操作が割り込む可能性あり。**1 リクエストにまとめるほうが原子性が高い**。

---

## ad-hoc を named に格上げするタイミング

ad-hoc が:
- 3 回以上同じ手順で書かれている
- リポジトリで共有したい
- CI で固定テストとして回したい

これらのいずれかなら、C# 側に `[LiminalScenario]` で宣言して named 化する。詳細: [named.md](named.md)。
