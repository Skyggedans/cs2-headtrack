using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Cinemachine;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Rendering;
using Game.SceneFlow;
using Game.Simulation;
using Game.UI.InGame;
using HeadTrack.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeadTrack.Systems
{
    /// <summary>
    /// Runs immediately after the game's CameraUpdateSystem (PreCulling): takes
    /// the camera exactly as the Cinemachine brain left it, asks the core where
    /// the head-coupled camera stands and how its frustum looks, and writes it
    /// — position (rotation untouched), far plane, and the off-axis projection,
    /// as an explicit matrix or through the physical lens. Everything after
    /// this in the frame (CPU pre-culling, UI, BRG, HDRP and the next frame's
    /// mouse ray) sees the head-coupled camera; Restore undoes it next frame.
    /// </summary>
    public sealed class HeadTrackApplySystem : GameSystemBase
    {
        /// <summary>Sensor height used by the physical path (mm); only ratios matter with gate fit None.</summary>
        private const double PhysicalSensorHeight = 24.0;

        private readonly List<PoseSample> m_Samples = new List<PoseSample>(64);
        private readonly ObstacleLimiter m_Obstacles = new ObstacleLimiter();
        private CameraUpdateSystem m_CameraUpdate = null!;
        private TerrainSystem m_Terrain = null!;
        private WaterSystem m_Water = null!;
        private RaycastSystem m_Raycast = null!;
        private Action<CameraUpdateSystem, float3>? m_SetCachedPosition;
        private Func<Vec3, Vec3> m_Clamp = null!;
        private Camera? m_LastCamera;
        private GameMode m_LastMode;
        private IGameCameraController? m_LastController;
        private Vec3 m_Base;
        private Vec3 m_GroundWanted;
        private bool m_HasGroundWanted;
        private ObstacleCast m_PendingCast;
        private bool m_CastPending;
        private bool m_HasObstacleUpdate;
        private double m_LastObstacleUpdate;
        private bool m_DragOverWorld;
        private double m_LastDepth = 100;
        private TerrainHeightData m_Height;
        private WaterSurfacesData m_Surfaces;
        private WaterSurfaceData<half> m_MaxHeight;
        private bool m_HasTerrain;
        private bool m_HasWater;
        private double m_FloorOffset;
        private double m_NextStatus;
        private long m_StatusSamples;
        private long m_StatusFrames;
        private long m_StatusApplied;
        private double m_StatusStart;

        protected override void OnCreate()
        {
            base.OnCreate();
            Runtime.World = World;
            m_CameraUpdate = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_Terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_Water = World.GetOrCreateSystemManaged<WaterSystem>();
            m_Raycast = World.GetOrCreateSystemManaged<RaycastSystem>();
            m_Clamp = ClampPosition;
            MethodInfo? setter = typeof(CameraUpdateSystem)
                .GetProperty("position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetSetMethod(true);
            if (setter != null)
            {
                m_SetCachedPosition = (Action<CameraUpdateSystem, float3>)Delegate.CreateDelegate(typeof(Action<CameraUpdateSystem, float3>), setter);
            }
            else
            {
                Mod.Log.Warn("CameraUpdateSystem.position has no setter: water, clouds and VFX stay centred on the vanilla camera");
            }
        }

        protected override void OnUpdate()
        {
            try
            {
                Step();
            }
            catch (Exception e)
            {
                Mod.Log.Error($"HeadTrackApplySystem failed and is disabled: {e}");
                Runtime.CoverageActive = false;
                Runtime.LodPinActive = false;
                CameraRestorer.Restore();
                Enabled = false;
            }
        }

        private void Step()
        {
            double now = FrameTime();
            // Read before draining: every packet up to this arrival is then
            // taken, so it is a repeat of a sample already fed, never a new
            // pose still in the ring.
            long lastArrival = Runtime.Ring.LastArrival;
            m_Samples.Clear();
            Runtime.LastSequence = Runtime.Ring.TakeNewer(Runtime.LastSequence, m_Samples);
            foreach (PoseSample sample in m_Samples)
            {
                double previous = sample.PreviousArrival > 0 ? Runtime.TicksToSeconds(sample.PreviousArrival) : double.NaN;
                Runtime.Head.AddPose(Runtime.TicksToSeconds(sample.Timestamp), sample.Pose, previous);
            }
            if (lastArrival > 0)
            {
                // Repeats of the previous pose were dropped by the ring, but a
                // still head sends nothing else: they keep it present.
                Runtime.Head.MarkAlive(Runtime.TicksToSeconds(lastArrival));
            }
            m_StatusSamples += m_Samples.Count;

            Runtime.CoverageActive = false;
            Runtime.LodPinActive = false;
            double? clear = TakeObstacleResult();

            GameManager gameManager = GameManager.instance;
            Camera camera = m_CameraUpdate.activeCamera;
            GameMode mode = gameManager != null ? gameManager.gameMode : GameMode.None;
            bool inGame = gameManager != null && mode == GameMode.Game && !gameManager.isGameLoading;
            IGameCameraController controller = m_CameraUpdate.activeCameraController;
            // A camera, mode or controller change (photo mode, a cinematic
            // camera) drops the effect at once; it ramps in again after.
            bool cut = camera != m_LastCamera || mode != m_LastMode || !ReferenceEquals(controller, m_LastController);
            m_LastCamera = camera;
            m_LastMode = mode;
            m_LastController = controller;
            if (!inGame || m_CameraUpdate.activeViewer == null || camera == null)
            {
                return;
            }

            CameraController gameplay = m_CameraUpdate.gamePlayController;
            bool isGameplay = gameplay != null && controller != null && ReferenceEquals(controller, gameplay);
            bool gateOpen = Runtime.Enabled && isGameplay && !MapTilesUISystem.mapTileViewActive && !BrainBlending();

            Transform transform = camera.transform;
            FrustumTangents rendered = UnityConvert.Tangents(camera.projectionMatrix);
            var frame = new CameraFrame(transform.position.ToVec3(), transform.right.ToVec3(), transform.up.ToVec3(),
                transform.forward.ToVec3(), camera.nearClipPlane, camera.farClipPlane, rendered);
            if (isGameplay && gameplay != null)
            {
                m_LastDepth = WindowDepth.AimPoint(gameplay.pivot.ToVec3(), frame.Position, frame.Forward, gameplay.zoom,
                    frame.Near, frame.Far, gameplay.zoomRange.min);
            }
            PrepareClamp(gameplay);
            UpdateObstacles(now, cut, clear);
            m_Base = frame.Position;
            m_HasGroundWanted = false;
            bool freeze = Runtime.ForceFreeze || Dragging();

            FrameResult result = Runtime.Head.Compute(now, frame, m_LastDepth, gateOpen, cut, m_Clamp, freeze);
            if (result.Weight > 0)
            {
                QueueObstacleCast(frame.Near);
            }
            Runtime.LastFrame = result;
            Runtime.LastRendered = rendered;
            Runtime.LastVanillaPosition = transform.position;
            Runtime.FramesSeen++;
            m_StatusFrames++;
            if (result.Apply)
            {
                if (Runtime.PinLodFov && camera.TryGetCullingParameters(out ScriptableCullingParameters culling))
                {
                    Runtime.VanillaLodFov = culling.lodParameters.fieldOfView;
                }
                Write(camera, result.View);
                if (Runtime.PinLodFov)
                {
                    Runtime.LodPinActive = true;
                }
                m_SetCachedPosition?.Invoke(m_CameraUpdate, (float3)result.View.Position.ToVector3());
                Runtime.CoverageActive = true;
                Runtime.CoverageRendered = rendered;
                Runtime.CoverageView = result.View.Tangents;
                Runtime.FramesApplied++;
                m_StatusApplied++;
                if (result.Clamped)
                {
                    Runtime.FramesClamped++;
                }
            }
            LogStatus(now, result);
        }

        private static void Write(Camera camera, OffAxisView view)
        {
            Runtime.Snapshot = new CameraSnapshot
            {
                Camera = camera,
                Position = camera.transform.position,
                Far = camera.farClipPlane,
                Physical = camera.usePhysicalProperties,
                SensorSize = camera.sensorSize,
                GateFit = camera.gateFit,
                FocalLength = camera.focalLength,
                LensShift = camera.lensShift,
                WroteMatrix = Runtime.Path == ProjectionPath.Explicit,
            };
            Runtime.Dirty = true;
            camera.farClipPlane = (float)view.Far;
            camera.transform.position = view.Position.ToVector3();
            if (Runtime.Path == ProjectionPath.Explicit)
            {
                // On a physical camera HDRP builds the view rays of the sky,
                // fog and clouds from the symmetric lens; without it, from the
                // matrix. Restore turns it back on.
                camera.usePhysicalProperties = false;
                camera.projectionMatrix = UnityConvert.ToMatrix(view.Matrix);
                return;
            }
            PhysicalLens lens = PhysicalLens.FromTangents(view.Tangents, PhysicalSensorHeight);
            camera.usePhysicalProperties = true;
            camera.gateFit = Camera.GateFitMode.None;
            camera.sensorSize = new Vector2((float)lens.SensorWidth, (float)lens.SensorHeight);
            camera.focalLength = (float)lens.FocalLength;
            camera.lensShift = new Vector2((float)lens.ShiftX, (float)lens.ShiftY);
        }

        private static bool BrainBlending()
        {
            CinemachineCore core = CinemachineCore.Instance;
            return core.BrainCount > 0 && core.GetActiveBrain(0).IsBlending;
        }

        /// <summary>Fetches the terrain and water heights the clamp samples, as the game's own camera does each frame.</summary>
        private void PrepareClamp(CameraController? gameplay)
        {
            m_Height = m_Terrain.GetHeightData();
            m_HasTerrain = m_Height.isCreated;
            m_HasWater = false;
            if (m_Water.Loaded)
            {
                m_Surfaces = m_Water.GetSurfacesData(out JobHandle surfaces);
                surfaces.Complete();
                m_MaxHeight = m_Water.GetMexHeightSurfaceData(out JobHandle maxHeight);
                maxHeight.Complete();
                m_HasWater = m_Surfaces.depths.isCreated;
            }
            double zoomMin = gameplay != null ? gameplay.zoomRange.min : 10.0;
            double zoom = gameplay != null ? gameplay.zoom : zoomMin;
            m_FloorOffset = 0.5 * zoomMin + 0.1 * Math.Max(0, zoom - zoomMin);
        }

        /// <summary>The start of this frame on the clock the samples are stamped with, wherever in the frame this system runs.</summary>
        private static double FrameTime() =>
            UnityEngine.Time.unscaledTimeAsDouble + (Runtime.Now - UnityEngine.Time.realtimeSinceStartupAsDouble);

        /// <summary>
        /// A mouse button held since a press over the world, not the UI: a
        /// tool is being dragged or the camera turned, so the world must stay
        /// put under the cursor.
        /// </summary>
        private bool Dragging()
        {
            UnityEngine.InputSystem.Mouse? mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || !(mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed))
            {
                m_DragOverWorld = false;
                return false;
            }
            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
            {
                Game.Input.InputManager input = Game.Input.InputManager.instance;
                m_DragOverWorld = input != null && input.controlOverWorld;
            }
            return m_DragOverWorld;
        }

        /// <summary>
        /// What the segment cast last frame found: the clear length along it
        /// (+∞ when it hit nothing), or null when nothing was cast or the
        /// result is missing.
        /// </summary>
        private double? TakeObstacleResult()
        {
            if (!m_CastPending)
            {
                return null;
            }
            m_CastPending = false;
            NativeArray<RaycastResult> results = m_Raycast.GetResult(this);
            if (!results.IsCreated || results.Length == 0)
            {
                return null;
            }
            RaycastResult hit = results[0];
            double clear = hit.m_Owner != Entity.Null ? m_PendingCast.ClearLength(hit.m_Hit.m_NormalizedDistance) : double.PositiveInfinity;
            Runtime.LastObstacleClear = clear;
            return clear;
        }

        private void UpdateObstacles(double now, bool cut, double? clear)
        {
            double dt = m_HasObstacleUpdate ? Math.Min(0.1, Math.Max(0, now - m_LastObstacleUpdate)) : 0;
            m_HasObstacleUpdate = true;
            m_LastObstacleUpdate = now;
            if (cut)
            {
                m_Obstacles.Reset();
                return;
            }
            // The allowance grows back no faster than the head may move the camera.
            double scale = Runtime.LastFrame.Window.Scale;
            double metresPerMm = scale > 0 && !double.IsInfinity(scale) ? scale : 1.0;
            m_Obstacles.Update(dt, clear, Runtime.Settings.Signal.MaxSpeed * metresPerMm);
            Runtime.ObstacleAllowed = m_Obstacles.Allowed;
        }

        /// <summary>Casts the move the head asks for this frame; the result comes with the next frame.</summary>
        private void QueueObstacleCast(double near)
        {
            if (!m_HasGroundWanted || !ObstacleCast.TryCreate(m_Base, m_GroundWanted, 2 * near, out ObstacleCast cast))
            {
                return;
            }
            m_Raycast.AddInput(this, new RaycastInput
            {
                m_Line = new Line3.Segment(ToFloat3(cast.Start), ToFloat3(cast.End)),
                m_TypeMask = TypeMask.StaticObjects | TypeMask.Net,
                m_CollisionMask = CollisionMask.OnGround | CollisionMask.Overground,
                m_NetLayerMask = Game.Net.Layer.All,
            });
            m_PendingCast = cast;
            m_CastPending = true;
        }

        /// <summary>
        /// Above ground and water as the game keeps its own camera, and no
        /// farther from it along the move than the last cast found clear.
        /// </summary>
        private Vec3 ClampPosition(Vec3 wanted)
        {
            Vec3 ground = ClampToGround(wanted);
            m_GroundWanted = ground;
            m_HasGroundWanted = true;
            return ClampToGround(m_Obstacles.Limit(m_Base, ground));
        }

        private static float3 ToFloat3(Vec3 v) => new float3((float)v.X, (float)v.Y, (float)v.Z);

        /// <summary>Keeps the camera at least as high above ground or water as the game keeps its own.</summary>
        private Vec3 ClampToGround(Vec3 position)
        {
            if (!m_HasTerrain)
            {
                return position;
            }
            var point = new float3((float)position.X, (float)position.Y, (float)position.Z);
            float ground = m_HasWater
                ? WaterUtils.SampleHeight(ref m_MaxHeight, ref m_Surfaces, ref m_Height, point)
                : TerrainUtils.SampleHeight(ref m_Height, point);
            double floor = ground + m_FloorOffset;
            return position.Y < floor ? new Vec3(position.X, floor, position.Z) : position;
        }

        private void LogStatus(double now, FrameResult result)
        {
            if (now < m_NextStatus)
            {
                return;
            }
            double span = now - m_StatusStart;
            if (m_StatusStart > 0 && span > 0)
            {
                HeadSignalOutput signal = result.Signal;
                UdpPoseReceiver? receiver = Runtime.Receiver;
                Vec3 shift = result.Apply ? result.View.Position - Runtime.LastVanillaPosition.ToVec3() : Vec3.Zero;
                Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "status: {0:F1} samples/s, age {1:F2} s, presence {2:F2}, weight {3:F2}, applied {4:P0}, eye {5} mm, depth {6:F0} m, scale {7:F3} m/mm, shift {8:F1} m, rx {9} rejected {10} repeats {11} lost {12} restarts {13} period {14:F1} ms, allowed {15:F1} m, frozen {16}",
                    m_StatusSamples / span, signal.Age, signal.Presence, result.Weight,
                    m_StatusFrames > 0 ? (double)m_StatusApplied / m_StatusFrames : 0, result.Eye, result.Depth,
                    result.Window.Scale, shift.Length, receiver?.Received ?? 0, receiver?.Rejected ?? 0,
                    Runtime.Ring.Duplicates, Runtime.Ring.Overwritten, Runtime.Head.Signal.Restarts, signal.Period * 1000,
                    m_Obstacles.Allowed, result.Frozen));
            }
            m_StatusStart = now;
            m_StatusSamples = 0;
            m_StatusFrames = 0;
            m_StatusApplied = 0;
            m_NextStatus = now + Runtime.StatusSeconds;
        }
    }
}
