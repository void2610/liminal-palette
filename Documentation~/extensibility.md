# Extensibility

LiminalPalette を利用側で拡張するための 4 つのフックポイント:

1. **`ITypeConverter`** — 任意の型を引数として受け付ける
2. **`IParameterEditor`** — 任意の型に専用 UI を提供する
3. **`ICommandHistory`** — 履歴の永続化先を差し替える (PlayerPrefs 以外に DB 等)
4. **`IInstanceResolver`** — インスタンスメソッド `[LiminalCommand]` のインスタンス解決経路 (DI コンテナ統合用)

`ITypeConverter` / `IParameterEditor` は **後勝ち登録** (新しく登録したものが既存より優先される) のルール。`IInstanceResolver` は単一インスタンス差替え。

---

## `ITypeConverter`: 型変換の拡張

### インタフェース

```csharp
public interface ITypeConverter
{
    bool CanConvert(Type targetType);
    bool TryFromString(string raw, Type targetType, out object value, out string error);
    string ToDisplayString(object value);
}
```

| メソッド | 役割 |
|---|---|
| `CanConvert` | この型を扱えるなら true。`TypeConverterRegistry.Resolve` がチェック |
| `TryFromString` | 文字列 → 型解決済み値。失敗時は `false` + エラーメッセージ (例外は投げない) |
| `ToDisplayString` | 値 → 表示用文字列。Result の `value`、UI のプレビュー等で使われる |

### 実装例: `Quaternion`

```csharp
using System;
using System.Globalization;
using UnityEngine;
using Void2610.LiminalPalette;

public sealed class QuaternionConverter : ITypeConverter
{
    public bool CanConvert(Type targetType) => targetType == typeof(Quaternion);

    public bool TryFromString(string raw, Type targetType, out object value, out string error)
    {
        value = null;
        error = null;
        if (string.IsNullOrEmpty(raw))
        {
            error = "Empty input";
            return false;
        }
        // "x,y,z,w" 形式を想定
        var parts = raw.Split(',');
        if (parts.Length != 4)
        {
            error = "Expected 'x,y,z,w'";
            return false;
        }
        if (!TryParseFloat(parts[0], out var x) ||
            !TryParseFloat(parts[1], out var y) ||
            !TryParseFloat(parts[2], out var z) ||
            !TryParseFloat(parts[3], out var w))
        {
            error = "Invalid float";
            return false;
        }
        value = new Quaternion(x, y, z, w);
        return true;
    }

    public string ToDisplayString(object value)
    {
        if (!(value is Quaternion q)) return "";
        return $"({q.x:F2}, {q.y:F2}, {q.z:F2}, {q.w:F2})";
    }

    private static bool TryParseFloat(string s, out float v)
        => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
```

### 登録

```csharp
public static class MyBootstrap
{
    [InitializeOnLoadMethod]                      // Editor 起動時
    [RuntimeInitializeOnLoadMethod]               // Runtime 起動時 (両方付ける)
    static void Register()
    {
        LiminalPalette.RegisterTypeConverter(new QuaternionConverter());
    }
}
```

これだけで `[LiminalCommand]` メソッドが `Quaternion` 引数を取れるようになる:

```csharp
[LiminalCommand("Player/Rotate")]
public static void Rotate(Quaternion rotation) { /* ... */ }
```

### 既存コンバータ (Phase 1 で導入済み)

| コンバータ | 対応型 |
|---|---|
| `PrimitiveConverter` | `int` / `uint` / `long` / `ulong` / `short` / `ushort` / `byte` / `sbyte` / `float` / `double` / `decimal` / `bool` / `char` / `string` |
| `EnumConverter` | enum (`[Flags]` 含む) |
| `VectorConverter` | `Vector2/3/4` / `Vector2Int` / `Vector3Int` |
| `ColorConverter` | `Color` / `Color32` (`#RRGGBB` / `#RRGGBBAA` / `r,g,b,a`) |
| `UnityObjectConverter` | `UnityEngine.Object` 派生 (`@<entityID>` / `GameObject:<name>`) |

利用側の `ITypeConverter` を後から登録すれば既存を上書きできる (例: `ColorConverter` を独自実装で置き換える)。

---

## `IParameterEditor`: 引数 UI の拡張

### インタフェース

```csharp
public interface IParameterEditor
{
    bool CanHandle(Type type);
    VisualElement Build(ParameterDescriptor param, Action<object> onChanged);
}
```

| メソッド | 役割 |
|---|---|
| `CanHandle` | この型を扱えるなら true |
| `Build` | 引数入力 UI を生成。値が変わったら `onChanged` を呼ぶ |

### 実装例: `Quaternion` 用 UI (Slider 4 本)

```csharp
using System;
using UnityEngine;
using UnityEngine.UIElements;
using Void2610.LiminalPalette.UI;

public sealed class QuaternionEditor : IParameterEditor
{
    public bool CanHandle(Type type) => type == typeof(Quaternion);

    public VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
    {
        var initial = param.HasDefault ? (Quaternion)param.DefaultValue : Quaternion.identity;
        var current = initial;
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Column;

        AddSlider(root, "x", initial.x, v => { current.x = v; onChanged(current); });
        AddSlider(root, "y", initial.y, v => { current.y = v; onChanged(current); });
        AddSlider(root, "z", initial.z, v => { current.z = v; onChanged(current); });
        AddSlider(root, "w", initial.w, v => { current.w = v; onChanged(current); });

        return root;
    }

    private static void AddSlider(VisualElement parent, string label, float initial, Action<float> on)
    {
        var s = new Slider(-1f, 1f) { value = initial, label = label };
        s.RegisterValueChangedCallback(e => on(e.newValue));
        parent.Add(s);
    }
}
```

### 登録

```csharp
public static class MyBootstrap
{
    [InitializeOnLoadMethod]
    [RuntimeInitializeOnLoadMethod]
    static void Register()
    {
        ParameterEditorRegistry.Register(new QuaternionEditor());
    }
}
```

### 既存エディタの優先順位

`ParameterEditorRegistry.Register` は **先頭挿入 = 高優先** のルール。3 段階で登録される:

| 段階 | 登録タイミング | 登録元 | 内容 | Editor 優先 | Runtime 優先 |
|---|---|---|---|---|---|
| 1 | static cctor | UI asmdef | `FallbackText` / `Primitive` / `Enum` / `Vector` | 最低 | 最低 |
| 2 | `[RuntimeInitializeOnLoadMethod]` | UI asmdef | `RuntimeColor` / `RuntimeObject` / `RuntimeEnumFlags` | 中 | **最高** |
| 3 | `[InitializeOnLoadMethod]` | Editor asmdef | `EditorColor` / `EditorObject` / `EditorEnumFlags` | **最高** | 該当なし |

利用側が後から `Register` すると最も優先度が高くなる。例えば `Color` を Slider ではなく自作のホイール UI に置き換えたいなら:

```csharp
public sealed class MyColorWheelEditor : IParameterEditor
{
    public bool CanHandle(Type type) => type == typeof(Color);
    public VisualElement Build(...) { /* HSV ホイール UI */ }
}

[RuntimeInitializeOnLoadMethod]
[InitializeOnLoadMethod]
static void Register()
{
    ParameterEditorRegistry.Register(new MyColorWheelEditor());
}
```

これで `Color` 引数は MyColorWheelEditor が生成する UI で入力されるようになる (Editor / Runtime 両方)。

### `FallbackTextEditor`

すべての型を `CanHandle = true` で受ける最終手段のエディタ。`TextField` + `TypeConverterRegistry.TryConvert` の組合せで動く。

つまり:
- **`ITypeConverter` だけ実装**: 文字列入力可能になる (UI は TextField + 赤縁エラー表示)
- **`IParameterEditor` も実装**: 専用 UI で入力できる

`ITypeConverter` を実装するだけでも HTTP 経由の文字列引数で叩けるので、最低限はそれで十分。

---

## `ICommandHistory`: 履歴の永続化先

### インタフェース

```csharp
public interface ICommandHistory
{
    IReadOnlyList<string> RecentPaths { get; }
    void Record(string path);
    void Clear();
    bool Contains(string path);
    int IndexOf(string path);
}
```

`RecentPaths` は新しい順。`MaxEntries = 50` 件で打ち切られる (`InMemoryCommandHistory.MaxEntries`)。

### 既存実装

| 実装 | 永続化先 | 利用 |
|---|---|---|
| `InMemoryCommandHistory` | プロセス内のみ | 既定 / テスト |
| `EditorCommandHistory` | `EditorPrefs` | `LiminalPaletteWindow` で自動採用 |
| `PlayerPrefsCommandHistory` | `PlayerPrefs` | `LiminalPaletteRuntime` で自動採用 |

### カスタム実装例: SQLite に保存

```csharp
public sealed class SqliteCommandHistory : ICommandHistory
{
    private readonly InMemoryCommandHistory _inner = new InMemoryCommandHistory();

    public SqliteCommandHistory(string dbPath)
    {
        // SQLite から既存履歴をロードして _inner に積む
        // (Record / Clear で _inner と SQLite を同期)
    }

    public IReadOnlyList<string> RecentPaths => _inner.RecentPaths;
    public void Record(string path) { _inner.Record(path); /* SQLite に INSERT */ }
    public void Clear() { _inner.Clear(); /* SQLite を DELETE */ }
    public bool Contains(string path) => _inner.Contains(path);
    public int IndexOf(string path) => _inner.IndexOf(path);
}
```

### 差し替え

`PaletteController` のコンストラクタに渡す:

```csharp
var controller = new PaletteController(
    CommandRegistry.Default,
    new CommandExecutor(CommandRegistry.Default),
    new SqliteCommandHistory("/path/to/history.db"));
```

ただし `LiminalPaletteWindow` / `LiminalPaletteRuntime` は内部で固定の History 実装を使うため、ホスト側のコードを上書きする必要がある (現状は public API なし)。将来検討事項として、history factory のフックを公開する案がある。

---

## `IInstanceResolver`: インスタンスメソッド解決経路

インスタンスメソッド `[LiminalCommand]` 実行時に、メソッドが属する型のインスタンスをどこから取ってくるかを差し替えるフックポイント。

### インタフェース

```csharp
namespace Void2610.LiminalPalette
{
    public interface IInstanceResolver
    {
        /// <summary>type のインスタンスを取得。解決できない場合は null。</summary>
        object Resolve(Type type);
    }
}
```

null を返すと `CommandExecutor` が「Instance not resolved」エラーを利用者向けメッセージ付きで返す。

### 既定実装

| 実装 | 動作 |
|---|---|
| `NullInstanceResolver` (Core 同梱、internal) | 常に null を返す。SetInstanceResolver を呼ばないとこれが使われ、インスタンスメソッドコマンドは Fail する |
| `VContainerInstanceResolver` (Integration.VContainer) | `IObjectResolver.Resolve(type)` をラップ。VContainer の登録に従って解決 |

### カスタム実装例: 自前のサービスロケータ

```csharp
public sealed class MyServiceLocatorResolver : IInstanceResolver
{
    public object Resolve(Type type)
    {
        // ServiceLocator から取り出すなど
        return MyServiceLocator.Get(type);
    }
}

[RuntimeInitializeOnLoadMethod]
static void RegisterResolver()
{
    LiminalPalette.SetInstanceResolver(new MyServiceLocatorResolver());
}
```

### 差替えタイミング

- VContainer 統合経由の場合: `LiminalPaletteEntryPoint.Initialize` (`IInitializable`) が VContainer の Initialize 段階で呼ばれる
- 自前の場合: `[InitializeOnLoadMethod]` / `[RuntimeInitializeOnLoadMethod]` で `LiminalPalette.SetInstanceResolver` を呼ぶ

詳細は [integrations.md](integrations.md)。

---

## 動的コマンド登録

`[LiminalCommand]` 属性に依らないコマンドの登録経路:

```csharp
public static class DynamicRegistration
{
    [InitializeOnLoadMethod]
    static void Register()
    {
        var path = "Dynamic/Spawn";
        var parameters = new[]
        {
            new ParameterDescriptor("name", typeof(string), 0, false, null, "オブジェクト名", Array.Empty<string>())
        };

        var descriptor = new CommandDescriptor(
            path: path,
            description: "動的に登録されたコマンド",
            aliases: Array.Empty<string>(),
            parameters: parameters,
            returnType: typeof(GameObject),
            isAsync: false,
            method: null,
            invoker: args =>
            {
                // args は ParameterDescriptor の順序と同じ object[]。
                // ここでは name を受け取って Cube を生成し、生成した GameObject に名前を付けて返す。
                var name = (string)args[0];
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                return go;
            });

        CommandRegistry.Default.Register(descriptor);
    }
}
```

ポイント:
- `CommandDescriptor.Invoker` (`Func<object[], object>`) を non-null で指定
- `CommandExecutor` は `MethodInfo.Invoke` の代わりに `Invoker` を呼ぶ
- 戻り値の型は `ReturnType` で宣言する必要がある (UI / IPC のスキーマ表示用)

実用例: `EditorMenuItemBootstrap` が Unity の MenuItem を全部スキャンしてコマンド化している (Phase 2 で導入)。

---

## 拡張のテスト

新しい `ITypeConverter` / `IParameterEditor` のテストを書く際は、既存テストを参考にする:

- `Assets/Plugins/LiminalPalette/Tests/Editor/TypeConverterTests.cs`
- `Assets/Plugins/LiminalPalette/Tests/Editor/UI/ParameterEditorRegistryTests.cs`
- `Assets/Plugins/LiminalPalette/Tests/Editor/UI/RuntimeColorEditorTests.cs` (Slider 系のテストパターン)

テストの `[SetUp]` で `ParameterEditorRegistry.ResetToDefaults()` / `TypeConverterRegistry.ResetToDefaults()` を呼んでから登録するパターンが安全。

---

## 関連ドキュメント

- [commands.md](commands.md) — `[LiminalCommand]` の引数型一覧
- [ui.md](ui.md) — `IParameterEditor` の UI が表示される文脈
- [asmdef.md](asmdef.md) — どの asmdef がどの拡張ポイントを公開しているか
