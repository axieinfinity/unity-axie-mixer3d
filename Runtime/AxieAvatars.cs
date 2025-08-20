using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyMavis.AxieMixer3D
{
    public class AxieAvatars : System.IDisposable
    {
        public RenderTexture Front => GetOrCreate(nameof(Front), Vector3.back);
        public RenderTexture Back => GetOrCreate(nameof(Back), Vector3.forward);
        public RenderTexture Left => GetOrCreate(nameof(Left), Vector3.right);
        public RenderTexture Right => GetOrCreate(nameof(Right), Vector3.left);
        public RenderTexture FrontLeft => GetOrCreate(nameof(FrontLeft), Vector3.back + Vector3.right);
        public RenderTexture FrontRight => GetOrCreate(nameof(FrontRight), Vector3.back + Vector3.left);
        public RenderTexture FrontLeftTop => GetOrCreate(nameof(FrontLeftTop), Vector3.back + Vector3.right + Vector3.down);
        public RenderTexture FrontRightTop => GetOrCreate(nameof(FrontRightTop), Vector3.back + Vector3.left + Vector3.down);

        readonly AxieCharacter3D _character;
        readonly Dictionary<string, RenderTexture> _textures = new();

        internal AxieAvatars(AxieCharacter3D character)
        {
            _character = character;
            _character.Root.transform.eulerAngles = new(0f, 180f, 0f);
            GetOrCreate(nameof(Front), Vector3.back);
            GetOrCreate(nameof(Back), Vector3.forward);
            GetOrCreate(nameof(Left), Vector3.right);
            GetOrCreate(nameof(Right), Vector3.left);
            GetOrCreate(nameof(FrontLeft), Vector3.back + Vector3.right);
            GetOrCreate(nameof(FrontRight), Vector3.back + Vector3.left);
            GetOrCreate(nameof(FrontLeftTop), Vector3.back + Vector3.right + Vector3.down);
            GetOrCreate(nameof(FrontRightTop), Vector3.back + Vector3.left + Vector3.down);
            _character.Root.transform.eulerAngles = Vector3.zero;
        }

        public void Dispose()
        {
            foreach (var (_, texture) in _textures)
            {
                if (texture.IsCreated()) texture.Release();
                Object.Destroy(texture);
            }
        }

        /// <summary>
        /// Renders an Axie avatar from the specified view direction into the given <see cref="RenderTexture"/>.
        /// Use this method if you want full control over the <see cref="RenderTexture"/> lifecycle.
        /// </summary>
        /// <param name="viewDirection">The direction from which the Axie will be rendered.</param>
        /// <param name="targetTexture">The target <see cref="RenderTexture"/> that will receive the rendered avatar.</param>
        public void Render(Vector3 viewDirection, RenderTexture targetTexture)
        {
            using var command = new CommandBuffer { name = $"{nameof(AxieAvatars)}.{nameof(Render)}" };
            command.SetRenderTarget(targetTexture);
            command.ClearRenderTarget(true, true, Color.clear);
            command.SetViewProjectionMatrices(
                Matrix4x4.Scale(new(1f, 1f, -1f)) * Matrix4x4.Inverse(
                    _character.Root.transform.localToWorldMatrix *
                    Matrix4x4.LookAt(.75f * Vector3.up, .75f * Vector3.up + viewDirection, Vector3.up)
                ),
                Matrix4x4.Ortho(-1f, 1f, -1f, 1f, -2f, 2f)
            );
            command.SetGlobalVector("unity_OrthoParams", new(2f, 2f, 0f, 1f));

            foreach (var renderer in _character.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var materials = renderer.sharedMaterials;

                for (var subMeshIndex = 0; subMeshIndex < materials.Length; subMeshIndex++)
                {
                    var material = materials[subMeshIndex];
                    Render("ExtraPrePass");
                    Render("Forward");

                    void Render(string passName)
                    {
                        var passIndex = material.FindPass(passName);
                        if (passIndex >= 0) command.DrawRenderer(renderer, material, subMeshIndex, passIndex);
                    }
                }
            }

            Graphics.ExecuteCommandBuffer(command);
        }

        RenderTexture GetOrCreate(string name, Vector3 viewDirection)
        {
            if (!_textures.TryGetValue(name, out var texture))
            {
                _textures[name] = texture = new RenderTexture(1024, 1024, 16, RenderTextureFormat.ARGB32);
                texture.Create();
                Render(viewDirection, texture);
            }

            return texture;
        }
    }
}
