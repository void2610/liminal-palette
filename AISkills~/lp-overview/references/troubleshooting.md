# LP HTTP API — Troubleshooting

## セットアップが失敗する

### 全ポートで `/health` が応答しない

#### 原因 1: Unity Editor が起動していない

```bash
# Editor のプロセスを確認
ps aux | grep -i unity | grep -v grep
```

何も出ない → Editor を起動する。

#### 原因 2: Editor は起動しているが LP が読み込まれていない

LP は UPM パッケージ。利用側プロジェクトの `Packages/manifest.json` に `com.void2610.liminal-palette` が登録されているか確認:

```bash
cat <project>/Packages/manifest.json | jq '.dependencies."com.void2610.liminal-palette"'
```

### `IpcSettings.Enabled = false` で明示的に切られている

利用側の C# コードで明示的に切っている可能性:

```csharp
// 利用側の InitializeOnLoad で切られているか確認
[InitializeOnLoadMethod]
static void DisableLp() {
    Void2610.LiminalPalette.Ipc.IpcSettings.Enabled = false;
}
```

### 原因 3: Production ビルドを叩いている

LP の HTTP サーバは asmdef defineConstraints で **Player Production からコンパイル除外** される。Production ビルドの実行ファイルに `lp` を向けても応答することはない。

→ Development build に切り替えるか、Editor を使う。

### 原因 4: ポート占有

別のプロセスが 7610〜7615 を全て占有している:

```bash
for p in 7610 7611 7612 7613 7614 7615; do
  lsof -i :$p 2>/dev/null | head -2
done
```

LP 以外のプロセス (Node サーバや Docker 等) が複数のポートを占有している場合は、それらを停止するか LP の `IpcSettings.Port` を別の番号 (例: 8610) に変更:

```csharp
[InitializeOnLoadMethod]
static void TweakPort() {
    Void2610.LiminalPalette.Ipc.IpcSettings.Port = 8610;
}
```

---

## トークン関連

### token ファイルが空

```bash
[ -s ~/.liminal-palette/token ] || echo "EMPTY"
```

EMPTY なら破損している → 削除して Editor 再起動:

```bash
rm ~/.liminal-palette/token
# Editor を再起動。次回起動時に新規生成される。
```

### 401 が返る

詳細は [auth.md](auth.md) の「エラー: 401 Unauthorized」セクション。

### 環境変数 `$LP_TOKEN` が古いまま使われる

`lp` は `$LP_TOKEN` をファイルより優先する。Editor が token を再生成した直後だと古い値が使われ 401 になる。対処:

```bash
unset LP_TOKEN   # → ~/.liminal-palette/token から最新を読み直す
```

`~/.zshrc` 等に `export LP_TOKEN=$(cat ~/.liminal-palette/token)` を入れている場合は、token 更新後に新しいシェルを開く必要がある。AI Agent ベースの運用ならむしろ環境変数を設定せず、`lp` の自動読込に任せる方が安全。

---

## /commands や /execute が応答しない

### connection refused

サーバ自体に届いていない:

- ポートが間違っている → `lp health` で再発見
- LP が落ちた / Domain Reload 中 → 数秒待って再試行

### 応答が遅い (10 秒以上)

- async コマンドは Task 完了まで待つ。`isAsync: true` のコマンドはそうなる
- メインスレッドが詰まっている (Editor で重い処理が走っている)
- `lp` のタイムアウトは現状 10 秒固定 (`Tools~/lp/lp` 内 `TIMEOUT_SEC`)。長時間 async を扱うなら値を上げる

### 200 だが `success: false` で `error: null`

戻り値が `void` / `Task` / `ValueTask` のコマンドは正常実行で `value: null` になる。`success: true` なら成功。`success: false` で `error: null` は通常発生しない (起きたらバグ報告)。

---

## Domain Reload 関連

### Editor で C# を編集 → Reload 後に LP が応答しない

通常は数秒で listener が立ち直る。応答しない場合:

- Editor Console に `LiminalPalette/IpcServer started on port: <N>` ログが出ているか確認
- 出ていない場合、コンパイルエラーで `[InitializeOnLoadMethod]` が走っていない可能性
- Editor Console のエラーを修正

### 古い listener が残ってポートが取られる

LP は `AppDomain.DomainUnload` で listener を停止するが、稀に残ることがある:

```bash
# 残った listener を強制終了
lsof -i :7610 | tail -1 | awk '{print $2}' | xargs kill -9
```

その後 Editor を再起動。

---

## レートリミット (429)

`/execute` と `/scenarios/run` で 30 req/s 共有 (1 秒スライディングウィンドウ)。AI Agent が短時間に大量のコマンドを叩くと 429 が来る。

### 対処

```bash
# 短い sleep を挟む
for cmd in path1 path2 path3 ...; do
  lp exec "$cmd"
  sleep 0.05  # 1秒/30req = 33ms 以上空ける
done
```

または `lp run --steps -` の ad-hoc に複数操作をまとめると 1 リクエストになり、リミット消費が 1 で済む。

### リミットを上げたい場合 (利用側で C# 設定)

```csharp
[InitializeOnLoadMethod]
static void TweakIpcLimits() {
    Void2610.LiminalPalette.Ipc.IpcSettings.ExecuteRateLimitPerSecond = 100;
}
```

---

## body 上限 (413)

既定 1 MB。大きい引数を送ると 413 Payload Too Large。

### 対処

- 引数経路でなくファイルパスを引数で渡し、コマンド側で `File.ReadAllText` する設計にリファクタ
- どうしても引数に詰めたい場合は利用側で:

```csharp
[InitializeOnLoadMethod]
static void EnlargeBody() {
    Void2610.LiminalPalette.Ipc.IpcSettings.MaxRequestBodyBytes = 4 * 1024 * 1024;
}
```

---

## 関連 doc

- [ports.md](ports.md) — ポート割り当ての全表 + 両稼働判別
- [auth.md](auth.md) — トークン管理 + 401 詳解
- LP 本体: `Documentation~/troubleshooting.md` — Editor 内部の Domain Reload や DI 周り
