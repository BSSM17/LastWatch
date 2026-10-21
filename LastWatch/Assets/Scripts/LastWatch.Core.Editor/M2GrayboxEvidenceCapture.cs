using System;
using System.IO;
using LastWatch.Core.Space;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastWatch.Core.Editor
{
    public static class M2GrayboxEvidenceCapture
    {
        private const int Width = 1600;
        private const int Height = 900;

        [MenuItem("LastWatch/M2/Capture Graybox Evidence")]
        public static void CaptureAll()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 throw new InvalidOperationException("Cannot resolve LastWatch project root.");
            string repositoryRoot = Directory.GetParent(projectRoot)?.FullName ??
                                    throw new InvalidOperationException("Cannot resolve repository root.");
            string output = Path.Combine(repositoryRoot, "handoffs", "HANDOFF", "ART", "evidence");
            Directory.CreateDirectory(output);

            TowerGeometryFixture normal = TowerGeometryFixture.CreateNormal();
            Capture(normal, Path.Combine(output, "LastWatch_M2_Graybox_Normal_Overview.png"),
                new Vector3(52f, 38f, -60f), new Vector3(0f, 12f, 0f), false, 45f, false);
            Capture(normal, Path.Combine(output, "LastWatch_M2_Graybox_Normal_Top_Grayscale.png"),
                new Vector3(0f, 95f, 0.01f), Vector3.zero, true, 50f, true);
            Capture(normal, Path.Combine(output, "LastWatch_M2_Graybox_Normal_Side_Grayscale.png"),
                new Vector3(55f, 22f, -55f), new Vector3(0f, 14f, 0f), true, 28f, true);
            CaptureFaceDetail(normal, FaceId.FaceA, output);
            CaptureFaceDetail(normal, FaceId.FaceB, output);
            CaptureFaceDetail(normal, FaceId.FaceC, output);
            CaptureUnavailable(FaceId.FaceA, output);
            CaptureUnavailable(FaceId.FaceB, output);
            CaptureUnavailable(FaceId.FaceC, output);

            Debug.Log($"LASTWATCH_M2_GRAYBOX_EVIDENCE_COMPLETE path={output}");
        }

        [MenuItem("LastWatch/M2/Verify Path Smoke")]
        public static void VerifyPathSmoke()
        {
            VerifyFixture("Normal", TowerGeometryFixture.CreateNormal());
            VerifyFixture("FaceAUnavailable", TowerGeometryFixture.CreateOneFaceUnavailable(FaceId.FaceA));
            VerifyFixture("FaceBUnavailable", TowerGeometryFixture.CreateOneFaceUnavailable(FaceId.FaceB));
            VerifyFixture("FaceCUnavailable", TowerGeometryFixture.CreateOneFaceUnavailable(FaceId.FaceC));
            Debug.Log("LASTWATCH_M2_PATH_SMOKE_COMPLETE fixtures=4");
        }

        private static void VerifyFixture(string name, TowerGeometryFixture fixture)
        {
            M2PathSmokeSnapshot result = M2PathSmokeProbe.Execute(fixture);
            foreach (PathSmokeRouteObservation route in result.Routes)
                Debug.Log($"LASTWATCH_M2_PATH_SMOKE_ROUTE fixture={name} route={route.RouteId} " +
                          $"samples={route.CompletedSamples}/{route.TotalSamples} progress={route.Progress:F3} " +
                          $"status={route.CompletionStatus} technicalFailures={route.TechnicalPathOrColliderFailureCount} " +
                          $"falls={route.FallCount} deaths={route.DeathCount}");
            Debug.Log($"LASTWATCH_M2_PATH_SMOKE fixture={name} routes={result.Routes.Count} " +
                      $"complete={result.AllAvailableRoutesCompleted} technicalFailures={result.TechnicalPathOrColliderFailureCount} " +
                      $"falls={result.FallCount} deaths={result.DeathCount}");
            if (!result.AllAvailableRoutesCompleted || result.TechnicalPathOrColliderFailureCount != 0 ||
                result.FallCount != 0 || result.DeathCount != 0)
                throw new InvalidOperationException($"M2 path smoke failed for {name}.");
        }

        private static void CaptureFaceDetail(TowerGeometryFixture fixture, FaceId face, string output)
        {
            Vector3 direction = TowerGeometryFixture.DirectionFor(face);
            Vector3 focus = direction * 5f + Vector3.up * TowerGeometryFixture.UpperDeckY;
            Capture(fixture, Path.Combine(output, $"LastWatch_M2_Graybox_{face}_StationDetail_Grayscale.png"),
                focus + Vector3.up * 20f - direction * 0.01f, focus, true, 8f, true);
        }

        private static void CaptureUnavailable(FaceId face, string output)
        {
            TowerGeometryFixture fixture = TowerGeometryFixture.CreateOneFaceUnavailable(face);
            Capture(fixture, Path.Combine(output, $"LastWatch_M2_Graybox_{face}_Unavailable_Top_Grayscale.png"),
                new Vector3(0f, 65f, 0.01f), Vector3.up * TowerGeometryFixture.UpperDeckY, true, 9f, true);
        }

        private static void Capture(TowerGeometryFixture fixture, string path, Vector3 cameraPosition,
            Vector3 focus, bool orthographic, float viewSize, bool grayscale)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "M2GrayboxEvidence";

            var root = new GameObject("LastWatch_M2_Graybox");
            root.AddComponent<M2GrayboxBuilder>().Build(fixture);

            var lightObject = new GameObject("KeyLight");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            lightObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);

            var cameraObject = new GameObject("EvidenceCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.11f);
            camera.orthographic = orthographic;
            camera.orthographicSize = viewSize;
            camera.fieldOfView = viewSize;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 250f;
            camera.transform.position = cameraPosition;
            camera.transform.LookAt(focus);

            var renderTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                if (grayscale) ConvertToGrayscale(image);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }

        private static void ConvertToGrayscale(Texture2D image)
        {
            Color32[] pixels = image.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                byte luminance = (byte)Mathf.Clamp(
                    Mathf.RoundToInt(pixels[i].r * 0.2126f + pixels[i].g * 0.7152f + pixels[i].b * 0.0722f),
                    0, 255);
                pixels[i] = new Color32(luminance, luminance, luminance, pixels[i].a);
            }
            image.SetPixels32(pixels);
            image.Apply();
        }
    }
}
