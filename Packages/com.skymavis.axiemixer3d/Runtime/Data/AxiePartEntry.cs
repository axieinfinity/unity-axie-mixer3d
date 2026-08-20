using System.Collections.Generic;

namespace SkyMavis.AxieMixer3D
{
    // One catalog entry per part (an S{skin}_{class}{variant}_L{level}_{partType}). Stored
    // inline in the AxieFactory catalog, each holding a direct reference to its rig prefab
    // (AxieRigData.prefab).
    [System.Serializable]
    internal struct AxiePartEntry
    {
        public string name;
        public List<AxieRigData> rigs;
    }
}
