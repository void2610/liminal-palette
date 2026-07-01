using System;
using System.Collections.Generic;
using System.Reflection;
using LitMotion;

namespace Void2610.LiminalPalette.Runtime
{
    /// <summary>
    /// LitMotion のアクティブな全 tween をリフレクション経由で即時完了 / キャンセルする
    /// テスト用の組み込みコマンド。E2E シナリオで演出待機 (WaitSeconds / AssertEventually)
    /// を撲滅するのが目的。CompleteAll は bound property の書き込みと OnComplete の発火を
    /// 同フレーム内で行うため、直後の行で AssertEquals をそのまま書ける。
    ///
    /// この asmdef は com.annulusgames.lit-motion &gt;= 1.0 が導入されていて
    /// LIMINAL_PALETTE_LITMOTION define が立っているときのみコンパイルされる。
    /// LitMotion 未導入プロジェクトでは lp 本体に一切影響しない。
    ///
    /// LitMotion 側の <c>MotionManager.list</c> / <c>MotionStorage&lt;&gt;.sparseIndexLookup</c> /
    /// <c>SparseIndex</c> はいずれも internal のためリフレクションで到達している。
    /// LitMotion の内部構造が変わったらこのファイル 1 つを直せば済む形で隔離してある。
    /// </summary>
    public static class LitMotionCommands
    {
        // 連鎖 OnComplete で新 tween が生成されるケースに対応する上限反復回数。
        // 進捗ゼロで打ち切るため通常は 1〜2 で収束するが、pathological case への安全弁。
        private const int MaxIterations = 8;

        // ---- Reflection init (static ctor) ----

        // MotionManager.list への到達可否。init エラーは戻り値文字列で明示。
        private static readonly FieldInfo? _motionListField;
        private static readonly MethodInfo? _fastListAsArray;
        private static readonly string _initError;

        // MotionStorage<TValue,TOptions,TAdapter> は generic instantiation ごとに別型のため
        // Id / Count / sparseIndexLookup のリフレクションは型別にキャッシュする。
        private static readonly Dictionary<Type, StorageAccessors> _storageAccessorsCache = new();

        // SparseIndex 型は internal readonly struct で 1 つのみ。最初の live storage 走査時に確定する。
        private static PropertyInfo? _sparseIndexIndexProp;
        private static PropertyInfo? _sparseIndexVersionProp;

        static LitMotionCommands()
        {
            _initError = "";
            try
            {
                var litMotionAsm = typeof(MotionHandle).Assembly;
                var managerType = litMotionAsm.GetType("LitMotion.MotionManager", throwOnError: false);
                if (managerType == null) { _initError = "LitMotion.MotionManager type not found"; return; }

                _motionListField = managerType.GetField("list", BindingFlags.NonPublic | BindingFlags.Static);
                if (_motionListField == null) { _initError = "MotionManager.list field not found"; return; }

                var fastListType = _motionListField.FieldType;
                _fastListAsArray = fastListType.GetMethod("AsArray", BindingFlags.Public | BindingFlags.Instance);
                if (_fastListAsArray == null) { _initError = "FastListCore.AsArray method not found"; return; }
            }
            catch (Exception ex)
            {
                _initError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        // ---- Commands ----

        [LiminalCommand("Anim/CompleteAll",
            Description = "アクティブな LitMotion tween すべてを最終値まで即座に進める (bound property の反映と OnComplete が同フレーム内に発火)。Sequence 内 tween と無限ループ tween は仕様上 Complete できないので残る。戻り値の skipped は最終時点で残ったハンドル数。MaxIterations (連鎖 OnComplete 深度) を超えて打ち切った場合は末尾に truncated=true を付与する")]
        public static string CompleteAll()
        {
            if (_initError.Length > 0) return "reflection init failed: " + _initError;

            var completed = 0;
            var iterations = 0;
            var truncated = false;
            while (true)
            {
                var handles = SnapshotActiveHandles();
                if (handles.Count == 0) break;

                // イテレーション上限判定はスナップショット取得後に行う。まだ残っているのに打ち切る場合を
                // 呼び出し側が truncated マーカーで区別できるようにする (通常収束との差別化)。
                if (iterations >= MaxIterations)
                {
                    truncated = true;
                    break;
                }

                var completedBefore = completed;
                foreach (var h in handles)
                {
                    // TryComplete が false のときは Sequence 内 / 無限ループ / 既完了 のいずれか。
                    // ここではカウントせず、最終残数 (下の再スナップショット) に含めることで水増しを避ける。
                    if (h.TryComplete()) completed++;
                }
                iterations++;

                // 進捗ゼロ (全て Complete 不可能) ならこれ以上回しても無駄なので break。
                if (completed == completedBefore) break;
            }

            // skipped は最終スナップショット時点で残っているハンドル数 = CompleteAll で終わらせられなかった tween。
            // 各イテレーションで加算する方式だと同じ handle が複数回カウントされて水増しになるためこの形にする。
            var skipped = SnapshotActiveHandles().Count;

            return truncated
                ? $"completed={completed} skipped={skipped} iterations={iterations} truncated=true"
                : $"completed={completed} skipped={skipped} iterations={iterations}";
        }

        [LiminalCommand("Anim/CancelAll",
            Description = "アクティブな LitMotion tween すべてを Cancel する (無限ループ tween / Sequence 内も対象)。OnCancel コールバックが同フレーム内に発火。CompleteAll で終わらない tween を掃くための逃げ道")]
        public static string CancelAll()
        {
            if (_initError.Length > 0) return "reflection init failed: " + _initError;

            var handles = SnapshotActiveHandles();
            int cancelled = 0, skipped = 0;
            foreach (var h in handles)
            {
                if (h.TryCancel()) cancelled++;
                else skipped++;
            }
            return $"cancelled={cancelled} skipped={skipped}";
        }

        // ---- Snapshot ----

        // 全 IMotionStorage を走査して alive な (Index, Version, StorageId) を MotionHandle として集める。
        // 集めきってから TryComplete / TryCancel を回すことで、コールバックが起こす storage 変異
        // (RemoveAt による swap) が走査中に発生しても index ズレが起きないようにする。
        private static List<MotionHandle> SnapshotActiveHandles()
        {
            var result = new List<MotionHandle>();
            var listBox = _motionListField!.GetValue(null);
            if (listBox == null) return result;

            // FastListCore<IMotionStorage> は struct のため GetValue で boxed copy が返るが、
            // AsArray() は内部配列参照 (backing store) を返すので実データにそのまま届く。
            if (_fastListAsArray!.Invoke(listBox, null) is not object[] storages) return result;

            foreach (var storage in storages)
            {
                if (storage == null) continue;

                var accessors = GetOrBuildAccessors(storage.GetType());
                if (accessors == null) continue;

                var count = (int)accessors.CountProp.GetValue(storage)!;
                if (count <= 0) continue;

                if (accessors.LookupField.GetValue(storage) is not Array lookup) continue;
                var id = (int)accessors.IdProp.GetValue(storage)!;

                for (var i = 0; i < count && i < lookup.Length; i++)
                {
                    // SparseIndex は value type のため GetValue は必ず boxed 非 null (Array.GetValue の仕様)。
                    var sparseIndex = lookup.GetValue(i)!;

                    if (_sparseIndexIndexProp == null)
                    {
                        var sType = sparseIndex.GetType();
                        _sparseIndexIndexProp = sType.GetProperty("Index");
                        _sparseIndexVersionProp = sType.GetProperty("Version");
                    }
                    if (_sparseIndexIndexProp == null || _sparseIndexVersionProp == null) continue;

                    var idx = (int)_sparseIndexIndexProp.GetValue(sparseIndex)!;
                    var ver = (int)_sparseIndexVersionProp.GetValue(sparseIndex)!;
                    // Version <= 0 は SparseSetCore の未使用スロットマーカー。念のため防衛的に弾く。
                    if (ver <= 0) continue;

                    result.Add(new MotionHandle { Index = idx, Version = ver, StorageId = id });
                }
            }
            return result;
        }

        private static StorageAccessors? GetOrBuildAccessors(Type storageType)
        {
            if (_storageAccessorsCache.TryGetValue(storageType, out var cached)) return cached;

            var idProp = storageType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            var countProp = storageType.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            var lookupField = storageType.GetField("sparseIndexLookup", BindingFlags.NonPublic | BindingFlags.Instance);
            if (idProp == null || countProp == null || lookupField == null) return null;

            var accessors = new StorageAccessors(idProp, countProp, lookupField);
            _storageAccessorsCache[storageType] = accessors;
            return accessors;
        }

        private sealed class StorageAccessors
        {
            public readonly PropertyInfo IdProp;
            public readonly PropertyInfo CountProp;
            public readonly FieldInfo LookupField;

            public StorageAccessors(PropertyInfo idProp, PropertyInfo countProp, FieldInfo lookupField)
            {
                IdProp = idProp;
                CountProp = countProp;
                LookupField = lookupField;
            }
        }
    }
}
