using NUnit.Framework;
using UnityEngine;
using Void2610.LiminalPalette.Runtime;

namespace Void2610.LiminalPalette.Tests.Runtime
{
    public sealed class PaletteRuntimeSettingsTests
    {
        [Test]
        public void LoadOrCreateDefault_NotNull_AndHasReasonableDefaults()
        {
            var s = PaletteRuntimeSettings.LoadOrCreateDefault();
            Assert.IsNotNull(s);
            Assert.IsTrue(s.EnableInRuntime);
            // 既定は Editor 側 LiminalPaletteWindow の Cmd+K と統一。
            Assert.AreEqual(KeyCode.K, s.ToggleKey);
            Assert.IsTrue(s.RequireModifier);
            Assert.IsTrue(s.PanelSortingOrder > 0);
        }
    }
}
