using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace SkyMavis.AxieMixer3D.Editor
{
    [ScriptedImporter(1, new string[] { }, new string[] { "json" })]
    public class AxieMixerConfigImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var config = ScriptableObject.CreateInstance<AxieMixerConfig>();
            JsonUtility.FromJsonOverwrite(File.ReadAllText(ctx.assetPath), config);
            ctx.AddObjectToAsset("config", config);
            ctx.SetMainObject(config);
        }
    }
}
