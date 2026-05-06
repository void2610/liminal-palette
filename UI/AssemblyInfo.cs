using System.Runtime.CompilerServices;

// テストアセンブリから UI の internal 型 (ParameterEditorRegistry.ResetToDefaults 等) を参照可能にする。
// 本来は実装詳細だが、Phase 1 の TypeConverterRegistry と同じ流儀でテスト間の状態リセットを提供する。
[assembly: InternalsVisibleTo("Void2610.LiminalPalette.Tests")]
