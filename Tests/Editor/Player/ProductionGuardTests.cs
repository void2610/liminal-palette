using NUnit.Framework;
using UnityEngine;
using Void2610.LiminalPalette.Player;

namespace Void2610.LiminalPalette.Tests.Player
{
    public sealed class ProductionGuardTests
    {
        private PaletteRuntimeSettings _settings;

        [SetUp]
        public void SetUp() => _settings = ScriptableObject.CreateInstance<PaletteRuntimeSettings>();

        [TearDown]
        public void TearDown()
        {
            if (_settings != null) Object.DestroyImmediate(_settings);
            _settings = null;
        }

        [Test]
        public void NullSettings_DoesNotDisable()
        {
            // null は実プロジェクトで起こりにくいが、防御的に false (= 起動許可) を返す挙動を期待。
            Assert.IsFalse(ProductionGuard.ShouldDisableInRuntime(null));
        }

        [Test]
        public void EnableInRuntimeFalse_Disables()
        {
            _settings.EnableInRuntime = false;
            Assert.IsTrue(ProductionGuard.ShouldDisableInRuntime(_settings));
        }

        [Test]
        public void EnableInRuntimeTrue_AndDevelopmentBuild_DoesNotDisable()
        {
            _settings.EnableInRuntime = true;
            // Editor では Debug.isDebugBuild が常に true なので DisableInProductionBuilds が立っていても無効化されない。
            _settings.DisableInProductionBuilds = true;
            Assert.IsTrue(Debug.isDebugBuild, "Editor では Debug.isDebugBuild が true であるべき (前提条件)");
            Assert.IsFalse(ProductionGuard.ShouldDisableInRuntime(_settings));
        }
    }
}
