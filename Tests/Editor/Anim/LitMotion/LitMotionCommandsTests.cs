using LitMotion;
using NUnit.Framework;
using Void2610.LiminalPalette.Runtime;

namespace Void2610.LiminalPalette.Tests.LitMotion
{
    /// <summary>
    /// LitMotionCommands (Anim/CompleteAll / Anim/CancelAll) の動作検証。
    /// EditMode で走らせるが EditorMotionDispatcher も MotionManager.Register 経由で
    /// 共有ストレージに登録するため、リフレクション経路は Play Mode と同じものが走る。
    /// 各テストは先頭で CancelAll してから開始し、テスト間の tween 漏れを断つ。
    /// </summary>
    public sealed class LitMotionCommandsTests
    {
        [SetUp]
        public void ClearAllMotions() => LitMotionCommands.CancelAll();

        [Test]
        public void CompleteAll_FinishesActiveTweenAndAppliesEndValue()
        {
            var value = 0f;
            var handle = LMotion.Create(0f, 100f, 5f).Bind(v => value = v);

            Assert.IsTrue(handle.IsActive(), "Bind 直後は tween がアクティブであるべき");

            var result = LitMotionCommands.CompleteAll();

            Assert.AreEqual(100f, value, 0.0001f, "CompleteAll 後は bound property が終端値になっているべき");
            StringAssert.StartsWith("completed=1", result);
            Assert.IsFalse(handle.IsActive(), "Complete 後は tween は非アクティブ");
        }

        [Test]
        public void CompleteAll_ReturnsZeroWhenNoActiveTweens()
        {
            var result = LitMotionCommands.CompleteAll();
            Assert.AreEqual("completed=0 skipped=0 iterations=0", result);
        }

        [Test]
        public void CompleteAll_SkipsInfiniteLoopTweenButFinishesFinite()
        {
            var infHandle = LMotion.Create(0f, 1f, 1f).WithLoops(-1).Bind(_ => { });
            var finished = 0f;
            var finiteHandle = LMotion.Create(0f, 42f, 3f).Bind(v => finished = v);

            var result = LitMotionCommands.CompleteAll();

            Assert.AreEqual(42f, finished, 0.0001f, "有限 tween は Complete で終端値に到達");
            Assert.IsFalse(finiteHandle.IsActive());
            Assert.IsTrue(infHandle.IsActive(), "無限ループは Complete では止まらない仕様なので生き残る");
            StringAssert.Contains("skipped=", result);
        }

        [Test]
        public void CancelAll_CancelsInfiniteLoopTween()
        {
            var handle = LMotion.Create(0f, 1f, 1f).WithLoops(-1).Bind(_ => { });
            Assert.IsTrue(handle.IsActive());

            var result = LitMotionCommands.CancelAll();

            Assert.IsFalse(handle.IsActive(), "無限ループも Cancel なら止まる");
            StringAssert.StartsWith("cancelled=1", result);
        }

        [Test]
        public void CompleteAll_FiresOnCompleteCallback()
        {
            var onCompleteFired = false;
            LMotion.Create(0f, 1f, 5f)
                .WithOnComplete(() => onCompleteFired = true)
                .Bind(_ => { });

            LitMotionCommands.CompleteAll();

            Assert.IsTrue(onCompleteFired, "OnComplete は Complete と同フレームで発火するべき");
        }
    }
}
