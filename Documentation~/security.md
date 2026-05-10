# Security

LiminalPalette のセキュリティ設計と運用上の注意点。

主目的は **「開発機 (= localhost) のデバッグ用途に閉じる」** こと。LAN / インターネット越しの利用は明示的に **未対応**。

---

## 攻撃面の最小化

### 1. localhost のみバインド

`HttpServer` は `127.0.0.1` と `localhost` の 2 つだけにバインドする:

```csharp
_listener.Prefixes.Add($"http://127.0.0.1:{port}/");
_listener.Prefixes.Add($"http://localhost:{port}/");
```

**`0.0.0.0` には絶対にバインドしない**。これによって LAN や同 WiFi の他端末からは到達できない。

`localhost` を入れているのは、OS によっては DNS で `::1` に解決されるケースがあるため (両方カバー)。

### 2. Bearer トークン認証

すべての endpoint で `Authorization: Bearer <token>` 必須 (`/health` のみ例外、生存確認用)。

実装ポイント:
- 256 bit のランダムを `RandomNumberGenerator` で生成
- 比較は **固定時間** (`TokenAuthenticator.FixedTimeEquals`) でタイミング攻撃対策
- `Bearer` プレフィックスは大小区別 (RFC 6750 準拠)

### 3. レートリミット

`POST /api/v1/execute` に 1 秒スライディングウィンドウのレートリミット:
- 既定 30 req/s
- 超過すると 429 Too Many Requests
- 他の endpoint (`/health` / `/commands` / `/logs`) はリミットなし (読み取り系)

### 4. body サイズ上限

すべての POST に対して `MaxRequestBodyBytes` (既定 1 MB) のサイズ制限:
- チャンク読みで累積バイト数を都度判定
- 上限超過で即時打ち切り → 413 Payload Too Large

これにより `Content-Length` を偽装した DoS 攻撃 (大量メモリ確保) を防ぐ。

---

## トークンの保護

### 保存先

| OS | パス |
|---|---|
| macOS / Linux | `~/.liminal-palette/token` |
| Windows | `%USERPROFILE%\.liminal-palette\token` |

### ファイル権限

- **macOS / Linux**: 生成時に `chmod 600` を best-effort で実行
- **Windows**: ユーザープロファイル配下なので OS の NTFS ACL に任せる (他ユーザーは読めない)

### 取り扱いの注意

⚠️ **トークンを共有しない**。

具体的には:
- スクリーンショットに写さない
- Discord / Slack / GitHub Issue 等にコピペしない
- `.bashrc` / `.zshrc` にハードコードしない (代わりに `$(cat ~/.liminal-palette/token)` で都度読む)
- `git status` で `~/.liminal-palette/` がプロジェクトに混ざっていないか確認 (ホームディレクトリなので通常は混ざらないが、`.dotfiles` レポなどで間違って commit しないこと)

### 漏れた場合

トークンが漏れた疑いがあれば:

1. `~/.liminal-palette/token` を削除
2. Unity Editor を再起動
3. 新しいトークンが自動生成される

---

## Production ビルドからの完全除外

LiminalPalette は **Development build 限定** の機能として設計されている。Production ビルド (= Development フラグ無しの Standalone build) には HTTP サーバーが一切混入しない。

### 三重防御

| 層 | 仕組み | 強度 |
|---|---|---|
| 1 | asmdef `defineConstraints` | **最強** (コンパイル対象外) |
| 2 | `ProductionGuard.ShouldDisableInRuntime` | コード内チェック |
| 3 | `IpcSettings.EnableInRuntime` / `EnableInEditor` | 利用側オプトアウト |

すべて独立。1 つでも倒せば起動しない。

### 1. asmdef `defineConstraints`

`Void2610.LiminalPalette.Player.Ipc.asmdef`:
```json
"defineConstraints": [
    "UNITY_EDITOR || DEVELOPMENT_BUILD"
]
```

- Production ビルドでは asmdef 自体がコンパイル対象外
- `RuntimeIpcBootstrap` / `IpcRuntimeTicker` のシンボルが Player に存在しない
- ビルドログに「`Void2610.LiminalPalette.Player.Ipc` がスキップされた」旨が出る

### 2. `ProductionGuard`

```csharp
public static class ProductionGuard
{
    public static bool ShouldDisableInRuntime(PaletteRuntimeSettings settings)
    {
#if LIMINAL_PALETTE_DISABLED
        return true;  // 利用側が define を立てたらハード OFF
#else
        if (settings == null) return false;
        if (!settings.EnableInRuntime) return true;
        if (settings.DisableInProductionBuilds && !Debug.isDebugBuild) return true;
        return false;
#endif
    }
}
```

`Debug.isDebugBuild`:
- Editor では常に true
- Player ビルドでは Development フラグに連動

### 3. 利用側オプトアウト

```csharp
[RuntimeInitializeOnLoadMethod]
static void DisableLiminalPalette()
{
    Void2610.LiminalPalette.Ipc.IpcSettings.EnableInRuntime = false;
    Void2610.LiminalPalette.Ipc.IpcSettings.EnableInEditor = false;
}
```

ScriptableObject `PaletteRuntimeSettings.EnableInRuntime = false` でも同等。

---

## 検証方法

### Production ビルドで HTTP がリンクされていないことの確認

```bash
# Standalone Mac build を作成 (Development フラグ無し)
unity -batchmode -buildOSXUniversalPlayer ... # 詳細は GameCI ドキュメント

# ビルド成果物の dll 内に HttpServer シンボルが無いこと
strings Build/MyGame.app/Contents/Resources/Data/Managed/Assembly-CSharp.dll | grep "LiminalPalette.Ipc.Server.HttpServer"
# → 何も出ない (asmdef ごとリンク除外)
```

### LAN から到達できないことの確認

別端末から:
```bash
curl -m 5 http://<unity-pc-ip>:7610/api/v1/health
# → タイムアウト or "Connection refused" (127.0.0.1 だけなので)
```

### Bearer 必須の確認

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:7610/api/v1/commands
# → 401
```

---

## リスク評価 (現時点で残るもの)

| リスク | 影響 | 緩和策 |
|---|---|---|
| トークンが他プロセスから読み取られる | 同マシン内の任意プロセスから操作可能 | `chmod 600` (Unix) + ユーザー権限分離 |
| トークンファイルがバックアップで流出 | 過去のトークンが復元される可能性 | 定期的に削除して再生成 (将来検討: ローテーション機能) |
| HTTP リクエストがプロキシ / VPN ソフトに見える | 平文通信 | 開発機内通信なので許容。LAN 越しは Tailscale / SSH トンネル前提 |
| `[LiminalCommand]` で危険な操作が登録されている | curl 経由で破壊操作実行可能 | Production ビルドでは asmdef defineConstraints + `ProductionGuard` の二層で HTTP 機構自体が起動しない (= ビルド単位で防ぐ)。さらに個別メソッドを除外したいなら `#if DEVELOPMENT_BUILD` で囲むか別 asmdef に分離 |
| LogCapture で取り込んだログにトークン / 機密情報が含まれる | `result.logs[]` に残る | コマンド側で機密情報を `Debug.Log` しない (利用側責任) |
| 同マシンで悪意あるブラウザタブが localhost にリクエスト送信 (DNS rebinding 等) | XSS で `/api/v1/execute` が叩かれる可能性 | Bearer トークンファイルはブラウザから直接読めない (file://読み込み不可)。ただし他経由でトークン漏洩した場合のリスクは残る |

---

## Phase 4 で扱わなかった項目 (将来検討)

- **HTTPS / TLS** — localhost 限定なら平文で十分。LAN 越し対応するときに追加
- **トークンのローテーション** — 現状は手動削除での再生成のみ
- **OAuth / OIDC** — 単独開発機 + 単一ユーザー想定なので過剰
- **動的コマンド登録 API (`POST /api/v1/commands`)** — 任意コード実行リスクのため意図的に未対応
- **CSRF 対策** — Bearer 認証 + localhost only で実質的に保護されている。SameSite cookie 等の追加は不要

---

## 関連ドキュメント

- [ipc.md](ipc.md) — HTTP API の挙動とトークン取り扱いの実例
- [asmdef.md](asmdef.md) — `defineConstraints` の仕組み詳細
- [troubleshooting.md](troubleshooting.md) — トークンファイルが見つからない / 401 エラー等
