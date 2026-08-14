using NUnit.Framework;
using UnityEngine;
using WorldSim.Presentation;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Serialization;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic4")]
    public class CameraLodPolicyTests
    {
        [TestCase(0f, CameraLodLevel.Individual)]
        [TestCase(6f, CameraLodLevel.Individual)]
        [TestCase(6.01f, CameraLodLevel.Settlement)]
        [TestCase(11f, CameraLodLevel.Settlement)]
        [TestCase(11.01f, CameraLodLevel.Civilization)]
        [TestCase(18f, CameraLodLevel.Civilization)]
        [TestCase(18.01f, CameraLodLevel.GenerationOverview)]
        public void Evaluate_MapsEveryBoundaryToExpectedLod(float distance, CameraLodLevel expected)
        {
            Assert.AreEqual(expected, CameraLodPolicy.Evaluate(distance).Level);
        }

        [Test]
        public void Zoom_ChangesTargetAndAppliesTargetLodBeforeVisualSmoothing()
        {
            var host = new GameObject("CameraLodPolicyTests_Host");
            var cameraObject = new GameObject("CameraLodPolicyTests_Camera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var controller = host.AddComponent<CameraLodController>();
                controller.Bind(camera, null, null, null, null);

                controller.Zoom(1f);

                // InitialDistance=14, ZoomStep=1.6 → 15.6；落在 Civilization 档
                Assert.AreEqual(15.6f, controller.TargetDistance, 0.0001f);
                Assert.AreEqual(CameraLodLevel.Civilization, controller.CurrentLod);
                Assert.IsTrue(controller.ReduceMotion);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void EvaluatingAllLods_DoesNotChangeSimulationMonthlyHash()
        {
            var world = WorldState.CreateMinimalSlice(404);
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);

            CameraLodPolicy.Evaluate(3f);
            CameraLodPolicy.Evaluate(8f);
            CameraLodPolicy.Evaluate(14f);
            CameraLodPolicy.Evaluate(24f);

            Assert.AreEqual(before, WorldStateSerializer.ComputeMonthlyHash(world));
        }

        [Test]
        public void Hysteresis_IsUsedByControllerOnZoomNearBoundary()
        {
            var host = new GameObject("CameraLodHysteresis_Host");
            var cameraObject = new GameObject("CameraLodHysteresis_Camera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var controller = host.AddComponent<CameraLodController>();
                controller.Bind(camera, null, null, null, null);

                // InitialDistance=14 → Civilization；迟滞下需越过 11-0.75 才进 Settlement
                controller.Zoom(-3f); // 14 - 4.8 = 9.2 → Settlement
                Assert.AreEqual(CameraLodLevel.Settlement, controller.CurrentLod);
                Assert.AreEqual(120, controller.MeshLonSegments);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Evaluate_IncludesMeshPrecisionFields()
        {
            var d = CameraLodPolicy.Evaluate(3f);
            Assert.AreEqual(180, d.MeshLonSegments);
            Assert.AreEqual(90, d.MeshLatSegments);
            Assert.Greater(d.ElevationScale, 0f);
        }
    }
}
