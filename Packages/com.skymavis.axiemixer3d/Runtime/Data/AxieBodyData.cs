using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace SkyMavis.AxieMixer3D
{
    internal class AxieBodyData : ScriptableObject
    {
        public GameObject prefab;

        // Renamed from "liteAnimations" — there is no longer a separate lite/full tier.
        // FormerlySerializedAs keeps existing baked body assets loading without a re-bake.
        [FormerlySerializedAs("liteAnimations")]
        public List<AxieAnimationData> animations = new();

        public Dictionary<string, AxieAnimationData> Animations { get; private set; }

        void OnEnable()
        {
            Animations = BuildMap(animations);

            static Dictionary<string, AxieAnimationData> BuildMap(List<AxieAnimationData> list)
            {
                // Case-insensitive so GetAnimClip("default.idle") resolves "Default.Idle".
                var map = new Dictionary<string, AxieAnimationData>(list.Count, System.StringComparer.OrdinalIgnoreCase);
                foreach (var data in list)
                {
                    if (string.IsNullOrEmpty(data.name)) continue;
                    if (!map.TryAdd(data.name, data))
                        Debug.LogWarning($"{nameof(AxieBodyData)}: duplicate animation name '{data.name}' ignored.");
                }
                return map;
            }
        }
    }
}
