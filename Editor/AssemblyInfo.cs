using System.Runtime.CompilerServices;

// テストアセンブリから Editor 側の internal メンバ (Editor 専用の IParameterEditor 実装等) を参照可能にする。
// 本来は実装詳細だが、スモークテスト / 統合テストで挙動を検証する都合で公開している。
[assembly: InternalsVisibleTo("Void2610.LiminalPalette.Tests")]
