using System.Collections;
using UnityEngine;

namespace HeadTrackProbe
{
    /// <summary>Probe points outside the ECS update: Update, LateUpdate and the end of the frame.</summary>
    public sealed class ProbeBehaviour : MonoBehaviour
    {
        private void Start()
        {
            StartCoroutine(EndOfFrameLoop());
        }

        private void Update()
        {
            Recorder.Guard("mono.Update", () => Recorder.Point("mono.Update"));
        }

        private void LateUpdate()
        {
            Recorder.Guard("mono.LateUpdate", () => Recorder.Point("mono.LateUpdate"));
        }

        private IEnumerator EndOfFrameLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                Recorder.Guard("mono.endOfFrame", () => Recorder.Point("mono.endOfFrame"));
            }
        }
    }
}
