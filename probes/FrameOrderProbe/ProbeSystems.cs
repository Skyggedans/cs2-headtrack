using System;
using Game;

namespace HeadTrackProbe
{
    /// <summary>
    /// A managed ECS system that only logs. It uses no SystemAPI, jobs or
    /// Burst, so it needs none of the code the official toolchain generates;
    /// whether it runs at all is one of the things the probe answers.
    /// </summary>
    public abstract class ProbeSystemBase : GameSystemBase
    {
        protected abstract string Label { get; }

        protected virtual bool Full => false;

        protected override void OnCreate()
        {
            base.OnCreate();
            Recorder.Attach(World);
            Mod.Log.Info($"{GetType().Name} created");
        }

        protected override void OnUpdate()
        {
            try
            {
                Recorder.Point(Label, Full);
            }
            catch (Exception e)
            {
                Mod.Log.Error($"{GetType().Name} failed and is disabled: {e}");
                Enabled = false;
            }
        }
    }

    /// <summary>PreCulling, immediately before the game's camera update.</summary>
    public sealed class ProbeBeforeCameraSystem : ProbeSystemBase
    {
        protected override string Label => "ecs.beforeCameraUpdate";
    }

    /// <summary>PreCulling, immediately after the game's camera update.</summary>
    public sealed class ProbeAfterCameraSystem : ProbeSystemBase
    {
        protected override string Label => "ecs.afterCameraUpdate";

        protected override bool Full => true;
    }

    /// <summary>PreCulling, immediately after the CPU pre-culling.</summary>
    public sealed class ProbeAfterPreCullingSystem : ProbeSystemBase
    {
        protected override string Label => "ecs.afterPreCulling";
    }

    /// <summary>Raycast phase, after the tool raycast system built the mouse ray.</summary>
    public sealed class ProbeRaycastSystem : ProbeSystemBase
    {
        protected override string Label => "ecs.raycast";
    }

    /// <summary>UIUpdate phase.</summary>
    public sealed class ProbeUIUpdateSystem : ProbeSystemBase
    {
        protected override string Label => "ecs.uiUpdate";
    }

    /// <summary>LateUpdate phase.</summary>
    public sealed class ProbeLateUpdateSystem : ProbeSystemBase
    {
        protected override string Label => "ecs.lateUpdate";
    }
}
