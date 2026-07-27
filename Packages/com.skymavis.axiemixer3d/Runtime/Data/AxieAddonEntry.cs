using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    [System.Serializable]
    internal class AxieAddonEntry
    {
        public string name;
        public AxieAddonMaterial[] materials;
        public GameObject[] prefabs;
    }

    [System.Serializable]
    internal struct AxieAddonMaterial
    {
        public string name;
        public Material material;
    }
}
