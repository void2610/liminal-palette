using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Void2610.LiminalPalette.Editor;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Tests.UI
{
    public sealed class ParameterEditorRegistryTests
    {
        // Editor 起動時 [InitializeOnLoadMethod] で Color / Object / EnumFlags が追加登録されている前提。
        // 各テスト前に標準状態にしたいが、ResetToDefaults は UI の標準のみを復元するため、
        // Editor 側追加分はその後に明示的に再登録する。
        [SetUp]
        public void SetUp()
        {
            ParameterEditorRegistry.ResetToDefaults();
            ParameterEditorRegistry.Register(new EditorColorEditor());
            ParameterEditorRegistry.Register(new EditorObjectEditor());
            ParameterEditorRegistry.Register(new EditorEnumFlagsEditor());
        }

        // テスト用のダミー ParameterDescriptor を作るヘルパ。
        private static ParameterDescriptor Param(Type type)
            => new ParameterDescriptor("p", type, 0, false, null, "", Array.Empty<string>());

        [Test]
        public void Resolve_Int_GetsPrimitiveEditor()
        {
            var editor = ParameterEditorRegistry.Resolve(typeof(int));
            Assert.IsInstanceOf<PrimitiveEditor>(editor);
        }

        [Test]
        public void Resolve_Bool_GetsPrimitiveEditor()
        {
            var editor = ParameterEditorRegistry.Resolve(typeof(bool));
            Assert.IsInstanceOf<PrimitiveEditor>(editor);
        }

        [Test]
        public void Resolve_String_GetsPrimitiveEditor()
        {
            var editor = ParameterEditorRegistry.Resolve(typeof(string));
            Assert.IsInstanceOf<PrimitiveEditor>(editor);
        }

        [Test]
        public void Resolve_Vector3_GetsVectorEditor()
        {
            var editor = ParameterEditorRegistry.Resolve(typeof(Vector3));
            Assert.IsInstanceOf<VectorEditor>(editor);
        }

        private enum SampleEnum { A, B, C }
        [Flags] private enum SampleFlags { None = 0, X = 1, Y = 2, Z = 4 }

        [Test]
        public void Resolve_Enum_GetsEnumEditor()
        {
            var editor = ParameterEditorRegistry.Resolve(typeof(SampleEnum));
            Assert.IsInstanceOf<EnumEditor>(editor);
        }

        [Test]
        public void Resolve_FlagsEnum_GetsEditorEnumFlagsEditor()
        {
            // Flags なら Editor 側の EnumFlagsEditor が高優先で解決される。
            var editor = ParameterEditorRegistry.Resolve(typeof(SampleFlags));
            Assert.IsInstanceOf<EditorEnumFlagsEditor>(editor);
        }

        [Test]
        public void Resolve_Color_GetsEditorColorEditor()
        {
            var editor = ParameterEditorRegistry.Resolve(typeof(Color));
            Assert.IsInstanceOf<EditorColorEditor>(editor);
        }

        [Test]
        public void Resolve_GameObject_GetsEditorObjectEditor()
        {
            var editor = ParameterEditorRegistry.Resolve(typeof(GameObject));
            Assert.IsInstanceOf<EditorObjectEditor>(editor);
        }

        [Test]
        public void Resolve_UnregisteredReferenceType_FallsBackToTextEditor()
        {
            // どのエディタも CanHandle しない型 (System.Uri) は最終的に FallbackTextEditor に落ちる。
            var editor = ParameterEditorRegistry.Resolve(typeof(System.Uri));
            Assert.IsInstanceOf<FallbackTextEditor>(editor);
        }

        // 利用側カスタムエディタ。組み込みを上書きできることの検証用。
        private sealed class CustomIntEditor : IParameterEditor
        {
            public bool CanHandle(Type type) => type == typeof(int);
            public VisualElement Build(ParameterDescriptor param, Action<object> onChanged) => new TextField();
        }

        [Test]
        public void Register_CustomEditor_OverridesBuiltin()
        {
            ParameterEditorRegistry.Register(new CustomIntEditor());
            var resolved = ParameterEditorRegistry.Resolve(typeof(int));
            Assert.IsInstanceOf<CustomIntEditor>(resolved);
            // [SetUp] が次のテスト開始時に ResetToDefaults を呼ぶので、漏れ防止のクリーンアップは不要。
        }

        [Test]
        public void Build_PrimitiveInt_ReturnsIntegerField()
        {
            var editor = ParameterEditorRegistry.Resolve(typeof(int));
            var ve = editor.Build(Param(typeof(int)), _ => { });
            Assert.IsInstanceOf<IntegerField>(ve);
        }

        [Test]
        public void Build_Vector3_ReturnsVector3Field()
        {
            var editor = ParameterEditorRegistry.Resolve(typeof(Vector3));
            var ve = editor.Build(Param(typeof(Vector3)), _ => { });
            Assert.IsInstanceOf<Vector3Field>(ve);
        }
    }
}
