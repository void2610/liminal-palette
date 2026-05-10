# LP `/execute` Type Conversion — 完全リファレンス

LP の `TypeConverterRegistry` は `lp exec` で送られた `args` (全 string) をターゲット型へ変換する。本ドキュメントは各型の受理フォーマット、寛容度、失敗メッセージを網羅する。

> 共通: **数値・bool・enum も含めて全引数を string で送る**。`lp exec name=value` は内部で `{"value":"<v>"}` の形で送るので何も意識せずに済む。直接 JSON を組み立てる場合 (例: `lp run --steps -` の中) も string でクォートしておくと将来の挙動変更を避けられる。
>
> 以下の例の JSON はワイヤーフォーマット (LP が受け取る側の形)。`lp exec` から送る場合は `name=value` の `value` 部分にそのまま書けばよい (例: JSON `{"v3":"1,2,3"}` は `lp exec ... v3=1,2,3` に対応)。

---

## Primitive (`PrimitiveConverter`)

### 対応型

`bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, `char`, `string`

### 数値 (整数 / 浮動小数)

```json
{"i": "42", "neg": "-100", "hex_NG": "0x2A"}
```

- `InvariantCulture` でパース (小数点は `.` 固定、`,` は不可)
- 16進リテラル (`0x...`) は **非対応**
- 範囲外 (例: int に `99999999999`) は `OverflowException`

### bool

```json
{"b": "true"}
{"b": "false"}
{"b": "True"}    // 大小無視
{"b": "FALSE"}
```

`yes`/`no`/`1`/`0` は **非対応**。`bool.TryParse` ベース。

### char

```json
{"c": "A"}      // 1 文字のみ
{"c": "AB"}     // ✗ 失敗 ("Cannot convert 'AB' to Char")
```

### string

そのまま渡される。空文字 `""` も valid。

### よくある失敗

| 入力 | エラー |
|---|---|
| `"3,14"` (int に) | カンマ含むため失敗 |
| `"  100  "` (int に) | Trim はされない (実装依存)。確実にしたい場合は前処理で `.trim()` |
| `"true"` (int に) | 失敗 |
| `null` (どの型でも) | "Cannot convert null to <Type>" |

---

## Vector (`VectorConverter`)

### 対応型

`Vector2`, `Vector3`, `Vector4`, `Vector2Int`, `Vector3Int`

### 受理形式

カンマ・空白・タブいずれも区切り。括弧類 `()` `[]` `{}` は許容 (剥がされる)。

```json
{"v3": "1,2,3"}
{"v3": "1, 2, 3"}
{"v3": "(1,2,3)"}
{"v3": "[1 2 3]"}
{"v3": "{1\t2\t3}"}
{"v2": "0.5, -0.5"}
{"v3i": "10,20,30"}
{"v4": "1,0,0,1"}
```

### 要素数

- `Vector2`: 2 要素必須
- `Vector3`: 3 要素必須
- `Vector4`: 4 要素必須

要素数違いは `"Cannot parse '<raw>' as Vector3 (expected 3 components, got N)"` で失敗。

### 整数版

`Vector2Int` / `Vector3Int` は要素ごとに `int.Parse`。小数を含むと失敗:

```json
{"v3i": "1.5, 2, 3"}   // ✗ 失敗
```

---

## Color (`ColorConverter`)

### 対応型

`Color` (0..1 範囲), `Color32` (0..255 範囲)

### HEX 表記

`#` から始まる場合は Unity の `ColorUtility.TryParseHtmlString` 経由:

```json
{"c": "#FF8800"}      // RGB
{"c": "#FF8800CC"}    // RGBA
{"c": "#F80"}         // 短縮形 (RGB)
```

⚠️ Unity 標準色名 (`"red"`, `"blue"`) は `#` なしでは弾かれる。**必ず `#` 付き HEX か数値で送る**。

### 数値表記

カンマ・空白区切り。括弧 `()` `[]` `{}` 許容。要素数 3 か 4。

#### Color (0..1 範囲)

```json
{"c": "1, 0.53, 0"}        // RGB (alpha=1.0 自動補完)
{"c": "1, 0.53, 0, 0.5"}   // RGBA
```

#### Color32 (0..255 範囲、byte に切り詰め)

```json
{"c": "255, 136, 0"}       // RGB (alpha=255 自動補完)
{"c": "255, 136, 0, 128"}  // RGBA
```

範囲外 (Color32 で 300 等) は `Mathf.Clamp(0, 255)` でクランプされる。

### 失敗メッセージ

| 入力 | エラー |
|---|---|
| `"red"` (Color に) | "Cannot parse 'red' as color (invalid hex)" |
| `"1,2"` (Color に) | "expected 3 or 4 components, got 2" |
| `"#GGGGGG"` | "Cannot parse '#GGGGGG' as color (invalid hex)" |

---

## Enum (`EnumConverter`)

### 受理形式

#### 名前指定 (大小無視)

```json
{"dir": "Up"}
{"dir": "up"}
{"dir": "UP"}
```

`Enum.TryParse(..., ignoreCase: true)` ベース。

#### 数値指定

```json
{"dir": "0"}    // 0 → 列挙の最初の値 (Direction.None 等)
```

整数文字列も valid。範囲外の数値は **valid** として扱われる (C# の enum は任意の整数値を取れるため)。

#### `[Flags]` Enum (カンマ区切り)

```csharp
[Flags]
public enum Permission { None = 0, Read = 1, Write = 2, Execute = 4 }
```

```json
{"perm": "Read"}
{"perm": "Read,Write"}        // OR で合成
{"perm": "Read, Write"}       // 空白許容
{"perm": "3"}                 // 数値での合成 (1+2=3)
```

### `[Choices]` 属性付きの場合

`/api/v1/commands` の `parameters[].choices` が空でない時は **そこから選ぶ**。choices 外の値は valid な enum 値であっても弾かれる:

```json
// parameters: [{"name":"type", "type":"EnemyType", "choices":["Goblin","Orc"]}]
{"type": "Goblin"}    // ✓
{"type": "Slime"}     // ✗ ("'Slime' is not a valid choice for parameter 'type'")
```

### 失敗メッセージ

| 入力 | エラー |
|---|---|
| `"Foo"` (該当なし) | "Cannot convert 'Foo' to <EnumType>" |
| `"Read|Write"` ([Flags] にパイプ) | "|" は区切りとして非対応。カンマで送る |

---

## UnityEngine.Object 派生 (`UnityObjectConverter`)

`GameObject`, `Component`, `Texture`, `AudioClip` 等。HTTP 経由はサポートが限定的。

### 受理形式

| 形式 | 用途 | 制限 |
|---|---|---|
| `"@<entityID>"` | `Resources.EntityIdToObject` で解決 | UI ピッカーで取得した ID 前提 (CLI から組み立てるのは現実的でない) |
| `"GameObject:<name>"` | シーン上の GameObject 名前検索 | **Runtime 限定**。`GameObject.Find(name)` ベース |
| `"<name>"` (フォールバック) | (未対応) | 現状はエラー |

### Runtime 名前検索の例

```json
{"target": "GameObject:Player"}
{"target": "GameObject:Enemies/Goblin01"}    // パス指定可
```

### 推奨パターン

CLI / HTTP から `UnityEngine.Object` 引数を送るのは難しい。**利用側で「名前で解決して内部で UnityEngine.Object に変換するファサードコマンド」を `[LiminalCommand]` で書く** のが筋:

```csharp
// 利用側コード
[LiminalCommand("Player/Equip")]
public void Equip(string itemName) {
    var item = ItemDatabase.Find(itemName);
    if (item == null) throw new ArgumentException($"Item not found: {itemName}");
    _player.Equip(item);
}
```

これなら CLI 側は `lp exec Player/Equip itemName=IronSword` のような string で済む。

---

## 失敗時の `result.error` の読み方

```json
{
  "success": false,
  "value": null,
  "error": "Cannot parse '1,2' as Vector3 (expected 3 components, got 2)",
  "exceptionType": null,
  "stackTrace": null,
  "durationMs": 0.12,
  "logs": []
}
```

| パターン | 解釈 |
|---|---|
| `error: "..."` + `exceptionType: null` | **引数バインド失敗** (型変換段階)。`lp exec ... --json` で詳細を見て値を見直す |
| `error: "..."` + `exceptionType: "System.X"` | **コマンド実行中に例外** が投げられた。stackTrace で原因確認 |
| `error: "Required parameter '<name>' is missing"` | `name=value` のキー名が parameters[].name と一致していない (typo / 大小違い) |
| `error: "'<value>' is not a valid choice for parameter '<name>'"` | choices 制約違反。`lp commands --json` で valid 値を確認 |

---

## 高度な話題

### Custom TypeConverter の追加

利用側で独自型 (例: `ItemId` 構造体) を引数に取りたい場合、`ITypeConverter` を実装して登録する:

```csharp
public sealed class ItemIdConverter : ITypeConverter {
    public bool CanConvert(Type t) => t == typeof(ItemId);
    public bool TryFromString(string raw, Type t, out object value, out string error) {
        // ... raw を ItemId に変換
    }
    public string ToDisplayString(object value) => value?.ToString() ?? "";
}

[InitializeOnLoadMethod]
static void RegisterConverters() {
    Void2610.LiminalPalette.TypeConverterRegistry.Default.Register(new ItemIdConverter());
}
```

これで `args` で `ItemId` 型の引数も string として送れるようになる。

### TypeConverter のフォールバック順

LP は登録順序の **逆順** に `CanConvert(t)` を試す。最後に登録された Converter が優先される。Primitive / Vector / Color / Enum / UnityObject の標準 5 つは LP 側で先に登録されるので、ユーザー Converter のほうが優先される。

### 失敗時のリトライ戦略 (AI Agent 向け)

1. `lp exec ... --json` の `success: false` + `error` を読む (装飾出力でも `failed` + `error` 行が出る)
2. 「引数バインド失敗」(exceptionType: null) なら → `lp commands --filter <prefix>` で正しい型を確認 → 値を修正して再実行
3. 「実行中の例外」(exceptionType: 非 null) なら → stackTrace を読む → 利用側のコード修正が必要なケースが多い → ユーザに報告
