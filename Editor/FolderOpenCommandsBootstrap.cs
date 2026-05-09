using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// Editor 起動時に Assets/ 直下の各フォルダ (Prefabs / Materials / Scripts / ...) を
    /// `Editor/Open/<FolderName>` というコマンドとして CommandRegistry に動的登録する。
    /// fuzzy 検索で「Open Prefabs」「Open Mat」などで Project View に該当フォルダを表示できる。
    ///
    /// プロジェクト構成変化 (フォルダ追加 / 削除 / リネーム) に追随するため
    /// EditorApplication.projectChanged で再スキャンする。
    /// </summary>
    [InitializeOnLoad]
    internal static class FolderOpenCommandsBootstrap
    {
        // パレット表示時のパス prefix。"Editor/" 始まりなので CommandDescriptor.IsEditorOnly が
        // 自動的に true になり、Play Mode / Player ビルドのランタイムパレットからは除外される。
        private const string PathPrefix = "Editor/Open/";

        // 直前の Rescan で登録したコマンドパス。次回 Rescan で先に Unregister するために保持する。
        // 利用側が手書きで登録した [ConsoleCommand("Editor/Open/Foo")] を巻き込まないよう、
        // 自動登録分のみここで管理する。
        private static readonly HashSet<string> _registeredPaths = new HashSet<string>();

        // CommandDescriptor は MethodInfo を要求するため、ダミーの static メソッドへの参照を持つ。
        // 実際の呼び出しは Invoker デリゲートで行われるので Method は呼ばれない。
        private static readonly MethodInfo DummyMethod
            = typeof(FolderOpenCommandsBootstrap).GetMethod(nameof(NoOp), BindingFlags.Static | BindingFlags.NonPublic);

        private static void NoOp() { }

        static FolderOpenCommandsBootstrap()
        {
            Rescan();
            // フォルダの追加 / 削除 / リネーム時に projectChanged が飛ぶので再登録する。
            EditorApplication.projectChanged -= Rescan;
            EditorApplication.projectChanged += Rescan;
        }

        // Assets/ 直下のフォルダを列挙し、自動登録分を作り直す。
        // 既存自動登録は全削除 → 新リストで再登録、と単純化している (フォルダ数は通常数十なのでコスト無視)。
        private static void Rescan()
        {
            try
            {
                var registry = LiminalPalette.Registry;

                // 前回の自動登録分だけを掃除。手書き [ConsoleCommand] と被らないよう参照同一性ではなく
                // _registeredPaths のセット管理で識別する。
                foreach (var p in _registeredPaths)
                {
                    registry.Unregister(p);
                }
                _registeredPaths.Clear();

                var assetsRoot = Application.dataPath; // <project>/Assets を絶対パスで返す
                if (!Directory.Exists(assetsRoot)) return;

                var folders = Directory.GetDirectories(assetsRoot, "*", SearchOption.TopDirectoryOnly);
                Array.Sort(folders, StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < folders.Length; i++)
                {
                    var name = Path.GetFileName(folders[i]);
                    if (string.IsNullOrEmpty(name)) continue;
                    // 隠しフォルダ (.git など Unity も無視するもの) はスキップ。
                    if (name.StartsWith(".", StringComparison.Ordinal)) continue;

                    var assetPath = "Assets/" + name;
                    var commandPath = PathPrefix + name;

                    // 利用側が同名の手書きコマンドを定義していたらそちらを優先する。
                    if (registry.Find(commandPath) != null) continue;

                    // assetPath はクロージャに入れて invoker から参照する。
                    var pathForInvoker = assetPath;
                    var descriptor = new CommandDescriptor(
                        path: commandPath,
                        description: $"Project View で {assetPath} を開く",
                        aliases: Array.Empty<string>(),
                        parameters: Array.Empty<ParameterDescriptor>(),
                        returnType: typeof(void),
                        isAsync: false,
                        method: DummyMethod,
                        invoker: _ => { OpenFolder(pathForInvoker); return null; });
                    registry.Register(descriptor);
                    _registeredPaths.Add(commandPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LiminalPalette] FolderOpenCommands rescan failed: {ex.Message}");
            }
        }

        // 指定フォルダを Project View で「開く」(double-click と同じ挙動)。
        //   1. ユーザーが触っている ProjectBrowser を s_LastInteractedProjectBrowser から取得
        //      (無ければ GetWindow で開く)
        //   2. SetTwoColumns() で 2-column モードに強制 (ShowFolderContents は 2-column 専用)
        //   3. ShowFolderContents(EntityId, true) で右ペイン中身 + 左ツリー展開
        //   4. Selection と ping を当てて視覚フィードバック
        // OpenSelectedFolders() は m_ListArea のリストエリア選択を読むので、Selection.activeObject を
        // 当てても効かない。直接 ShowFolderContents を叩く方が確実。
        private static void OpenFolder(string assetPath)
        {
            var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (folder == null)
            {
                Debug.LogWarning($"[LiminalPalette] フォルダが見つかりません: {assetPath}");
                return;
            }

            var pbType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
            if (pbType == null)
            {
                FallbackSelect(folder);
                return;
            }

            // s_LastInteractedProjectBrowser はユーザーが直前に触っていた Project window への参照。
            // これを優先することで、新規 floating window を生成せず既存タブを操作できる。
            // 起動直後など null の場合は GetWindow にフォールバックする。
            var lastField = pbType.GetField("s_LastInteractedProjectBrowser",
                BindingFlags.Static | BindingFlags.Public);
            var browser = lastField?.GetValue(null) as EditorWindow
                          ?? EditorWindow.GetWindow(pbType);
            if (browser == null)
            {
                FallbackSelect(folder);
                return;
            }
            browser.Focus();

            try
            {
                // ShowFolderContents は 2-column 限定 (1-column では Debug.LogError + 中身遷移しない)。
                // SetTwoColumns は no-op when already 2-column。
                var setTwoColumns = pbType.GetMethod("SetTwoColumns",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                setTwoColumns?.Invoke(browser, null);

                // ShowFolderContents(EntityId folderInstanceID, bool revealAndFrameInFolderTree)。
                // 第 1 引数は Unity 6.2+ で EntityId 型なので Object.GetEntityId() の戻り値を使う。
                // 第 2 引数 = true で左ツリー側もそのフォルダにスクロール / 展開させる。
                var showFolderContents = pbType.GetMethod("ShowFolderContents",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (showFolderContents == null)
                {
                    Debug.LogWarning("[LiminalPalette] ProjectBrowser.ShowFolderContents が見つかりません (Unity API 変更の可能性)。");
                    return;
                }

                var folderIdArg = ResolveFolderIdArgument(showFolderContents, folder);
                showFolderContents.Invoke(browser, new[] { folderIdArg, (object)true });
                browser.Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LiminalPalette] ProjectBrowser リフレクション呼び出しに失敗: {ex.Message}");
            }

            // 注意: ここで `Selection.activeObject = folder` を当ててはいけない。
            // 2-column ProjectBrowser の OnSelectionChange はフォルダアセットが選択されると
            // FrameObjectInTwoColumnMode → 親フォルダ (例: "Assets") を ShowFolderContents し直す経路に入る。
            // 結果としてせっかく開いたフォルダ中身ペインが親フォルダに巻き戻り、対象は単に親内で行ハイライトされるだけになる。
            // ShowFolderContents 自体が左ツリー側のハイライト + 右ペインの中身表示を行うので、Selection の追加更新は不要。
        }

        // ProjectBrowser へのリフレクションが失敗した時の最終手段。
        // 少なくとも Selection 上はそのフォルダに飛ぶので、ユーザーは手動で展開できる。
        private static void FallbackSelect(UnityEngine.Object folder)
        {
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }

        // ShowFolderContents の第 1 引数の型に合わせて folderId を組み立てる。
        //   - int           : 旧 Unity。GetInstanceID() をそのまま渡す
        //   - EntityId 等   : Unity 6.2+。Object.GetEntityId() があればその戻り値を使い、
        //                     無ければ ctor(int) で構築。さらに無ければ最終的に int にフォールバック。
        // method の最初のパラメータが値型 / クラスのいずれであっても InvokeMethod の引数 boxed として
        // 渡せれば良いので、戻り値 object をそのまま渡す。
        private static object ResolveFolderIdArgument(MethodInfo method, UnityEngine.Object folder)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0) return folder.GetInstanceID();
            var paramType = parameters[0].ParameterType;

            if (paramType == typeof(int)) return folder.GetInstanceID();

            // Object.GetEntityId() が存在し、その戻り値が paramType に代入可能ならそれを採用。
            var getEntityId = typeof(UnityEngine.Object).GetMethod("GetEntityId", Type.EmptyTypes);
            if (getEntityId != null && paramType.IsAssignableFrom(getEntityId.ReturnType))
            {
                return getEntityId.Invoke(folder, null);
            }

            // ctor(int) で構築できるならそれを使う (EntityId(int) など)。
            var ctorInt = paramType.GetConstructor(new[] { typeof(int) });
            if (ctorInt != null)
            {
                return ctorInt.Invoke(new object[] { folder.GetInstanceID() });
            }

            // ctor(long) も試す。
            var ctorLong = paramType.GetConstructor(new[] { typeof(long) });
            if (ctorLong != null)
            {
                return ctorLong.Invoke(new object[] { (long)folder.GetInstanceID() });
            }

            // 最終フォールバック: int を boxed で渡す (実行時に InvalidCast になるが LogWarning に拾わせる)。
            return folder.GetInstanceID();
        }
    }
}
