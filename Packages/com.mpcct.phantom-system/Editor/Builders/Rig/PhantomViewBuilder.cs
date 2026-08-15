using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Builds the per-slot stereo cameras and local full-screen Phantom View display.</summary>
    internal static class PhantomViewBuilder
    {
        private const string DisplayShaderAssetPath =
            "Packages/com.mpcct.phantom-system/Asset/Shader/PhantomView.shader";
        private const int PlayerLocalLayer = 10;
        private const float CaptureVerticalFieldOfView = 90f;
        private const float CaptureAspect = 1f;

        public static void Build(
            BuildContext context,
            PhantomSystemBuildState system,
            PhantomSlotBuildState slot,
            PhantomBuildReport report)
        {
            if (slot.Slot == null || !slot.Slot.enablePhantomView || slot.CloneRoot == null)
            {
                return;
            }

            if (!slot.CloneBones.TryGetValue(HumanBodyBones.Head, out var phantomHead))
            {
                report.InternalError(
                    $"Slot '{slot.SlotId}' enables Phantom View, but its prebaked avatar has no Humanoid Head.",
                    slot.CloneRoot);
                return;
            }

            if (slot.BakedAvatar == null)
            {
                report.InternalError(
                    $"Slot '{slot.SlotId}' enables Phantom View, but its prebaked avatar descriptor is unavailable.",
                    slot.CloneRoot);
                return;
            }

            var baseAnimator = context.AvatarRootObject.GetComponent<Animator>();
            var baseHead = baseAnimator != null
                ? baseAnimator.GetBoneTransform(HumanBodyBones.Head)
                : null;
            if (baseHead == null)
            {
                report.InternalError(
                    $"Slot '{slot.SlotId}' enables Phantom View, but the base avatar has no Humanoid Head.",
                    baseAnimator != null ? (Object)baseAnimator : context.AvatarRootObject);
                return;
            }

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(DisplayShaderAssetPath);
            if (shader == null)
            {
                report.InternalError(
                    $"Phantom View display shader was not found at '{DisplayShaderAssetPath}'.",
                    slot.CloneRoot);
                return;
            }

            var viewsRoot = EnsureViewsRoot(system);
            var viewRoot = ConstraintRigBuilder.EnsureChild(
                viewsRoot.transform,
                slot.HierarchyName);
            viewRoot.gameObject.layer = PlayerLocalLayer;
            slot.PhantomViewRoot = viewRoot;

            // Keep a zero-offset source under the animated Head so the descriptor
            // viewpoint follows Head motion and inherits the Slot's scale naturally.
            // The cameras themselves remain outside MirrorRoot to avoid swapping the
            // stereo eyes when MirrorRoot uses a negative X scale.
            var viewAnchor = ConstraintRigBuilder.EnsureChild(
                phantomHead,
                "PhantomViewAnchor");
            viewAnchor.gameObject.layer = PlayerLocalLayer;
            slot.PhantomViewAnchor = viewAnchor;

            var captureRoot = ConstraintRigBuilder.EnsureChild(
                slot.SlotRoot.transform,
                "PhantomViewCapture");
            captureRoot.gameObject.layer = PlayerLocalLayer;
            // Humanoid Head bone axes are not guaranteed to use Unity's camera-forward
            // convention. Start from the phantom root rotation so the child cameras'
            // local +Z points toward the avatar's default forward direction, then bake
            // the relative rotation into the parent constraint. Position the rig at
            // the descriptor View Position rather than at the Head bone origin.
            var phantomViewPosition = slot.BakedAvatar.transform.TransformPoint(
                slot.BakedAvatar.ViewPosition);
            viewAnchor.SetPositionAndRotation(
                phantomViewPosition,
                slot.CloneRoot.transform.rotation);
            captureRoot.SetPositionAndRotation(
                viewAnchor.position,
                viewAnchor.rotation);
            AddParentConstraint(captureRoot, viewAnchor);
            slot.PhantomViewCaptureRoot = captureRoot;

            EnsureGlobalRenderTextures(context, system);
            var leftTexture = system.PhantomViewLeftTexture;
            var rightTexture = system.PhantomViewRightTexture;
            var initialHalfDistance = PhantomViewAnimatorModule.MaximumStereoStrength
                                      * PhantomViewAnimatorModule.DefaultStereoStrengthParameter
                                      * 0.5f;
            var nearClipPlane = NormalizeNearClipPlane(
                slot.Slot.phantomViewNearClipPlane);
            slot.PhantomViewLeftCamera = CreateCamera(
                captureRoot,
                "LeftEyeCamera",
                -initialHalfDistance,
                leftTexture,
                nearClipPlane);
            slot.PhantomViewRightCamera = CreateCamera(
                captureRoot,
                "RightEyeCamera",
                initialHalfDistance,
                rightTexture,
                nearClipPlane);

            var displayHost = ConstraintRigBuilder.EnsureChild(viewRoot, "Display");
            displayHost.gameObject.layer = PlayerLocalLayer;
            displayHost.SetPositionAndRotation(baseHead.position, baseHead.rotation);
            AddParentConstraint(displayHost, baseHead);
            slot.PhantomViewDisplayHost = displayHost;

            if (system.PhantomViewDisplayMesh == null)
            {
                system.PhantomViewDisplayMesh = CreateDisplayMesh();
                context.AssetSaver.SaveAsset(system.PhantomViewDisplayMesh);
            }

            var meshFilter = displayHost.gameObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = system.PhantomViewDisplayMesh;
            var renderer = displayHost.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateDisplayMaterial(
                context,
                slot,
                shader,
                leftTexture,
                rightTexture);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.enabled = false;
        }

        private static GameObject EnsureViewsRoot(PhantomSystemBuildState system)
        {
            if (system.ViewsRoot != null)
            {
                return system.ViewsRoot;
            }

            system.ViewsRoot = new GameObject("Views");
            system.ViewsRoot.transform.SetParent(system.RuntimeRoot.transform, false);
            system.ViewsRoot.layer = PlayerLocalLayer;
            return system.ViewsRoot;
        }

        private static void EnsureGlobalRenderTextures(
            BuildContext context,
            PhantomSystemBuildState system)
        {
            if (system.PhantomViewLeftTexture == null)
            {
                system.PhantomViewLeftTexture = CreateRenderTexture(
                    context,
                    "Left",
                    system.ProjectSettings.PhantomViewTextureSize);
            }

            if (system.PhantomViewRightTexture == null)
            {
                system.PhantomViewRightTexture = CreateRenderTexture(
                    context,
                    "Right",
                    system.ProjectSettings.PhantomViewTextureSize);
            }
        }

        private static RenderTexture CreateRenderTexture(
            BuildContext context,
            string eyeName,
            int textureSize)
        {
            var texture = new RenderTexture(
                textureSize,
                textureSize,
                16,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default)
            {
                name = $"PhantomView_Global_{eyeName}",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            context.AssetSaver.SaveAsset(texture);
            return texture;
        }

        private static Transform CreateCamera(
            Transform parent,
            string name,
            float localX,
            RenderTexture targetTexture,
            float nearClipPlane)
        {
            var cameraTransform = ConstraintRigBuilder.EnsureChild(parent, name);
            cameraTransform.gameObject.layer = PlayerLocalLayer;
            cameraTransform.localPosition = new Vector3(localX, 0f, 0f);
            cameraTransform.localRotation = Quaternion.identity;
            cameraTransform.localScale = Vector3.one;

            var camera = cameraTransform.gameObject.AddComponent<Camera>();
            camera.targetTexture = targetTexture;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = Color.black;
            camera.fieldOfView = CaptureVerticalFieldOfView;
            camera.aspect = CaptureAspect;
            camera.nearClipPlane = nearClipPlane;
            camera.farClipPlane = 250f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.allowDynamicResolution = false;
            camera.useOcclusionCulling = true;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.depth = -100f;
            camera.cullingMask = ~(1 << PlayerLocalLayer);
            camera.enabled = false;
            return cameraTransform;
        }

        internal static float NormalizeNearClipPlane(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                return PhantomSlot.DefaultPhantomViewNearClipPlane;
            }

            return Mathf.Clamp(
                value,
                PhantomSlot.MinimumPhantomViewNearClipPlane,
                PhantomSlot.MaximumPhantomViewNearClipPlane);
        }

        private static Material CreateDisplayMaterial(
            BuildContext context,
            PhantomSlotBuildState slot,
            Shader shader,
            RenderTexture leftTexture,
            RenderTexture rightTexture)
        {
            var material = new Material(shader)
            {
                name = $"PhantomView_{slot.SlotId}_Display"
            };
            material.SetTexture("_LeftEyeTexture", leftTexture);
            material.SetTexture("_RightEyeTexture", rightTexture);
            material.SetFloat(
                "_CaptureTanHalfVerticalFov",
                Mathf.Tan(CaptureVerticalFieldOfView * 0.5f * Mathf.Deg2Rad));
            context.AssetSaver.SaveAsset(material);
            return material;
        }

        private static Mesh CreateDisplayMesh()
        {
            var mesh = new Mesh
            {
                name = "PhantomView_FullScreenQuad"
            };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }

        private static void AddParentConstraint(Transform target, Transform source)
        {
            var constraint = target.gameObject.AddComponent<VRCParentConstraint>();
            constraint.Locked = false;
            constraint.IsActive = true;
            constraint.SolveInLocalSpace = false;
            constraint.FreezeToWorld = false;
            constraint.RebakeOffsetsWhenUnfrozen = false;
            constraint.Sources = new VRCConstraintSourceKeyableList
            {
                new VRCConstraintSource
                {
                    SourceTransform = source,
                    Weight = 1f
                }
            };
            constraint.TryBakeCurrentOffsets(VRCConstraintBase.BakeOptions.BakeOffsets);
            constraint.Locked = true;
            constraint.enabled = true;
        }
    }
}
