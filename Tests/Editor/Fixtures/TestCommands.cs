using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Void2610.LiminalPalette.Tests
{
    /// <summary>
    /// 各テストから参照する [ConsoleCommand] 付き static メソッド群。
    /// パスは "Test/..." で始め、製品コードのコマンドと衝突しないようにしている。
    /// </summary>
    internal static class TestCommands
    {
        [ConsoleCommand("Test/Int", Aliases = new[] { "Test/I" }, Description = "int + default int")]
        public static int IntCommand(int a, int b = 10) => a + b;

        [ConsoleCommand("Test/Throws", Description = "throws to verify failure handling")]
        public static void Throws() => throw new InvalidOperationException("boom");

        [ConsoleCommand("Test/Vector", Description = "vector roundtrip")]
        public static Vector3 Vec(Vector3 v) => v * 2f;

        [ConsoleCommand("Test/Log", Description = "writes a log to verify capture")]
        public static void Log(string msg) => Debug.Log(msg);
        [ConsoleCommand("Test/NoArg", Description = "no-arg command for tests")]
        public static void NoArg() { }

        [ConsoleCommand("Test/Async", Description = "async command")]
        public static async Task<string> AsyncCommand(string s)
        {
            await Task.Yield();
            return s.ToUpperInvariant();
        }
    }

    // 非属性メソッド: Scanner が無視することを確認する用途。
    internal static class NonCommands
    {
        public static void Plain() { }
    }
}
