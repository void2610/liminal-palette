#!/usr/bin/env bash
# LP の稼働ポートを 7610..7615 で全スキャンし、Editor / Runtime を判別する。
# Editor + Play Mode の両稼働を検出するときに使う。
#
# 使い方:
#   ./examples/multi-instance.sh           # 検出結果を表示
#   source examples/multi-instance.sh      # env も export する
#
# 出力環境変数 (source した時のみ):
#   LP_PORT_EDITOR  ← Editor 側 (commandCount が大きい方)
#   LP_PORT_RUNTIME ← Runtime 側 (Play Mode、片方しかなければ未設定)
#
# 通常運用は `liminal --port 7610 ...` / `liminal --port 7611 ...` を直接使えば足りる。
# 本スクリプトは「どちらが Editor でどちらが Runtime か自動判別したい」時のヘルパー。

set -u

declare -a found_ports=()
declare -a found_counts=()

for p in 7610 7611 7612 7613 7614 7615; do
    resp=$(liminal --port "$p" --json health 2>/dev/null) || continue
    [ -n "$resp" ] || continue
    cnt=$(echo "$resp" | jq -r '.commandCount // 0' 2>/dev/null) || cnt=0
    found_ports+=("$p")
    found_counts+=("$cnt")
done

n=${#found_ports[@]}
case "$n" in
0)
    echo "ERROR: LP not running on any of 7610..7615" >&2
    return 1 2>/dev/null || exit 1
    ;;
1)
    # 単一インスタンス。Editor として扱う。
    export LP_PORT_EDITOR="${found_ports[0]}"
    unset LP_PORT_RUNTIME
    echo "Found 1 instance: port=$LP_PORT_EDITOR (commandCount=${found_counts[0]})"
    ;;
*)
    # 複数。commandCount が大きいほうを Editor、もう片方を Runtime とする。
    if [ "${found_counts[0]}" -ge "${found_counts[1]}" ]; then
        editor_idx=0
        runtime_idx=1
    else
        editor_idx=1
        runtime_idx=0
    fi
    export LP_PORT_EDITOR="${found_ports[$editor_idx]}"
    export LP_PORT_RUNTIME="${found_ports[$runtime_idx]}"
    echo "Found $n instances:"
    echo "  Editor : port=$LP_PORT_EDITOR (commandCount=${found_counts[$editor_idx]})"
    echo "  Runtime: port=$LP_PORT_RUNTIME (commandCount=${found_counts[$runtime_idx]})"
    echo ""
    echo "使用例:"
    echo "  liminal --port \$LP_PORT_EDITOR exec Editor/Console/Clear"
    echo "  liminal --port \$LP_PORT_RUNTIME exec Player/Health/Set value=100"
    ;;
esac
