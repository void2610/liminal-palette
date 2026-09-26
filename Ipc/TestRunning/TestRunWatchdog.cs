using System;

namespace Void2610.LiminalPalette.Ipc.TestRunning
{
    /// <summary>
    /// 「実行中」の印が残骸かどうかを判定する純粋関数。
    ///
    /// 状態の読み書き (SessionState) は編集時サブ asmdef 側が持ち、判定だけをここに置いて
    /// test-framework に依存せず単体テストできるようにする。
    /// </summary>
    public static class TestRunWatchdog
    {
        /// <summary>
        /// 実行が始まった後、この秒数どのコールバックも来なければ中断されたと見なす。
        /// テスト 1 件ごとに TestStarted / TestFinished が来るので、実行中なら必ず更新され続ける。
        /// 実測 (PlayMode 53 件で 155 秒) に対して十分な余裕を取り、単体で長いテストを誤検知しない値にする。
        /// </summary>
        public const double StaleAfterSeconds = 300.0;

        /// <summary>
        /// Execute を受け付けてから、この秒数 RunStarted が来なければ実行は始まらなかったと見なす。
        /// PlayMode は DomainReload と Play Mode 突入を挟んでから RunStarted が来るため、その所要時間に余裕を取る。
        /// </summary>
        public const double NotStartedAfterSeconds = 90.0;

        /// <summary>
        /// 実行中の印が残骸なら true。
        /// <paramref name="lastBeatUtc"/> は開始時と各コールバックで更新される最終生存時刻 (未記録なら null)。
        /// </summary>
        public static bool IsStale(bool running, bool runStarted, DateTime? lastBeatUtc, DateTime nowUtc)
        {
            if (!running) return false;
            // 開始時に必ず打つので、印が無い = 旧版が残した状態。復旧対象にする。
            if (lastBeatUtc == null) return true;
            var elapsed = (nowUtc - lastBeatUtc.Value).TotalSeconds;
            return elapsed > (runStarted ? StaleAfterSeconds : NotStartedAfterSeconds);
        }
    }
}
