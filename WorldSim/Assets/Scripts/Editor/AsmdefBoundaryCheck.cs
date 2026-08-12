#if UNITY_EDITOR
namespace WorldSim.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEditor.Compilation;
    using UnityEngine;

    /// <summary>
    /// 静态引用扫描: 断言 WorldSim.Simulation.* 零 UnityEngine.CoreModule 引用 (G0-1 / ADR-001 方案 C).
    /// 两层: (1) 编译后程序集依赖 (2) 源文件符号扫描. 任意违例抛异常, 可由 CI / 测试调用.
    /// 注: 模拟核心 asmdef 已设 noEngineReferences=true, 从源头屏蔽引擎模块; 本脚本是 CI 兜底.
    /// </summary>
    public static class AsmdefBoundaryCheck
    {
        // 禁止的引擎模块前缀. Unity.Mathematics 包以 "Unity." 开头但非引擎模块, 放行.
        private const string EngineModulePrefix = "UnityEngine";

        [MenuItem("WorldSim/Checks/Assert Simulation Asmdef Boundary")]
        public static void MenuAssert() => AssertSimulationBoundary();

        /// <summary>断言模拟核心零 CoreModule 引用; 违例抛 Exception (CI / 测试可调用).</summary>
        public static void AssertSimulationBoundary()
        {
            AssertNoEngineModuleReferences();
            AssertNoForbiddenSourceTokens();
        }

        private static void AssertNoEngineModuleReferences()
        {
            foreach (var asm in CompilationPipeline.GetAssemblies())
            {
                if (!asm.name.StartsWith("WorldSim.Simulation.", StringComparison.Ordinal)) continue;
                foreach (var dep in asm.assemblyReferences)
                {
                    if (dep.name == "UnityEngine" || dep.name.StartsWith(EngineModulePrefix + ".", StringComparison.Ordinal))
                        throw new Exception(
                            $"[AsmdefBoundary] {asm.name} 非法引用引擎模块 '{dep.name}' (应仅引用 Unity.Mathematics + System.*).");
                }
            }
        }

        private static void AssertNoForbiddenSourceTokens()
        {
            string simRoot = Path.Combine(Application.dataPath, "Scripts", "Simulation");
            if (!Directory.Exists(simRoot)) return;

            // "using UnityEngine" 命中 UnityEngine 与 UnityEngine.*, 但不命中 "using Unity.Mathematics"
            foreach (var file in Directory.GetFiles(simRoot, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                if (text.Contains("using UnityEngine"))
                    throw new Exception($"[AsmdefBoundary] {file} 包含 'using UnityEngine' (模拟核心禁止引用 UnityEngine).");

                if (ContainsSymbol(text, "GameObject") ||
                    ContainsSymbol(text, "MonoBehaviour") ||
                    ContainsSymbol(text, "UnityEngine.Time") ||
                    ContainsSymbol(text, "System.DateTime.Now"))
                {
                    throw new Exception($"[AsmdefBoundary] {file} 包含禁止的 UnityEngine.CoreModule 符号.");
                }
            }
        }

        // 单词边界近似匹配 (前后非标识符字符), 避免子串误报 (如 "MonoBehaviour" 在注释/字符串中).
        private static bool ContainsSymbol(string text, string symbol)
        {
            int idx = text.IndexOf(symbol, StringComparison.Ordinal);
            while (idx >= 0)
            {
                bool beforeOk = idx == 0 || (!char.IsLetterOrDigit(text[idx - 1]) && text[idx - 1] != '_');
                int end = idx + symbol.Length;
                bool afterOk = end >= text.Length || (!char.IsLetterOrDigit(text[end]) && text[end] != '_');
                if (beforeOk && afterOk) return true;
                idx = text.IndexOf(symbol, idx + symbol.Length, StringComparison.Ordinal);
            }
            return false;
        }
    }
}
#endif
