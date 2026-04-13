using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using STGEngine.Core.Scene;
using STGEngine.Core.Timeline;
using STGEngine.Runtime.Scene;

namespace STGEngine.Tests.PlayMode.Camera
{
    public class CameraScriptWorkflowTests
    {
        [UnityTest]
        public IEnumerator CameraScriptPlayer_Play_AppliesFirstKeyframeWorldPose()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 55f;

            var player = new GameObject("Player");
            player.transform.position = new Vector3(10f, 20f, 30f);

            var host = new GameObject("CameraScriptHost");
            var playerComponent = host.AddComponent<CameraScriptPlayer>();
            playerComponent.Initialize(new StaticFrameProvider(player.transform.position, Vector3.right, Vector3.up, Vector3.forward));

            var script = CreateSingleFrameScript(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f), 42f);

            playerComponent.Play(script);
            yield return null;

            Assert.That(camera.transform.position, Is.EqualTo(new Vector3(11f, 22f, 33f)).Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(camera.transform.rotation.eulerAngles.y, Is.EqualTo(90f).Within(0.5f));
            Assert.That(camera.fieldOfView, Is.EqualTo(42f).Within(0.01f));
            Assert.That(playerComponent.IsActive, Is.False);
        }

        [UnityTest]
        public IEnumerator CameraScriptPlayer_Play_SuppressesPlayerCameraAndRestoresAfterCompletion()
        {
            var player = new GameObject("Player");
            player.transform.position = Vector3.zero;

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            var playerCamera = cameraObject.AddComponent<STGEngine.Runtime.Player.PlayerCamera>();
            playerCamera.SetTarget(player.transform);

            var host = new GameObject("CameraScriptHost");
            var playerComponent = host.AddComponent<CameraScriptPlayer>();
            playerComponent.Initialize(new StaticFrameProvider(player.transform.position, Vector3.right, Vector3.up, Vector3.forward));

            var script = CreateSingleFrameScript(new Vector3(0f, 1f, 5f), Quaternion.identity, 40f);

            playerComponent.Play(script);
            Assert.That(playerCamera.Suppressed, Is.True);

            yield return null;
            yield return null;

            Assert.That(playerCamera.Suppressed, Is.False);
            Assert.That(playerComponent.IsActive, Is.False);
            Assert.That(camera.fieldOfView, Is.EqualTo(40f).Within(0.01f));
        }

        private static CameraScriptParams CreateSingleFrameScript(Vector3 positionOffset, Quaternion rotation, float fov)
        {
            return new CameraScriptParams
            {
                BlendIn = 0f,
                BlendOut = 0f,
                Keyframes =
                {
                    new CameraKeyframe
                    {
                        Time = 0f,
                        PositionOffset = positionOffset,
                        Rotation = rotation,
                        FOV = fov
                    }
                }
            };
        }

        private sealed class StaticFrameProvider : ICameraFrameProvider
        {
            public StaticFrameProvider(Vector3 playerWorldPosition, Vector3 frameRight, Vector3 frameUp, Vector3 frameForward)
            {
                PlayerWorldPosition = playerWorldPosition;
                FrameRight = frameRight;
                FrameUp = frameUp;
                FrameForward = frameForward;
            }

            public Vector3 PlayerWorldPosition { get; }
            public Vector3 FrameRight { get; }
            public Vector3 FrameUp { get; }
            public Vector3 FrameForward { get; }
        }
    }
}
