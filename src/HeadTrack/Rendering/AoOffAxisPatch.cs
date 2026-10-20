using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace HeadTrack.Rendering
{
    /// <summary>
    /// GTAO rebuilds view-space positions from pixel coordinates with
    /// _AODepthToViewParams, which HDRP fills from projMatrix[1,1] and the
    /// aspect only. For an off-axis projection the offset terms (z, w) must
    /// come from the frustum's left and bottom tangents.
    /// </summary>
    internal static class AoOffAxisPatch
    {
        private static FieldInfo? s_Cb;
        private static FieldInfo? s_DepthToView;
        private static FieldInfo? s_BufferSize;

        public static bool Apply(Harmony harmony)
        {
            Type pipeline = typeof(HDRenderPipeline);
            Type? parameters = AccessTools.Inner(pipeline, "RenderAOParameters");
            Type? cbType = AccessTools.TypeByName("UnityEngine.Rendering.HighDefinition.ShaderVariablesAmbientOcclusion");
            MethodInfo? target = AccessTools.Method(pipeline, "PrepareRenderAOParameters");
            if (parameters == null || cbType == null || target == null)
            {
                return false;
            }
            s_Cb = AccessTools.Field(parameters, "cb");
            s_DepthToView = AccessTools.Field(cbType, "_AODepthToViewParams");
            s_BufferSize = AccessTools.Field(cbType, "_AOBufferSize");
            if (s_Cb == null || s_DepthToView == null || s_BufferSize == null)
            {
                return false;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(AoOffAxisPatch), nameof(Postfix)));
            return true;
        }

        /// <summary>
        /// Postfix of the private instance method
        /// RenderAOParameters PrepareRenderAOParameters(HDCamera camera, Vector2 historySize, in HDUtils.PackedMipChainInfo depthMipInfo).
        /// The struct is returned by value and its type is private, so it
        /// comes in boxed: Harmony 2.2.2 boxes the result for a
        /// "ref object __result" and unboxes it back after the postfix.
        /// </summary>
        public static void Postfix(HDCamera camera, ref object __result)
        {
            // Unity convention (no y flip), without TAA jitter, like the
            // vanilla code, which ignores the jitter too.
            Matrix4x4 p = camera.camera.projectionMatrix;
            if (p.m02 == 0f && p.m12 == 0f)
            {
                return; // symmetric: HDRP's own values are exact
            }
            object cb = s_Cb!.GetValue(__result);
            Vector4 size = (Vector4)s_BufferSize!.GetValue(cb); // xy: the AO pass's running resolution
            var depthToView = new Vector4(
                2f / (p.m00 * size.x),  // (tanR - tanL) / W
                2f / (p.m11 * size.y),  // (tanT - tanB) / H
                (1f - p.m02) / p.m00,   // -tanL
                (1f - p.m12) / p.m11);  // -tanB: positionSS.y = 0 is the bottom row (HDRP screen space is y-up)
            s_DepthToView!.SetValue(cb, depthToView); // edits the boxed ShaderVariablesAmbientOcclusion
            s_Cb.SetValue(__result, cb);              // and writes it back into the boxed RenderAOParameters
        }
    }
}
