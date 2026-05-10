# liminal-execute — 高度なパターン

retry / async / 連続実行 / 結果連携の実用パターン。

## 1. async コマンドの実行

`isAsync: true` のコマンドは Task 完了まで HTTP がブロックされる。`liminal` の HTTP タイムアウトは 10 秒固定なので、長時間 async は注意:

```bash
liminal exec Stage/LoadAsync name=Stage02
```

10 秒を超える可能性があれば現状は CLI ソースの `TIMEOUT_SEC` を上げるか、シナリオ化して非同期に流す。

### async 一覧の発見

```bash
liminal commands --json | jq -r '.commands[] | select(.isAsync == true) | .path'
```

---

## 2. 失敗時のリトライ

### 引数バインド失敗 → スキーマ確認 → 修正リトライ

```bash
# 第 1 試行
RESP=$(liminal exec Player/Position/Teleport pos=1,2 --json)

if [ "$(echo "$RESP" | jq -r '.success')" = "false" ]; then
  echo "First attempt failed: $(echo "$RESP" | jq -r '.error')"

  # スキーマ確認
  liminal commands --json \
    | jq '.commands[] | select(.path == "Player/Position/Teleport") | .parameters'
  # → [{"name":"pos","type":"Vector3",...}]

  # Vector3 なので 3 要素必要だった。修正してリトライ
  RESP=$(liminal exec Player/Position/Teleport pos=1,2,3 --json)
fi

echo "$RESP" | jq '{success, value}'
```

### 401 が返ったとき

`liminal` はトークンを `~/.liminal-palette/token` から自動で読むので通常 401 は出ないが、token がローテートされた直後だけ環境変数 `$LP_TOKEN` の方が古いケースがある:

```bash
unset LP_TOKEN   # 環境変数を消してファイル読込みに戻す
liminal exec ...
```

明示指定したい場合は `--token`:

```bash
liminal --token "$(cat ~/.liminal-palette/token)" exec ...
```

---

## 3. 連続実行とレートリミット回避

### 30 req/s 上限を意識した連投

```bash
# 50 ms 間隔 = 20 req/s で安全圏
for i in 1 2 3 4 5 6 7 8 9 10; do
  liminal exec Enemy/Spawn type=Goblin "position=$i,0,0" --json | jq -r '.success'
  sleep 0.05
done
```

### scenarios の ad-hoc にまとめる (推奨)

10 spawn を 1 リクエストにすると rate limit 消費 1:

```bash
# steps を jq で組み立てて liminal run --steps - に流す
jq -n '
  [range(1; 11) | {
    type: "command",
    path: "Enemy/Spawn",
    args: { type: "Goblin", position: "\(.),0,0" }
  }]
' | liminal run --steps -
```

詳細: `/liminal-run-scenario`。

---

## 4. 戻り値を次のコマンドに渡す

LP に変数バインディング機構は無い。**シェル側で取り出して再注入**する:

```bash
# 1. 現在位置を取得
POS=$(liminal exec Player/Position/Get --json | jq -r '.value')
# POS="(1.50, 2.00, 3.00)"

# 2. パース ("(1.50, 2.00, 3.00)" → "1.50,2.00,3.00")
POS_CSV=$(echo "$POS" | sed -E 's/[()]//g; s/, /,/g')

# 3. 別コマンドに渡す
liminal exec Marker/Spawn "pos=$POS_CSV"
```

⚠️ 実行間に他のコマンドが走って状態が変わる可能性 (race condition)。同期的に必要なら `liminal-run-scenario` の ad-hoc を検討。

---

## 5. 実行履歴の活用

### 直近の失敗を再現してデバッグ

```bash
# 直近の失敗 1 件を取得
FAILED=$(liminal logs --limit 200 --json \
  | jq '[.invocations[] | select(.result.success == false)] | .[0]')

echo "$FAILED" | jq '{path, args, error: .result.error}'

# 引数を修正して再実行
PATH_TO_FIX=$(echo "$FAILED" | jq -r '.path')
liminal exec "$PATH_TO_FIX" value=50
```

### args オブジェクトを横展開して再実行

```bash
# 履歴の args を CLI 形式に変換 (key=value...)
KV=$(echo "$FAILED" | jq -r '.args | to_entries | map("\(.key)=\(.value)") | join(" ")')
PATH=$(echo "$FAILED" | jq -r '.path')
eval "liminal exec \"$PATH\" $KV"
```

⚠️ `eval` は値に空白が入ると壊れる。空白を含む引数があるなら配列で組み立てる:

```bash
mapfile -t KV_ARR < <(echo "$FAILED" | jq -r '.args | to_entries[] | "\(.key)=\(.value)"')
liminal exec "$PATH" "${KV_ARR[@]}"
```

---

## 6. 大きい引数を渡す

### 1 MB 以下: そのまま渡す

`liminal exec` 経由でも大きい文字列は渡せるが、シェルが値を展開する都合上、`'key=...'` のシングルクォート内に直書きすると ARG_MAX に当たることがある。

```bash
BIG_TEXT="..."  # ~500 KB
liminal exec Data/Process "text=$BIG_TEXT"
```

### 1 MB 超: ファイルパス渡し

LP 側でファイルパスを引数に受け取って中身を読む設計に変える:

```bash
TMPFILE=$(mktemp /tmp/lp-payload-XXXXXX.json)
echo "$HUGE_DATA" > "$TMPFILE"

# 利用側に [LiminalCommand("Data/ImportFile")] public void Import(string path) を実装しておく
liminal exec Data/ImportFile "path=$TMPFILE"

rm "$TMPFILE"
```

---

## 7. Editor / Runtime ポートを使い分ける

両稼働時、操作対象に応じて `--port` を切り替える:

```bash
# Editor 側 (asset / scene 操作)
liminal --port 7610 exec Editor/Console/Clear

# Runtime 側 (ゲーム状態操作)
liminal --port 7611 exec Player/Health/Set value=100
```

エイリアスを作っておくと便利:

```bash
alias lpe='liminal --port 7610'   # Editor
alias lpr='liminal --port 7611'   # Runtime

lpe exec Editor/Console/Clear
lpr exec Player/Health/Set value=100
```

---

## 8. シェル関数化 (使い回し)

`liminal` 自体が薄いので普通は不要だが、戻り値だけ取り出す用途なら:

```bash
lp_value() {
  liminal exec "$@" --json | jq -r '.value'
}

lp_success() {
  liminal exec "$@" --json | jq -r '.success'
}

# 使用例
HP=$(lp_value Player/HP/Get)
OK=$(lp_success Player/HP/Set value=100)
```

---

## 9. デバッグログ全部見る

```bash
RESP=$(liminal exec Diagnostic/RunFullCheck --json)

echo "Result: $(echo "$RESP" | jq -r '.success')"
echo "Logs:"
echo "$RESP" | jq -r '.logs[] | "[\(.type)] \(.message)"'

# Error / Exception level だけ
echo "$RESP" | jq '.logs[] | select(.type == "Error" or .type == "Exception")'
```

`liminal exec` の人間向け出力 (`--json` 無し) でも logs はカラー付きで列挙される。詳細解析が要らないならそちらで十分:

```bash
liminal exec Diagnostic/RunFullCheck
```

---

## 10. 環境変数とトークン管理

`liminal` は基本的に環境変数不要 (`~/.liminal-palette/token` を自動で読む)。明示指定したい場合のみ:

| 上書き方法 | 優先順位 |
|---|---|
| `--token T` / `--port N` / `--base-url URL` フラグ | 最優先 |
| 環境変数 `$LP_TOKEN` | フラグ無しの時のみ |
| `~/.liminal-palette/token` ファイル | 環境変数も無い時 |

開発中に token ローテート → ファイルが新しい場合は環境変数を消す:

```bash
unset LP_TOKEN
liminal exec ...   # → ファイルから最新 token を読む
```
