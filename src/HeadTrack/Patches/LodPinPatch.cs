using System.Reflection;
using Game.Rendering;
using HarmonyLib;
using UnityEngine.Rendering;

namespace HeadTrack.Patches
{
    /// <summary>
    /// While the camera is shifted, every LOD consumer of the game gets the
    /// field of view the engine reported before the shift, whatever the engine
    /// makes of an explicit or re-lensed projection (31.74 degrees from the
    /// explicit matrix against the physical camera's 37.38).
    /// </summary>
    internal static class LodPinPatch
    {
        public static bool Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(typeof(CameraUpdateSystem), nameof(CameraUpdateSystem.TryGetLODParameters));
            if (target == null)
            {
                return false;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(LodPinPatch), nameof(Postfix)));
            return true;
        }

        public static void Postfix(bool __result, ref LODParameters lodParameters)
        {
            if (__result && Runtime.LodPinActive)
            {
                Runtime.ShiftedLodFov = lodParameters.fieldOfView;
                lodParameters.fieldOfView = Runtime.VanillaLodFov;
            }
        }
    }
}
