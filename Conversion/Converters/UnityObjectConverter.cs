using System;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// UnityEngine.Object 派生型のコンバータ (Phase 1 のテキスト経路向け)。
    /// サポートする入力形式:
    ///   - "@<entityID>"             : Resources.EntityIdToObject で解決 (UI ピッカー経由の選択結果を想定)
    ///   - "GameObject:<name>"       : シーン上の GameObject を名前検索 (Runtime 限定)
    ///   - "<name>" 等のフォールバック : 未対応 (Phase 2 で UI ピッカー経由に切り替え予定)
    /// UI 層が実装される Phase 2 以降では Object ピッカー経由でこのコンバータを呼び出す形になる。
    /// </summary>
    public sealed class UnityObjectConverter : ITypeConverter
    {
        public bool CanConvert(Type targetType) => targetType != null && typeof(Object).IsAssignableFrom(targetType);

        public bool TryFromString(string raw, Type targetType, out object value, out string error)
        {
            value = null;
            error = null;

            if (string.IsNullOrEmpty(raw))
            {
                // 空文字は null 参照を示す。SerializeField と異なり、コマンド引数の null は許容する。
                value = null;
                return true;
            }

            // "@<entityID>" : Resources.EntityIdToObject で解決 (Unity 6 で EntityId 構造体化、int からは暗黙変換)
            if (raw.StartsWith("@") && int.TryParse(raw.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                var obj = Resources.EntityIdToObject(id);
                if (obj == null)
                {
                    error = $"EntityID {id} resolved to null";
                    return false;
                }
                if (!targetType.IsInstanceOfType(obj))
                {
                    error = $"EntityID {id} resolved to {obj.GetType().Name}, not {targetType.Name}";
                    return false;
                }
                value = obj;
                return true;
            }

            // "GameObject:<name>" 形式は Runtime のシーン上検索。
            if (raw.StartsWith("GameObject:"))
            {
                var name = raw.Substring("GameObject:".Length);
                var go = GameObject.Find(name);
                if (go == null)
                {
                    error = $"GameObject '{name}' not found in active scene";
                    return false;
                }
                if (targetType == typeof(GameObject))
                {
                    value = go;
                    return true;
                }
                // Component を要求された場合は GetComponent で解決。
                if (typeof(Component).IsAssignableFrom(targetType))
                {
                    var comp = go.GetComponent(targetType);
                    if (comp == null)
                    {
                        error = $"GameObject '{name}' has no {targetType.Name}";
                        return false;
                    }
                    value = comp;
                    return true;
                }
                error = $"Cannot bind GameObject '{name}' to {targetType.Name}";
                return false;
            }

            // フォールバック: 型に応じた名前検索。Resources / FindObjectsByType を使う。
            // Phase 1 では UI ピッカー経由の利用を主想定とし、ここでは最小サポートに留める。
            error = $"Cannot resolve '{raw}' to {targetType.Name}. Use '@<instanceID>' or 'GameObject:<name>'.";
            return false;
        }

        public string ToDisplayString(object value)
        {
            if (value == null) return "";
            if (value is Object uo) return uo == null ? "" : $"@{uo.GetInstanceID()} ({uo.name})";
            return value.ToString();
        }
    }
}
