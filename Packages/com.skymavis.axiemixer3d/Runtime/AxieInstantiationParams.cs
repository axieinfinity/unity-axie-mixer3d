using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    [System.Serializable]
    public class AxieInstantiationParams
    {
        [Tooltip("Change the rendering layer of specific Axie parts.")]
        public List<PartLayerOverride> partLayerOverrides = new();

        [Tooltip("Combine the Axie's part renderers into two SkinnedMeshRenderers at creation to cut draw " +
                 "calls and skinning cost. Disable to keep the original per-part GameObject hierarchy.")]
        public bool combineMeshes = true;

        public AxieInstantiationParams Merge(AxieInstantiationParams other)
        {
            if (other == null) return this;

            // Merge the part layer overrides pairwise, with the other's overrides taking precedence.
            var partLayerOverrides = new List<PartLayerOverride>(this.partLayerOverrides);
            foreach (var partLayerOverride in other.partLayerOverrides)
            {
                var index = partLayerOverrides.FindIndex(x => x.type == partLayerOverride.type);

                if (index >= 0)
                {
                    partLayerOverrides[index] = partLayerOverride;
                }
                else
                {
                    partLayerOverrides.Add(partLayerOverride);
                }
            }

            return new AxieInstantiationParams
            {
                partLayerOverrides = partLayerOverrides,
                combineMeshes = other.combineMeshes,
            };
        }

        [System.Serializable]
        public struct PartLayerOverride
        {
            public AxiePartType type;
            [LayerField]
            public int layer;
        }
    }
}
