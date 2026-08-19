using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// メソッドをデバッグコンソールのシナリオ (コマンドチェイン) として公開する属性。
    /// 対象メソッドは <see cref="ScenarioStep"/> の列を返す必要がある (戻り値型は
    /// IEnumerable&lt;ScenarioStep&gt; もしくはそれを実装するコレクション)。
    ///
    /// インスタンスメソッドの場合は VContainer 経由で解決される (<see cref="LiminalCommandAttribute"/> と同じ流儀)。
    ///
    /// 継承してプロジェクト固有のプリセット属性を定義できる。コンストラクタで
    /// <see cref="Scene"/> / <see cref="ReadyWhen"/> / <see cref="ReuseScene"/> / <see cref="Setup"/> 等の
    /// 既定値を設定した派生クラスを作れば、全シナリオで同じプロパティを繰り返すボイラープレートが消える
    /// (ScenarioScanner は基底型でこの属性を取得するため、派生属性もそのまま拾われる)。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class LiminalScenarioAttribute : Attribute
    {
        /// <summary>シナリオの識別子。"/" 区切りで階層を表現する (例: "Combat/EnemyTakesDamage")。</summary>
        public string Path { get; }

        /// <summary>UI / API 表示用の説明文。</summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// 任意。指定するとシナリオ実行直前に SceneManager.LoadSceneAsync(Scene, Single) が自動で走り、
        /// 完了後にステップ本体に進む。「テストごとに専用シーンで実行したい」「テスト間で状態を漏らさない」
        /// 用途。PlayMode 専用 (Edit Mode では LoadScene ステップが失敗してシナリオごと fail)。
        /// シナリオ完了後のシーン復帰は行わない (= 最後にロードされたシーンが残る)。
        /// </summary>
        public string Scene { get; set; } = "";

        /// <summary>
        /// 任意。true にすると、<see cref="Scene"/> が既にアクティブなシーンなら LoadScene を省略する。
        /// シーンの再ロードを毎回払わずに済む代わりに前のシナリオの状態が残るため、<see cref="Setup"/> で
        /// ゲーム側の初期化コマンド (例: 新規ラン開始) を走らせて状態を戻す前提で使う。
        /// </summary>
        public bool ReuseScene { get; set; }

        /// <summary>任意。"観測パス=期待値" 形式 (例: "Game/State=WorldMap")。Scene ロード後に AssertEventually を自動挿入し、条件成立まで本体ステップの開始を遅延する。</summary>
        public string ReadyWhen { get; set; } = "";

        /// <summary>
        /// 任意。本体ステップの前に引数なしで実行するコマンド path (例: "Run/StartNew")。
        /// 実ロード時は ReadyWhen 成立後、<see cref="ReuseScene"/> でロードを省略した時は ReadyWhen より先に走る
        /// (前シナリオの終了状態では ReadyWhen が成立しないため)。シーンを使い回しつつ各シナリオを既知の状態から始めるための入口。
        /// </summary>
        public string Setup { get; set; } = "";

        /// <summary>任意。0 より大きければシナリオ実行中だけ Time.timeScale をこの値にし、終了時 (失敗・キャンセル含む) に必ず元の値へ復元する。</summary>
        public float TimeScale { get; set; }

        public LiminalScenarioAttribute(string path) => Path = path;
    }
}
