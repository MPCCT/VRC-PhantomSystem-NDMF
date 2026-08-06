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
        private const int RenderTextureSize = 1024;
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
                report.Error(
                    $"Slot '{slot.SlotId}' enables Phantom View, but its prebaked avatar has no Humanoid Head.",
                    slot.CloneRoot);
                return;
            }

            if (slot.BakedAvatar == null)
            {
                report.Error(
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
                report.Error(
                    $"Slot '{slot.SlotId}' enables Phantom View, but the base avatar has no Humanoid Head.",
                    baseAnimator != null ? (Object)baseAnimator : context.AvatarRootObject);
                return;
            }

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(DisplayShaderAssetPath);
            if (shader == null)
            {
                report.Error(
                    $"Phantom View display shader was not found at '{DisplayShaderAssetPath}'.",
                    slot.CloneRoot);
                return;
            }

            var viewsRoot = EnsureViewsRoot(system);
            var viewRoot = ConstraintRigBuilder.EnsureChild(
                viewsRoot.transform,
                TransformPathUtility.SafeName(slot.SlotId));
            viewRoot.gameObject.layer = PlayerLocalLayer;
            slot.PhantomViewRoot = viewRoot;

            var captureRoot = ConstraintRigBuilder.EnsureChild(viewRoot, "CaptureRoot");
            captureRoot.gameObject.layer = PlayerLocalLayer;
            // Humanoid Head bone axes are not guaranteed to use Unity's camera-forward
            // convention. Start from the phantom root rotation so the child cameras'
            // local +Z points toward the avatar's default forward direction, then bake
            // the relative rotation into the parent constraint. Position the rig at
            // the descriptor View Position rather than at the Head bone origin.
            var phantomViewPosition = slot.BakedAvatar.transform.TransformPoint(
                slot.BakedAvatar.ViewPosition);
            captureRoot.SetPositionAndRotation(
                phantomViewPosition,
                slot.CloneRoot.transform.rotation);
            AddParentConstraint(captureRoot, phantomHead);
            slot.PhantomViewCaptureRoot = captureRoot;

            EnsureGlobalRenderTextures(context, system);
            var leftTexture = system.PhantomViewLeftTexture;
            var rightTexture = system.PhantomViewRightTexture;
            var initialHalfDistance = PhantomViewAnimatorModule.MaximumStereoStrength
                                      * PhantomViewAnimatorModule.DefaultStereoStrengthParameter
                                      * 0.5f;
            slot.PhantomViewLeftCamera = CreateCamera(
                captureRoot,
                "LeftEyeCamera",
                -initialHalfDistance,
                leftTexture);
            slot.PhantomViewRightCamera = CreateCamera(
                captureRoot,
                "RightEyeCamera",
                initialHalfDistance,
                rightTexture);

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
                    "Left");
            }

            if (system.PhantomViewRightTexture == null)
            {
                system.PhantomViewRightTexture = CreateRenderTexture(
                    context,
                    "Right");
            }
        }

        private static RenderTexture CreateRenderTexture(
            BuildContext context,
            string eyeName)
        {
            var texture = new RenderTexture(
                RenderTextureSize,
                RenderTextureSize,
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
            RenderTexture targetTexture)
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
            camera.nearClipPlane = 0.03f;
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
