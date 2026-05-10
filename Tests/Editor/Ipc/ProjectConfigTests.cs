using System.IO;
using NUnit.Framework;
using Void2610.LiminalPalette.Ipc;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    /// <summary>
    /// ProjectConfig.GetPreferredPortAt のテスト。
    /// 一時ディレクトリを project root に見立てて ProjectSettings/LiminalPalette.json を
    /// 読み書きしながら parse の各分岐 (正常 / 欠落 / 範囲外 / 壊れた JSON) を検証する。
    /// </summary>
    public sealed class ProjectConfigTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "lp-test-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_tempRoot, "ProjectSettings"));
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
            catch { /* swallow: test cleanup */ }
        }

        private string ConfigPath => Path.Combine(_tempRoot, "ProjectSettings", ProjectConfig.FileName);

        [Test]
        public void GetPreferredPortAt_NoFile_ReturnsNull()
        {
            Assert.IsNull(ProjectConfig.GetPreferredPortAt(_tempRoot));
        }

        [Test]
        public void GetPreferredPortAt_ValidPort_ReturnsPort()
        {
            File.WriteAllText(ConfigPath, "{\"port\":7613}");
            Assert.AreEqual(7613, ProjectConfig.GetPreferredPortAt(_tempRoot));
        }

        [Test]
        public void GetPreferredPortAt_PortOutOfRange_ReturnsNull()
        {
            File.WriteAllText(ConfigPath, "{\"port\":0}");
            Assert.IsNull(ProjectConfig.GetPreferredPortAt(_tempRoot));

            File.WriteAllText(ConfigPath, "{\"port\":70000}");
            Assert.IsNull(ProjectConfig.GetPreferredPortAt(_tempRoot));

            File.WriteAllText(ConfigPath, "{\"port\":-1}");
            Assert.IsNull(ProjectConfig.GetPreferredPortAt(_tempRoot));
        }

        [Test]
        public void GetPreferredPortAt_MalformedJson_ReturnsNull()
        {
            File.WriteAllText(ConfigPath, "not json at all");
            // JsonUtility はパース失敗で例外を投げるが、ProjectConfig 側で握って null を返す。
            Assert.IsNull(ProjectConfig.GetPreferredPortAt(_tempRoot));
        }

        [Test]
        public void GetPreferredPortAt_EmptyFile_ReturnsNull()
        {
            File.WriteAllText(ConfigPath, "");
            Assert.IsNull(ProjectConfig.GetPreferredPortAt(_tempRoot));
        }

        [Test]
        public void GetPreferredPortAt_MissingPortField_ReturnsNull()
        {
            // JsonUtility は field 不在時に default 値 (= 0) を入れるので、port=0 として扱われ null になる想定。
            File.WriteAllText(ConfigPath, "{\"otherField\":42}");
            Assert.IsNull(ProjectConfig.GetPreferredPortAt(_tempRoot));
        }

        [Test]
        public void GetPreferredPortAt_NullOrEmptyRoot_ReturnsNull()
        {
            Assert.IsNull(ProjectConfig.GetPreferredPortAt(null));
            Assert.IsNull(ProjectConfig.GetPreferredPortAt(""));
        }

        // ---------- runtimePort (Play Mode 専用ポート) ----------

        [Test]
        public void GetPreferredRuntimePortAt_NoFile_ReturnsNull()
        {
            Assert.IsNull(ProjectConfig.GetPreferredRuntimePortAt(_tempRoot));
        }

        [Test]
        public void GetPreferredRuntimePortAt_ValidPort_ReturnsPort()
        {
            File.WriteAllText(ConfigPath, "{\"runtimePort\":7700}");
            Assert.AreEqual(7700, ProjectConfig.GetPreferredRuntimePortAt(_tempRoot));
        }

        [Test]
        public void GetPreferredRuntimePortAt_OnlyEditorPortSet_ReturnsNull()
        {
            // port (Editor) のみ書かれていて runtimePort 未設定なら、Runtime 側は null。
            // 呼び出し側で port にフォールバックする責務。
            File.WriteAllText(ConfigPath, "{\"port\":7613}");
            Assert.AreEqual(7613, ProjectConfig.GetPreferredPortAt(_tempRoot));
            Assert.IsNull(ProjectConfig.GetPreferredRuntimePortAt(_tempRoot));
        }

        [Test]
        public void GetPreferredRuntimePortAt_BothSet_ReturnsEachIndependently()
        {
            File.WriteAllText(ConfigPath, "{\"port\":7613,\"runtimePort\":7700}");
            Assert.AreEqual(7613, ProjectConfig.GetPreferredPortAt(_tempRoot));
            Assert.AreEqual(7700, ProjectConfig.GetPreferredRuntimePortAt(_tempRoot));
        }

        [Test]
        public void GetPreferredRuntimePortAt_OutOfRange_ReturnsNull()
        {
            File.WriteAllText(ConfigPath, "{\"runtimePort\":-1}");
            Assert.IsNull(ProjectConfig.GetPreferredRuntimePortAt(_tempRoot));

            File.WriteAllText(ConfigPath, "{\"runtimePort\":70000}");
            Assert.IsNull(ProjectConfig.GetPreferredRuntimePortAt(_tempRoot));
        }
    }
}
