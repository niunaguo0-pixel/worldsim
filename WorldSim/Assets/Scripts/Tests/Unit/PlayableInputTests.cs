using NUnit.Framework;
using UnityEngine;
using WorldSim.Presentation;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic6")]
    [Category("Input")]
    public class PlayableInputTests
    {
        [Test]
        public void ControlMap_CycleWrapsBothDirections()
        {
            Assert.AreEqual(0, PlayableControlMap.CycleInterveneIndex(5, 1, 6));
            Assert.AreEqual(5, PlayableControlMap.CycleInterveneIndex(0, -1, 6));
            Assert.AreEqual(2, PlayableControlMap.CycleInterveneIndex(0, 8, 6));
        }

        [Test]
        public void ControlMap_HelpText_CoversCameraTimeIntervene()
        {
            string help = PlayableControlMap.HelpText();
            StringAssert.Contains("WASD", help);
            StringAssert.Contains("Space", help);
            StringAssert.Contains("干预", help);
            StringAssert.Contains("滚轮", help);
        }

        [Test]
        public void ControlMap_IntervenePresets_HaveStableOrder()
        {
            Assert.AreEqual(6, PlayableControlMap.IntervenePresets.Length);
            Assert.AreEqual("rainfall_0", PlayableControlMap.IntervenePresets[0].Key);
            Assert.IsTrue(PlayableControlMap.IntervenePresets[3].IsEmergency);
        }

        [Test]
        public void Camera_PanAndReset_AreDeterministicForPresentation()
        {
            var host = new GameObject("PlayableInputTests_CamHost");
            var camGo = new GameObject("PlayableInputTests_Cam");
            try
            {
                var camera = camGo.AddComponent<Camera>();
                var controller = host.AddComponent<CameraLodController>();
                controller.Bind(camera, null, null, null, null);

                Vector3 before = controller.TargetFocus;
                controller.Pan(new Vector2(1.5f, -0.5f));
                Assert.AreEqual(before.x + 1.5f, controller.TargetFocus.x, 1e-4f);
                Assert.AreEqual(before.z - 0.5f, controller.TargetFocus.z, 1e-4f);

                controller.ResetView();
                Assert.AreEqual(0.5f, controller.TargetFocus.y, 1e-4f);
                Assert.AreEqual(14f, controller.TargetDistance, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Camera_Zoom_StillUpdatesLodBeforeSmoothing()
        {
            var host = new GameObject("PlayableInputTests_ZoomHost");
            var camGo = new GameObject("PlayableInputTests_ZoomCam");
            try
            {
                var camera = camGo.AddComponent<Camera>();
                var controller = host.AddComponent<CameraLodController>();
                controller.Bind(camera, null, null, null, null);
                controller.Zoom(1f);
                Assert.AreEqual(15.6f, controller.TargetDistance, 0.0001f);
                Assert.AreEqual(CameraLodLevel.Civilization, controller.CurrentLod);
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(host);
            }
        }
    }
}
