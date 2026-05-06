using System.Runtime.CompilerServices;

// テストアセンブリから internal 型 (ArgumentBinder / AttributeScanner / LogCapture など) を参照可能にする。
// 本来は実装詳細だが、ユニットテストで挙動を検証する必要があるため。
[assembly: InternalsVisibleTo("Void2610.LiminalPalette.Tests")]

// Scenario の派生ステップ型 (CommandStep / WaitStep / AssertStep) は internal だが、
// IPC / UI レイヤから JSON シリアライズ・UI 描画のため詳細フィールドを参照する必要がある。
// 利用側 (アプリケーションコード) からは隠したいため、ファサード (ScenarioStep) のみ public にしつつ、
// 派生型は LiminalPalette 系列の asmdef に対してのみ可視化する。
[assembly: InternalsVisibleTo("Void2610.LiminalPalette.Ipc")]
[assembly: InternalsVisibleTo("Void2610.LiminalPalette.UI")]
