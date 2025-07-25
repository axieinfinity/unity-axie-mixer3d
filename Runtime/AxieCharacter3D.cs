using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    public class AxieCharacter3D : System.IDisposable
    {
        public GameObject Root { get; }
        public ReadOnlyDictionary<string, List<(Transform rootBone, AnimationClip clip)>> Animations { get; }
        public Transform RightWeaponAttachPoint { get; }
        public Transform LeftWeaponAttachPoint { get; }

        public static AxieCharacter3D FromDescriptor(AxieDescriptor axieDescriptor) => AxieFactory.Default.CreateCharacter(axieDescriptor);

        public static AxieCharacter3D FromGenes(string genes) => AxieFactory.Default.CreateCharacter(AxieDescriptor.FromGenes(genes));

        internal AxieCharacter3D(
            GameObject root,
            Dictionary<string, List<(Transform, AnimationClip)>> animations,
            Transform rightWeaponAttachPoint,
            Transform leftWeaponAttachPoint)
        {
            Root = root;
            Animations = new(animations);
            RightWeaponAttachPoint = rightWeaponAttachPoint;
            LeftWeaponAttachPoint = leftWeaponAttachPoint;
        }

        public void Dispose()
        {
            if (Root != null) Object.Destroy(Root);
        }
    }
}
