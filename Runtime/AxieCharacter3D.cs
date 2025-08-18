using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    public class AxieCharacter3D : System.IDisposable
    {
        public GameObject Root { get; }
        [System.Obsolete("Animations is deprecated. Please use GetLiteAnimationClip(animationName) or GetFullAnimationClip(animationName) instead.", true)]
        public ReadOnlyDictionary<string, List<(Transform rootBone, AnimationClip clip)>> Animations { get; }
        public Transform RightWeaponAttachPoint { get; }
        public Transform LeftWeaponAttachPoint { get; }
        public ReadOnlyCollection<string> AnimationNames { get; }

        readonly AxieBodyData _bodyData;

        public static AxieCharacter3D FromDescriptor(AxieDescriptor axieDescriptor) => AxieFactory.Default.CreateCharacter(axieDescriptor);

        public static AxieCharacter3D FromGenes(string genes) => AxieFactory.Default.CreateCharacter(AxieDescriptor.FromGenes(genes));

        internal AxieCharacter3D(
            GameObject root,
            Transform rightWeaponAttachPoint,
            Transform leftWeaponAttachPoint,
            AxieBodyData bodyData
        )
        {
            Root = root;
            RightWeaponAttachPoint = rightWeaponAttachPoint;
            LeftWeaponAttachPoint = leftWeaponAttachPoint;
            AnimationNames = new(bodyData.liteAnimations.Select(a => a.name).ToArray());
            _bodyData = bodyData;
        }

        public void Dispose()
        {
            if (Root != null) Object.Destroy(Root);
        }

        /// <summary>Include only body animations.</summary>
        public AnimationClip GetLiteAnimationClip(string animationName) => _bodyData.LiteAnimations[animationName].clip.asset;

        /// <summary>Include part animations in additional to body animations.</summary>
        public AnimationClip GetFullAnimationClip(string animationName) => _bodyData.FullAnimations[animationName].clip.asset;
    }
}
