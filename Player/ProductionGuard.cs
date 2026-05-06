using UnityEngine;

namespace Void2610.LiminalPalette.Player
{
    /// <summary>
    /// Runtime パレットを起動するか否かを判定するゲート。
    /// PaletteRuntimeSettings.EnableInRuntime / DisableInProductionBuilds と
    /// scripting define シンボル LIMINAL_PALETTE_DISABLED の 3 軸で判定する。
    /// Production 除外はビルド単位で行う設計 (= 個別コマンド単位のフラグは持たない)。
    /// メソッド単位で除外したい場合は利用側で #if !DEVELOPMENT_BUILD や
    /// [Conditional("DEVELOPMENT_BUILD")] を使うこと。
    /// </summary>
    public static class ProductionGuard
    {
        /// <summary>true なら Runtime パレットを起動しない。</summary>
        public static bool ShouldDisableInRuntime(PaletteRuntimeSettings settings)
        {
#if LIMINAL_PALETTE_DISABLED
            // スクリプトシンボルでハード OFF。Production プロファイルなどで使う。
            return true;
#else
            if (settings == null) return false;
            if (!settings.EnableInRuntime) return true;
            // Debug.isDebugBuild は Editor では常に true、Player ビルドでは Development Build フラグに連動する。
            if (settings.DisableInProductionBuilds && !Debug.isDebugBuild) return true;
            return false;
#endif
        }
    }
}
