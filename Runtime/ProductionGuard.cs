using UnityEngine;

namespace Void2610.LiminalPalette.Runtime
{
    /// <summary>
    /// Runtime パレットを起動するか否かを判定するゲート。
    /// PaletteRuntimeSettings.EnableInRuntime / DisableInProductionBuilds と
    /// scripting define シンボル LIMINAL_PALETTE_DISABLED / LIMINAL_PALETTE_FORCE_ENABLE
    /// の組合せで判定する。
    /// Production 除外はビルド単位で行う設計 (= 個別コマンド単位のフラグは持たない)。
    /// メソッド単位で除外したい場合は利用側で #if !DEVELOPMENT_BUILD や
    /// [Conditional("DEVELOPMENT_BUILD")] を使うこと。
    ///
    /// LIMINAL_PALETTE_FORCE_ENABLE: Development Build を有効化せずに Production ビルドでも
    /// パレットを起動したい場合のオプトイン (例: WebGL 開発デプロイ環境では Cloudflare Pages の
    /// 25MB ファイル上限を超えないために BuildOptions.Development を使えないが、
    /// パレットは利用したいケース)。DisableInProductionBuilds の判定をバイパスする。
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
#if !LIMINAL_PALETTE_FORCE_ENABLE
            // Debug.isDebugBuild は Editor では常に true、Player ビルドでは Development Build フラグに連動する。
            // FORCE_ENABLE が定義されていれば Production ビルドでも判定をスキップして起動する。
            if (settings.DisableInProductionBuilds && !Debug.isDebugBuild) return true;
#endif
            return false;
#endif
        }
    }
}
