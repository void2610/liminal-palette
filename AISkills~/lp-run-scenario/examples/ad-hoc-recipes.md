# Ad-hoc Scenario — レシピ集

curl 側で `steps[]` を組み立てて **その場限りの統合テスト**を回すパターン集。AI Agent が状況に応じて動的にステップ列を組む用途に。

## 基本: spawn → assert

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d '{
    "steps": [
      {"type":"command","path":"Enemy/Spawn","args":{"type":"Goblin"}},
      {"type":"assert_equals","path":"Enemy/Hp","expected":"100"}
    ]
  }' | jq '{success, failedAtStep}'
```

## HEREDOC で長い JSON

bash の引用エスケープを避ける:

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d @- <<'EOF'
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
STEPS=$(for i in $(seq 1 10); do
  echo "{\"type\":\"command\",\"path\":\"Enemy/Spawn\",\"args\":{\"type\":\"Goblin\",\"position\":\"$i,0,0\"}}"
done | paste -sd, -)

curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d "{\"steps\":[$STEPS,{\"type\":\"assert_equals\",\"path\":\"Enemy/Count\",\"expected\":\"10\"}]}"
```

10 個の execute を 1 リクエストにまとめている → rate limit 消費 1。

## レシピ 2: jq で steps を組み立てる (型安全)

bash の文字列連結より jq のほうが安全:

```bash
STEPS=$(jq -n '[
  {type:"command", path:"Player/Health/Set", args:{value:"100"}},
  {type:"command", path:"Player/Mana/Set",   args:{value:"50"}},
  {type:"assert_equals", path:"Player/Health", expected:"100"},
  {type:"assert_equals", path:"Player/Mana",   expected:"50"}
]')

curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d "$(jq -n --argjson steps "$STEPS" '{steps:$steps}')"
```

## レシピ 3: 戻り値を取得 (commandResult.value)

```bash
RESP=$(curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d '{
    "steps": [
      {"type":"command","path":"Player/Position/Get","args":{}},
      {"type":"command","path":"Enemy/Count/Get","args":{}}
    ]
  }')

# 各 command ステップの戻り値だけ抜き出す
echo "$RESP" | jq '.steps[] | select(.kind == "Command") | {path: .commandPath, value: .commandResult.value}'
```

## レシピ 4: 物理シミュレーションのテスト

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d '{
    "steps": [
      {"type":"command","path":"Object/SpawnAt","args":{"prefab":"Ball","pos":"0,5,0"}},
      {"type":"command","path":"Time/Pause","args":{}},
      {"type":"assert_equals","path":"Ball/Position","expected":"(0.00, 5.00, 0.00)","description":"初期位置"},
      {"type":"command","path":"Time/Resume","args":{}},
      {"type":"wait_frames","frames":60,"description":"1 秒物理を進める"},
      {"type":"assert_not_equals","path":"Ball/Position","expected":"(0.00, 5.00, 0.00)","description":"重力で落下したはず"}
    ]
  }'
```

## レシピ 5: AB テスト (条件分岐は無いので 2 回叩く)

LP のシナリオには if/else が無い。条件分岐は外側の bash で:

```bash
state=$(curl -s -H "Authorization: Bearer $LP_TOKEN" \
          "$LP_BASE/api/v1/state?path=Game/State" | jq -r '.value')

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

curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d "{\"steps\":$STEPS}"
```

## レシピ 6: ファジング (ランダム引数で複数回実行)

```bash
for i in $(seq 1 20); do
  hp=$((RANDOM % 100 + 1))
  amount=$((RANDOM % 50 + 1))
  expected=$(( hp - amount > 0 ? hp - amount : 0 ))

  curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
    -X POST "$LP_BASE/api/v1/scenarios/run" \
    -d "{\"steps\":[
      {\"type\":\"command\",\"path\":\"Player/Health/Set\",\"args\":{\"value\":\"$hp\"}},
      {\"type\":\"command\",\"path\":\"Player/Health/Damage\",\"args\":{\"amount\":\"$amount\"}},
      {\"type\":\"assert_equals\",\"path\":\"Player/Health\",\"expected\":\"$expected\"}
    ]}" \
    | jq -r --arg i "$i" '"[\($i)] hp=$hp dmg=$amount expected=$expected → success=\(.success)"' \
    | sed "s/\$hp/$hp/g; s/\$amount/$amount/g; s/\$expected/$expected/g"

  sleep 0.05
done
```

## レシピ 7: 既存の状態を保存して終了時に復元

```bash
# 1. 現状を取得
HP_BEFORE=$(curl -s -H "Authorization: Bearer $LP_TOKEN" \
              "$LP_BASE/api/v1/state?path=Player/Health" | jq -r '.value')

# 2. テスト実行
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d '{
    "steps": [
      {"type":"command","path":"Player/Health/Set","args":{"value":"1"}},
      {"type":"command","path":"Player/Health/Damage","args":{"amount":"100"}},
      {"type":"assert_equals","path":"Player/Health","expected":"0"}
    ]
  }'

# 3. 復元
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d "{\"path\":\"Player/Health/Set\",\"args\":{\"value\":\"$HP_BEFORE\"}}"
```

## レシピ 8: 失敗ステップの詳細レポート

```bash
RESP=$(curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d '{ "steps": [ ... ] }')

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

LP は scenario の 1 並列制限があるので、bash で並列 curl しても LP 側でシリアライズされる:

```bash
# どっちかが 409 Conflict で弾かれる
curl ... -d '{"path":"Test/A"}' &
curl ... -d '{"path":"Test/B"}' &
wait
```

並列を諦めて 1 シナリオに連結:

```bash
curl ... -d '{"steps":[
  ... A の steps,
  ... B の steps
]}'
```

## レシピ 10: rate limit を意識した分割実行

100 step 以上の巨大な ad-hoc は (実装上の問題ないが) 時間がかかるので、論理的単位で分けてレポートする:

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
curl -s ... -X POST ... -d "{\"steps\":$SETUP_STEPS}" | jq '.success'

# Test (失敗してもセットアップは効いている状態)
curl -s ... -X POST ... -d "{\"steps\":$TEST_STEPS}" | jq '.success'
```

ただし scenarios 同士は 1 並列 = 直列で走るので、間に時間が空くと別の操作が割り込む可能性あり。**1 リクエストにまとめるほうが原子性が高い**。

---

## ad-hoc を named に格上げするタイミング

ad-hoc が:
- 3 回以上同じ手順で書かれている
- リポジトリで共有したい
- CI で固定テストとして回したい

これらのいずれかなら、C# 側に `[ConsoleScenario]` で宣言して named 化する。詳細: [named.md](named.md)。
