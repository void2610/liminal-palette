# LP HTTP Server — Port Allocation

## 既定の割り当て

| 環境 | サーバー起動 | 既定ポート | 占有時の挙動 |
|---|---|---|---|
| Unity Editor | ✅ | 7610 | 隣接 (7611, 7612, ..., 7615) を順に試行 |
| Editor の Play Mode | ✅ | 7611 | Editor が 7610 を占有しているため隣接から開始 |
| Standalone Development build | ✅ | 7610 | Editor が走っていないなら 7610 |
| Standalone Production build | ❌ | (起動しない) | asmdef defineConstraints で **コンパイル除外** |

最大 5 個 (`7610..7615`) まで隣接を試して全部失敗したら listener は立たない。

## Editor + Play Mode 両稼働パターン

最も頻出するシナリオ。Editor で Play Mode に入ると、Runtime 用の listener が **新規** に立つ:

```
Editor    → 7610 (Editor 用 IpcServer)
PlayMode  → 7611 (Runtime 用 IpcServer、Play Mode 中のみ生存)
```

`/health` は両方が応答する。`lp` は最初に応答した方を取るが、`--port` で個別に指定可能:

```bash
for p in 7610 7611; do
  lp --port "$p" --json health 2>/dev/null \
    | jq --arg p "$p" '. + {port: ($p|tonumber)}'
done
# {"status":"ok","version":"0.4.0","commandCount":420,"port":7610}   ← Editor 側
# {"status":"ok","version":"0.4.0","commandCount":312,"port":7611}   ← Runtime 側
```

### どちらを叩くか

| 操作 | 推奨 port |
|---|---|
| Asset / Editor Window / Scene Edit / Console Clear | **7610 (Editor)** |
| Player HP / Enemy Spawn / Damage / 物理状態 | **7611 (Runtime)** |
| 両方に存在するコマンド (例: 共通の `Debug/PrintTime`) | どちらでも可。文脈で選ぶ |

### 判別ヒューリスティック

明示的なフラグはないが、`commandCount` の差で推定可能:

- Editor 側のほうが大きい (Editor 限定 `[LiminalCommand]` の分)
- Runtime 側は Editor 限定コマンドが含まれない

### 両方を使い分けるパターン

エイリアスやフラグで CLI を切り分ける:

```bash
alias lpe='lp --port 7610'   # Editor
alias lpr='lp --port 7611'   # Runtime

# Editor 操作
lpe exec Editor/Console/Clear

# Runtime 操作
lpr exec Player/Health/Set value=100
```

## Editor 再起動時の動作

- 通常は **同じポートに戻る** (7610 が他プロセスに取られていない限り)
- Domain Reload 直後は listener が一時的に応答しない瞬間がありうる (数百 ms)
- 再起動を挟んだ場合は `lp health` でポート再発見が安全

## なぜポートを 5 個までしか試さないか

- LP の `IpcSettings.PortRetryCount` の既定が 5
- 7615 まで埋まっているのは「他の LP プロジェクトが多数同時起動」「他のサービスがポートを取っている」など異常状態
- 利用側で `IpcSettings.PortRetryCount = 10` 等に拡張可能

## トラブルシューティング (ポート絡み)

### Q. Play Mode に入ったら lp が反応しない
A. Editor (7610) と Runtime (7611) で別 listener。`lp --port 7611 ...` で明示指定するか、`lp health` で再スキャン。

### Q. Editor を再起動したら 7610 で応答しない
A. 先に他プロセスが 7610 を占有している。`lsof -i :7610` で確認。LP 側は次の隣接 (7611...) にずれているはずなので `lp health` で発見できる。

### Q. Production ビルドの実行ファイルに `lp` を向けても応答しない
A. **仕様**。Production build は LP HTTP サーバ自体がコンパイル除外される。Development build でビルドし直すこと。

### Q. /health で応答するが /commands で connection refused
A. ありえない。/health と他 endpoint は同じ listener。両方 timeout している場合は別問題 (ファイアウォール / VPN / Docker NAT 等)。

## Internal: ポート選択ロジック

LP の `IpcServer.Start()` は以下:

1. `IpcSettings.Port` (既定 7610) で listen を試行
2. `EADDRINUSE` (バインド失敗) なら `Port + 1` で再試行
3. `PortRetryCount` (既定 5) まで繰り返し、全失敗したら `Debug.LogWarning` で諦める

エンドユーザーに見える値は `LiminalPalette/IpcServer started on port: <N>` というログメッセージ (Editor Console)。

`lp` 側のスキャンも同じ範囲 (7610〜7615) を順に叩いて最初に応答した方を採用する。
