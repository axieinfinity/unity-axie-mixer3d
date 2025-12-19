using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    [System.Serializable]
    public class AxieInstantiationParams
    {
        public int lodLevel = 0;
        [Tooltip("Use MaterialPropertyBlocks to colorize the Axie, instead of duplicating materials, but breaks SRP batching.")]
        public bool useMaterialPropertyBlocks = false;
        [Tooltip("Change the rendering layer of specific Axie parts.")]
        public List<PartLayerOverride> partLayerOverrides = new();

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
                lodLevel = other.lodLevel < 0 ? this.lodLevel : other.lodLevel,
                useMaterialPropertyBlocks = other.useMaterialPropertyBlocks,
                partLayerOverrides = partLayerOverrides,
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
