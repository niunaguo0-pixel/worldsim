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

            Assert.IsNotNull(runner.World);
            Assert.IsNotNull(runner.World.Geography);
            Assert.IsNotNull(GameObject.Find("WorldSim_RealEarthMap"));
            Assert.IsNull(GameObject.Find("WorldSim_Ground"));
            Assert.IsNull(GameObject.Find("WorldSim_GeoBundleError"));

            Object.Destroy(runnerObject);
            Object.Destroy(cameraObject);
            yield return null;
        }
    }
}
