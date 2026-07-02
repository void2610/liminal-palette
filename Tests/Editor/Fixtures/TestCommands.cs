using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Void2610.LiminalPalette.Tests
{
    /// <summary>
    /// 各テストから参照する [LiminalCommand] 付き static メソッド群。
    /// パスは "Test/..." で始め、製品コードのコマンドと衝突しないようにしている。
    /// </summary>
    internal static class TestCommands
    {
        [LiminalCommand("Test/Int", Aliases = new[] { "Test/I" }, Description = "int + default int")]
        public static int IntCommand(int a, int b = 10) => a + b;

        [LiminalCommand("Test/Throws", Description = "throws to verify failure handling")]
        public static void Throws() => throw new InvalidOperationException("boom");

        [LiminalCommand("Test/Vector", Description = "vector roundtrip")]
        public static Vector3 Vec(Vector3 v) => v * 2f;

        [LiminalCommand("Test/Log", Description = "writes a log to verify capture")]
        public static void Log(string msg) => Debug.Log(msg);
        [LiminalCommand("Test/NoArg", Description = "no-arg command for tests")]
        public static void NoArg() { }

        [LiminalCommand("Test/Async", Description = "async command")]
        public static async Task<string> AsyncCommand(string s)
        {
            await Task.Yield();
            return s.ToUpperInvariant();
        }

        [LiminalCommand("Test/UniTaskVoid", Description = "UniTask (non-generic) command")]
        public static async UniTask UniTaskVoidCommand()
        {
            await UniTask.Yield();
        }

        [LiminalCommand("Test/UniTaskString", Description = "UniTask<T> command")]
        public static async UniTask<string> UniTaskStringCommand(string s)
        {
            await UniTask.Yield();
            return s.ToUpperInvariant();
        }
    }

    // 非属性メソッド: Scanner が無視することを確認する用途。
    internal static class NonCommands
    {
        public static void Plain() { }
    }
}
