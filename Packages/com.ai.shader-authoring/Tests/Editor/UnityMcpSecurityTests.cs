#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
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

        [Test]
        public void KnowledgeBaseAtomicWritePublishesCompleteContent()
        {
            var type = typeof(UnityMcpConnection).Assembly.GetType("UnityMcp.Editor.UnityMcpKnowledgeBaseIntegrity", true);
            var method = type.GetMethod("AtomicWrite", BindingFlags.NonPublic | BindingFlags.Static);
            var root = "Library/UnityMcpTests";
            var path = root + "/atomic.json";
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "old");
            method.Invoke(null, new object[] { path, "new-complete-content" });
            Assert.That(File.ReadAllText(path), Is.EqualTo("new-complete-content"));
        }

        [Test]
        public void KnowledgeBaseSnapshotTracksShaderSourcesAndIncludes()
        {
            var type = typeof(UnityMcpConnection).Assembly.GetType("UnityMcp.Editor.UnityMcpKnowledgeBaseIntegrity", true);
            var method = type.GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static);
            var snapshot = method.Invoke(null, null);
            var sourceFingerprint = snapshot.GetType().GetField("sourceFingerprint").GetValue(snapshot) as string;
            var sources = snapshot.GetType().GetField("sources").GetValue(snapshot) as Array;
            Assert.That(sourceFingerprint, Has.Length.EqualTo(64));
            Assert.That(sources.Length, Is.GreaterThan(0));
        }

        [Test]
        public void ShaderStructureParserIgnoresCommentedOutKeywordsAndExtractsPropertyContract()
        {
            var parserType = typeof(UnityMcpConnection).Assembly.GetType("UnityMcp.Editor.UnityMcpShaderStructureAnalysis", true);
            var parse = parserType.GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static);
            var source = "Shader \"Tests/Structure\"\n{\nProperties { _Color (\"颜色\", Color) = (1,1,1,1) }\nSubShader { Pass { // #pragma shader_feature _COMMENTED\n#pragma shader_feature _REAL\n#pragma vertex Vert\nBlend SrcAlpha OneMinusSrcAlpha\n} }\n}";
            var structure = parse.Invoke(null, new object[] { source });
            var keywords = structure.GetType().GetField("keywords").GetValue(structure) as System.Collections.IEnumerable;
            var properties = structure.GetType().GetField("properties").GetValue(structure) as System.Collections.IEnumerable;
            var keywordValues = new System.Collections.Generic.List<string>();
            foreach (var item in keywords) keywordValues.Add((string)item.GetType().GetField("key").GetValue(item));
            Assert.That(keywordValues, Does.Contain("_REAL"));
            Assert.That(keywordValues, Does.Not.Contain("_COMMENTED"));
            Assert.That(properties, Is.Not.Empty);
        }

        [Test]
        public void ShaderStructureParserExtractsInlinePropertiesBlock()
        {
            var parserType = typeof(UnityMcpConnection).Assembly.GetType("UnityMcp.Editor.UnityMcpShaderStructureAnalysis", true);
            var parse = parserType.GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static);
            var structure = parse.Invoke(null, new object[]
            {
                "Shader \"Tests/Inline\" { Properties { _Cutoff (\"Cutoff\", Range(0,1)) = 0.5 } SubShader { Pass { } } }"
            });
            var properties = structure.GetType().GetField("properties").GetValue(structure) as System.Collections.IEnumerable;
            var keys = new System.Collections.Generic.List<string>();
            foreach (var item in properties) keys.Add((string)item.GetType().GetField("key").GetValue(item));
            Assert.That(keys, Does.Contain("_Cutoff"));
        }

        [Test]
        public void CaptureValidationRequiresExactlyOneBaselineAndGeneratedCapture()
        {
            var method = ToolType.GetMethod("RequireCaptureRequests", BindingFlags.NonPublic | BindingFlags.Static);
            using (var document = JsonDocument.Parse("{\"captures\":[{\"captureName\":\"generated\",\"bindingMode\":\"generated_material\"}]}"))
            {
                var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { document.RootElement }));
                Assert.That(exception.InnerException, Is.TypeOf<ArgumentException>());
                Assert.That(exception.InnerException.Message, Does.Contain("exactly one preserve_original"));
            }
        }

        [Test]
        public void CaptureValidationRejectsDuplicateBindingModes()
        {
            var method = ToolType.GetMethod("RequireCaptureRequests", BindingFlags.NonPublic | BindingFlags.Static);
            using (var document = JsonDocument.Parse("{\"captures\":[{\"captureName\":\"first\",\"bindingMode\":\"generated_material\"},{\"captureName\":\"second\",\"bindingMode\":\"generated_material\"}]}"))
            {
                var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { document.RootElement }));
                Assert.That(exception.InnerException, Is.TypeOf<ArgumentException>());
                Assert.That(exception.InnerException.Message, Does.Contain("each bindingMode exactly once"));
            }
        }

        [Test]
        public void CaptureValidationRejectsSanitizedNameCollision()
        {
            var method = ToolType.GetMethod("RequireCaptureRequests", BindingFlags.NonPublic | BindingFlags.Static);
            using (var document = JsonDocument.Parse("{\"captures\":[{\"captureName\":\"target/a\",\"bindingMode\":\"preserve_original\"},{\"captureName\":\"target\\\\a\",\"bindingMode\":\"generated_material\"}]}"))
            {
                var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { document.RootElement }));
                Assert.That(exception.InnerException, Is.TypeOf<ArgumentException>());
                Assert.That(exception.InnerException.Message, Does.Contain("unique after filename sanitization"));
            }
        }

        [Test]
        public void TargetMaskHelpersCalculateUnionOverlapAndPixelDifference()
        {
            var union = ToolType.GetMethod("UnionMasks", BindingFlags.NonPublic | BindingFlags.Static);
            var overlap = ToolType.GetMethod("CalculateMaskOverlapRatio", BindingFlags.NonPublic | BindingFlags.Static);
            var difference = ToolType.GetMethod("CalculateMaskedDifferenceRatio", BindingFlags.NonPublic | BindingFlags.Static);
            var left = new[] { true, false, true, false };
            var right = new[] { true, true, false, false };
            var combined = (bool[])union.Invoke(null, new object[] { left, right });
            Assert.That(combined, Is.EqualTo(new[] { true, true, true, false }));
            Assert.That((float)overlap.Invoke(null, new object[] { left, right }), Is.EqualTo(1f / 3f).Within(0.0001f));

            var reference = new[] { new UnityEngine.Color32(0, 0, 0, 255), new UnityEngine.Color32(0, 0, 0, 255) };
            var generated = new[] { new UnityEngine.Color32(255, 255, 255, 255), new UnityEngine.Color32(0, 0, 0, 255) };
            Assert.That((float)difference.Invoke(null, new object[] { reference, generated, new[] { true, true } }), Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void ShaderStructureComparisonReportsUnapprovedDifference()
        {
            var parserType = typeof(UnityMcpConnection).Assembly.GetType("UnityMcp.Editor.UnityMcpShaderStructureAnalysis", true);
            var parse = parserType.GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static);
            var compare = parserType.GetMethod("Compare", BindingFlags.NonPublic | BindingFlags.Static);
            var reference = parse.Invoke(null, new object[] { "Shader \"Tests/A\" { Properties { _Color (\"Color\", Color) = (1,1,1,1) } SubShader { Pass { } } }" });
            var target = parse.Invoke(null, new object[] { "Shader \"Tests/B\" { Properties { _Cutoff (\"Cutoff\", Range(0,1)) = 0.5 } SubShader { Pass { } } }" });
            var result = compare.Invoke(null, new object[] { reference, target, new string[0] });
            var count = (int)result.GetType().GetProperty("unapprovedDifferenceCount").GetValue(result, null);
            Assert.That(count, Is.GreaterThan(0));
        }
    }
}
#endif
