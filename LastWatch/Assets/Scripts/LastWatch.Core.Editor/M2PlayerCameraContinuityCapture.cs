using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using LastWatch.Core.Space;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastWatch.Core.Editor
{
    public static class M2PlayerCameraContinuityCapture
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const int FramesPerSecond = 12;
        private const float FieldOfView = 100f;
        private const float DownwardPitch = 12f;
        private const string OutputFolderName = "M2_PlayerCameraContinuity_v0.1";
        private const string AttackPointOutputFolderName = "M2_PlayerCameraAttackPointPitch_v0.3";

        [Serializable]
        private sealed class FrameManifest
        {
            public int frameIndex;
            public float timeSeconds;
            public float yawDegrees;
            public float pitchDegrees;
            public string file;
            public string sha256;
        }

        [Serializable]
        private sealed class StationSequenceManifest
        {
            public string stationId;
            public string associatedLane;
            public string attackPointContext;
            public string cameraPosition;
            public float yawStartDegrees;
            public float yawEndDegrees;
            public float pitchStartDegrees;
            public float pitchEndDegrees;
            public FrameManifest[] frames;
        }

        [Serializable]
        private sealed class CaptureManifest
        {
            public string schemaVersion;
            public string captureMethod;
            public string sourcePackageSha256;
            public int width;
            public int height;
            public int framesPerSecond;
            public float fieldOfView;
            public bool grayscale;
            public StationSequenceManifest[] sequences;
        }

        [UnityEditor.MenuItem("LastWatch/M2/Capture Player Camera Continuity")]
        public static void CaptureAll()
        {
            string sourcePackageHash = RequiredArgument("-lastWatchSourcePackageSha256");
            RequireSha256(sourcePackageHash);

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 throw new InvalidOperationException("Cannot resolve LastWatch project root.");
            string repositoryRoot = Directory.GetParent(projectRoot)?.FullName ??
                                    throw new InvalidOperationException("Cannot resolve repository root.");
            string output = Path.Combine(repositoryRoot, "handoffs", "HANDOFF", "ART", "evidence", OutputFolderName);
            if (Directory.Exists(output))
                throw new InvalidOperationException("Continuity evidence output already exists: " + output);
            Directory.CreateDirectory(output);

            TowerGeometryFixture fixture = TowerGeometryFixture.CreateNormal();
            var sequences = new List<StationSequenceManifest>
            {
                CaptureSequence(fixture, StationId.S_0, "LaneA|LaneB|LaneC", string.Empty,
                    -180f, 180f, DownwardPitch, DownwardPitch, 49, false, output),
                CaptureSequence(fixture, StationId.S_A, RouteId.LaneA.ToString(), string.Empty,
                    -40f, 40f, DownwardPitch, DownwardPitch, 25, false, output),
                CaptureSequence(fixture, StationId.S_B, RouteId.LaneB.ToString(), string.Empty,
                    80f, 160f, DownwardPitch, DownwardPitch, 25, false, output),
                CaptureSequence(fixture, StationId.S_C, RouteId.LaneC.ToString(), string.Empty,
                    -160f, -80f, DownwardPitch, DownwardPitch, 25, false, output)
            };

            var manifest = new CaptureManifest
            {
                schemaVersion = "1",
                captureMethod = "Editor-only fixed player-eye pivot; ordered cut-free yaw frames; no geometry mutation",
                sourcePackageSha256 = sourcePackageHash.ToUpperInvariant(),
                width = Width,
                height = Height,
                framesPerSecond = FramesPerSecond,
                fieldOfView = FieldOfView,
                grayscale = true,
                sequences = sequences.ToArray()
            };
            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonUtility.ToJson(manifest, true));
            Debug.Log($"LASTWATCH_M2_PLAYER_CAMERA_CONTINUITY_COMPLETE path={output} " +
                      $"stations={sequences.Count} frames={49 + 25 * 3} sourcePackageSha256={sourcePackageHash.ToUpperInvariant()}");
        }

        [UnityEditor.MenuItem("LastWatch/M2/Capture Player Camera AttackPoint Pitch")]
        public static void CaptureAttackPointPitchSweep()
        {
            string sourcePackageHash = RequiredArgument("-lastWatchSourcePackageSha256");
            RequireSha256(sourcePackageHash);

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 throw new InvalidOperationException("Cannot resolve LastWatch project root.");
            string repositoryRoot = Directory.GetParent(projectRoot)?.FullName ??
                                    throw new InvalidOperationException("Cannot resolve repository root.");
            string output = Path.Combine(repositoryRoot, "handoffs", "HANDOFF", "ART", "evidence",
                AttackPointOutputFolderName);
            if (Directory.Exists(output))
                throw new InvalidOperationException("AttackPoint evidence output already exists: " + output);
            Directory.CreateDirectory(output);

            TowerGeometryFixture fixture = TowerGeometryFixture.CreateNormal();
            var sequences = new List<StationSequenceManifest>
            {
                CaptureSequence(fixture, StationId.S_0, "LaneA reference", "AP_A_G|AP_A_C",
                    0f, 0f, 35f, 80f, 19, true, output),
                CaptureSequence(fixture, StationId.S_A, RouteId.LaneA.ToString(), "AP_A_G|AP_A_C",
                    0f, 0f, 35f, 80f, 19, true, output),
                CaptureSequence(fixture, StationId.S_B, RouteId.LaneB.ToString(), "AP_B_G|AP_B_C",
                    120f, 120f, 35f, 80f, 19, true, output),
                CaptureSequence(fixture, StationId.S_C, RouteId.LaneC.ToString(), "AP_C_G|AP_C_C",
                    -120f, -120f, 35f, 80f, 19, true, output)
            };

            var manifest = new CaptureManifest
            {
                schemaVersion = "2",
                captureMethod = "Editor-only fixed player-eye pivot; fixed face yaw; ordered downward pitch frames; no geometry mutation",
                sourcePackageSha256 = sourcePackageHash.ToUpperInvariant(),
                width = Width,
                height = Height,
                framesPerSecond = FramesPerSecond,
                fieldOfView = FieldOfView,
                grayscale = true,
                sequences = sequences.ToArray()
            };
            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonUtility.ToJson(manifest, true));
            Debug.Log($"LASTWATCH_M2_PLAYER_CAMERA_ATTACKPOINT_PITCH_COMPLETE path={output} " +
                      $"stations={sequences.Count} frames={19 * 4} sourcePackageSha256={sourcePackageHash.ToUpperInvariant()}");
        }

        private static StationSequenceManifest CaptureSequence(TowerGeometryFixture fixture, StationId stationId,
            string associatedLane, string attackPointContext, float yawStart, float yawEnd,
            float pitchStart, float pitchEnd, int frameCount, bool applyAttackPointContrast, string outputRoot)
        {
            StationDescriptor station = FindStation(fixture, stationId);
            if (!station.IsAvailable) throw new InvalidOperationException(stationId + " is unavailable.");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "M2PlayerCameraContinuity_" + stationId;
            var root = new GameObject("LastWatch_M2_Graybox");
            root.AddComponent<M2GrayboxBuilder>().Build(fixture);
            if (applyAttackPointContrast) ApplyAttackPointDiagnosticContrast(root);

            var lightObject = new GameObject("KeyLight");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            lightObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);

            Vector3 outward = station.Face.HasValue
                ? TowerGeometryFixture.DirectionFor(station.Face.Value)
                : Vector3.forward;
            Vector3 cameraPosition = station.EyePosition - outward * 1.25f;
            var cameraObject = new GameObject("PlayerEvidenceCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.11f);
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 250f;
            camera.transform.position = cameraPosition;

            string sequenceFolder = Path.Combine(outputRoot, stationId.ToString());
            Directory.CreateDirectory(sequenceFolder);
            var frames = new FrameManifest[frameCount];
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                float progress = frameCount == 1 ? 0f : (float)frameIndex / (frameCount - 1);
                float yaw = Mathf.Lerp(yawStart, yawEnd, progress);
                float pitch = Mathf.Lerp(pitchStart, pitchEnd, progress);
                camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
                byte[] png = RenderGrayscale(camera);
                string fileName = string.Format(CultureInfo.InvariantCulture, "frame_{0:D3}.png", frameIndex);
                File.WriteAllBytes(Path.Combine(sequenceFolder, fileName), png);
                frames[frameIndex] = new FrameManifest
                {
                    frameIndex = frameIndex,
                    timeSeconds = frameIndex / (float)FramesPerSecond,
                    yawDegrees = yaw,
                    pitchDegrees = pitch,
                    file = stationId + "/" + fileName,
                    sha256 = Sha256(png)
                };
            }

            return new StationSequenceManifest
            {
                stationId = stationId.ToString(),
                associatedLane = associatedLane,
                attackPointContext = attackPointContext,
                cameraPosition = string.Format(CultureInfo.InvariantCulture, "({0:R},{1:R},{2:R})",
                    cameraPosition.x, cameraPosition.y, cameraPosition.z),
                yawStartDegrees = yawStart,
                yawEndDegrees = yawEnd,
                pitchStartDegrees = pitchStart,
                pitchEndDegrees = pitchEnd,
                frames = frames
            };
        }

        private static void ApplyAttackPointDiagnosticContrast(GameObject root)
        {
            foreach (M2GrayboxColliderTag tag in root.GetComponentsInChildren<M2GrayboxColliderTag>())
            {
                if (tag.Kind != M2GrayboxColliderKind.AttackCapsule) continue;
                Renderer renderer = tag.GetComponent<Renderer>();
                if (renderer != null) renderer.material.color = new Color(0.12f, 0.14f, 0.16f);
            }
            foreach (LineRenderer line in root.GetComponentsInChildren<LineRenderer>())
            {
                if (!line.name.StartsWith("LOS_", StringComparison.Ordinal)) continue;
                line.material.color = new Color(0.32f, 0.36f, 0.34f);
            }
        }

        private static StationDescriptor FindStation(TowerGeometryFixture fixture, StationId stationId)
        {
            foreach (StationDescriptor station in fixture.Stations)
                if (station.Id == stationId) return station;
            throw new InvalidOperationException("Station is missing: " + stationId);
        }

        private static byte[] RenderGrayscale(Camera camera)
        {
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
                Color32[] pixels = image.GetPixels32();
                for (int index = 0; index < pixels.Length; index++)
                {
                    byte luminance = (byte)Mathf.Clamp(Mathf.RoundToInt(
                        pixels[index].r * 0.2126f + pixels[index].g * 0.7152f + pixels[index].b * 0.0722f), 0, 255);
                    pixels[index] = new Color32(luminance, luminance, luminance, pixels[index].a);
                }
                image.SetPixels32(pixels);
                image.Apply();
                return image.EncodeToPNG();
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }

        private static string RequiredArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
                if (string.Equals(arguments[index], name, StringComparison.Ordinal)) return arguments[index + 1];
            throw new ArgumentException("Required command-line argument is missing: " + name);
        }

        private static void RequireSha256(string value)
        {
            if (value == null || value.Length != 64) throw new ArgumentException("Source package SHA-256 is invalid.");
            for (int index = 0; index < value.Length; index++)
                if (!Uri.IsHexDigit(value[index])) throw new ArgumentException("Source package SHA-256 is invalid.");
        }

        private static string Sha256(byte[] value)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(value)).Replace("-", string.Empty);
        }
    }
}
