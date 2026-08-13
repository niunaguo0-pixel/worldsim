// 一键装配可玩月循环场景（无需 Hub）
// CLI: Unity.exe -batchmode -projectPath WorldSim -executeMethod WorldSim.Editor.PlayableSceneSetup.SetupAndQuit

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldSim.Presentation;

namespace WorldSim.Editor
{
    public static class PlayableSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("WorldSim/Setup Playable Month Loop Scene")]
        public static void SetupFromMenu()
        {
            if (!SetupInternal(save: true))
                Debug.LogError("PlayableSceneSetup failed.");
            else
                Debug.Log("PlayableSceneSetup OK — 打开 Game 视图后点 Play。");
        }

        public static void SetupAndQuit()
        {
            bool ok = SetupInternal(save: true);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        private static bool SetupInternal(bool save)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // 相机对准原点
            var cam = Object.FindAnyObjectByType<Camera>();
            if (cam != null)
            {
                cam.transform.position = new Vector3(7f, 5.5f, -7f);
                cam.transform.LookAt(new Vector3(0f, 0.5f, 0f));
            }

            // 永久挂上 Runner（进 Play 必有 HUD）
            var runner = Object.FindAnyObjectByType<SimulationRunner>();
            if (runner == null)
            {
                var go = new GameObject("WorldSim_PlayableLoop");
                runner = go.AddComponent<SimulationRunner>();
                Undo.RegisterCreatedObjectUndo(go, "Add SimulationRunner");
            }

            // 编辑器里也放一个看得见的地面+方块（不进 Play 也能看见）
            EnsureEditPreview();

            if (save)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                return EditorSceneManager.SaveScene(scene);
            }
            return true;
        }

        private static void EnsureEditPreview()
        {
            if (GameObject.Find("WorldSim_Ground") == null)
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "WorldSim_Ground";
                ground.transform.localScale = new Vector3(2.5f, 1f, 2.5f);
            }
            if (GameObject.Find("Settlement_Alpha") == null)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Settlement_Alpha";
                cube.transform.position = new Vector3(0f, 0.75f, 0f);
                cube.transform.localScale = new Vector3(1.6f, 1.5f, 1.6f);
            }
        }
    }
}
