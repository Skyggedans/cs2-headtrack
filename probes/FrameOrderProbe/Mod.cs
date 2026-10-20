using System;
using System.Collections.Generic;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeadTrackProbe
{
    /// <summary>
    /// Registers the probe points: ECS systems around the game's camera
    /// update, a MonoBehaviour for Update/LateUpdate/end of frame, and the
    /// render pipeline callbacks. Nothing here writes to the game.
    /// </summary>
    public sealed class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(HeadTrackProbe)).SetShowsErrorsInUI(false);

        private static GameObject? s_Host;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("OnLoad");
            updateSystem.UpdateBefore<ProbeBeforeCameraSystem, CameraUpdateSystem>(SystemUpdatePhase.PreCulling);
            updateSystem.UpdateAfter<ProbeAfterCameraSystem, CameraUpdateSystem>(SystemUpdatePhase.PreCulling);
            updateSystem.UpdateAfter<ProbeAfterPreCullingSystem, PreCullingSystem>(SystemUpdatePhase.PreCulling);
            updateSystem.UpdateAt<ProbeRaycastSystem>(SystemUpdatePhase.Raycast);
            updateSystem.UpdateAt<ProbeUIUpdateSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<ProbeLateUpdateSystem>(SystemUpdatePhase.LateUpdate);

            s_Host = new GameObject(nameof(HeadTrackProbe));
            UnityEngine.Object.DontDestroyOnLoad(s_Host);
            s_Host.AddComponent<ProbeBehaviour>();

            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            RenderPipelineManager.endContextRendering += OnEndContextRendering;
            Log.Info("probe points registered");
        }

        public void OnDispose()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
            if (s_Host != null)
            {
                UnityEngine.Object.Destroy(s_Host);
                s_Host = null;
            }
            Log.Info("OnDispose");
        }

        private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            Recorder.Guard("beginContextRendering", () =>
            {
                var names = new List<string>(cameras.Count);
                foreach (var camera in cameras)
                {
                    names.Add(Recorder.Describe(camera));
                }
                Recorder.Point("render.beginContext", full: true, extra: "cameras=[" + string.Join(", ", names) + "]");
            });
        }

        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            Recorder.Guard("beginCameraRendering", () => Recorder.Point("render.beginCamera", camera: camera));
        }

        private static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            Recorder.Guard("endCameraRendering", () => Recorder.Point("render.endCamera", camera: camera));
        }

        private static void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            Recorder.Guard("endContextRendering", () => Recorder.Point("render.endContext"));
        }
    }
}
