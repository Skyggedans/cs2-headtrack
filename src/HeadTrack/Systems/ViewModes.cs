using System.Reflection;
using Game;
using Game.Rendering;
using Game.UI.InGame;
using Game.UI.Menu;
using Unity.Entities;

namespace HeadTrack.Systems
{
    /// <summary>Which of the game's cameras is live, as HeadTrack sees it.</summary>
    internal enum ViewMode
    {
        /// <summary>No controller is enabled.</summary>
        None,

        /// <summary>CameraController: the free city camera.</summary>
        Gameplay,

        /// <summary>The gameplay controller while the map-tile view is open or animating in or out.</summary>
        MapTiles,

        /// <summary>OrbitCameraController in Mode.Follow with a followed entity (cim, vehicle, building).</summary>
        Follow,

        /// <summary>Photo mode with the free CinematicCameraController.</summary>
        PhotoFree,

        /// <summary>Photo mode with the OrbitCameraController (its mode is then PhotoMode).</summary>
        PhotoOrbit,

        /// <summary>The cinematic controller outside photo mode: the benchmark's camera track.</summary>
        Cinematic,

        /// <summary>Anything else (an orbit without an entity outside photo mode, editor cameras).</summary>
        Other,
    }

    internal static class ViewModes
    {
        /// <summary>CameraController.m_MapViewTimer: above 0 while the map-tile view is open or still animating in or out.</summary>
        private static readonly FieldInfo? s_MapViewTimer =
            typeof(CameraController).GetField("m_MapViewTimer", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool HasMapViewTimer => s_MapViewTimer != null;

        public static ViewMode Classify(CameraUpdateSystem cameras, GamePanelUISystem? panels)
        {
            IGameCameraController? active = cameras.activeCameraController;
            if (active == null)
            {
                return ViewMode.None;
            }
            if (BenchmarkUISystem.isBenchmarkRunning)
            {
                return ViewMode.Cinematic;
            }
            bool photo = panels != null && panels.activePanel is PhotoModePanel;
            switch (active)
            {
                case CameraController gameplay:
                    return MapViewBusy(gameplay) ? ViewMode.MapTiles : ViewMode.Gameplay;
                case CinematicCameraController _:
                    return photo ? ViewMode.PhotoFree : ViewMode.Cinematic;
                case OrbitCameraController _ when photo:
                    return ViewMode.PhotoOrbit;
                case OrbitCameraController orbit:
                    return orbit.mode == OrbitCameraController.Mode.Follow && orbit.followedEntity != Entity.Null
                        ? ViewMode.Follow
                        : ViewMode.Other;
                default:
                    return ViewMode.Other;
            }
        }

        private static bool MapViewBusy(CameraController gameplay)
        {
            if (MapTilesUISystem.mapTileViewActive)
            {
                return true;
            }
            return s_MapViewTimer != null && s_MapViewTimer.GetValue(gameplay) is float timer && timer > 0f;
        }
    }
}
