# lp-execute — 高度なパターン

retry / async / 連続実行 / 結果連携の実用パターン。

## 1. async コマンドの実行

`isAsync: true` のコマンドは Task 完了まで HTTP がブロックされる。タイムアウト指定が安全:

```bash
curl --max-time 30 -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Stage/LoadAsync","args":{"name":"Stage02"}}'
```

`--max-time 30` は curl 全体のタイムアウト。LP 側にコマンド実行のタイムアウト機構は無い (Task が永遠に終わらないと curl 側で切るしかない)。

### async 一覧の発見

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq '.commands[] | select(.isAsync == true) | .path'
```

---

## 2. 失敗時のリトライ

### 引数バインド失敗 → スキーマ確認 → 修正リトライ

```bash
RUN_LP() {
  curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
    -X POST "$LP_BASE/api/v1/execute" -d "$1"
}

PAYLOAD='{"path":"Player/Position/Teleport","args":{"pos":"1,2"}}'
RESP=$(RUN_LP "$PAYLOAD")

if [ "$(echo "$RESP" | jq -r '.success')" = "false" ]; then
  echo "First attempt failed: $(echo "$RESP" | jq -r '.error')"

  # スキーマ確認
  curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
    | jq '.commands[] | select(.path == "Player/Position/Teleport") | .parameters'
  # → [{"name":"pos","type":"Vector3",...}]

  # Vector3 なので 3 要素必要だった。修正してリトライ
  PAYLOAD='{"path":"Player/Position/Teleport","args":{"pos":"1,2,3"}}'
  RESP=$(RUN_LP "$PAYLOAD")
fi

echo "$RESP" | jq '{success, value}'
```

### 401 → token 再読み込み → リトライ

```bash
http_status=$(curl -s -o /tmp/resp -w '%{http_code}' \
  -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" -d "$PAYLOAD")

if [ "$http_status" = "401" ]; then
  export LP_TOKEN=$(cat ~/.liminal-palette/token)
  curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
    -X POST "$LP_BASE/api/v1/execute" -d "$PAYLOAD"
else
  cat /tmp/resp
fi
```

---

## 3. 連続実行とレートリミット回避

### 30 req/s 上限を意識した連投

```bash
# 50 ms 間隔 = 20 req/s で安全圏
for i in 1 2 3 4 5 6 7 8 9 10; do
  curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
    -X POST "$LP_BASE/api/v1/execute" \
    -d "{\"path\":\"Enemy/Spawn\",\"args\":{\"type\":\"Goblin\",\"position\":\"$i,0,0\"}}" \
    | jq -r '.success'
  sleep 0.05
done
```

### scenarios の ad-hoc にまとめる (推奨)

10 spawn を 1 リクエストにすると rate limit 消費 1:

```bash
# steps 配列を bash で組み立て
STEPS=$(for i in 1 2 3 4 5 6 7 8 9 10; do
  echo "{\"type\":\"command\",\"path\":\"Enemy/Spawn\",\"args\":{\"type\":\"Goblin\",\"position\":\"$i,0,0\"}}"
done | paste -sd, -)

curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d "{\"steps\":[$STEPS]}"
```

詳細: `/lp-run-scenario`。

---

## 4. 戻り値を次のコマンドに渡す

LP に変数バインディング機構は無い。**bash 側で取り出して再注入**する:

```bash
# 1. 現在位置を取得
POS=$(curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Player/Position/Get","args":{}}' \
  | jq -r '.value')
# POS="(1.50, 2.00, 3.00)"

# 2. パース ("(1.50, 2.00, 3.00)" → "1.50,2.00,3.00")
POS_CSV=$(echo "$POS" | sed -E 's/[()]//g; s/, /,/g')

# 3. オフセットを足して別コマンドに渡す (実用では計算が必要)
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d "{\"path\":\"Marker/Spawn\",\"args\":{\"pos\":\"$POS_CSV\"}}"
```

⚠️ 実行間に他のコマンドが走って状態が変わる可能性 (race condition)。同期的に必要なら `lp-run-scenario` の ad-hoc を検討。

---

## 5. 実行履歴の活用

### 直近の失敗を再現してデバッグ

```bash
# 直近 10 件の失敗を取得
FAILED=$(curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=200" \
  | jq '[.invocations[] | select(.result.success == false)] | .[0]')

echo "$FAILED" | jq '{path, args, error: .result.error}'

# 引数を修正して再実行
PATH_TO_FIX=$(echo "$FAILED" | jq -r '.path')
NEW_ARGS='{"value":"50"}'   # 修正後の args
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d "{\"path\":\"$PATH_TO_FIX\",\"args\":$NEW_ARGS}"
```

---

## 6. 大きい引数を渡す

### 1 MB 以下: そのまま JSON に詰める

```bash
BIG_TEXT="..."  # ~500 KB
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d "$(jq -n --arg t "$BIG_TEXT" '{path:"Data/Process", args:{text:$t}}')"
```

`jq -n --arg` で string を安全にエスケープ。

### 1 MB 超: ファイルパス渡し

```bash
TMPFILE=$(mktemp /tmp/lp-payload-XXXXXX.json)
echo "$HUGE_DATA" > "$TMPFILE"

# 利用側に [ConsoleCommand("Data/ImportFile")] public void Import(string path) を実装しておく
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d "{\"path\":\"Data/ImportFile\",\"args\":{\"path\":\"$TMPFILE\"}}"

rm "$TMPFILE"
```

---

## 7. Editor / Runtime ポートを使い分ける

両稼働時、操作対象に応じて base URL を切り替える:

```bash
# Editor 側 (asset / scene 操作)
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE_EDITOR/api/v1/execute" \
  -d '{"path":"Editor/Console/Clear","args":{}}'

# Runtime 側 (ゲーム状態操作)
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE_RUNTIME/api/v1/execute" \
  -d '{"path":"Player/Health/Set","args":{"value":"100"}}'
```

`$LP_BASE_EDITOR` / `$LP_BASE_RUNTIME` のセットは `/lp-find-port` の examples/multi-instance.sh を参照。

---

## 8. shell 関数化 (使い回し)

```bash
lp_exec() {
  local path="$1"
  local args="${2:-{\\}}"  # 既定は空オブジェクト
  curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
    -X POST "$LP_BASE/api/v1/execute" \
    -d "$(jq -n --arg p "$path" --argjson a "$args" '{path:$p, args:$a}')"
}

# 使用例
lp_exec "Player/Health/Set" '{"value":"100"}'
lp_exec "Editor/Console/Clear" '{}'
lp_exec "Player/Position/Teleport" '{"pos":"1,2,3"}'
```

---

## 9. デバッグログ全部見る

```bash
RESP=$(curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Diagnostic/RunFullCheck","args":{}}')

echo "Result: $(echo "$RESP" | jq -r '.success')"
echo "Logs:"
echo "$RESP" | jq -r '.logs[] | "[\(.type)] \(.message)"'

# Error / Exception level だけ
echo "$RESP" | jq '.logs[] | select(.type == "Error" or .type == "Exception")'
```

---

## 10. 環境変数を全部 reset

debug 中にトークンやポートが変わった時:

```bash
unset LP_TOKEN LP_PORT LP_BASE LP_BASE_EDITOR LP_BASE_RUNTIME LP_PORT_EDITOR LP_PORT_RUNTIME

export LP_TOKEN=$(cat ~/.liminal-palette/token)
source <(curl -s file:///path/to/AISkills~/lp-find-port/examples/multi-instance.sh)
# あるいは手動で再セット
```
