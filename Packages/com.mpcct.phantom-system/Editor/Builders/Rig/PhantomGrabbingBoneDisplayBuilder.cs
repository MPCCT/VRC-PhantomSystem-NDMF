using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Builds one skinned, x-ray bone display mesh for a Phantom Grabbing proxy rig.</summary>
    internal static class PhantomGrabbingBoneDisplayBuilder
    {
        private const string TemplateMeshAssetPath =
            "Packages/com.mpcct.phantom-system/Asset/BoneDisplayMesh.fbx";
        private const string DisplayMaterialAssetPath =
            "Packages/com.mpcct.phantom-system/Asset/Material/BoneMaterial.mat";

        public static void Build(
            BuildContext context,
            PhantomSlotBuildState slot,
            Transform rigRoot,
            PhantomBuildReport report)
        {
            var template = LoadTemplateMesh(report, slot);
            var material = AssetDatabase.LoadAssetAtPath<Material>(DisplayMaterialAssetPath);
            if (template == null || material == null)
            {
                if (material == null)
                {
                    report.Error(
                        $"Phantom Grabbing bone display material was not found at '{DisplayMaterialAssetPath}'.",
                        slot.CloneRoot);
                }

                return;
            }

            var segments = slot.PhantomGrabbingBodyPhysBoneHosts
                .Where(pair => pair.Value != null
                               && slot.PhantomGrabbingBodySegmentEndpoints.TryGetValue(
                                   pair.Key,
                                   out var endpoint)
                               && endpoint.sqrMagnitude > 0.000001f)
                .OrderBy(pair => (int)pair.Key)
                .ToList();
            if (segments.Count == 0)
            {
                report.Error(
                    $"Slot '{slot.SlotId}' generated no Phantom Grabbing body segments for bone display.",
                    slot.CloneRoot);
                return;
            }

            var displayHost = ConstraintRigBuilder.EnsureChild(rigRoot, "BoneDisplay");
            var mesh = BuildMesh(template, displayHost, segments, slot, report);
            if (mesh == null)
            {
                return;
            }

            var renderer = displayHost.gameObject.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null)
            {
                renderer = displayHost.gameObject.AddComponent<SkinnedMeshRenderer>();
            }

            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.bones = segments.Select(segment => segment.Value).ToArray();
            renderer.rootBone = renderer.bones[0];
            renderer.quality = SkinQuality.Bone1;
            renderer.updateWhenOffscreen = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.enabled = false;

            slot.PhantomGrabbingBoneDisplayHost = displayHost;
            context.AssetSaver.SaveAsset(mesh);
        }

        private static Mesh LoadTemplateMesh(
            PhantomBuildReport report,
            PhantomSlotBuildState slot)
        {
            var mesh = AssetDatabase.LoadAllAssetsAtPath(TemplateMeshAssetPath)
                .OfType<Mesh>()
                .FirstOrDefault(candidate => candidate.vertexCount > 0 && candidate.subMeshCount > 0);
            if (mesh == null)
            {
                report.Error(
                    $"Phantom Grabbing bone display mesh was not found in '{TemplateMeshAssetPath}'.",
                    slot.CloneRoot);
            }

            return mesh;
        }

        private static Mesh BuildMesh(
            Mesh template,
            Transform displayHost,
            IReadOnlyList<KeyValuePair<HumanBodyBones, Transform>> segments,
            PhantomSlotBuildState slot,
            PhantomBuildReport report)
        {
            var bounds = template.bounds;
            if (bounds.size.x <= 0.000001f
                || bounds.size.y <= 0.000001f
                || bounds.size.z <= 0.000001f)
            {
                report.Error(
                    $"Phantom Grabbing bone display template '{template.name}' must have non-zero X, Y, and Z bounds.",
                    template);
                return null;
            }

            var sourceVertices = template.vertices;
            var sourceNormals = template.normals;
            var sourceTriangles = template.triangles;
            if (sourceVertices.Length == 0 || sourceTriangles.Length == 0)
            {
                report.Error(
                    $"Phantom Grabbing bone display template '{template.name}' contains no geometry.",
                    template);
                return null;
            }

            var vertexCount = sourceVertices.Length * segments.Count;
            var vertices = new List<Vector3>(vertexCount);
            var normals = new List<Vector3>(vertexCount);
            var weights = new List<BoneWeight>(vertexCount);
            var triangles = new List<int>(sourceTriangles.Length * segments.Count);
            var bones = new Transform[segments.Count];
            var bindPoses = new Matrix4x4[segments.Count];
            var templateOrigin = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            for (var boneIndex = 0; boneIndex < segments.Count; boneIndex++)
            {
                var pair = segments[boneIndex];
                var proxyBone = pair.Value;
                var endpoint = slot.PhantomGrabbingBodySegmentEndpoints[pair.Key];
                var length = endpoint.magnitude;
                var uniformScale = length / bounds.size.y;
                var templateToBone = Matrix4x4.TRS(
                    Vector3.zero,
                    Quaternion.FromToRotation(Vector3.up, endpoint / length),
                    Vector3.one * uniformScale)
                    * Matrix4x4.Translate(-templateOrigin);
                var boneToRenderer = displayHost.worldToLocalMatrix * proxyBone.localToWorldMatrix;
                var templateToRenderer = boneToRenderer * templateToBone;
                var normalToRenderer = templateToRenderer.inverse.transpose;
                var vertexOffset = vertices.Count;

                bones[boneIndex] = proxyBone;
                bindPoses[boneIndex] =
                    proxyBone.worldToLocalMatrix * displayHost.localToWorldMatrix;

                foreach (var vertex in sourceVertices)
                {
                    vertices.Add(templateToRenderer.MultiplyPoint3x4(vertex));
                    weights.Add(new BoneWeight
                    {
                        boneIndex0 = boneIndex,
                        weight0 = 1f
                    });
                }

                if (sourceNormals.Length == sourceVertices.Length)
                {
                    foreach (var normal in sourceNormals)
                    {
                        normals.Add(normalToRenderer.MultiplyVector(normal).normalized);
                    }
                }

                foreach (var triangle in sourceTriangles)
                {
                    triangles.Add(vertexOffset + triangle);
                }
            }

            var mesh = new Mesh
            {
                name = $"PhantomGrabbing_{slot.SlotId}_BoneDisplay"
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            if (normals.Count == vertices.Count)
            {
                mesh.SetNormals(normals);
            }
            else
            {
                mesh.RecalculateNormals();
            }

            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bindPoses;
            mesh.RecalculateBounds();
            return mesh;
        }

    }
}
