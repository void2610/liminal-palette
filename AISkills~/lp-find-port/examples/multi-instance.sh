#!/usr/bin/env bash
# LP の稼働ポートを 7610..7615 で全スキャンし、Editor / Runtime を判別して env を export する。
# Editor + Play Mode の両稼働ケースに対応。
#
# 使い方:
#   source examples/multi-instance.sh
#
# 出力環境変数:
#   LP_PORT_EDITOR  / LP_BASE_EDITOR   ← Editor 側 (commandCount が大きい方)
#   LP_PORT_RUNTIME / LP_BASE_RUNTIME  ← Runtime 側 (Play Mode、片方しかなければ未設定)
#   LP_PORT         / LP_BASE          ← フォールバック (Editor 優先、無ければ Runtime)

set -u

declare -a found_ports=()
declare -a found_counts=()

for p in 7610 7611 7612 7613 7614 7615; do
    resp=$(curl -s -m 1 "http://127.0.0.1:$p/api/v1/health" 2>/dev/null) || continue
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
    export LP_PORT="${found_ports[0]}"
    export LP_BASE="http://127.0.0.1:$LP_PORT"
    export LP_PORT_EDITOR="$LP_PORT"
    export LP_BASE_EDITOR="$LP_BASE"
    unset LP_PORT_RUNTIME LP_BASE_RUNTIME
    echo "Found 1 instance: port=$LP_PORT (commandCount=${found_counts[0]})"
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
    export LP_BASE_EDITOR="http://127.0.0.1:$LP_PORT_EDITOR"
    export LP_PORT_RUNTIME="${found_ports[$runtime_idx]}"
    export LP_BASE_RUNTIME="http://127.0.0.1:$LP_PORT_RUNTIME"
    # フォールバックは Editor 優先
    export LP_PORT="$LP_PORT_EDITOR"
    export LP_BASE="$LP_BASE_EDITOR"
    echo "Found $n instances:"
    echo "  Editor : port=$LP_PORT_EDITOR (commandCount=${found_counts[$editor_idx]})"
    echo "  Runtime: port=$LP_PORT_RUNTIME (commandCount=${found_counts[$runtime_idx]})"
    ;;
esac
