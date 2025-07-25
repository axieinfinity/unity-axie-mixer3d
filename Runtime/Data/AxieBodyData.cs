using System.Collections.Generic;
using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    internal class AxieBodyData : ScriptableObject
    {
        public GameObject prefab;
        public List<AnimationClip> animationClips = new();
    }
}
