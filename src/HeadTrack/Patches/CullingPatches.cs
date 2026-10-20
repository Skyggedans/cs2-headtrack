using System;
using Game.Rendering;
using HarmonyLib;
using HeadTrack.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeadTrack.Patches
{
    /// <summary>
    /// Keeps the game's CPU pre-culling in step with the off-axis frustum.
    ///
    /// PreCullingSystem and AreaBatchSystem each call
    /// FrustumPlanes.BuildPlanePacket twice per update: first for this frame,
    /// then with last frame's arguments, and cull incrementally against both
    /// packets. The first call gets covering arguments (CoverageFrustum) when
    /// the camera is shifted this frame; the second replays exactly what was
    /// substituted for the same caller last frame, recognised by its
    /// arguments, bit for bit — so the previous packet is never wider or
    /// narrower than the one actually used, and the frame after the effect
    /// turns off still sees last frame's cover.
    /// </summary>
    internal static class CullingPatches
    {
        private static readonly CallerState s_PreCulling = new CallerState();
        private static readonly CallerState s_AreaBatch = new CallerState();
        private static CallerState? s_Active;

        public static int Substituted { get; private set; }

        public static int Replayed { get; private set; }

        public static void Apply(Harmony harmony)
        {
            Patch(harmony, AccessTools.Method(typeof(PreCullingSystem), "OnUpdate"), nameof(PreCullingPrefix), nameof(CallerPostfix));
            Patch(harmony, AccessTools.Method(typeof(AreaBatchSystem), "OnUpdate"), nameof(AreaBatchPrefix), nameof(CallerPostfix));
            Patch(harmony, AccessTools.Method(typeof(Game.Rendering.FrustumPlanes), nameof(Game.Rendering.FrustumPlanes.BuildPlanePacket)), nameof(BuildPlanePacketPrefix), null);
            Patch(harmony, AccessTools.Method(typeof(CameraUpdateSystem), nameof(CameraUpdateSystem.TryGetLODParameters)), null, nameof(LodParametersPostfix));
        }

        private static void Patch(Harmony harmony, System.Reflection.MethodBase? target, string? prefix, string? postfix)
        {
            if (target == null)
            {
                Mod.Log.Error($"patch target not found for {prefix ?? postfix}");
                return;
            }
            harmony.Patch(target,
                prefix: prefix != null ? new HarmonyMethod(typeof(CullingPatches), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(CullingPatches), postfix) : null);
            Mod.Log.Info($"patched {target.DeclaringType?.Name}.{target.Name}");
        }

        public static void PreCullingPrefix()
        {
            Begin(s_PreCulling);
        }

        public static void AreaBatchPrefix()
        {
            Begin(s_AreaBatch);
        }

        public static void CallerPostfix()
        {
            s_Active = null;
        }

        public static void BuildPlanePacketPrefix(float3 cameraDirection, float3 cameraDirectionRight, float3 cameraPosition, ref float fovFactor, ref float aspectRatio)
        {
            CallerState? state = s_Active;
            if (state == null)
            {
                return;
            }
            int call = state.Calls++;
            var key = new Key(cameraDirection, cameraDirectionRight, cameraPosition, fovFactor);
            if (call == 0)
            {
                if (!Runtime.CoverageActive)
                {
                    return;
                }
                PlanePacketArguments cover = CoverageFrustum.Cover(fovFactor, aspectRatio, Runtime.CoverageRendered, Runtime.CoverageView);
                state.HasCurrent = true;
                state.CurrentKey = key;
                state.CurrentFov = (float)cover.FovFactor;
                state.CurrentAspect = (float)cover.Aspect;
                fovFactor = state.CurrentFov;
                aspectRatio = state.CurrentAspect;
                Runtime.LastCoverFov = fovFactor;
                Runtime.LastCoverAspect = aspectRatio;
                Substituted++;
            }
            else if (call == 1 && state.HasPrevious && state.PreviousKey.Equals(key))
            {
                fovFactor = state.PreviousFov;
                aspectRatio = state.PreviousAspect;
                Replayed++;
            }
        }

        /// <summary>
        /// While the camera is shifted, all of the game's LOD consumers get
        /// the field of view the engine reported before the shift, whatever the
        /// engine makes of an explicit or re-lensed projection.
        /// </summary>
        public static void LodParametersPostfix(bool __result, ref LODParameters lodParameters)
        {
            if (__result && Runtime.LodPinActive)
            {
                Runtime.ShiftedLodFov = lodParameters.fieldOfView;
                lodParameters.fieldOfView = Runtime.VanillaLodFov;
            }
        }

        private static void Begin(CallerState state)
        {
            state.HasPrevious = state.HasCurrent;
            state.PreviousKey = state.CurrentKey;
            state.PreviousFov = state.CurrentFov;
            state.PreviousAspect = state.CurrentAspect;
            state.HasCurrent = false;
            state.Calls = 0;
            s_Active = state;
        }

        private sealed class CallerState
        {
            public int Calls;
            public bool HasCurrent;
            public Key CurrentKey;
            public float CurrentFov;
            public float CurrentAspect;
            public bool HasPrevious;
            public Key PreviousKey;
            public float PreviousFov;
            public float PreviousAspect;
        }

        /// <summary>The unmodified arguments of a call, compared bit for bit.</summary>
        private readonly struct Key : IEquatable<Key>
        {
            private readonly float3 m_Direction;
            private readonly float3 m_Right;
            private readonly float3 m_Position;
            private readonly float m_FovFactor;

            public Key(float3 direction, float3 right, float3 position, float fovFactor)
            {
                m_Direction = direction;
                m_Right = right;
                m_Position = position;
                m_FovFactor = fovFactor;
            }

            public bool Equals(Key other) =>
                Same(m_Direction, other.m_Direction) && Same(m_Right, other.m_Right) && Same(m_Position, other.m_Position)
                && BitConverter.SingleToInt32Bits(m_FovFactor) == BitConverter.SingleToInt32Bits(other.m_FovFactor);

            public override bool Equals(object? obj) => obj is Key other && Equals(other);

            public override int GetHashCode() => m_Position.GetHashCode();

            private static bool Same(float3 a, float3 b) =>
                BitConverter.SingleToInt32Bits(a.x) == BitConverter.SingleToInt32Bits(b.x)
                && BitConverter.SingleToInt32Bits(a.y) == BitConverter.SingleToInt32Bits(b.y)
                && BitConverter.SingleToInt32Bits(a.z) == BitConverter.SingleToInt32Bits(b.z);
        }
    }
}
