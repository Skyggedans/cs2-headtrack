using System;
using Cinemachine;
using Game.Rendering;
using Game.UI.InGame;

namespace HeadTrack.Systems
{
    /// <summary>
    /// Follows the live camera mode for HeadTrackApplySystem: which mode it is,
    /// whether it may carry the effect, and whether this frame is a hard cut
    /// (the brain switched virtual cameras without a blend).
    /// </summary>
    internal sealed class CameraModeTracker : IDisposable
    {
        private readonly CameraUpdateSystem m_Cameras;
        private readonly GamePanelUISystem m_Panels;
        private readonly GameScreenUISystem m_Screens;
        private IGameCameraController? m_LastController;
        private bool m_BrainCut;

        public CameraModeTracker(CameraUpdateSystem cameras, GamePanelUISystem panels, GameScreenUISystem screens)
        {
            m_Cameras = cameras;
            m_Panels = panels;
            m_Screens = screens;
            // Fired inside the brain's ManualUpdate, i.e. inside CameraUpdateSystem, before Apply runs.
            CinemachineCore.CameraCutEvent.AddListener(OnCameraCut);
        }

        public ViewMode Mode { get; private set; }

        /// <summary>The mode or the controller changed since the last frame.</summary>
        public bool Switched { get; private set; }

        /// <summary>The brain cut to another virtual camera this frame, without a blend.</summary>
        public bool HardCut { get; private set; }

        /// <summary>Pause menu, save, load, options (GameScreen at or above PauseMenu).</summary>
        public bool MenuOpen => m_Screens.isMenuActive;

        public static bool BrainBlending()
        {
            CinemachineCore core = CinemachineCore.Instance;
            return core.BrainCount > 0 && core.GetActiveBrain(0).IsBlending;
        }

        /// <summary>Once per frame, right after CameraUpdateSystem.</summary>
        public void Update()
        {
            IGameCameraController? controller = m_Cameras.activeCameraController;
            ViewMode mode = ViewModes.Classify(m_Cameras, m_Panels);
            Switched = mode != Mode || !ReferenceEquals(controller, m_LastController);
            HardCut = m_BrainCut;
            m_BrainCut = false;
            Mode = mode;
            m_LastController = controller;
        }

        public bool Allowed(bool followCamera, bool photoMode) => Mode switch
        {
            ViewMode.Gameplay => true,
            ViewMode.Follow => followCamera,
            ViewMode.PhotoFree or ViewMode.PhotoOrbit => photoMode,
            _ => false,
        };

        public void Dispose()
        {
            CinemachineCore.CameraCutEvent.RemoveListener(OnCameraCut);
        }

        private void OnCameraCut(CinemachineBrain brain)
        {
            m_BrainCut = true;
        }
    }
}
