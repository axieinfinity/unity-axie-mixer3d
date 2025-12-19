using System.Collections.Generic;
using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    [System.Serializable]
    internal class AxieRigData
    {
        public AxieRigType type;
        public GameObject prefab;
        public List<Mesh> lodMeshes = new();
    }
}
