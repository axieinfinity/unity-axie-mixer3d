using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    [PreferBinarySerialization]
    internal class AxieBodyData : ScriptableObject
    {
        public GameObject prefab;
        public List<AxieAnimationData> liteAnimations = new();
        public List<AxieAnimationData> fullAnimations = new();

        public Dictionary<string, AxieAnimationData> LiteAnimations { get; private set; }
        public Dictionary<string, AxieAnimationData> FullAnimations { get; private set; }

        void OnEnable()
        {
            LiteAnimations = liteAnimations.ToDictionary(a => a.name);
            FullAnimations = fullAnimations.ToDictionary(a => a.name);
        }
    }
}
