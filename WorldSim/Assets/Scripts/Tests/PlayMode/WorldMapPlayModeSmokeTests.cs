using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WorldSim.Presentation;

namespace WorldSim.Tests.PlayMode
{
    public class WorldMapPlayModeSmokeTests
    {
        [UnityTest]
        public IEnumerator SimulationRunner_LoadsRealEarthMeshWithoutPlaneFallback()
        {
            var cameraObject = new GameObject("PlayModeSmokeCamera");
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();
            var runnerObject = new GameObject("PlayModeSmokeRunner");
            var runner = runnerObject.AddComponent<SimulationRunner>();
            yield return null;

            Assert.IsNotNull(runner.World, "SimulationRunner should construct a WorldState");
            Assert.IsNotNull(runner.World.Geography, "SimulationRunner should build real Geography from the committed bundle");
            Assert.IsNotNull(GameObject.Find("WorldSim_RealEarthMap"),
                "Real Earth mesh (WorldSim_RealEarthMap) must load from the regenerated bundle");
            Assert.IsNull(GameObject.Find("WorldSim_Ground"),
                "No simplified plane fallback (WorldSim_Ground) should be present");
            Assert.IsNull(GameObject.Find("WorldSim_GeoBundleError"),
                "No geo bundle error placeholder should be present when the real bundle loads");
            // Task 6: 已提交派生包是真实重生产物, buildId 为 lock 派生 (非 simplified)。
            Assert.IsTrue(runner.World.Map.GeoDataBuild.StartsWith("geo-v1-", System.StringComparison.Ordinal),
                "PlayMode buildId must be lock-derived: " + runner.World.Map.GeoDataBuild);
            Assert.IsFalse(runner.World.Map.GeoDataBuild.Contains("simplified"),
                "PlayMode buildId must not be the legacy simplified id: " + runner.World.Map.GeoDataBuild);

            Object.Destroy(runnerObject);
            Object.Destroy(cameraObject);
            yield return null;
        }
    }
}
