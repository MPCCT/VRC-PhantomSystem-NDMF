using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>
    /// Build-only placeholder for a layer control whose target lives in another
    /// controller, or whose source playable is being merged into the final FX controller.
    /// </summary>
    internal sealed class PhantomAnimatorLayerControlMarker : StateMachineBehaviour
    {
        public VRCAvatarDescriptor.AnimLayerType targetPlayable;
        public string targetLayerName;
        public float goalWeight;
        public float blendDuration;
        public string debugString;
    }
}
