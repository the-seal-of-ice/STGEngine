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
        private readonly System.Collections.Generic.List<GameObject> _created = new();

        private GameObject Create(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // 销毁场景中已有的 MainCamera，避免 Camera.main 指向错误对象
            foreach (var cam in GameObject.FindGameObjectsWithTag("MainCamera"))
                Object.DestroyImmediate(cam);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        [UnityTest]
        public IEnumerator CameraScriptPlayer_Play_AppliesFirstKeyframeWorldPose()
        {
            var cameraObject = Create("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<UnityEngine.Camera>();
            camera.fieldOfView = 55f;

            var player = Create("Player");
            player.transform.position = new Vector3(10f, 20f, 30f);

            var playerComponent = cameraObject.AddComponent<CameraScriptPlayer>();
            playerComponent.Initialize(new StaticFrameProvider(player.transform.position, Vector3.right, Vector3.up, Vector3.forward));

            var script = CreateSingleFrameScript(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f), 42f);

            playerComponent.Play(script);

            // 单关键帧脚本 (duration=0) 需要 2 帧完成：
            // 第 1 帧：写入关键帧值，_elapsed 仍为 0
            // 第 2 帧：_elapsed > 0，状态转为 Idle
            yield return null;

            // 第 1 帧后关键帧值已写入相机
            Assert.That(Vector3.Distance(camera.transform.position, new Vector3(11f, 22f, 33f)), Is.LessThan(0.01f));
            Assert.That(camera.transform.rotation.eulerAngles.y, Is.EqualTo(90f).Within(1f));
            Assert.That(camera.fieldOfView, Is.EqualTo(42f).Within(0.1f));

            yield return null;

            // 第 2 帧后演出结束
            Assert.That(playerComponent.IsActive, Is.False);
        }

        [UnityTest]
        public IEnumerator CameraScriptPlayer_Play_SuppressesPlayerCameraAndRestoresAfterCompletion()
        {
            var player = Create("Player");
            player.transform.position = Vector3.zero;

            var cameraObject = Create("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<UnityEngine.Camera>();
            var playerCamera = cameraObject.AddComponent<STGEngine.Runtime.Player.PlayerCamera>();
            playerCamera.SetTarget(player.transform);

            var playerComponent = cameraObject.AddComponent<CameraScriptPlayer>();
            playerComponent.Initialize(new StaticFrameProvider(player.transform.position, Vector3.right, Vector3.up, Vector3.forward));

            var script = CreateSingleFrameScript(new Vector3(0f, 1f, 5f), Quaternion.identity, 40f);

            playerComponent.Play(script);

            // Play 后第 1 帧：CameraScriptPlayer 在 LateUpdate 中接管相机并 suppress PlayerCamera
            yield return null;
            Assert.That(playerCamera.Suppressed, Is.True);
            Assert.That(playerComponent.IsActive, Is.True);

            // 第 2 帧：_elapsed > 0，演出结束，恢复 PlayerCamera
            yield return null;

            Assert.That(playerCamera.Suppressed, Is.False);
            Assert.That(playerComponent.IsActive, Is.False);
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
