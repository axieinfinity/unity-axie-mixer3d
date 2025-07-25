using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    public class AxieMixerConfig : ScriptableObject
    {
        public Items items;

        [System.Serializable]
        public struct Items
        {
            public ColorVariant[] colors;
        }

        [System.Serializable]
        public struct ColorVariant
        {
            public int index;
            public string key;
            public int skin;
            public string @class;
            public int color_value;
            public string primary1;
            public string primary2;
        }
    }
}
