using System.Collections.Generic;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// 動的な入力補完候補を提供するインターフェース。
    /// パラメータレスコンストラクタ必須（Activator.CreateInstanceで生成される）。
    /// GetChoices() はUI表示・API応答のたびに呼ばれるため、最新の候補を返すこと。
    /// </summary>
    public interface IChoicesProvider
    {
        IReadOnlyList<ChoiceItem> GetChoices();
    }
}
