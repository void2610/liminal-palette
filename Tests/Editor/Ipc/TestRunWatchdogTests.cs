using System;
using NUnit.Framework;
using Void2610.LiminalPalette.Ipc.TestRunning;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    /// <summary>
    /// 実行中の印を残骸と見なす判定 (<see cref="TestRunWatchdog"/>) の単体テスト。
    /// </summary>
    public sealed class TestRunWatchdogTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);

        private static DateTime? SecondsAgo(double seconds) => Now.AddSeconds(-seconds);

        [Test]
        public void NotRunning_IsNeverStale()
        {
            Assert.IsFalse(TestRunWatchdog.IsStale(false, false, null, Now));
            Assert.IsFalse(TestRunWatchdog.IsStale(false, true, SecondsAgo(10000), Now));
        }

        [Test]
        public void RunningWithoutHeartbeat_IsStale()
        {
            Assert.IsTrue(TestRunWatchdog.IsStale(true, true, null, Now));
        }

        [Test]
        public void NotStarted_IsStaleAfterNotStartedTimeout()
        {
            Assert.IsFalse(TestRunWatchdog.IsStale(true, false, SecondsAgo(TestRunWatchdog.NotStartedAfterSeconds - 1), Now));
            Assert.IsTrue(TestRunWatchdog.IsStale(true, false, SecondsAgo(TestRunWatchdog.NotStartedAfterSeconds + 1), Now));
        }

        [Test]
        public void Started_KeepsLongerGraceThanNotStarted()
        {
            // 始まった実行は単体で長いテストがあり得るので、開始待ちの猶予を過ぎても倒さない
            var between = (TestRunWatchdog.NotStartedAfterSeconds + TestRunWatchdog.StaleAfterSeconds) / 2;
            Assert.IsFalse(TestRunWatchdog.IsStale(true, true, SecondsAgo(between), Now));
            Assert.IsTrue(TestRunWatchdog.IsStale(true, true, SecondsAgo(TestRunWatchdog.StaleAfterSeconds + 1), Now));
        }
    }
}
