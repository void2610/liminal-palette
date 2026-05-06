using System;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// 引数の型に応じた入力 UI を生成する拡張ポイント。
    /// Phase 1 の ITypeConverter と命名・登録ルール・テスト API を揃える。
    /// </summary>
    public interface IParameterEditor
    {
        /// <summary>このエディタが指定型を扱えるなら true。</summary>
        bool CanHandle(Type type);

        /// <summary>
        /// 指定パラメータに対する入力 UI を生成する。値が変わったら onChanged コールバックを呼ぶ。
        /// 初期値は param.HasDefault が true なら DefaultValue、そうでなければ型のデフォルト値を採用すること。
        /// </summary>
        VisualElement Build(ParameterDescriptor param, Action<object> onChanged);
    }
}
