using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace HeadTrack.Rendering
{
    /// <summary>
    /// Takes the head-induced part of the camera motion out of motion blur
    /// only; TAA, DLSS/FSR2, SSR/SSGI, AO and clouds keep HDRP's true motion
    /// vectors. The prep kernel's CAMERA_ROT_CLAMP variant subtracts the
    /// screen motion between two matrices, which in the shipped build it reads
    /// from the XR constant buffer (_XRViewProjMatrixNoCameraTrans[0],
    /// _XRPrevViewProjMatrixNoCameraTrans[0]; no other shipped kernel reads
    /// them), and adds it back clamped to _CameraRotationClampNDC. With that
    /// clamp at 0 and the two matrices set to "the previous camera with this
    /// render's head offset and projection" (A) and "the previous camera as it
    /// was" (B), what it subtracts is exactly the head-induced motion.
    /// </summary>
    internal static class HeadMotionBlurPatches
    {
        /// <summary>The game camera.</summary>
        public static Camera? Target;

        /// <summary>
        /// Head offset in force on the camera now, in its local axes (x right,
        /// y up, z forward, metres): Apply sets it, Restore and a vanilla
        /// capture set it to zero.
        /// </summary>
        public static Vector3 CurrentOffset;

        private static Vector3 s_RenderOffset;
        private static Vector3 s_PreviousRenderOffset;
        private static bool s_Active;

        private static AccessTools.FieldRef<HDCamera, HDCamera.ViewConstants[]>? s_XRViews;
        private static AccessTools.StructFieldRef<HDCamera.ViewConstants, Matrix4x4>? s_CurrNoTrans;
        private static AccessTools.FieldRef<object, Vector4>? s_DataParams1;
        private static AccessTools.FieldRef<object, ComputeShader>? s_DataPrepCS;

        public struct Saved
        {
            public bool Valid;
            public Matrix4x4 Prev;
            public Matrix4x4 Curr;
        }

        public static bool Apply(Harmony harmony)
        {
            Type pipeline = typeof(HDRenderPipeline);
            Type? data = AccessTools.Inner(pipeline, "MotionBlurData");
            MethodInfo? prepare = AccessTools.Method(pipeline, "PrepareMotionBlurPassData");
            MethodInfo? xrCB = AccessTools.Method(typeof(HDCamera), "UpdateShaderVariablesXRCB");
            if (data == null || prepare == null || xrCB == null)
            {
                return false;
            }
            s_XRViews = AccessTools.FieldRefAccess<HDCamera, HDCamera.ViewConstants[]>("m_XRViewConstants");
            s_CurrNoTrans = AccessTools.StructFieldRefAccess<HDCamera.ViewConstants, Matrix4x4>("viewProjectionNoCameraTrans");
            s_DataParams1 = AccessTools.FieldRefAccess<Vector4>(data, "motionBlurParams1");
            s_DataPrepCS = AccessTools.FieldRefAccess<ComputeShader>(data, "motionVecPrepCS");
            harmony.Patch(xrCB,
                prefix: new HarmonyMethod(typeof(HeadMotionBlurPatches), nameof(XRConstantsPrefix)),
                finalizer: new HarmonyMethod(typeof(HeadMotionBlurPatches), nameof(XRConstantsFinalizer)));
            harmony.Patch(prepare, postfix: new HarmonyMethod(typeof(HeadMotionBlurPatches), nameof(PreparePostfix)));
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            return true;
        }

        public static void Remove()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        /// <summary>
        /// HDRP fires this per render after HDCamera.Update (view constants and
        /// volumes are current) and before it builds the constant buffers and
        /// records the render graph. Captures count as renders.
        /// </summary>
        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != Target)
            {
                return;
            }
            s_PreviousRenderOffset = s_RenderOffset;
            s_RenderOffset = CurrentOffset;
            MotionBlur settings = HDCamera.GetOrCreate(camera).volumeStack.GetComponent<MotionBlur>();
            // Off: HDRP already drops all camera motion. Another clamp mode:
            // its meaning would change, so leave it alone.
            s_Active = settings.IsActive()
                && settings.cameraMotionBlur.value
                && settings.specialCameraClampMode.value == CameraClampMode.None
                && (s_RenderOffset != Vector3.zero || s_PreviousRenderOffset != Vector3.zero);
        }

        /// <summary>Prefix of internal unsafe void HDCamera.UpdateShaderVariablesXRCB(ref ShaderVariablesXR cb).</summary>
        public static void XRConstantsPrefix(HDCamera __instance, out Saved __state)
        {
            __state = default;
            if (!s_Active || __instance.camera != Target)
            {
                return;
            }
            HDCamera.ViewConstants[] views = s_XRViews!(__instance);
            if (views == null || views.Length == 0)
            {
                return;
            }
            ref HDCamera.ViewConstants vc = ref __instance.mainViewConstants;
            ref HDCamera.ViewConstants xr = ref views[0];
            ref Matrix4x4 curr = ref s_CurrNoTrans!(ref xr);
            __state = new Saved { Valid = true, Prev = xr.prevViewProjMatrixNoCameraTrans, Curr = curr };

            // B: the previous render as HDRP has it (camera-relative, moved to this render's origin).
            Matrix4x4 b = vc.prevViewProjMatrix;
            // A: the previous camera with this render's head offset and projection.
            Vector3 deltaLocal = s_RenderOffset - s_PreviousRenderOffset;
            // prevViewMatrix is rotation only (camera-relative rendering) with
            // Unity's view z flip, so its transpose maps view to world.
            Vector3 deltaWorld = vc.prevViewMatrix.transpose.MultiplyVector(new Vector3(deltaLocal.x, deltaLocal.y, -deltaLocal.z));
            // prevWorldSpaceCameraPos holds E_prev - E_now after HDCamera.UpdateViewConstants.
            Vector3 translation = -vc.prevWorldSpaceCameraPos - deltaWorld;
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(__instance.camera.projectionMatrix, true);
            Matrix4x4 a = projection * vc.prevViewMatrix * Matrix4x4.Translate(translation);

            xr.prevViewProjMatrixNoCameraTrans = b; // -> _XRPrevViewProjMatrixNoCameraTrans[0]
            curr = a;                               // -> _XRViewProjMatrixNoCameraTrans[0]
        }

        public static Exception? XRConstantsFinalizer(Exception? __exception, HDCamera __instance, Saved __state)
        {
            if (__state.Valid)
            {
                ref HDCamera.ViewConstants xr = ref s_XRViews!(__instance)[0];
                xr.prevViewProjMatrixNoCameraTrans = __state.Prev;
                s_CurrNoTrans!(ref xr) = __state.Curr;
            }
            return __exception;
        }

        /// <summary>
        /// Postfix of the private instance method
        /// void PrepareMotionBlurPassData(RenderGraph renderGraph, in RenderGraphBuilder builder, MotionBlurData data, HDCamera hdCamera, TextureHandle source, TextureHandle motionVectors, TextureHandle depthTexture).
        /// </summary>
        public static void PreparePostfix(object data, HDCamera hdCamera)
        {
            if (!s_Active || hdCamera.camera != Target)
            {
                return;
            }
            ComputeShader prep = s_DataPrepCS!(data);
            prep.DisableKeyword("NO_SPECIAL_CLAMP");
            prep.EnableKeyword("CAMERA_ROT_CLAMP");
            s_DataParams1!(data).w = 0f; // _CameraRotationClampNDC: what is subtracted is not added back
        }
    }
}
