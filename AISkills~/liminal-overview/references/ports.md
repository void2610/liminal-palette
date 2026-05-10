# LP HTTP Server — Port Allocation

## 既定の割り当て

| 環境 | サーバー起動 | 既定ポート | 占有時の挙動 |
|---|---|---|---|
| Unity Editor | ✅ | 7610 | 隣接 (7611, 7612, ..., 7615) を順に試行 |
| Editor の Play Mode | ✅ | 7611 | Editor が 7610 を占有しているため隣接から開始 |
| Standalone Development build | ✅ | 7610 | Editor が走っていないなら 7610 |
| Standalone Production build | ❌ | (起動しない) | asmdef defineConstraints で **コンパイル除外** |

最大 5 個 (`7610..7615`) まで隣接を試して全部失敗したら listener は立たない。
プロジェクト固有のポートを `ProjectSettings/LiminalPalette.json` で固定すると上記既定を上書きできる (後述)。

## プロジェクトごとの固定ポート (推奨)

```json
// <project>/ProjectSettings/LiminalPalette.json
{
  "port": 7613,
  "runtimePort": 7700
}
```

| フィールド | 用途 |
|---|---|
| `port` | Editor IpcServer の bind port (`runtimePort` 未設定時は Runtime のフォールバックも兼ねる) |
| `runtimePort` | Play Mode / Runtime IpcServer 専用 (省略可) |

CLI から書ける:

```bash
liminal project set-port 7613             # Editor
liminal project set-port --runtime 7700   # Play Mode
liminal project show                      # 確認 + ライブ probe
liminal project unset-port [--runtime]    # 削除
```

複数 Unity プロジェクトを同時起動する場合は、各プロジェクトに別ポートを割り当てれば衝突しない。

## Editor + Play Mode 両稼働パターン

Editor で Play Mode に入ると、Runtime 用の listener が **新規** に立つ:

```
Editor    → port (既定 7610、または ProjectSettings/LiminalPalette.json の port)
PlayMode  → runtimePort (未設定なら port+1 などにずれる、Play Mode 中のみ生存)
```

`/health` は両方が応答し、レスポンスに `mode` フィールド (`"editor"` か `"runtime"`) が入る。`liminal` は `--mode` で 1 つに絞れる:

```bash
liminal --mode editor health    # Editor 側
liminal --mode runtime health   # Play Mode (Runtime IpcServer)

# 一覧したい場合
liminal doctor                  # 全 listener + 解決結果を表示
```

### どちらを叩くか

| 操作 | 推奨 mode |
|---|---|
| Asset / Editor Window / Scene Edit / Console Clear | **editor** |
| Player HP / Enemy Spawn / Damage / 物理状態 | **runtime** (Play Mode 中) |
| 両方に存在するコマンド (例: 共通の `Debug/PrintTime`) | どちらでも可。文脈で選ぶ |

### 両方を使い分けるパターン

エイリアスで切り分ける:

```bash
alias lpe='liminal --mode editor'    # Editor
alias lpr='liminal --mode runtime'   # Runtime

# Editor 操作
lpe exec Editor/Console/Clear

# Runtime 操作
lpr exec Player/Health/Set value=100
```

## Editor 再起動時の動作

- 通常は **同じポートに戻る** (7610 が他プロセスに取られていない限り)
- Domain Reload 直後は listener が一時的に応答しない瞬間がありうる (数百 ms)
- 再起動を挟んだ場合は `liminal health` でポート再発見が安全

## なぜポートを 5 個までしか試さないか

- LP の `IpcSettings.PortRetryCount` の既定が 5
- 7615 まで埋まっているのは「他の LP プロジェクトが多数同時起動」「他のサービスがポートを取っている」など異常状態
- 利用側で `IpcSettings.PortRetryCount = 10` 等に拡張可能

## トラブルシューティング (ポート絡み)

### Q. Play Mode に入ったら liminal が反応しない
A. Editor と Runtime で別 listener (両方生存して曖昧になっている可能性)。`liminal --mode runtime ...` で Play Mode 側を指定するか、`liminal doctor` で生存中 listener を一覧する。

### Q. Editor を再起動したら 7610 で応答しない
A. 先に他プロセスが 7610 を占有している。`lsof -i :7610` で確認。LP 側は次の隣接 (7611...) にずれているはずなので `liminal health` で発見できる。`ProjectSettings/LiminalPalette.json` で固定ポート (`port`) を宣言すれば次回以降は安定する。

### Q. Production ビルドの実行ファイルに `liminal` を向けても応答しない
A. **仕様**。Production build は LP HTTP サーバ自体がコンパイル除外される。Development build でビルドし直すこと。

### Q. /health で応答するが /commands で connection refused
A. ありえない。/health と他 endpoint は同じ listener。両方 timeout している場合は別問題 (ファイアウォール / VPN / Docker NAT 等)。

## Internal: ポート選択ロジック

LP の `IpcServer.Start()` は以下:

1. `IpcSettings.Port` (既定 7610) で listen を試行
2. `EADDRINUSE` (バインド失敗) なら `Port + 1` で再試行
3. `PortRetryCount` (既定 5) まで繰り返し、全失敗したら `Debug.LogWarning` で諦める

エンドユーザーに見える値は `LiminalPalette/IpcServer started on port: <N>` というログメッセージ (Editor Console)。

`liminal` 側のスキャンも同じ範囲 (7610〜7615) を順に叩いて最初に応答した方を採用する。
