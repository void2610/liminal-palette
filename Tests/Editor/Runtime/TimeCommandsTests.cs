using NUnit.Framework;
using UnityEngine;
using Void2610.LiminalPalette.Runtime;

namespace Void2610.LiminalPalette.Tests.Runtime
{
    /// <summary>
    /// `TimeCommands.SetScale` / `Reset` / `Pause` / `Resume` の動作と、
    /// 観測フィールド `Time/Scale` (= TimeCommands.Scale ReactiveProperty) の即時同期を検証する。
    /// Time.timeScale はグローバル状態なので、各テストで [TearDown] により必ず 1f に戻して
    /// 他テストへ漏れさせない (Poller は PlayMode 以外では動かないため EditMode テストでは
    /// SetScale/Reset/etc. の直接呼び出しが Scale ReactiveProperty に即時反映される経路だけを検証)。
    /// </summary>
    public sealed class TimeCommandsTests
    {
        [TearDown]
        public void TearDown()
        {
            // テスト間で timeScale が残らないように明示的に等速へ戻す。
            Time.timeScale = 1f;
        }

        [Test]
        public void SetScale_AppliesValueToTimeTimeScale()
        {
            TimeCommands.SetScale(2.5f);
            Assert.AreEqual(2.5f, Time.timeScale, 0.0001f);
        }

        [Test]
        public void SetScale_UpdatesObservableScaleImmediately()
        {
            // ApplyScale 内で _scale.Value も即時書き込む経路 (Poller を待たない) の検証。
            TimeCommands.SetScale(3f);
            Assert.AreEqual(3f, TimeCommands.Scale.Value, 0.0001f);
        }

        [Test]
        public void Reset_RestoresTimeScaleToOne()
        {
            TimeCommands.SetScale(5f);
            TimeCommands.Reset();
            Assert.AreEqual(1f, Time.timeScale, 0.0001f);
            Assert.AreEqual(1f, TimeCommands.Scale.Value, 0.0001f);
        }

        [Test]
        public void Pause_SetsTimeScaleToZero()
        {
            TimeCommands.Pause();
            Assert.AreEqual(0f, Time.timeScale, 0.0001f);
            Assert.AreEqual(0f, TimeCommands.Scale.Value, 0.0001f);
        }

        [Test]
        public void Resume_AfterPause_RestoresToOne()
        {
            TimeCommands.Pause();
            TimeCommands.Resume();
            Assert.AreEqual(1f, Time.timeScale, 0.0001f);
            Assert.AreEqual(1f, TimeCommands.Scale.Value, 0.0001f);
        }

        [Test]
        public void SetScaleZero_IsAccepted()
        {
            // Min=0 のバリデーション境界。0 自体は ArgumentBinder の Min チェックを通る (含む)。
            TimeCommands.SetScale(0f);
            Assert.AreEqual(0f, Time.timeScale, 0.0001f);
        }

        [Test]
        public void SetScale_ReturnStringUsesInvariantCulture()
        {
            // ロケール差 (fr-FR 等の "," 小数点) で文字列が揺れないことを抜き打ちで検証。
            // 戻り値に "1.5" のように "." が含まれるパスを通る (CurrentCulture 依存だと "1,5" になる)。
            var s = TimeCommands.SetScale(1.5f);
            StringAssert.Contains("1.5", s);
        }
    }
}
