using UnityEngine;

namespace Void2610.LiminalPalette.Runtime
{
    /// <summary>
    /// Runtime パレットの設定値。利用側は Resources/PaletteRuntimeSettings.asset を作って上書きできる。
    /// 既定は Editor 側の LiminalPaletteWindow と同じ Ctrl/Cmd + K。
    /// Unity はフォーカスを持つウィンドウのショートカットしか発火しないため、Editor / Game ウィンドウが
    /// 同時に Cmd+K を取り合うことはない (フォーカス側が排他で開閉する)。
    /// </summary>
    [CreateAssetMenu(fileName = "PaletteRuntimeSettings", menuName = "LiminalPalette/Runtime Settings", order = 1000)]
    public sealed class PaletteRuntimeSettings : ScriptableObject
    {
        // Resources からロードする際の既定パス (拡張子なし)。
        public const string ResourcesPath = "PaletteRuntimeSettings";

        [Tooltip("Runtime でパレットを有効にするか。false なら RuntimeBootstrap が何もしない。")]
        public bool EnableInRuntime = true;

        [Tooltip("Runtime ショートカットの修飾キー (Ctrl / Cmd 同等) を必須とするか。")]
        public bool RequireModifier = true;

        [Tooltip("Runtime ショートカットのキー。RequireModifier と組み合わせて押されたときにパレットがトグルする。既定は Editor 側 (Cmd+K) と統一。")]
        public KeyCode ToggleKey = KeyCode.K;

        [Tooltip("Production ビルド (Debug.isDebugBuild が false) で Runtime を無効化するか。")]
        public bool DisableInProductionBuilds = true;

        [Tooltip("PanelSettings の sortingOrder。利用側 UI と衝突する場合は調整する。")]
        public int PanelSortingOrder = 1000;

        [Tooltip("Show のたびに Reset するか。false なら前回の検索クエリ / 選択を保持する。")]
        public bool ResetOnEachOpen = true;

        /// <summary>
        /// Resources からユーザー定義 asset を探し、無ければデフォルト値の ScriptableObject を返す。
        /// 戻り値は破壊せず読み取り専用として扱う想定 (ScriptableObject のため意図せず Inspector 編集はされない)。
        /// </summary>
        public static PaletteRuntimeSettings LoadOrCreateDefault()
        {
            var loaded = Resources.Load<PaletteRuntimeSettings>(ResourcesPath);
            if (loaded != null) return loaded;
            // CreateInstance はシーンに紐づかないインスタンスを返す。アプリ終了時に GC される。
            return CreateInstance<PaletteRuntimeSettings>();
        }
    }
}
