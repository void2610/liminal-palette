# Named Scenario — 運用例

事前に C# で `[LiminalScenario]` を宣言したシナリオを `lp run` から実行するパターン。**CI / 開発者間で共有する固定テスト**に向く。

## 1. C# 側の宣言例 (利用側プロジェクト)

```csharp
using System.Collections.Generic;
using Void2610.LiminalPalette;

public static class CombatScenarios
{
    [LiminalScenario("Combat/EnemyTakesDamage", Description = "敵にダメージを与えて HP が減ることを検証")]
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

これで Cmd+K → Scenario タブと `lp scenarios` の両方に `Combat/EnemyTakesDamage` が並ぶ。

## 2. CLI から名前指定で実行

```bash
lp run Combat/EnemyTakesDamage
```

## 3. インスタンスメソッド版 (VContainer 必須)

```csharp
public sealed class CombatScenarios
{
    private readonly EnemySpawner _spawner;
    public CombatScenarios(EnemySpawner spawner) { _spawner = spawner; }

    [LiminalScenario("Combat/EnemyTakesDamage")]
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

VContainer 登録が無いと `lp scenarios` で `stepCount: -1` が出て、実行時は 500 エラー (Instance not resolved)。

## 4. CI / シェルスクリプトから

`lp run` の exit code を使えば CI 統合は数行で済む:

```bash
#!/usr/bin/env bash
# ci-run-scenario.sh - シナリオを実行して終了コードで成否を返す
set -u

SCENARIO="${1:-}"
[ -z "$SCENARIO" ] && { echo "usage: $0 <scenario-path>" >&2; exit 3; }

# lp の exit code:
#   0 = 全ステップ成功
#   2 = シナリオ失敗 (assert / command 失敗、409 Conflict、404 NotFound など含む)
#   1 = 通信エラー / Editor 未起動
if lp run "$SCENARIO"; then
  echo "PASS"
  exit 0
else
  rc=$?
  echo "FAIL (rc=$rc)"
  # 詳細を JSON で
  lp run "$SCENARIO" --json | jq '{failedAtStep, failed: [.steps[] | select(.success == false)]}'
  exit "$rc"
fi
```

### 環境変数で挙動調整

`lp` のグローバルオプションをシェル経由で渡したい場合:

```bash
# 別ホスト / カスタムポート / カスタムトークン
LP_TOKEN=$(cat /path/to/token) lp --port 7611 run Combat/EnemyTakesDamage
```

---

## 5. 全シナリオを順次実行 (smoke test)

```bash
SCENARIOS=$(lp scenarios --json | jq -r '.scenarios[].path')

passed=0
failed=0
for s in $SCENARIOS; do
  echo "=== $s ==="
  if lp run "$s"; then
    passed=$((passed + 1))
  else
    failed=$((failed + 1))
  fi
  sleep 0.1   # rate limit (30 req/s で枠を共有) を意識
done

echo "=== Summary ==="
echo "Passed: $passed / Failed: $failed"
```

## 6. シナリオの結果を JUnit XML 化 (CI 統合)

```bash
SCENARIOS=$(lp scenarios --json | jq -r '.scenarios[].path')

cat > /tmp/junit.xml <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<testsuites>
EOF

failures=0
total=0
for s in $SCENARIOS; do
  total=$((total + 1))
  resp=$(lp run "$s" --json)
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
