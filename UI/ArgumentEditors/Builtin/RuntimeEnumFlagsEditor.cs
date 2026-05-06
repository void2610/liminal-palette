using System;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// [Flags] 属性付き enum 用の Runtime エディタ。
    /// UnityEditor.UIElements.EnumFlagsField は Editor 専用なので、Runtime では値ごとに Toggle を縦に並べる構成で代替する。
    /// Toggle が変わるたびにビット OR で全体値を再構築して onChanged に渡す。
    /// 0 値の "None" 相当は表示するが Toggle を立てても 0 を OR しても効果がない (これは UI 仕様)。
    /// </summary>
    public sealed class RuntimeEnumFlagsEditor : IParameterEditor
    {
        public bool CanHandle(Type type)
            => type != null && type.IsEnum && type.IsDefined(typeof(FlagsAttribute), inherit: false);

        public VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
        {
            var t = param.Type;
            var values = Enum.GetValues(t);
            var names = Enum.GetNames(t);

            // 初期値の long 表現。HasDefault が無ければ 0 (= None ビット)。
            var initialLong = 0L;
            if (param.HasDefault && param.DefaultValue != null)
            {
                initialLong = Convert.ToInt64(param.DefaultValue);
            }

            var root = new VisualElement();
            root.AddToClassList("lp-flags-runtime");
            root.style.flexDirection = FlexDirection.Column;

            // 各ビットの状態を保持する long と Toggle 配列。Toggle 変更で current を再構築する。
            var current = initialLong;
            var toggles = new Toggle[values.Length];
            // 0 ビット (None) の Toggle 参照を保持する。非 None bit を変更したときに current==0 かどうかで
            // 自動 ON/OFF させるために必要。複数の 0 値が定義されている場合は最初に見つけたものを採用。
            Toggle noneToggle = null;

            for (var i = 0; i < values.Length; i++)
            {
                var bit = Convert.ToInt64(values.GetValue(i));
                var toggle = new Toggle(names[i])
                {
                    name = $"lp-flag-{names[i]}",
                    // 0 ビット (None) は current が 0 のときだけ ON 表示。
                    value = bit == 0 ? current == 0 : (current & bit) == bit,
                };
                if (bit == 0 && noneToggle == null) noneToggle = toggle;
                var bitCaptured = bit;
                var indexCaptured = i;
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (bitCaptured == 0)
                    {
                        // None を OFF にしようとしたら無視して true に戻す (None は他 bit を ON にすることでのみ OFF になる)。
                        // current は変えず onChanged も呼ばない。
                        if (!e.newValue)
                        {
                            toggle.SetValueWithoutNotify(true);
                            return;
                        }
                        // None を ON: 他 toggle を OFF にして current=0 へ。
                        current = 0;
                        for (var j = 0; j < toggles.Length; j++)
                        {
                            if (j == indexCaptured) continue;
                            toggles[j]?.SetValueWithoutNotify(false);
                        }
                    }
                    else
                    {
                        if (e.newValue) current |= bitCaptured;
                        else current &= ~bitCaptured;
                        // 非 None bit を変更したら None Toggle の表示を current==0 と同期させる。
                        // これがないと None toggle が他 bit と整合しない (UI 状態と値が乖離する) ことがある。
                        noneToggle?.SetValueWithoutNotify(current == 0);
                    }
                    onChanged(Enum.ToObject(t, current));
                });
                toggles[i] = toggle;
                root.Add(toggle);
            }

            return root;
        }
    }
}
