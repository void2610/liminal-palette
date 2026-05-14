# Integrations

LiminalPalette は **R3 + VContainer 必須**。本章では両方をどう統合するかを示す。
(Phase 4 までは依存ゼロ路線だったが、Phase 5a で利用側のコード量最小化を優先して必須化に方針転換した)

---

## 必須パッケージ

`Packages/manifest.json` に両方が入っていること:

```json
{
  "dependencies": {
    "com.cysharp.r3": "https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity",
    "jp.hadashikick.vcontainer": "1.x.y"
  }
}
```

R3 は `ReactiveProperty<T>` / `Observable<T>` を、VContainer は `IObjectResolver` でインスタンス解決を提供する。

---

## VContainer: 1 行で接続

利用側 `LifetimeScope` の `Configure` に **`RegisterEntryPoint<LiminalPaletteEntryPoint>()` を 1 行**書くだけで、コンテナ内で登録された全型がインスタンスメソッド `[LiminalCommand]` から解決可能になる。

```csharp
using VContainer;
using VContainer.Unity;
using Void2610.LiminalPalette.Integration.VContainer;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // ゲームのクラス登録
        builder.RegisterComponentInHierarchy<Player>();
        builder.RegisterComponentInHierarchy<Enemy>();
        builder.Register<TimeManager>(Lifetime.Singleton);

        // LiminalPalette 統合 ← これだけ
        builder.RegisterEntryPoint<LiminalPaletteEntryPoint>();
    }
}
```

`LiminalPaletteEntryPoint` は `IInitializable` で、VContainer の Initialize 段階で `LiminalPalette.SetInstanceResolver(new VContainerInstanceResolver(container))` を呼んで自身を resolver として登録する。これでインスタンスメソッド `[LiminalCommand]` と `[LiminalObservableField]` の両方が VContainer 経由で動く。

### 解決の挙動

`builder.RegisterComponentInHierarchy<Player>()` で登録された `Player` は、`[LiminalCommand]` のインスタンスメソッドが叩かれるたびに `IObjectResolver.Resolve(typeof(Player))` で取得される。VContainer は通常シングルトンを返すため、毎回同じインスタンスが使われる。

未登録の型に対しては `VContainerException` が投げられるが、`VContainerInstanceResolver` がそれを catch して null を返し、`CommandExecutor` 側で「Instance not resolved」エラーに変換する (利用者向けの明確なエラーメッセージ付き)。

---

## R3: ReactiveProperty を直接公開

`[LiminalObservableField]` 属性を `ReactiveProperty<T>` または `Observable<T>` のプロパティ / フィールドに付与する。UI が R3 push 駆動で値変更を即時反映する。

```csharp
using R3;
using UnityEngine;
using Void2610.LiminalPalette;

public class Player : MonoBehaviour
{
    public ReactiveProperty<int> Hp { get; } = new(100);

    [LiminalObservableField("Player/Health")]
    public ReactiveProperty<int> HpField => Hp;

    [LiminalCommand("Player/Health/Set")]
    public void SetHealth(int value) => Hp.Value = value;
}
```

UI 上の挙動:
1. ユーザーが `Player/Health/Set` をパレットで選択
2. UI が同 prefix `Player/Health` を持つ `[LiminalObservableField]` を検索 → `HpField` (= `Hp`) を発見
3. `Hp.Subscribe(v => label.text = $"Current: {v}")` で R3 購読
4. `Hp.Value = X` するたびに UI のラベルが自動更新 (polling 不要、フレーム毎の負荷ゼロ)
5. ユーザーが別コマンドを選択すると旧購読が `IDisposable.Dispose()` で解除される

### 同一クラス内に複数のフィールドがあっても OK

```csharp
public class Player : MonoBehaviour
{
    public ReactiveProperty<int> Hp { get; } = new(100);
    public ReactiveProperty<int> Mp { get; } = new(50);
    public ReactiveProperty<Vector3> Position { get; } = new(Vector3.zero);

    [LiminalObservableField("Player/Health")]
    public ReactiveProperty<int> HpField => Hp;

    [LiminalObservableField("Player/Mana")]
    public ReactiveProperty<int> MpField => Mp;

    [LiminalObservableField("Player/Position")]
    public ReactiveProperty<Vector3> PosField => Position;

    [LiminalCommand("Player/Health/Set")]
    public void SetHealth(int value) => Hp.Value = value;

    [LiminalCommand("Player/Mana/Set")]
    public void SetMana(int value) => Mp.Value = value;
}
```

`Player/Health/Set` を選んだとき "Current values" に `Player/Health: 75` だけが出る (prefix が一致するもののみ)。`Player/Mana/Set` なら `Player/Mana: 25` だけ。

---

## HTTP API での状態取得

`GET /api/v1/state?path=Player/Health` で現在値スナップショットを取れる ([ipc.md](ipc.md) 参照):

```bash
TOKEN=$(cat ~/.liminal-palette/token)
curl -s -H "Authorization: Bearer $TOKEN" \
     "http://127.0.0.1:7610/api/v1/state?path=Player/Health"
# → {"path":"Player/Health","value":"75","type":"Int32"}
```

これで AI Agent が「コマンド実行前に現在値を確認 → 引数を決めて実行」のフローを取れる。

---

## 未登録時のエラーメッセージ

VContainer に登録していないクラスのインスタンスメソッドを叩くと、コマンド結果に明確なエラーが返る:

```json
{
  "success": false,
  "error": "Instance not resolved for MyGame.Player. Register the type with VContainer (e.g. builder.RegisterComponentInHierarchy<T>()) and call builder.RegisterEntryPoint<LiminalPaletteEntryPoint>() in your LifetimeScope.",
  "exceptionType": "System.InvalidOperationException",
  ...
}
```

UI の Status 行にも同じメッセージが出る。

---

## R3 / VContainer なしで使えないか

ライブラリの方針として **両方必須**。Core asmdef が `R3.Unity` を直接 references に持ち、`Integration.VContainer` asmdef も VContainer を required にしている。両方未導入のプロジェクトでは LiminalPalette 自体がコンパイルできない。

理由:
- 利用側が R3 + VContainer 慣れている前提なら、抽象を挟むより直接型を使う方が圧倒的に書きやすい (`.AsObservable()` の儀式不要、`SetInstanceResolver` 手書き不要)
- 本リポジトリは中規模ゲーム開発前提で R3 + VContainer 導入済み
- 将来 OSS 配布時に必要なら `defineConstraints` で隔離する選択肢を残す (Phase 3 の `Runtime.InputSystem` パターン)

利用側で R3 / VContainer を使わない選択肢を残したいなら、Phase 4 までの状態 (commit `e081958` 時点) を fork して使う。

---

## 関連ドキュメント

- [commands.md](commands.md) — `[LiminalCommand]` / `[LiminalObservableField]` の詳細仕様
- [ui.md](ui.md) — Current values セクションの UI 挙動
- [ipc.md](ipc.md) — `GET /api/v1/state` API
- [asmdef.md](asmdef.md) — `Integration.VContainer` asmdef 構成
- [extensibility.md](extensibility.md) — `IInstanceResolver` / `IObservableFieldRegistry` の差し替え
