using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyMavis.AxieMixer3D
{
    /// <summary>
    /// Renders an <see cref="AxieCharacter3D"/> into a caller-owned <see cref="RenderTexture"/> as a
    /// flat "avatar" thumbnail, using an orthographic <see cref="CommandBuffer"/> that draws each
    /// <see cref="SkinnedMeshRenderer"/>'s <c>ExtraPrePass</c> then <c>Forward</c> passes.
    ///
    /// <para>This is an optional addon kept separate from the core character: create one per character
    /// you want to snapshot, <see cref="Render"/> as many frames as you need, and <see cref="Dispose"/>
    /// it (or the whole character) when done. Transient use — create, render, dispose — is the common
    /// pattern for one-off thumbnails.</para>
    /// </summary>
    public sealed class AxieAvatarRenderer : System.IDisposable
    {
        readonly AxieCharacter3D _character;

        // Cached draw list — built lazily on first Render, reused every frame. Rebuilt if the
        // character's Root changes (e.g. after an in-place AxieCharacter3D.ApplyDescriptor).
        //
        // One item per SkinnedMeshRenderer. Each render we CPU-bake the renderer's current pose into
        // BakeTarget and draw that with DrawMesh, rather than DrawRenderer'ing the live renderer. This
        // is what makes off-screen snapshots work: a renderer only gets GPU-skinned when a camera culls
        // it in, but this avatar path executes synchronously before any camera runs — and combined
        // characters replace the prefab's renderers with runtime-created ones that have never been
        // skinned at all, so DrawRenderer would emit nothing. BakeMesh computes the skin on the spot.
        sealed class DrawItem
        {
            public SkinnedMeshRenderer Renderer;
            public Mesh BakeTarget;               // reused every render; owned/destroyed by this class
            public Material[] Materials;          // sharedMaterials, one per sub-mesh
            public int[] PrePassIndices;          // FindPass("ExtraPrePass") per material, -1 if absent
            public int[] ForwardIndices;          // FindPass("Forward") per material, -1 if absent
        }
        DrawItem[] _drawItems;
        GameObject _cachedRoot;
        CommandBuffer _command;

        public AxieAvatarRenderer(AxieCharacter3D character)
        {
            _character = character ?? throw new System.ArgumentNullException(nameof(character));
        }

        void EnsureCache()
        {
            var root = _character.Root;
            // Rebuild if never built or the character was rebuilt in place onto a new Root.
            if (_drawItems != null && _cachedRoot == root) return;

            DestroyBakeTargets();

            var items = new List<DrawItem>();
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                var prePass = new int[mats.Length];
                var forward = new int[mats.Length];
                for (var s = 0; s < mats.Length; s++)
                {
                    (prePass[s], forward[s]) = FindPasses(mats[s]);
                }
                items.Add(new DrawItem
                {
                    Renderer = r,
                    BakeTarget = new Mesh { name = $"{nameof(AxieAvatarRenderer)}.Bake" },
                    Materials = mats,
                    PrePassIndices = prePass,
                    ForwardIndices = forward,
                });
            }
            _drawItems = items.ToArray();
            _cachedRoot = root;
            _command ??= new CommandBuffer { name = $"{nameof(AxieAvatarRenderer)}.{nameof(Render)}" };
        }

        void DestroyBakeTargets()
        {
            if (_drawItems == null) return;
            foreach (var item in _drawItems)
                if (item.BakeTarget != null) Object.Destroy(item.BakeTarget);
        }

        static readonly ShaderTagId LightModeTag = new("LightMode");
        // Forward-pass names, in priority order. Covers the Amplify V4 shader (pass named "Forward")
        // and the V5 URP ShaderGraph, whose forward pass is named "Universal Forward" (and reports no
        // LightMode tag, so it can only be matched by name).
        static readonly string[] ForwardPassNames = { "Forward", "Universal Forward" };
        // LightMode fallback for shaders that name their forward pass something else entirely.
        static readonly string[] ForwardLightModes = { "UniversalForwardOnly", "UniversalForward", "SRPDefaultUnlit" };

        // Locates the (ExtraPrePass, Forward) pass indices to draw for a material. The old V4/Amplify
        // shader exposed passes literally named "ExtraPrePass" and "Forward"; the V5 ShaderGraph has no
        // ExtraPrePass and names its forward pass "Universal Forward", so we match forward by name (with
        // a LightMode fallback). Returns (-1, -1) for a null material; pre is -1 when there's no ExtraPrePass.
        static (int pre, int fwd) FindPasses(Material mat)
        {
            if (mat == null) return (-1, -1);

            var pre = mat.FindPass("ExtraPrePass");   // V4 only; -1 on V5

            var fwd = -1;
            foreach (var name in ForwardPassNames)
                if ((fwd = mat.FindPass(name)) >= 0) break;
            if (fwd < 0) fwd = FindPassByLightMode(mat);

            if (fwd < 0)
                Debug.LogWarning($"[{nameof(AxieAvatarRenderer)}] No forward pass found on shader '{mat.shader.name}'. " +
                                 $"Passes: {DescribePasses(mat)}. Avatar will render empty for this material.");

            return (pre, fwd);
        }

        static int FindPassByLightMode(Material mat)
        {
            var shader = mat.shader;
            var count = mat.passCount;
            foreach (var want in ForwardLightModes)
            {
                var wanted = new ShaderTagId(want);
                for (var p = 0; p < count; p++)
                    if (shader.FindPassTagValue(p, LightModeTag) == wanted) return p;
            }
            return -1;
        }

        static string DescribePasses(Material mat)
        {
            var sb = new System.Text.StringBuilder();
            for (var p = 0; p < mat.passCount; p++)
            {
                if (p > 0) sb.Append(", ");
                sb.Append(mat.GetPassName(p)).Append("[LightMode=").Append(mat.shader.FindPassTagValue(p, LightModeTag).name).Append(']');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Renders the Axie into <paramref name="targetTexture"/> using the provided rendering parameters.
        /// </summary>
        /// <param name="targetTexture">
        /// The <see cref="RenderTexture"/> that will receive the rendered avatar image.
        /// Must be created and released by the caller. It is resized to match <paramref name="renderParams"/>.
        /// </param>
        /// <param name="renderParams">
        /// Rendering parameters that define resolution, camera setup, and model orientation.
        /// See <see cref="AxieAvatarRenderParams"/>.
        /// </param>
        public void Render(RenderTexture targetTexture, AxieAvatarRenderParams renderParams)
        {
            if (renderParams.width == 0) throw new System.ArgumentException($"Render width cannot be zero!");
            if (renderParams.height == 0) throw new System.ArgumentException($"Render height cannot be zero!");

            if ((targetTexture.width, targetTexture.height) != (renderParams.width, renderParams.height))
            {
                if (targetTexture.IsCreated()) targetTexture.Release();
                (targetTexture.width, targetTexture.height) = (renderParams.width, renderParams.height);
            }

            if (!targetTexture.IsCreated()) targetTexture.Create();

            EnsureCache();

            var root = _character.Root;
            var originalEulers = root.transform.eulerAngles;
            root.transform.eulerAngles = new(0f, renderParams.modelHeading, 0f);

            try
            {
                var aspect = (float)renderParams.height / renderParams.width;

                _command.Clear();
                _command.SetRenderTarget(targetTexture);
                _command.ClearRenderTarget(true, true, Color.clear);
                _command.SetViewProjectionMatrices(
                    Matrix4x4.Scale(new(1f, 1f, -1f)) * Matrix4x4.Inverse(
                        root.transform.localToWorldMatrix *
                        Matrix4x4.LookAt(renderParams.viewCenter, renderParams.viewCenter + renderParams.viewDirection, Vector3.up)
                    ),
                    Matrix4x4.Ortho(-1f, 1f, -aspect, aspect, -2f, 2f)
                );
                _command.SetGlobalVector("unity_OrthoParams", new(2f, 2f * aspect, 0f, 1f));

                foreach (var item in _drawItems)
                {
                    // Bake the current pose into a mesh (CPU skinning, synchronous), then draw that.
                    // Baked verts are in the renderer's local space, so localToWorldMatrix — which
                    // now includes the modelHeading rotation applied to root above — places them in
                    // world exactly as DrawRenderer would, mirror (negative-scale) parts included.
                    item.Renderer.BakeMesh(item.BakeTarget);
                    var matrix = item.Renderer.transform.localToWorldMatrix;
                    for (var s = 0; s < item.Materials.Length; s++)
                    {
                        if (item.PrePassIndices[s] >= 0) _command.DrawMesh(item.BakeTarget, matrix, item.Materials[s], s, item.PrePassIndices[s]);
                        if (item.ForwardIndices[s] >= 0) _command.DrawMesh(item.BakeTarget, matrix, item.Materials[s], s, item.ForwardIndices[s]);
                    }
                }

                Graphics.ExecuteCommandBuffer(_command);
            }
            finally
            {
                root.transform.eulerAngles = originalEulers;
            }
        }

        public void Dispose()
        {
            _command?.Dispose();
            _command = null;
            DestroyBakeTargets();
            _drawItems = null;
            _cachedRoot = null;
        }
    }
}
