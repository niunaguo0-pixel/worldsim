// ADR-003: CLI headless Win64 Player build
// Usage:
//   Unity.exe -batchmode -nographics -projectPath WorldSim \
//     -executeMethod WorldSim.Editor.BuildScript.BuildWin64 -logFile build.log -quit

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WorldSim.Editor
{
    public static class BuildScript
    {
        /// <summary>
        /// Headless Win64 Player → Builds/Win64/WorldSim.exe（相对工程根的上一级仓库根亦可）.
        /// </summary>
        public static void BuildWin64()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                // Prefer repo-level Builds/ next to WorldSim/; fallback inside project.
                string repoRoot = Path.GetFullPath(Path.Combine(projectRoot, ".."));
                string outDir = Path.Combine(repoRoot, "Builds", "Win64");
                Directory.CreateDirectory(outDir);
                string exePath = Path.Combine(outDir, "WorldSim.exe");

                string[] scenes = EditorBuildSettings.scenes
                    .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                    .Select(s => s.path)
                    .ToArray();
                if (scenes.Length == 0)
                {
                    const string fallback = "Assets/Scenes/SampleScene.unity";
                    if (!File.Exists(Path.Combine(Application.dataPath, "Scenes", "SampleScene.unity")))
                    {
                        Debug.LogError("BuildScript: no enabled scenes in EditorBuildSettings and SampleScene missing.");
                        EditorApplication.Exit(2);
                        return;
                    }
                    scenes = new[] { fallback };
                    Debug.LogWarning("BuildScript: EditorBuildSettings empty — using " + fallback);
                }

                var opts = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = exePath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                };

                Debug.Log("BuildScript.BuildWin64 → " + exePath);
                BuildReport report = BuildPipeline.BuildPlayer(opts);
                BuildSummary summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"BuildScript FAILED: {summary.result} errors={summary.totalErrors}");
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log($"BuildScript OK size={summary.totalSize}B time={summary.totalTime}");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("BuildScript exception: " + ex);
                EditorApplication.Exit(1);
            }
        }
    }
}
