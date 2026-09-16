#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UnityMcp.Editor
{
    /// <summary>
    /// Produces advisory Shader performance reports. Static forecasting is always available; Mali analysis
    /// consumes GLES GLSL exported by Unity's compiled-Shader pipeline and never changes visual-validation decisions.
    /// </summary>
    internal static class ShaderPerformanceAnalyzer
    {
        internal const string SummaryRoot = "Artifacts/ShaderPerformance/";
        private const string RunRoot = "Artifacts/ShaderRuns/";
        private static readonly Regex TextureRegex = new Regex(@"\b(?:SAMPLE_TEXTURE2D|SAMPLE_TEXTURE2D_LOD|SAMPLE_TEXTURECUBE|tex2D|texCUBE|TEXTURE2D|Texture2D|sampler2D)\b", RegexOptions.Compiled);
        private static readonly Regex BranchRegex = new Regex(@"\?|\bif\s*\(|\bfor\s*\(|\bwhile\s*\(", RegexOptions.Compiled);
        private static readonly Regex ExpensiveRegex = new Regex(@"\b(pow|exp|exp2|log|log2|sqrt|rsqrt|sin|cos|tan|normalize|reflect|refract|ddx|ddy|fwidth|clip)\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex KeywordRegex = new Regex(@"^\s*#pragma\s+(?:shader_feature(?:_local)?|multi_compile(?:_local)?)\b", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex RegisterRegex = new Regex(@"Work registers:\s*(?<value>\d+)\s*\((?<percent>\d+)%", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex UniformRegisterRegex = new Regex(@"Uniform registers:\s*(?<value>\d+)\s*\((?<percent>\d+)%", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex StackSizeRegex = new Regex(@"Stack size:\s*(?<value>\d+)\s*bytes", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex StackSpillRegex = new Regex(@"Stack spilling:\s*(?<value>true|false)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HalfArithmeticRegex = new Regex(@"16-bit arithmetic:\s*(?<value>\d+)%", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static object Analyze(JsonElement args, CancellationToken cancellationToken)
        {
            string shaderPath = RequireShaderPath(args);
            cancellationToken.ThrowIfCancellationRequested();
            string source = File.ReadAllText(shaderPath);
            string revision = Hash(source);
            string policy = GetString(args, "policy") ?? "collect_only";
            string policyLabel = GetPolicyLabel(policy);
            string[] targets = GetStringArray(args, "maliTargets");
            if (targets.Length == 0) targets = DefaultTargets(policy);

            List<CompiledGlesVariant> variants = ReadCompiledGlesVariants(args);
            string compiledGlesStatus = variants.Count > 0 ? "external_input" : null;
            string[] compiledGlesDiagnostics = Array.Empty<string>();
            if (variants.Count == 0)
            {
                CompiledGlesVariantExporter.ExportResult export = CompiledGlesVariantExporter.ExportVariants(shaderPath, cancellationToken);
                variants = export.variants.Select(item => new CompiledGlesVariant
                {
                    name = item.name,
                    keywords = item.keywords,
                    vertexGlsl = item.vertexGlsl,
                    fragmentGlsl = item.fragmentGlsl
                }).ToList();
                compiledGlesStatus = export.status;
                compiledGlesDiagnostics = export.diagnostics.ToArray();
            }
            List<ForecastItem> forecast = BuildForecast(source);
            int textureSamples = TextureRegex.Matches(source).Count;
            int branchCount = BranchRegex.Matches(source).Count;
            int keywordLines = KeywordRegex.Matches(source).Count;
            int fragmentEstimatedCost = textureSamples * 8 + branchCount * 3 + forecast.Sum(item => item.weight);
            int vertexEstimatedCost = CountVertexEstimate(source);
            Budget budget = ReadBudget(args, policy);
            MaliResult mali = AnalyzeMali(GetString(args, "maliCompilerPath"), targets, variants, cancellationToken);
            MaliBudgetAssessment maliBudgetAssessment = AssessMaliBudget(mali, budget);
            string rating = Rate(policy, fragmentEstimatedCost, textureSamples, branchCount, mali, budget, maliBudgetAssessment);
            string message = BuildMessage(rating, forecast, mali.status, budget, maliBudgetAssessment);
            string runId = GetSafeRunId(args);

            var report = new
            {
                schemaVersion = "2.0",
                analysisAvailability = "non_blocking",
                shaderPath,
                shaderRevision = revision,
                analyzedAtUtc = DateTime.UtcNow.ToString("o"),
                runId,
                policy,
                policyLabel,
                budgets = budget,
                maliTargets = targets,
                compiledGlesVariantCount = variants.Count,
                compiledGlesStatus,
                compiledGlesDiagnostics,
                rating,
                message,
                textureSampleCount = textureSamples,
                branchCount,
                keywordDirectiveCount = keywordLines,
                fragmentEstimatedCost,
                vertexEstimatedCost,
                highCostModules = forecast.Select(item => new { item.name, item.count, item.weight, item.risk, item.evidence }).ToArray(),
                maliStatus = mali.status,
                maliResults = mali.results,
                maliBudgetAssessment = new { highRisk = maliBudgetAssessment.highRisk, warning = maliBudgetAssessment.warning, violations = maliBudgetAssessment.violations.ToArray() },
                limitations = new[]
                {
                    "Static estimates are heuristics and do not represent device frame time.",
                    "Mali results apply only to supplied compiled GLES GLSL stages and selected target GPUs.",
                    "This report is advisory and never blocks visual validation."
                }
            };

            string summaryPath = SummaryRoot + SafeName(shaderPath) + "-" + revision + ".json";
            string archivePath = RunRoot + runId + "/performance/" + SafeName(shaderPath) + "-" + revision + ".json";
            string reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            WriteReport(summaryPath, reportJson);
            WriteReport(archivePath, reportJson);
            return new
            {
                status = "completed",
                decisionScope = "warning_only",
                report = new { path = summaryPath, archivePath, contentHash = Hash(reportJson) },
                shaderPath,
                shaderRevision = revision,
                policy,
                policyLabel,
                budget,
                rating,
                message,
                staticMetrics = new { textureSamples, branchCount, keywordLines, fragmentEstimatedCost, vertexEstimatedCost },
                compiledGlesVariantCount = variants.Count,
                compiledGlesStatus,
                compiledGlesDiagnostics,
                highCostModules = forecast.Select(item => new { item.name, item.risk, item.count, item.evidence }).ToArray(),
                maliStatus = mali.status,
                maliResults = mali.results,
                maliBudgetAssessment = new { highRisk = maliBudgetAssessment.highRisk, warning = maliBudgetAssessment.warning, violations = maliBudgetAssessment.violations.ToArray() }
            };
        }

        private static List<ForecastItem> BuildForecast(string source)
        {
            var result = new List<ForecastItem>();
            AddForecast(result, "高代价数学", ExpensiveRegex.Matches(source).Count, 5, "注意", "pow/exp/log/sqrt/normalize 等函数调用");
            AddForecast(result, "纹理采样", TextureRegex.Matches(source).Count, 4, "注意", "纹理、采样器或采样宏引用");
            AddForecast(result, "动态控制流", BranchRegex.Matches(source).Count, 3, "注意", "if/for/while/三元表达式");
            AddForecast(result, "透明或裁剪", Count(source, "clip(") + Count(source, "Blend "), 4, "注意", "clip 或 Blend 渲染路径");
            AddForecast(result, "视差或步进", Count(source, "Parallax") + Count(source, "raymarch"), 8, "预警", "视差或射线步进关键字");
            return result;
        }

        private static void AddForecast(List<ForecastItem> result, string name, int count, int unitWeight, string risk, string evidence)
        {
            if (count > 0) result.Add(new ForecastItem { name = name, count = count, weight = count * unitWeight, risk = risk, evidence = evidence });
        }

        private static List<CompiledGlesVariant> ReadCompiledGlesVariants(JsonElement args)
        {
            var variants = new List<CompiledGlesVariant>();
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("compiledGlesVariants", out var rawVariants) && rawVariants.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement raw in rawVariants.EnumerateArray())
                {
                    if (raw.ValueKind != JsonValueKind.Object) continue;
                    string vertex = GetString(raw, "vertexGlsl");
                    string fragment = GetString(raw, "fragmentGlsl");
                    if (string.IsNullOrWhiteSpace(vertex) && string.IsNullOrWhiteSpace(fragment)) continue;
                    variants.Add(new CompiledGlesVariant { name = GetString(raw, "name") ?? "variant-" + variants.Count, keywords = GetStringArray(raw, "keywords"), vertexGlsl = vertex, fragmentGlsl = fragment });
                }
            }

            // Compatibility input for callers that provide one exported GLES stage pair.
            if (variants.Count == 0)
            {
                string vertex = GetString(args, "vertexGlsl");
                string fragment = GetString(args, "fragmentGlsl");
                if (!string.IsNullOrWhiteSpace(vertex) || !string.IsNullOrWhiteSpace(fragment))
                    variants.Add(new CompiledGlesVariant { name = "default", keywords = Array.Empty<string>(), vertexGlsl = vertex, fragmentGlsl = fragment });
            }
            return variants;
        }

        private static MaliResult AnalyzeMali(string compilerPath, string[] targets, List<CompiledGlesVariant> variants, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(compilerPath) || !File.Exists(compilerPath)) return new MaliResult { status = "未配置", results = Array.Empty<MaliStageResult>() };
            if (variants.Count == 0) return new MaliResult { status = "GLES 编译导出不可用", results = Array.Empty<MaliStageResult>() };

            var results = new List<MaliStageResult>();
            foreach (CompiledGlesVariant variant in variants)
            foreach (string target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(variant.vertexGlsl)) results.Add(RunMali(compilerPath, target, "vert", variant, variant.vertexGlsl));
                if (!string.IsNullOrWhiteSpace(variant.fragmentGlsl)) results.Add(RunMali(compilerPath, target, "frag", variant, variant.fragmentGlsl));
            }
            return new MaliResult { status = results.Any(item => item.status == "failed") ? "部分失败" : "已分析", results = results.ToArray() };
        }

        private static MaliStageResult RunMali(string compilerPath, string target, string stage, CompiledGlesVariant variant, string glsl)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "unity-mcp-mali-" + Guid.NewGuid().ToString("N") + "." + stage);
            try
            {
                File.WriteAllText(tempPath, glsl, new UTF8Encoding(false));
                var startInfo = new ProcessStartInfo
                {
                    FileName = compilerPath,
                    Arguments = QuoteArgument(tempPath) + " -c " + QuoteArgument(target),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (Process process = Process.Start(startInfo))
                {
                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();
                    bool exited = process.WaitForExit(30000);
                    if (!exited)
                    {
                        try { process.Kill(); } catch { }
                    }
                    Task.WaitAll(outputTask, errorTask);
                    string output = outputTask.Result;
                    string error = errorTask.Result;
                    MaliMetrics metrics = ParseMaliMetrics(output + "\n" + error);
                    return new MaliStageResult
                    {
                        variant = variant.name,
                        keywords = variant.keywords,
                        target = target,
                        stage = stage,
                        status = exited && process.ExitCode == 0 ? "completed" : "failed",
                        exitCode = exited ? process.ExitCode : -1,
                        metrics = metrics,
                        longestPathWorstCycles = LargestCycleComponent(metrics.longestPathCycles),
                        output = output,
                        error = error
                    };
                }
            }
            catch (Exception exception)
            {
                return new MaliStageResult
                {
                    variant = variant.name,
                    keywords = variant.keywords,
                    target = target,
                    stage = stage,
                    status = "failed",
                    exitCode = -1,
                    metrics = new MaliMetrics(),
                    longestPathWorstCycles = 0,
                    output = string.Empty,
                    error = exception.Message
                };
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        private static MaliMetrics ParseMaliMetrics(string output)
        {
            var metrics = new MaliMetrics();
            Match work = RegisterRegex.Match(output);
            if (work.Success) { metrics.workRegisters = ToInt(work, "value"); metrics.workRegisterUtilizationPercent = ToInt(work, "percent"); }
            Match uniform = UniformRegisterRegex.Match(output);
            if (uniform.Success) { metrics.uniformRegisters = ToInt(uniform, "value"); metrics.uniformRegisterUtilizationPercent = ToInt(uniform, "percent"); }
            Match stack = StackSizeRegex.Match(output);
            if (stack.Success) metrics.stackSizeBytes = ToInt(stack, "value");
            Match spill = StackSpillRegex.Match(output);
            if (spill.Success) metrics.stackSpilling = string.Equals(spill.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase);
            Match half = HalfArithmeticRegex.Match(output);
            if (half.Success) metrics.halfArithmeticPercent = ToInt(half, "value");
            metrics.totalInstructionCycles = ParseCycleRow(output, "Total instruction cycles:");
            metrics.shortestPathCycles = ParseCycleRow(output, "Shortest path cycles:");
            metrics.longestPathCycles = ParseCycleRow(output, "Longest path cycles:");
            return metrics;
        }

        private static CycleMetrics ParseCycleRow(string output, string heading)
        {
            Match match = Regex.Match(output, Regex.Escape(heading) + @"\s*(?<arithmetic>[\d.]+|N/A)\s+(?<loadStore>[\d.]+|N/A)\s+(?:(?<varying>[\d.]+|N/A)\s+)?(?<texture>[\d.]+|N/A)\s+(?<bound>\S+)", RegexOptions.IgnoreCase);
            if (!match.Success) return new CycleMetrics();
            return new CycleMetrics { arithmetic = match.Groups["arithmetic"].Value, loadStore = match.Groups["loadStore"].Value, varying = match.Groups["varying"].Success ? match.Groups["varying"].Value : "N/A", texture = match.Groups["texture"].Value, bound = match.Groups["bound"].Value };
        }

        private static int ToInt(Match match, string group) { int value; return int.TryParse(match.Groups[group].Value, out value) ? value : 0; }
        private static int CountVertexEstimate(string source) => Count(source, "positionOS") * 2 + Count(source, "normalOS") * 2 + Count(source, "tangentOS") * 2 + Count(source, "vertex");
        private static int Count(string source, string token) => Regex.Matches(source, Regex.Escape(token), RegexOptions.IgnoreCase).Count;
        private static string[] DefaultTargets(string policy) => policy == "low_android" ? new[] { "Mali-G31" } : policy == "high_android" ? new[] { "Mali-G78" } : policy == "all_android" ? new[] { "Mali-G31", "Mali-G52", "Mali-G78" } : new[] { "Mali-G52" };
        private static string GetPolicyLabel(string policy) => policy == "low_android" ? "低端 Android · Mali-G31" : policy == "medium_android" ? "中端 Android · Mali-G52" : policy == "high_android" ? "高端 Android · Mali-G78" : policy == "all_android" ? "Android 三档覆盖" : policy == "custom" ? "项目自定义阈值" : "仅采集，不评级";

        private static Budget ReadBudget(JsonElement args, string policy)
        {
            int fragment = policy == "low_android" ? 55 : policy == "high_android" ? 150 : 95;
            return new Budget
            {
                fragmentEstimatedCost = GetInt(args, "maxFragmentEstimatedCost", fragment),
                textureSamples = GetInt(args, "maxTextureSamples", policy == "low_android" ? 6 : 8),
                branches = GetInt(args, "maxBranches", policy == "low_android" ? 6 : 10),
                longestPathCycles = GetInt(args, "maxLongestPathCycles", 0),
                workRegisters = GetInt(args, "maxWorkRegisters", 0)
            };
        }

        private static string Rate(string policy, int fragmentCost, int textures, int branches, MaliResult mali, Budget budget, MaliBudgetAssessment maliBudget)
        {
            if (policy == "collect_only") return "未评级";
            if (mali.status == "部分失败") return "高风险";

            bool staticHighRisk = fragmentCost > budget.fragmentEstimatedCost * 1.5f || textures > budget.textureSamples * 1.5f || branches > budget.branches * 1.5f;
            bool staticWarning = fragmentCost > budget.fragmentEstimatedCost || textures > budget.textureSamples || branches > budget.branches;
            if (staticHighRisk || maliBudget.highRisk) return "高风险";
            if (staticWarning || maliBudget.warning) return "预警";
            return fragmentCost > budget.fragmentEstimatedCost * 0.75f ? "注意" : "信息";
        }

        private static MaliBudgetAssessment AssessMaliBudget(MaliResult mali, Budget budget)
        {
            var assessment = new MaliBudgetAssessment();
            if (mali.results == null) return assessment;
            foreach (MaliStageResult result in mali.results)
            {
                if (result.status != "completed" || result.metrics == null) continue;
                int longestCycles = result.longestPathWorstCycles;
                if (result.metrics.stackSpilling)
                {
                    assessment.highRisk = true;
                    assessment.violations.Add(result.variant + "/" + result.target + "/" + result.stage + " 出现栈溢出");
                }
                if (budget.workRegisters > 0 && result.metrics.workRegisters > budget.workRegisters)
                {
                    if (result.metrics.workRegisters > budget.workRegisters * 1.5f) assessment.highRisk = true;
                    else assessment.warning = true;
                    assessment.violations.Add(result.variant + "/" + result.target + "/" + result.stage + " 工作寄存器 " + result.metrics.workRegisters + ">" + budget.workRegisters);
                }
                if (budget.longestPathCycles > 0 && longestCycles > budget.longestPathCycles)
                {
                    if (longestCycles > budget.longestPathCycles * 1.5f) assessment.highRisk = true;
                    else assessment.warning = true;
                    assessment.violations.Add(result.variant + "/" + result.target + "/" + result.stage + " 最长路径周期 " + longestCycles + ">" + budget.longestPathCycles);
                }
            }
            return assessment;
        }

        private static int LargestCycleComponent(CycleMetrics cycles)
        {
            if (cycles == null) return 0;
            return new[] { ToCycleInt(cycles.arithmetic), ToCycleInt(cycles.loadStore), ToCycleInt(cycles.varying), ToCycleInt(cycles.texture) }.Max();
        }

        private static int ToCycleInt(string value)
        {
            float parsed;
            return float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed) ? (int)Math.Ceiling(parsed) : 0;
        }

        private static string BuildMessage(string rating, List<ForecastItem> forecast, string maliStatus, Budget budget, MaliBudgetAssessment maliBudgetAssessment)
        {
            string modules = forecast.Count == 0 ? "未识别到明显高开销模块。" : "重点关注：" + string.Join("、", forecast.OrderByDescending(item => item.weight).Take(3).Select(item => item.name).ToArray()) + "。";
            string maliBudget = budget.longestPathCycles > 0 || budget.workRegisters > 0 ? " Mali 阈值：最长路径 " + (budget.longestPathCycles > 0 ? budget.longestPathCycles.ToString() : "未设置") + "、工作寄存器 " + (budget.workRegisters > 0 ? budget.workRegisters.ToString() : "未设置") + "。" : string.Empty;
            string violations = maliBudgetAssessment.violations.Count == 0 ? string.Empty : " Mali 预警：" + string.Join("；", maliBudgetAssessment.violations.Take(3).ToArray()) + "。";
            return modules + " 静态预算：片元 " + budget.fragmentEstimatedCost + "、采样 " + budget.textureSamples + "、分支 " + budget.branches + "。" + maliBudget + violations + "Mali 状态：" + maliStatus + "。性能结论仅预警，不会阻断视觉验收。";
        }

        private static string RequireShaderPath(JsonElement args)
        {
            string path = GetString(args, "shaderPath");
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new ArgumentException("shaderPath must reference an existing project-relative Assets/*.shader asset.");
            return path;
        }

        private static string GetSafeRunId(JsonElement args)
        {
            string value = GetString(args, "runId");
            return string.IsNullOrWhiteSpace(value) ? "manual-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") : SafeName(value);
        }

        private static void WriteReport(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static string GetString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        private static int GetInt(JsonElement element, string name, int fallback) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out int result) && result >= 0 ? result : fallback;
        private static string[] GetStringArray(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(item => !string.IsNullOrEmpty(item)).ToArray() : Array.Empty<string>();
        private static string QuoteArgument(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
        private static string SafeName(string path) => path.Replace('/', '_').Replace('\\', '_').Replace(':', '_').Replace("..", "_");
        private static string Hash(string text) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty).ToLowerInvariant(); }

        private sealed class ForecastItem { public string name; public int count; public int weight; public string risk; public string evidence; }
        private sealed class CompiledGlesVariant { public string name; public string[] keywords; public string vertexGlsl; public string fragmentGlsl; }
        private sealed class MaliResult { public string status; public MaliStageResult[] results; }
        private sealed class MaliStageResult { public string variant; public string[] keywords; public string target; public string stage; public string status; public int exitCode; public MaliMetrics metrics; public int longestPathWorstCycles; public string output; public string error; }
        private sealed class MaliBudgetAssessment { public bool highRisk; public bool warning; public readonly List<string> violations = new List<string>(); }
        private sealed class Budget { public int fragmentEstimatedCost; public int textureSamples; public int branches; public int longestPathCycles; public int workRegisters; }
        private sealed class MaliMetrics { public int workRegisters; public int workRegisterUtilizationPercent; public int uniformRegisters; public int uniformRegisterUtilizationPercent; public int stackSizeBytes; public bool stackSpilling; public int halfArithmeticPercent; public CycleMetrics totalInstructionCycles; public CycleMetrics shortestPathCycles; public CycleMetrics longestPathCycles; }
        private sealed class CycleMetrics { public string arithmetic = "N/A"; public string loadStore = "N/A"; public string varying = "N/A"; public string texture = "N/A"; public string bound = "N/A"; }
    }
}
#endif
