using System.Collections.Generic;
using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    /// <summary>
    /// Merges the many per-part <see cref="SkinnedMeshRenderer"/>s of an assembled Axie into one
    /// renderer per group (outline-excluded eyes/mouth vs. everything else), cutting skinning
    /// dispatches, culling entries, and the Draw-Objects outline redraw. Part bone Transforms are
    /// left alive (they still drive the merged skin); only the source renderer components are removed.
    /// </summary>
    internal static class AxieMeshCombiner
    {
        // Grouping sentinel: outline-excluded renderers (eyes/mouth) merge into their own renderer so
        // AxieCharacter3D.SetOutlineLayer can keep them off the outline layer. Non-excluded renderers
        // group by GameObject layer (so partLayerOverrides still land on distinct renderers).
        const int ExcludedGroupKey = int.MinValue;

        internal readonly struct CombinedRenderer
        {
            public readonly SkinnedMeshRenderer Renderer;
            public readonly Mesh Mesh;
            public readonly bool IsOutlineExcluded;
            public CombinedRenderer(SkinnedMeshRenderer renderer, Mesh mesh, bool excluded)
            { Renderer = renderer; Mesh = mesh; IsOutlineExcluded = excluded; }
        }

        public static List<CombinedRenderer> Combine(GameObject root, IReadOnlyList<GameObject> outlineExcludedParts)
        {
            var result = new List<CombinedRenderer>();
            var sources = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (sources.Length == 0) return result;

            // Body root bone (first renderer that has one) — reused as every merged renderer's rootBone.
            Transform rootBone = null;
            foreach (var smr in sources)
                if (smr.rootBone != null) { rootBone = smr.rootBone; break; }

            // Renderers that must stay off the outline layer (eyes/mouth), by direct reference.
            var excludedSet = new HashSet<SkinnedMeshRenderer>();
            if (outlineExcludedParts != null)
            {
                foreach (var part in outlineExcludedParts)
                {
                    if (part == null) continue;
                    foreach (var smr in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        excludedSet.Add(smr);
                }
            }

            // Group sources by (excluded ? sentinel : layer). Preserve encounter order for stable output.
            var groups = new Dictionary<int, List<SkinnedMeshRenderer>>();
            var groupOrder = new List<int>();
            foreach (var smr in sources)
            {
                if (smr.sharedMesh == null) continue;
                var key = excludedSet.Contains(smr) ? ExcludedGroupKey : smr.gameObject.layer;
                if (!groups.TryGetValue(key, out var list))
                {
                    groups[key] = list = new List<SkinnedMeshRenderer>();
                    groupOrder.Add(key);
                }
                list.Add(smr);
            }

            foreach (var key in groupOrder)
            {
                var isExcluded = key == ExcludedGroupKey;
                var layer = isExcluded ? 0 : key;

                if (!TryBuildGroupMesh(groups[key], out var mesh, out var bones, out var materials))
                    continue;

                var go = new GameObject(isExcluded ? "AxieMergedRenderer_Excluded" : $"AxieMergedRenderer_{layer}");
                go.transform.SetParent(root.transform, false);
                go.layer = layer;

                var merged = go.AddComponent<SkinnedMeshRenderer>();
                merged.sharedMesh = mesh;
                merged.bones = bones;
                merged.rootBone = rootBone;
                merged.sharedMaterials = materials;
                merged.localBounds = mesh.bounds;

                result.Add(new CombinedRenderer(merged, mesh, isExcluded));
            }

            // Strip the originals; their bone Transforms remain to drive the merged skin.
            foreach (var smr in sources) DestroySafe(smr);

            return result;
        }

        // Unions bones, remaps bone weights, and concatenates vertex streams into one mesh with one
        // sub-mesh per unique material. Returns false if the group produced no drawable geometry.
        static bool TryBuildGroupMesh(List<SkinnedMeshRenderer> group, out Mesh mesh, out Transform[] bones, out Material[] materials)
        {
            mesh = null; bones = null; materials = null;

            var boneIndex = new Dictionary<Transform, int>();
            var boneList = new List<Transform>();
            var bindposes = new List<Matrix4x4>();

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var colors = new List<Color>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();
            var weights = new List<BoneWeight>();

            // Sub-mesh (triangle) buckets, one per unique material, in first-seen order.
            var materialToSubmesh = new Dictionary<Material, int>();
            var submeshMaterials = new List<Material>();
            var submeshTris = new List<List<int>>();

            foreach (var smr in group)
            {
                var srcMesh = smr.sharedMesh;
                if (srcMesh == null) continue;

                // A renderer whose world transform has a negative determinant (e.g. localScale.x = -1 used
                // for mirroring) is rendered by Unity with triangle winding automatically flipped to
                // compensate. The combined renderer has no such scale, so we must pre-flip the winding
                // ourselves; otherwise those triangles are back-face culled and the part is invisible.
                // Tangent handedness (w) must also flip so normal-map bitangents stay correct.
                bool flip = smr.transform.localToWorldMatrix.determinant < 0f;

                // --- bones: union by Transform, build this source's local→unified remap ---
                var srcBones = smr.bones;
                var srcBindposes = srcMesh.bindposes;
                var remap = new int[srcBones.Length];
                for (var i = 0; i < srcBones.Length; i++)
                {
                    var bone = srcBones[i];
                    if (bone == null) { remap[i] = 0; continue; }
                    if (!boneIndex.TryGetValue(bone, out var unified))
                    {
                        unified = boneList.Count;
                        boneIndex[bone] = unified;
                        boneList.Add(bone);
                        // bindposes are parallel to bones; a bone is owned by exactly one source mesh.
                        bindposes.Add(i < srcBindposes.Length ? srcBindposes[i] : Matrix4x4.identity);
                    }
                    remap[i] = unified;
                }

                // --- vertex streams (pad missing streams so all lists stay the same length) ---
                var vertexOffset = verts.Count;
                var v = srcMesh.vertices;
                var count = v.Length;
                verts.AddRange(v);
                AppendOrPad(normals, srcMesh.normals, count, Vector3.up);
                if (flip)
                {
                    var srcTangents = srcMesh.tangents;
                    if (srcTangents != null && srcTangents.Length == count)
                    {
                        for (var i = 0; i < srcTangents.Length; i++) srcTangents[i].w = -srcTangents[i].w;
                        tangents.AddRange(srcTangents);
                    }
                    else
                    {
                        for (var i = 0; i < count; i++) tangents.Add(new Vector4(1f, 0f, 0f, 1f));
                    }
                }
                else
                {
                    AppendOrPad(tangents, srcMesh.tangents, count, new Vector4(1f, 0f, 0f, -1f));
                }
                AppendOrPad(colors, srcMesh.colors, count, Color.white);
                AppendOrPad(uv0, srcMesh.uv, count, Vector2.zero);
                AppendOrPad(uv1, srcMesh.uv2, count, Vector2.zero);

                // --- bone weights, remapped to unified indices ---
                var srcWeights = srcMesh.boneWeights;
                for (var i = 0; i < count; i++)
                {
                    if (i < srcWeights.Length)
                    {
                        var w = srcWeights[i];
                        w.boneIndex0 = remap[Mathf.Clamp(w.boneIndex0, 0, remap.Length - 1)];
                        w.boneIndex1 = remap[Mathf.Clamp(w.boneIndex1, 0, remap.Length - 1)];
                        w.boneIndex2 = remap[Mathf.Clamp(w.boneIndex2, 0, remap.Length - 1)];
                        w.boneIndex3 = remap[Mathf.Clamp(w.boneIndex3, 0, remap.Length - 1)];
                        weights.Add(w);
                    }
                    else
                    {
                        weights.Add(new BoneWeight { boneIndex0 = 0, weight0 = 1f });
                    }
                }

                // --- triangles bucketed by material ---
                var mats = smr.sharedMaterials;
                for (var s = 0; s < srcMesh.subMeshCount; s++)
                {
                    var material = s < mats.Length ? mats[s] : null;
                    if (material == null) continue;

                    if (!materialToSubmesh.TryGetValue(material, out var bucket))
                    {
                        bucket = submeshMaterials.Count;
                        materialToSubmesh[material] = bucket;
                        submeshMaterials.Add(material);
                        submeshTris.Add(new List<int>());
                    }

                    var tris = srcMesh.GetTriangles(s);
                    var dst = submeshTris[bucket];
                    if (flip)
                    {
                        for (var t = 0; t < tris.Length; t += 3)
                        {
                            dst.Add(tris[t]     + vertexOffset);
                            dst.Add(tris[t + 2] + vertexOffset);
                            dst.Add(tris[t + 1] + vertexOffset);
                        }
                    }
                    else
                    {
                        for (var t = 0; t < tris.Length; t++) dst.Add(tris[t] + vertexOffset);
                    }
                }
            }

            if (verts.Count == 0 || submeshMaterials.Count == 0) return false;

            mesh = new Mesh { name = "AxieCombinedMesh" };
            if (verts.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bindposes.ToArray();
            mesh.subMeshCount = submeshTris.Count;
            for (var s = 0; s < submeshTris.Count; s++) mesh.SetTriangles(submeshTris[s], s);
            mesh.RecalculateBounds();

            bones = boneList.ToArray();
            materials = submeshMaterials.ToArray();
            return true;
        }

        static void AppendOrPad<T>(List<T> dst, T[] src, int count, T pad)
        {
            if (src != null && src.Length == count) { dst.AddRange(src); return; }
            for (var i = 0; i < count; i++) dst.Add(pad);
        }

        static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
