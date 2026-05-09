# Commands

`[ConsoleCommand]` 属性とコマンドメソッドのすべて。

## 最小例

```csharp
using Void2610.LiminalPalette;
using UnityEngine;

public static class MyCommands
{
    [ConsoleCommand("Player/Health/Set")]
    public static void SetHealth(int value) => Debug.Log($"HP = {value}");
}
```

これで `Player/Health/Set` という名前で Editor / Runtime / HTTP の 3 経路から実行できる。

## `[ConsoleCommand]` の全パラメータ

```csharp
[ConsoleCommand(
    path:        "Category/Subcategory/Action",   // ← 必須・第 1 引数
    Description: "ヒトに見せる説明",                  // 任意
    Aliases:     new[] { "Cat/Sub/A" }            // 任意・別名
)]
```

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| `Path` | `string` | ✅ | コマンドの識別子。`/` 区切り。空文字・先頭/末尾 `/` は Scanner で例外 |
| `Description` | `string` | — | UI / `/api/v1/commands` で出る説明文 |
| `Aliases` | `string[]` | — | 別名リスト。fuzzy 検索の対象に含まれる |

> **Note**: Production 除外はビルド単位の防御層 (asmdef defineConstraints + `ProductionGuard` + `LIMINAL_PALETTE_DISABLED` define) で行う設計。個別コマンドだけ除外したい場合は C# 標準の `#if UNITY_EDITOR || DEVELOPMENT_BUILD` または `[System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]` を使う。詳細は [security.md](security.md)。

## メソッドの形

| 要件 | 必須 | 備考 |
|---|---|---|
| `public` | ✅ | `private` / `internal` は登録されない |
| 静的 / インスタンス | — | 両方対応。インスタンスメソッドは VContainer 経由でインスタンス解決される |
| 戻り値 | 任意 | `void` / プリミティブ / 構造体 / `Task` / `Task<T>` / `ValueTask` / `ValueTask<T>` |
| 引数 | 任意 | サポート型は下記参照 |

非対応のメソッドは `AttributeScanner` がスキップ + `Debug.LogWarning` で通知する。

### 静的メソッド

```csharp
public static class TimeCommands
{
    [ConsoleCommand("Editor/Time/Reset")]
    public static void Reset() => Time.timeScale = 1f;
}
```

最も簡単な形。インスタンス不要なのでセットアップゼロ。

### インスタンスメソッド

```csharp
public class Player : MonoBehaviour
{
    public ReactiveProperty<int> Hp { get; } = new(100);

    [ConsoleCommand("Player/Health/Set")]
    public void SetHealth(int value) => Hp.Value = value;
}
```

実行時に VContainer の `IObjectResolver` でインスタンスが解決される。利用側は `LifetimeScope.Configure` で型を登録 + `RegisterEntryPoint<LiminalPaletteEntryPoint>()` を呼ぶだけ:

```csharp
public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterComponentInHierarchy<Player>();
        builder.RegisterEntryPoint<LiminalPaletteEntryPoint>();
    }
}
```

詳細は [integrations.md](integrations.md)。

VContainer に未登録の型は実行時に「Instance not resolved」エラーで Fail する (利用者向けの対処方法を含むメッセージが返る)。

## サポートする引数の型

### プリミティブ

`int` / `uint` / `long` / `ulong` / `short` / `ushort` / `byte` / `sbyte` / `float` / `double` / `decimal` / `bool` / `char` / `string`

unsigned / 狭い範囲の整数 (`byte` / `sbyte` / `short` / `ushort` / `uint` / `ulong`) は範囲外入力で `OverflowException` が出るが UI 上はエラー表示にとどまり、コマンド側には届かない。

### enum

通常 enum: `EnumField` / `EnumEditor` で 1 つ選択。
`[Flags]` enum: ビット OR で複数選択 (Editor は `EnumFlagsField`、Runtime は Toggle 列)。

```csharp
[Flags]
public enum DamageType { None = 0, Fire = 1, Ice = 2, Poison = 4 }

[ConsoleCommand("Player/ApplyDamage")]
public static void ApplyDamage(int amount, DamageType types) { /* ... */ }
```

### Vector / Color

- `Vector2` / `Vector3` / `Vector4` / `Vector2Int` / `Vector3Int`
- `Color` / `Color32`

文字列入力 (HTTP 経由) は `"x,y,z"` 形式 (例: `"1,2,3"` → `Vector3(1, 2, 3)`)。

### `UnityEngine.Object` 派生

- `GameObject` / `Component` / `MonoBehaviour` / `ScriptableObject` 等

入力形式:
- **`@<entityID>`**: `Resources.EntityIdToObject` で解決 (UI ピッカー経由)
- **`GameObject:<name>`**: シーン上の GameObject を名前検索 (Runtime 限定)

### 任意型 (拡張)

`ITypeConverter` を実装して `LiminalPalette.RegisterTypeConverter()` で登録すれば任意型をサポートできる。詳細は [extensibility.md](extensibility.md)。

## デフォルト値

```csharp
[ConsoleCommand("Test/Greet")]
public static string Greet(string name, int times = 3, bool excited = false)
{
    var s = string.Join(" ", Enumerable.Repeat($"Hello {name}", times));
    return excited ? s + "!" : s;
}
```

UI 上では `times` と `excited` は省略可能 (デフォルト値が初期値として表示される)。HTTP 経由でも `args` から省略すればデフォルト値が使われる。

## 戻り値

| 型 | 扱い |
|---|---|
| `void` | `CommandResult.Value = null` |
| 値型 / 参照型 | そのまま `Value` に格納。HTTP API では `TypeConverterRegistry.ToDisplayString` で文字列化 |
| `Task` | await 完了を待ち、`Value = null` |
| `Task<T>` | await して T を `Value` に格納 |
| `ValueTask` / `ValueTask<T>` | 同上 (unwrap される) |

`UniTask` は **未対応** (将来の拡張で検討)。

## async コマンド

```csharp
[ConsoleCommand("Net/Fetch")]
public static async Task<string> FetchAsync(string url, CancellationToken ct = default)
{
    using var client = new HttpClient();
    return await client.GetStringAsync(url, ct);
}
```

`CancellationToken` 引数は **自動でバインドされる** (UI からは直接指定できないが、HTTP API のリクエスト中断時に伝播する)。

## 例外処理

`[ConsoleCommand]` メソッド内で例外が出ても、利用側の try-catch は不要。`CommandExecutor` が `CommandResult.Fail(message, exception)` に変換する:

```csharp
[ConsoleCommand("Test/Throws")]
public static void Throws() => throw new InvalidOperationException("boom");
```

UI / HTTP の戻り:
```json
{
  "success": false,
  "error": "boom",
  "exceptionType": "System.InvalidOperationException",
  "stackTrace": "..."
}
```

## ログ取り込み

実行中の `Debug.Log` / `Debug.LogWarning` / `Debug.LogError` は `LogCapture` で取り込まれ、`CommandResult.Logs` に蓄積される:

```csharp
[ConsoleCommand("Test/Log")]
public static void Log(string msg) => Debug.Log(msg);
```

UI の Log タブでメッセージが見える。HTTP API では `result.logs[]` 配列で返ってくる。

> 並列実行 (同じプロセスで複数コマンドが同時に走る) の場合、`LogCapture` は **混線する可能性がある** (既知の制約)。コマンド単位のログスコープ分離は将来検討。

## 動的登録

属性ベース以外に、ランタイム生成コマンドを登録するための経路:

```csharp
public static class DynamicRegistration
{
    [InitializeOnLoadMethod]
    static void Register()
    {
        var descriptor = new CommandDescriptor(
            path: "Dynamic/Hello",
            description: "ランタイム生成のコマンド",
            aliases: Array.Empty<string>(),
            parameters: Array.Empty<ParameterDescriptor>(),
            returnType: typeof(void),
            isAsync: false,
            method: null,
            invoker: args => { Debug.Log("hi"); return null; });

        CommandRegistry.Default.Register(descriptor);
    }
}
```

`CommandDescriptor.Invoker` を non-null で指定すると `MethodInfo.Invoke` の代わりにこのデリゲートが呼ばれる。Unity の MenuItem を一括コマンド化する用途で使われている (`EditorMenuItemBootstrap`)。

> セキュリティ: HTTP API 経由でのコマンド注入は **意図的に未対応**。任意コード実行に近づくため。

## `[ConsoleParam]` (補助情報 + 数値範囲)

引数に説明 / 候補リスト / 数値範囲を付与できる:

```csharp
[ConsoleCommand("Audio/Play")]
public static void Play(
    [ConsoleParam(Description = "再生する効果音のキー", Choices = new[] { "click", "open", "close" })]
    string clipKey)
{
    /* ... */
}

[ConsoleCommand("Player/Health/Damage")]
public string Damage(
    [ConsoleParam(Description = "ダメージ量", Min = 1, Max = 9999)] int amount)
{
    /* ... */
}
```

### 属性パラメータ

| プロパティ | 型 | 説明 |
|---|---|---|
| `Description` | `string` | UI / API 表示用の説明 |
| `Choices` | `string[]` | UI ドロップダウン候補 (Core では検証しない、参考情報のみ) |
| `Min` | `float` | 数値型パラメータの下限 (含む)。未指定なら下限なし |
| `Max` | `float` | 数値型パラメータの上限 (含む)。未指定なら上限なし |

### `Min` / `Max` の挙動

- 対象型: `byte` / `sbyte` / `short` / `ushort` / `int` / `uint` / `long` / `ulong` / `float` / `double` / `decimal` および対応する `Nullable<T>`
- 非数値型 (`string` / enum / `Vector3` / `Color` 等) に付けても黙って通す (UI ヒントとして残せる)
- バインド時 (`/execute` API、UI 引数欄の Submit、CLI 経由いずれも `ArgumentBinder` を通る) に範囲外なら `CommandResult.Fail` が返る。引数欄では赤いエラー表示になる
- **デフォルト値経由は検証されない** (`HasDefault` で省略された場合)。デフォルト値の妥当性は定義者の責任
- Sentinel: `float.NaN` を「未指定」として扱う。`float.NaN` 自体を境界値にすることはできない (実用上の制約はない想定)

## `[ConsoleObservableField]` (現在値の表示)

クラスの **読み取り専用状態** を LiminalPalette UI と HTTP API に公開する属性。`ReactiveProperty<T>` または `Observable<T>` を直接受ける。

```csharp
public class Player : MonoBehaviour
{
    public ReactiveProperty<int> Hp { get; } = new(100);

    [ConsoleObservableField("Player/Health")]
    public ReactiveProperty<int> HpField => Hp;

    [ConsoleCommand("Player/Health/Set")]
    public void SetHealth(int value) => Hp.Value = value;
}
```

### UI 上の挙動

ユーザーが `Player/Health/Set` をパレットで選択 → 引数欄上部に "Current values" セクションが出て、`Player/Health: 75` のラベルが **R3 push 駆動で自動更新** される。値変更があるたびにラベルが書き換わる (polling ゼロ)。

prefix マッチで関連付け: 選択コマンドの Path から最後の `/` 以前の部分を prefix として取り、`[ConsoleObservableField]` の Path が同 prefix で始まるものを表示する。例:

| 選択コマンド | 表示される ObservableField (一致 prefix `Player/Health`) |
|---|---|
| `Player/Health/Set` | `Player/Health`、`Player/Health/Max` 等 |
| `Player/Mana/Set` | `Player/Mana` のみ (`Player/Health` は出ない) |

### 属性パラメータ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| `Path` | `string` | ✅ | 状態の識別子。コマンド Path と同じ階層で命名する |
| `Description` | `string` | — | UI / API 表示用の説明文 |

### 対応する型

- `R3.ReactiveProperty<T>` (推奨。`Subscribe` で初期値が即時 push される)
- `R3.Observable<T>` (Subscribe 後に値が来るまで現在値は null)

### HTTP API での取得

`GET /api/v1/state?path=Player/Health` で現在値を JSON で取れる ([ipc.md](ipc.md))。AI Agent が「コマンド実行前に状態確認 → 引数決定 → 実行」を行うのに使う。

### 注意点

- インスタンスメソッドコマンドと同じく、所属クラスは VContainer に登録されている必要がある
- `T` の値型は `TypeConverterRegistry.ToDisplayString` で文字列化できる型 (プリミティブ / Vector / Color など)
- 詳細は [integrations.md](integrations.md) と [ui.md](ui.md)

## パスの命名規則

慣例として:

- 名詞 / カテゴリで階層化: `Player/Health/Set`、`Enemy/Spawn`
- 大文字始まり (PascalCase)
- 検索時は大小区別なしでマッチするので、好きな大文字使いで OK
- 末尾は動詞の命令形が読みやすい (`Set`、`Reload`、`Reset`、`Toggle`、`Spawn`)

### 予約 prefix (Editor 専用扱い)

以下 2 つの prefix で始まるパスは「Editor 専用コマンド」として
`CommandDescriptor.IsEditorOnly` が自動的に true を返し、Play Mode /
Player ビルドのランタイムパレット UI から除外される。Editor 側 Window では
従来どおり表示される (登録自体は同じレジストリ)。

| prefix | 用途 |
|---|---|
| `Editor/...` | 利用側が手書きする Editor 専用コマンド (`EditorUtility` / `Selection` / `PlayerSettings` 等を使うもの) |
| `Menu/...` | `EditorMenuItemBootstrap` が Unity の `[MenuItem]` から自動収集する分。利用側は通常使わない |

ランタイムでも実行可能なコマンドにはこれらの prefix を付けないこと。
セーブデータパスを Finder で開く類は `Editor/Assets/Show Persistent Data`、
ランタイムでも叩きたい体力セットは `Player/Health/Set` のように分ける。

避けるべき:
- `My/Cmd/` (末尾スラッシュは例外)
- 空文字パス
- `` (Unit Separator) を含むパス (`PlayerPrefs` 永続化で使う区切り文字)

## 検索とソート

`PaletteController` がコマンドの検索 / ソートを行う:

1. **Filter (タブ)** で全コマンドを絞る
2. **FuzzyMatcher** で空でないクエリならスコアリング
3. **History boost** で過去実行済みのコマンドのスコアを +30
4. スコア降順 → Path 昇順でソート
5. 上限 `MaxResults = 100` で切り詰め

クエリが空のときは:
- 履歴順で先頭、続いて Path アルファベット順

## 関連ドキュメント

- [getting-started.md](getting-started.md) — 最初の動かし方
- [ui.md](ui.md) — Editor Window / Runtime UI の操作
- [extensibility.md](extensibility.md) — 任意型のサポート / カスタムエディタ
- [ipc.md](ipc.md) — HTTP API でコマンドを叩く
