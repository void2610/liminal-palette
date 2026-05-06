using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Tests.UI
{
    public sealed class PaletteControllerTests
    {
        // ---- Fakes ----

        // 実 CommandRegistry を内部に持って委譲するだけのファサード。
        // PaletteController は ICommandRegistry のインタフェースしか触らないので、
        // event の発火確認が不要なここでは CommandRegistry をそのまま流用する方がシンプル。
        private sealed class FakeRegistry : ICommandRegistry
        {
            private readonly CommandRegistry _inner = new CommandRegistry();
            public IReadOnlyList<CommandDescriptor> All => _inner.All;
            public CommandDescriptor Find(string p) => _inner.Find(p);
            public IEnumerable<CommandDescriptor> FindByCategory(string p) => _inner.FindByCategory(p);
            public void Register(CommandDescriptor d) => _inner.Register(d);
            public bool Unregister(string p) => _inner.Unregister(p);
            public void Clear() => _inner.Clear();
            public event Action<CommandDescriptor> Registered { add { _inner.Registered += value; } remove { _inner.Registered -= value; } }
            public event Action<CommandDescriptor> Unregistered { add { _inner.Unregistered += value; } remove { _inner.Unregistered -= value; } }
        }

        // 呼び出し回数と最終引数を記録する偽 Executor。
        private sealed class FakeExecutor : ICommandExecutor
        {
            public int CallCount;
            public string LastPath;
            public IReadOnlyDictionary<string, object> LastTypedArgs;
            public CommandResult NextResult = CommandResult.Ok(null, Array.Empty<LogEntry>(), TimeSpan.Zero);

            public Task<CommandResult> ExecuteAsync(string p, IReadOnlyDictionary<string, string> a, CancellationToken c = default)
                => throw new NotImplementedException();
            public Task<CommandResult> ExecuteAsync(string p, IReadOnlyList<string> a, CancellationToken c = default)
                => throw new NotImplementedException();

            public Task<CommandResult> ExecuteWithTypedArgsAsync(string p, IReadOnlyDictionary<string, object> a, CancellationToken c = default)
            {
                CallCount++;
                LastPath = p;
                LastTypedArgs = a;
                return Task.FromResult(NextResult);
            }
        }

        // ---- Helpers ----

        // 実 MethodInfo は不要なので適当な static method を割り当てるだけ。
        private static readonly MethodInfo _dummyMethod = typeof(PaletteControllerTests).GetMethod(
            nameof(DummyCommand), BindingFlags.NonPublic | BindingFlags.Static);

        private static void DummyCommand() { }

        private static CommandDescriptor MakeDescriptor(string path, params string[] aliases)
        {
            return new CommandDescriptor(
                path: path,
                description: "",
                aliases: aliases ?? Array.Empty<string>(),
                parameters: Array.Empty<ParameterDescriptor>(),
                returnType: typeof(void),
                isAsync: false,
                method: _dummyMethod);
        }

        private static (PaletteController controller, FakeRegistry reg, FakeExecutor exec, InMemoryCommandHistory hist)
            CreateController(params string[] paths)
        {
            var reg = new FakeRegistry();
            foreach (var p in paths) reg.Register(MakeDescriptor(p));
            var exec = new FakeExecutor();
            var hist = new InMemoryCommandHistory();
            return (new PaletteController(reg, exec, hist), reg, exec, hist);
        }

        // ---- Tests ----

        [Test]
        public void EmptyQuery_ReturnsAllInAlphabeticalOrder_WhenNoHistory()
        {
            var (c, _, _, _) = CreateController("Player/Health/Set", "Enemy/Spawn", "Audio/Play");
            // 初期状態 (空クエリ) で Results が全件入っているはず。
            Assert.AreEqual(3, c.Results.Count);
            // アルファベット順。
            Assert.AreEqual("Audio/Play", c.Results[0].Descriptor.Path);
            Assert.AreEqual("Enemy/Spawn", c.Results[1].Descriptor.Path);
            Assert.AreEqual("Player/Health/Set", c.Results[2].Descriptor.Path);
        }

        [Test]
        public void EmptyQuery_HistoryItems_AppearFirst()
        {
            var (c, _, _, hist) = CreateController("A/X", "B/Y", "C/Z");
            hist.Record("C/Z");
            hist.Record("A/X");

            // SetQuery で再計算をトリガー (Reset でも同等)。
            c.SetQuery("");

            // 履歴順 (新しい順) → "A/X", "C/Z" → 残り "B/Y"
            Assert.AreEqual("A/X", c.Results[0].Descriptor.Path);
            Assert.IsTrue(c.Results[0].FromHistory);
            Assert.AreEqual("C/Z", c.Results[1].Descriptor.Path);
            Assert.IsTrue(c.Results[1].FromHistory);
            Assert.AreEqual("B/Y", c.Results[2].Descriptor.Path);
            Assert.IsFalse(c.Results[2].FromHistory);
        }

        [Test]
        public void Query_FiltersToFuzzyMatchesOnly()
        {
            var (c, _, _, _) = CreateController("Player/Health/Set", "Enemy/Spawn", "Audio/Play");
            c.SetQuery("phs");
            Assert.AreEqual(1, c.Results.Count);
            Assert.AreEqual("Player/Health/Set", c.Results[0].Descriptor.Path);
        }

        [Test]
        public void Query_HistoryBoost_RaisesScoreOfRecentCommand()
        {
            // 同じくらいマッチしうる 2 件で、片方を履歴に入れる。
            var (c, _, _, hist) = CreateController("Set/Foo", "Set/Bar");
            hist.Record("Set/Bar");
            c.SetQuery("set");

            // どちらもマッチする。Set/Bar が履歴ブーストで先頭に来る。
            Assert.AreEqual(2, c.Results.Count);
            Assert.AreEqual("Set/Bar", c.Results[0].Descriptor.Path);
            Assert.IsTrue(c.Results[0].FromHistory);
        }

        [Test]
        public void MoveSelection_ClampsAtBoundsAndDoesNotLoop()
        {
            var (c, _, _, _) = CreateController("A/X", "B/Y", "C/Z");
            Assert.AreEqual(0, c.SelectedIndex);

            c.MoveSelection(-1); // 上端にいるので変化なし
            Assert.AreEqual(0, c.SelectedIndex);

            c.MoveSelection(10); // 下方向に大きく動かしても末尾で止まる
            Assert.AreEqual(2, c.SelectedIndex);

            c.MoveSelection(1); // 末尾を超えない
            Assert.AreEqual(2, c.SelectedIndex);
        }

        [Test]
        public async Task ExecuteSelected_CallsExecutorWithTypedArgs_AndUpdatesLastResult()
        {
            var (c, _, exec, _) = CreateController("Player/Health/Set");
            var args = new Dictionary<string, object> { ["value"] = 100 };
            exec.NextResult = CommandResult.Ok(42, Array.Empty<LogEntry>(), TimeSpan.FromMilliseconds(5));

            var r = await c.ExecuteSelectedAsync(args);

            Assert.AreEqual(1, exec.CallCount);
            Assert.AreEqual("Player/Health/Set", exec.LastPath);
            Assert.AreSame(args, exec.LastTypedArgs);
            Assert.AreEqual(42, r.Value);
            Assert.AreSame(r, c.LastResult);
        }

        [Test]
        public async Task ExecuteSelected_RecordsToHistory_OnSuccess()
        {
            var (c, _, _, hist) = CreateController("A/X", "B/Y");
            await c.ExecuteSelectedAsync(new Dictionary<string, object>());
            Assert.IsTrue(hist.Contains("A/X"));
        }

        [Test]
        public async Task ExecuteSelected_RecordsToHistory_EvenOnFailure()
        {
            var (c, _, exec, hist) = CreateController("A/X");
            exec.NextResult = CommandResult.Fail("boom", null, Array.Empty<LogEntry>(), TimeSpan.Zero);
            await c.ExecuteSelectedAsync(new Dictionary<string, object>());
            // 失敗でも履歴には残す (UX 上「最近試したコマンド」を提示するため)。
            Assert.IsTrue(hist.Contains("A/X"));
        }

        [Test]
        public async Task ExecuteSelected_NoSelection_ReturnsFailWithoutCallingExecutor()
        {
            var (c, _, exec, _) = CreateController(); // 空 registry
            var r = await c.ExecuteSelectedAsync(new Dictionary<string, object>());
            Assert.IsFalse(r.Success);
            Assert.AreEqual(0, exec.CallCount);
        }

        [Test]
        public void Reset_ClearsQuerySelectionAndLastResult()
        {
            var (c, _, _, _) = CreateController("A/X", "B/Y");
            c.SetQuery("x");
            c.MoveSelection(1);

            c.Reset();
            Assert.AreEqual("", c.Query);
            Assert.AreEqual(0, c.SelectedIndex);
            Assert.IsNull(c.LastResult);
        }

        [Test]
        public void StateChanged_FiresOnSetQuery_AndOnExecute()
        {
            var (c, _, _, _) = CreateController("A/X");
            var fireCount = 0;
            c.StateChanged += () => fireCount++;

            c.SetQuery("a");
            Assert.GreaterOrEqual(fireCount, 1);
            var beforeExecute = fireCount;
            c.ExecuteSelectedAsync(new Dictionary<string, object>()).Wait();
            Assert.Greater(fireCount, beforeExecute);
        }

        [Test]
        public void SetFilter_NarrowsResults_ToOnlyMatchingDescriptors()
        {
            var (c, _, _, _) = CreateController("Player/Health/Set", "Enemy/Spawn", "Audio/Play");
            // Player カテゴリに絞る Filter を設定。
            c.SetFilter("Player", d => d.Path.StartsWith("Player/", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(1, c.Results.Count);
            Assert.AreEqual("Player/Health/Set", c.Results[0].Descriptor.Path);
            Assert.AreEqual("Player", c.FilterLabel);
        }

        [Test]
        public void SetFilter_CombinesWithQuery_ApplyingFilterBeforeFuzzy()
        {
            var (c, _, _, _) = CreateController("Player/Health/Set", "Player/Score/Set", "Enemy/Set");
            // Filter で Player のみに絞った上で fuzzy "set" を当てる。Enemy/Set はフィルタで除外される。
            c.SetFilter("Player", d => d.Path.StartsWith("Player/", StringComparison.OrdinalIgnoreCase));
            c.SetQuery("set");
            Assert.AreEqual(2, c.Results.Count);
            Assert.IsTrue(c.Results.All(r => r.Descriptor.Path.StartsWith("Player/")));
        }

        [Test]
        public void SetFilter_ResetsSelectedIndex()
        {
            var (c, _, _, _) = CreateController("A/X", "B/Y", "C/Z");
            c.MoveSelection(2);
            Assert.AreEqual(2, c.SelectedIndex);
            // Filter を変えると選択は先頭に戻る。
            c.SetFilter("B-only", d => d.Path.StartsWith("B/", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(0, c.SelectedIndex);
        }

        [Test]
        public void SetFilter_NullRestoresAll()
        {
            var (c, _, _, _) = CreateController("Player/Health/Set", "Enemy/Spawn");
            c.SetFilter("Player", d => d.Path.StartsWith("Player/", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(1, c.Results.Count);
            // null filter は全件通す。FilterLabel は "All" に戻る。
            c.SetFilter(null, null);
            Assert.AreEqual(2, c.Results.Count);
            Assert.AreEqual("All", c.FilterLabel);
        }

        [Test]
        public void Results_AreCappedAtMaxResults()
        {
            // 上限 + 余分にコマンドを登録して、Results が上限以下に切り詰められること。
            var paths = Enumerable.Range(0, PaletteController.MaxResults + 20)
                .Select(i => $"Cat/Cmd{i:D4}").ToArray();
            var (c, _, _, _) = CreateController(paths);
            c.SetQuery("c"); // 大量にマッチする
            Assert.LessOrEqual(c.Results.Count, PaletteController.MaxResults);
        }
    }
}
