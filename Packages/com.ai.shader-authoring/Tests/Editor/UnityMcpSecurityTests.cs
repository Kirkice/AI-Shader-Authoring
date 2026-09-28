#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace UnityMcp.Editor.Tests
{
    public sealed class UnityMcpSecurityTests
    {
        private static readonly Type ToolType = typeof(UnityMcpConnection).Assembly.GetType("UnityMcp.Editor.UnityMcpShaderTools", true);

        [TestCase("../outside.shader")]
        [TestCase("Assets/AIShader/Generated/../outside.shader")]
        [TestCase("C:/outside.shader")]
        public void ValidatePathRejectsTraversalAndRootedPaths(string path)
        {
            var method = ToolType.GetMethod("ValidatePath", BindingFlags.NonPublic | BindingFlags.Static);
            var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { path }));
            Assert.That(exception.InnerException, Is.TypeOf<UnauthorizedAccessException>());
        }

        [Test]
        public void GeneratedPathRejectsNonAllowListedExtension()
        {
            var method = ToolType.GetMethod("RequireGeneratedPath", BindingFlags.NonPublic | BindingFlags.Static);
            var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { "Assets/AIShader/Generated/payload.exe" }));
            Assert.That(exception.InnerException, Is.TypeOf<UnauthorizedAccessException>());
        }

        [Test]
        public void ArtifactRunIdRejectsPathSeparators()
        {
            var method = ToolType.GetMethod("SafePathSegment", BindingFlags.NonPublic | BindingFlags.Static);
            var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { "run/escape" }));
            Assert.That(exception.InnerException, Is.TypeOf<UnauthorizedAccessException>());
        }

        [Test]
        public void DynamicCSharpIsDisabledByDefault()
        {
            Assert.That(UnityMcpConnection.DynamicCSharpEnabled, Is.False);
        }
    }
}
#endif
