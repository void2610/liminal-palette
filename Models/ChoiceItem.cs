namespace Void2610.LiminalPalette
{
    /// <summary>
    /// 入力補完候補の1項目。内部値と表示名のペア。
    /// </summary>
    public readonly struct ChoiceItem
    {
        /// <summary>コマンドに渡される内部値</summary>
        public string Value { get; }

        /// <summary>UI/API表示用の名前（ローカライズ名等）。nullならValueと同じ。</summary>
        public string DisplayName { get; }

        public ChoiceItem(string value, string displayName = null)
        {
            Value = value;
            DisplayName = displayName ?? value;
        }
    }
}
