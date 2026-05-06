# Named Scenario — 運用例

事前に C# で `[ConsoleScenario]` を宣言したシナリオを HTTP から実行するパターン。**CI / 開発者間で共有する固定テスト**に向く。

## 1. C# 側の宣言例 (利用側プロジェクト)

```csharp
using System.Collections.Generic;
using Void2610.LiminalPalette;

public static class CombatScenarios
{
    [ConsoleScenario("Combat/EnemyTakesDamage", Description = "敵にダメージを与えて HP が減ることを検証")]
    public static IEnumerable<ScenarioStep> EnemyTakesDamage()
    {
        yield return ScenarioStep.Run("Enemy/Spawn", new() { ["type"] = "Goblin" });
        yield return ScenarioStep.AssertEquals("Enemy/Hp", 100, "spawn 直後は満タン");
        yield return ScenarioStep.Run("Enemy/Damage", new() { ["amount"] = 30 });
        yield return ScenarioStep.WaitFrames(1);
        yield return ScenarioStep.AssertEquals("Enemy/Hp", 70, "30 ダメージ後は 70");
    }
}
```

これで Cmd+K → Scenario タブと `/api/v1/scenarios` の両方に `Combat/EnemyTakesDamage` が並ぶ。

## 2. HTTP から名前指定で実行

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d '{"path":"Combat/EnemyTakesDamage"}'
```

## 3. インスタンスメソッド版 (VContainer 必須)

```csharp
public sealed class CombatScenarios
{
    private readonly EnemySpawner _spawner;
    public CombatScenarios(EnemySpawner spawner) { _spawner = spawner; }

    [ConsoleScenario("Combat/EnemyTakesDamage")]
    public IEnumerable<ScenarioStep> EnemyTakesDamage()
    {
        yield return ScenarioStep.Run("Enemy/Spawn", new() { ["type"] = _spawner.DefaultType });
        // ...
    }
}
```

利用側 LifetimeScope:

```csharp
builder.Register<CombatScenarios>(Lifetime.Singleton);
builder.RegisterEntryPoint<LiminalPaletteEntryPoint>();
```

VContainer 登録が無いと `lp-list-scenarios` で `stepCount: -1` が出て、実行時は 500 エラー (Instance not resolved)。

## 4. CI / シェルスクリプトから

LP 本体には CI ヘルパスクリプトの参考設計が `Documentation~/scenarios.md` にある (現状 `scripts/ci-run-scenario.sh` という想定だが、本リポジトリには未実装)。簡易版を自前で書くと:

```bash
#!/usr/bin/env bash
# ci-run-scenario.sh - シナリオを実行して終了コードで成否を返す
set -u

SCENARIO="${1:-}"
[ -z "$SCENARIO" ] && { echo "usage: $0 <scenario-path>" >&2; exit 3; }

LP_TOKEN=$(cat "${LIMINAL_PALETTE_TOKEN_FILE:-$HOME/.liminal-palette/token}" 2>/dev/null) || {
  echo "ERROR: token not found" >&2; exit 4;
}

# ポート発見
HOST="${LIMINAL_PALETTE_HOST:-127.0.0.1}"
PORT="${LIMINAL_PALETTE_PORT:-}"
if [ -z "$PORT" ]; then
  for p in 7610 7611 7612 7613 7614 7615; do
    if curl -s -m 1 "http://$HOST:$p/api/v1/health" >/dev/null 2>&1; then
      PORT="$p"; break
    fi
  done
fi
[ -z "$PORT" ] && { echo "ERROR: LP not running" >&2; exit 2; }

BASE="http://$HOST:$PORT"

# 実行
RESP=$(curl -s -w '\n%{http_code}' -H "Authorization: Bearer $LP_TOKEN" \
  -H "Content-Type: application/json" \
  -X POST "$BASE/api/v1/scenarios/run" \
  -d "{\"path\":\"$SCENARIO\"}")

http_code=$(echo "$RESP" | tail -n1)
body=$(echo "$RESP" | sed '$d')

case "$http_code" in
  200)
    success=$(echo "$body" | jq -r '.success')
    if [ "$success" = "true" ]; then
      echo "PASS: $SCENARIO ($(echo "$body" | jq -r '.durationMs')ms)"
      exit 0
    else
      failed_at=$(echo "$body" | jq -r '.failedAtStep')
      echo "FAIL: $SCENARIO (failedAtStep=$failed_at)"
      echo "$body" | jq '.steps[] | select(.success == false)'
      exit 1
    fi
    ;;
  409)
    echo "ALREADY_RUNNING: $SCENARIO"
    exit 6
    ;;
  401)
    echo "AUTH_FAILED"
    exit 4
    ;;
  *)
    echo "REQUEST_FAILED: HTTP $http_code"
    echo "$body"
    exit 2
    ;;
esac
```

### 終了コード規約

| code | 意味 |
|---|---|
| 0 | 全ステップ成功 |
| 1 | シナリオ失敗 (assert / command 失敗) |
| 2 | リクエスト送信失敗 (Editor 未起動 / ポート違い等) |
| 3 | 使用法エラー |
| 4 | 認証エラー |
| 6 | シナリオが既に実行中 (409) |

### 使い方

```bash
chmod +x ci-run-scenario.sh
./ci-run-scenario.sh Combat/EnemyTakesDamage
echo "exit=$?"
```

### 環境変数で挙動調整

```bash
LIMINAL_PALETTE_HOST=127.0.0.1 \
LIMINAL_PALETTE_PORT=7611 \
LIMINAL_PALETTE_TOKEN_FILE=/path/to/token \
./ci-run-scenario.sh Combat/EnemyTakesDamage
```

---

## 5. 全シナリオを順次実行 (smoke test)

```bash
SCENARIOS=$(curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/scenarios" \
  | jq -r '.scenarios[].path')

passed=0
failed=0
for s in $SCENARIOS; do
  echo "=== $s ==="
  resp=$(curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
    -X POST "$LP_BASE/api/v1/scenarios/run" \
    -d "{\"path\":\"$s\"}")
  if [ "$(echo "$resp" | jq -r '.success')" = "true" ]; then
    passed=$((passed + 1))
    echo "PASS ($(echo "$resp" | jq -r '.durationMs')ms)"
  else
    failed=$((failed + 1))
    echo "FAIL"
    echo "$resp" | jq '{failedAtStep, failed: [.steps[] | select(.success == false)]}'
  fi
  sleep 0.1   # rate limit (30 req/s で枠を共有) を意識
done

echo "=== Summary ==="
echo "Passed: $passed / Failed: $failed"
```

## 6. シナリオの結果を JUnit XML 化 (CI 統合)

```bash
SCENARIOS=$(curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/scenarios" | jq -r '.scenarios[].path')

cat > /tmp/junit.xml <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<testsuites>
EOF

failures=0
total=0
for s in $SCENARIOS; do
  total=$((total + 1))
  resp=$(curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
    -X POST "$LP_BASE/api/v1/scenarios/run" \
    -d "{\"path\":\"$s\"}")
  ok=$(echo "$resp" | jq -r '.success')
  ms=$(echo "$resp" | jq -r '.durationMs')
  duration=$(awk -v ms="$ms" 'BEGIN { printf "%.3f", ms/1000 }')

  if [ "$ok" = "true" ]; then
    cat >> /tmp/junit.xml <<EOF
  <testcase classname="LiminalPalette" name="$s" time="$duration"/>
EOF
  else
    failures=$((failures + 1))
    err=$(echo "$resp" | jq -r '.steps[] | select(.success == false) | .error // "step failed"' | head -1)
    cat >> /tmp/junit.xml <<EOF
  <testcase classname="LiminalPalette" name="$s" time="$duration">
    <failure message="$err"/>
  </testcase>
EOF
  fi
done

echo "</testsuites>" >> /tmp/junit.xml
echo "Total: $total, Failures: $failures"
cat /tmp/junit.xml
```

GitHub Actions / CircleCI / Jenkins の test report に食わせられる。
