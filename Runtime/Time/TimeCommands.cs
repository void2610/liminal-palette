using R3;
using UnityEngine;

namespace Void2610.LiminalPalette.Runtime
{
    /// <summary>
    /// `Time.timeScale` を操作・観測する組み込みランタイムコマンド + 観測フィールド。
    /// パス prefix は `Time/` (`Editor/` ではない) なので Editor / PlayMode / Player ビルド
    /// すべてから呼べる。利用例:
    /// <list type="bullet">
    ///   <item>シナリオで AI の状態遷移を待つ間 `Time/SetScale 10` で高速化</item>
    ///   <item>UI チェックや表示確認で `Time/Pause` してから `Time/Resume`</item>
    ///   <item>`Time/Scale` Observable を AssertEquals でスナップショット検証</item>
    /// </list>
    /// LP の Runtime asmdef は `autoReferenced: true` なので、利用側は何もせずに
    /// これらがパレットに出現する。`Time/Scale` は静的 ObservableField として
    /// VContainer 登録不要で値表示される (ObservableFieldDescriptor.IsStatic 経路)。
    /// </summary>
    public static class TimeCommands
    {
        // Time.timeScale の現在値を反応的に公開するための実体。
        // ReactiveProperty の初期値は Unity デフォルトの 1f。Runtime 起動後は
        // TimeScalePoller (下) が Update で Time.timeScale との差分を反映する。
        // 自分で SetScale / Reset などを呼んだ場合は即座に書き換えて 1 フレーム待たずに UI へ反映する。
        private static readonly ReactiveProperty<float> _scale = new ReactiveProperty<float>(1f);

        /// <summary>
        /// 現在の `Time.timeScale` を反応的に公開する観測フィールド。
        /// 静的プロパティだが LP の ObservableFieldDescriptor.IsStatic で VContainer 登録なしに扱われる。
        /// </summary>
        [LiminalObservableField("Time/Scale", Description = "現在の Time.timeScale (1=等速, 0=停止)")]
        public static ReactiveProperty<float> Scale => _scale;

        // ---- Set / Reset ----

        [LiminalCommand("Time/SetScale", Description = "Time.timeScale を指定値に設定 (例: 0=停止, 1=等速, 10=10倍速)")]
        public static string SetScale(
            [LiminalParam(Description = "スケール (0 以上)", Min = 0f)] float scale)
        {
            // 負値バリデーションは ArgumentBinder が Min で行うため、ここに到達した時点で scale >= 0 は保証される。
            ApplyScale(scale);
            return $"Time.timeScale = {Time.timeScale}";
        }

        [LiminalCommand("Time/Reset", Description = "Time.timeScale を 1 (等速) に戻す")]
        public static string Reset()
        {
            ApplyScale(1f);
            return "Time.timeScale = 1";
        }

        // ---- Shortcuts ----

        [LiminalCommand("Time/Pause", Description = "Time.timeScale を 0 にして時間停止")]
        public static string Pause()
        {
            ApplyScale(0f);
            return "Time.timeScale = 0 (paused)";
        }

        [LiminalCommand("Time/Resume", Description = "Time.timeScale を 1 にして時間再開 (Reset と同義)")]
        public static string Resume()
        {
            ApplyScale(1f);
            return "Time.timeScale = 1 (resumed)";
        }

        // Time.timeScale の書き換えと ReactiveProperty への伝播を 1 箇所に集約する。
        // ReactiveProperty 側も即座に同期させることで、Poller を 1 フレーム待たずに UI 表示が更新される。
        private static void ApplyScale(float scale)
        {
            Time.timeScale = scale;
            if (!Mathf.Approximately(_scale.Value, scale)) _scale.Value = scale;
        }

        // PlayMode / Player ビルドで Time.timeScale をポーリングして ReactiveProperty に流す常駐 MonoBehaviour を仕込む。
        // 「LP 経由ではない外部のスクリプトが Time.timeScale を書き換えた」場合でも UI に追従させるのが目的。
        // LP 自身の SetScale 等から書く経路は ApplyScale で即時同期しているので、Poller の責務は補完。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallPoller()
        {
            var go = new GameObject("[LiminalPalette] TimeScalePoller");
            // HideAndDontSave: ヒエラルキー上にも表示せず、シーン保存時にも書き出されない。
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            go.AddComponent<TimeScalePoller>();
        }

        // private nested MonoBehaviour: Update で Time.timeScale を監視し、ReactiveProperty に同期する。
        // timeScale=0 (一時停止中) でも Update は呼ばれる (Update が timeScale の影響を受けないため)。
        // ポーリング (毎フレーム比較) は非常に軽量で、Unity Time.timeScale には変更通知 API が存在しないため
        // 現実的にこのアプローチを取る。
        private sealed class TimeScalePoller : MonoBehaviour
        {
            private void Update()
            {
                var current = Time.timeScale;
                if (!Mathf.Approximately(_scale.Value, current)) _scale.Value = current;
            }
        }
    }
}
