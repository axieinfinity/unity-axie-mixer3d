using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyMavis.AxieMixer3D
{
    public class AxieCharacter3D : System.IDisposable
    {
        public AxieInstantiationParams InstantiationParams { get; }
        public GameObject Root { get; }
        [System.Obsolete("Animations is deprecated. Please use GetLiteAnimationClip(animationName) or GetFullAnimationClip(animationName) instead.", true)]
        public ReadOnlyDictionary<string, List<(Transform rootBone, AnimationClip clip)>> Animations { get; }
        public Transform RightWeaponAttachPoint { get; }
        public Transform LeftWeaponAttachPoint { get; }
        public ReadOnlyCollection<string> AnimationNames { get; }

        readonly AxieBodyData _bodyData;

        public static AxieCharacter3D FromDescriptor(AxieDescriptor axieDescriptor, AxieInstantiationParams instantiationParams = null) => AxieFactory.Default.CreateCharacter(axieDescriptor, instantiationParams);

        public static AxieCharacter3D FromGenes(string genes, AxieInstantiationParams instantiationParams = null) => FromDescriptor(AxieDescriptor.FromGenes(genes), instantiationParams);

        internal AxieCharacter3D(
            AxieInstantiationParams instantiationParams,
            GameObject root,
            Transform rightWeaponAttachPoint,
            Transform leftWeaponAttachPoint,
            AxieBodyData bodyData
        )
        {
            InstantiationParams = instantiationParams;
            Root = root;
            RightWeaponAttachPoint = rightWeaponAttachPoint;
            LeftWeaponAttachPoint = leftWeaponAttachPoint;
            AnimationNames = new(bodyData.liteAnimations.Select(a => a.name).ToArray());
            _bodyData = bodyData;
        }

        public void Dispose()
        {
            if (!InstantiationParams.useMaterialPropertyBlocks)
            {
                foreach (var renderer in Root.GetComponentsInChildren<Renderer>())
                {
                    Object.Destroy(renderer.sharedMaterial);
                }
            }

            if (Root != null) Object.Destroy(Root);
        }

        /// <summary>Include only body animations.</summary>
        public AnimationClip GetLiteAnimationClip(string animationName) => _bodyData.LiteAnimations[animationName].clip.asset;

        /// <summary>Include part animations in additional to body animations.</summary>
        public AnimationClip GetFullAnimationClip(string animationName) => _bodyData.FullAnimations[animationName].clip.asset;

        /// <summary>
        /// Renders an Axie avatar into the specified <see cref="RenderTexture"/> using the provided rendering parameters.
        /// </summary>
        /// <param name="targetTexture">
        /// The <see cref="RenderTexture"/> that will receive the rendered avatar image.
        /// Must be created and released by the caller.
        /// </param>
        /// <param name="renderParams">
        /// Rendering parameters that define resolution, camera setup, and model orientation.
        /// See <see cref="AxieAvatarRenderParams"/>.
        /// </param>
        public void RenderAvatar(RenderTexture targetTexture, AxieAvatarRenderParams renderParams)
        {
            if (renderParams.width == 0) throw new System.ArgumentException($"Render width cannot be zero!");
            if (renderParams.height == 0) throw new System.ArgumentException($"Render height cannot be zero!");

            if ((targetTexture.width, targetTexture.height) != (renderParams.width, renderParams.height))
            {
                if (targetTexture.IsCreated()) targetTexture.Release();
                (targetTexture.width, targetTexture.height) = (renderParams.width, renderParams.height);
            }

            if (!targetTexture.IsCreated()) targetTexture.Create();

            var originalEulers = Root.transform.eulerAngles;
            Root.transform.eulerAngles = new(0f, renderParams.modelHeading, 0f);

            try
            {
                var aspect = (float)renderParams.height / renderParams.width;

                using var command = new CommandBuffer { name = $"{nameof(AxieCharacter3D)}.{nameof(RenderAvatar)}" };
                command.SetRenderTarget(targetTexture);
                command.ClearRenderTarget(true, true, Color.clear);
                command.SetViewProjectionMatrices(
                    Matrix4x4.Scale(new(1f, 1f, -1f)) * Matrix4x4.Inverse(
                        Root.transform.localToWorldMatrix *
                        Matrix4x4.LookAt(renderParams.viewCenter, renderParams.viewCenter + renderParams.viewDirection, Vector3.up)
                    ),
                    Matrix4x4.Ortho(-1f, 1f, -aspect, aspect, -2f, 2f)
                );
                command.SetGlobalVector("unity_OrthoParams", new(2f, 2f * aspect, 0f, 1f));

                foreach (var renderer in Root.GetComponentsInChildren<SkinnedMeshRenderer>())
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
            finally
            {
                Root.transform.eulerAngles = originalEulers;
            }
        }
    }
}
