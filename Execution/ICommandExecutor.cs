using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// レジストリからコマンドを引き、引数を束縛し、メソッドを呼び出して CommandResult に整形する責務。
    /// </summary>
    public interface ICommandExecutor
    {
        /// <summary>名前指定 (パラメータ名 → 文字列値) で実行する。args が null の場合は空辞書相当として扱う。</summary>
        Task<CommandResult> ExecuteAsync(
            string pathOrAlias,
            IReadOnlyDictionary<string, string>? args,
            CancellationToken ct = default);

        /// <summary>位置指定で実行する。args[i] が i 番目のパラメータに対応。null の場合は空配列相当として扱う。</summary>
        Task<CommandResult> ExecuteAsync(
            string pathOrAlias,
            IReadOnlyList<string>? positionalArgs,
            CancellationToken ct = default);

        /// <summary>
        /// 型解決済みの値で実行する (Phase 2 で UI 入力経路から呼ばれる)。
        /// 文字列を介さないため Vector3 / Color などの精度を維持し、TypeConverter ラウンドトリップのコストも避ける。
        /// args が null の場合は空辞書相当として扱う。値が param.Type に合わない場合は CommandResult.Fail で返る。
        /// </summary>
        Task<CommandResult> ExecuteWithTypedArgsAsync(
            string pathOrAlias,
            IReadOnlyDictionary<string, object>? args,
            CancellationToken ct = default);
    }
}
