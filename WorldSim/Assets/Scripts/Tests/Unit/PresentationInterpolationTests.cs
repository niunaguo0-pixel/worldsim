using NUnit.Framework;
using UnityEngine;
using WorldSim.Presentation;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Core.Slice;
using WorldSim.Simulation.Time;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic6")]
    public class PresentationInterpolationTests
    {
        [TestCase(0.0, 0, 2.0, 0.0)]
        [TestCase(1.0, 0, 2.0, 0.5)]
        [TestCase(2.0, 0, 2.0, 1.0)]
        [TestCase(2.5, 1, 2.0, 0.25)]
        public void BoundaryAlpha_MapsClockIntoUnitInterval(
            double clock, int index, double seconds, double expected)
        {
            Assert.AreEqual(expected, PresentationInterpolator.BoundaryAlpha(clock, index, seconds), 1e-12);
        }

        [Test]
        public void SmoothStep_IsZeroOneAndMidpointHalf()
        {
            Assert.AreEqual(0.0, PresentationInterpolator.SmoothStep(0.0), 1e-12);
            Assert.AreEqual(1.0, PresentationInterpolator.SmoothStep(1.0), 1e-12);
            Assert.AreEqual(0.5, PresentationInterpolator.SmoothStep(0.5), 1e-12);
        }

        [Test]
        public void WorldView_Sync_InterpolatesPopulationBetweenBoundarySamples()
        {
            var world = WorldState.CreateMinimalSlice(3);
            world.Settlements.Clear();
            world.Settlements.Add(new SettlementStub { stableId = 1, name = "A", population = 100 });
            world.Resources.Clear();
            world.Resources.Add(new ResourceStub { stableId = 1, name = "Food", currentAmount = 50 });

            var view = new PresentationWorldView();
            var first = view.Sync(world);
            Assert.AreEqual(100.0, first.Population, 1e-9);

            // 模拟跨周：逻辑人口跳变，插值应介于新旧之间
            world.Time.weekIndex = 1;
            world.Time.gameClock = TimeDriver.WEEK_SECONDS * 1.5; // alpha=0.5 within week 1
            world.Settlements[0].population = 200;
            world.Resources[0].currentAmount = 150;

            var mid = view.Sync(world);
            Assert.Greater(mid.Population, 100.0);
            Assert.Less(mid.Population, 200.0);
            Assert.Greater(mid.FoodReserve, 50.0);
            Assert.Less(mid.FoodReserve, 150.0);
            Assert.AreEqual(0.5, mid.Alpha, 1e-12);
        }

        [Test]
        public void WorldView_Sync_NeverWritesBackWorldState()
        {
            var world = WorldState.CreateMinimalSlice(5);
            world.Settlements.Clear();
            world.Settlements.Add(new SettlementStub { stableId = 7, name = "S", population = 42 });
            world.Resources.Clear();
            world.Resources.Add(new ResourceStub { stableId = 1, name = "Food", currentAmount = 9 });
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);

            var view = new PresentationWorldView();
            for (int i = 0; i < 8; i++)
            {
                world.Time.gameClock = i * 0.1;
                view.Sync(world);
            }

            Assert.AreEqual(42.0, world.Settlements[0].population, 1e-12);
            Assert.AreEqual(9.0, world.Resources[0].currentAmount, 1e-12);
            Assert.AreEqual(before, WorldStateSerializer.ComputeMonthlyHash(world));
        }

        [Test]
        public void Evaluate_LerpsEntityAndCameraFields()
        {
            var from = new PresentationLogicSample
            {
                TotalPopulation = 0, FoodReserve = 0,
                EntityPosX = 0, EntityPosY = 0, EntityPosZ = 0,
                ResourceVisualAmount = 0,
                CameraFocusX = 0, CameraFocusY = 0, CameraFocusZ = 0, CameraDistance = 10
            };
            var to = new PresentationLogicSample
            {
                TotalPopulation = 100, FoodReserve = 20,
                EntityPosX = 2, EntityPosY = 4, EntityPosZ = 6,
                ResourceVisualAmount = 2,
                CameraFocusX = 1, CameraFocusY = 2, CameraFocusZ = 3, CameraDistance = 20
            };
            var snap = PresentationWorldView.Evaluate(from, to, 0.5, 0.5);
            Assert.AreEqual(50.0, snap.Population, 1e-9);
            Assert.AreEqual(1f, snap.EntityPosX, 1e-5f);
            Assert.AreEqual(15f, snap.CameraDistance, 1e-5f);
        }

        [Test]
        public void CameraHint_DoesNotMutateWorldState()
        {
            var world = WorldState.CreateMinimalSlice(8);
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);

            var host = new GameObject("P3_CamHost");
            var camGo = new GameObject("P3_Cam");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                var controller = host.AddComponent<CameraLodController>();
                controller.Bind(cam, null, null, null, null);
                controller.ApplyPresentationCameraHint(new Vector3(1, 0.5f, -1), 16f, 1f);
                Assert.AreEqual(before, WorldStateSerializer.ComputeMonthlyHash(world));
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CameraHint_YieldsToUserPanAndZoom()
        {
            var host = new GameObject("P3_UserCamHost");
            var camGo = new GameObject("P3_UserCam");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                var controller = host.AddComponent<CameraLodController>();
                controller.Bind(cam, null, null, null, null);

                float beforeDistance = controller.TargetDistance;
                Vector3 beforeFocus = controller.TargetFocus;
                controller.Zoom(2f);
                controller.Pan(new Vector2(1.5f, -0.8f));
                Assert.IsTrue(controller.IsUserDrivingCamera);

                controller.ApplyPresentationCameraHint(new Vector3(0f, 0.5f, 0f), beforeDistance, blend: 1f);
                Assert.AreNotEqual(beforeDistance, controller.TargetDistance);
                Assert.AreNotEqual(beforeFocus.x, controller.TargetFocus.x);
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AdvancingOrchestrator_WithWorldView_KeepsMonthlyHashStableAcrossPresentationOnlyReads()
        {
            var world = WorldState.CreateMinimalSlice(13);
            var orch = new SimOrchestrator(world);
            var view = new PresentationWorldView();
            view.Sync(world);
            orch.AdvanceGameTime(TimeDriver.WEEK_SECONDS * 0.5);
            ulong hash = WorldStateSerializer.ComputeMonthlyHash(world);
            view.Sync(world);
            Assert.AreEqual(hash, WorldStateSerializer.ComputeMonthlyHash(world));
        }

        [Test]
        public void Capture_PrefersCivilizationAndEcologyV2OverSliceStubs()
        {
            var world = WorldState.CreateMinimalSlice(17);
            world.Settlements.Clear();
            world.Settlements.Add(new SettlementStub { stableId = 99, name = "Slice", population = 1 });
            world.Resources.Clear();
            world.Resources.Add(new ResourceStub { stableId = 1, name = "Food", currentAmount = 1 });

            WorldSim.Simulation.Ecology.EcologySimEngine.AttachTo(world);
            WorldSim.Simulation.Civilization.CivilizationSimEngine.AttachTo(world);
            world.Civilization.Settlements[0].population = 777;
            world.Civilization.Economies[0].food = 123;

            var sample = PresentationWorldView.Capture(world);
            Assert.AreEqual(777.0, sample.TotalPopulation, 1e-9);
            Assert.AreEqual(123.0, sample.FoodReserve, 1e-9);
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);
            PresentationWorldView.Capture(world);
            Assert.AreEqual(before, WorldStateSerializer.ComputeMonthlyHash(world));
        }
    }
}
