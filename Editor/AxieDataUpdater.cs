using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace SkyMavis.AxieMixer3D.Editor
{
    public static class AxieDataUpdater
    {
        const string BaseResourcePath = "Packages/com.skymavis.axiemixer3d/Resources/AxieMixer3D";
        static readonly Regex AxieBodyRegex = new(@"(?<body>[^/]+)/Model/Model\.prefab$", RegexOptions.Compiled);
        static readonly Regex AxiePartRegex = new(@"/S(?<skin>\d\d)/(?<class>[^/]+)/\k<class>_(?<variant>\d\d)/Lvl_(?<level>\d)/[^/]+/(?<rigType>[^/]+)/(?<subRigName>[^/]+)/\k<subRigName>\.prefab$", RegexOptions.Compiled);

        [MenuItem("Tools/Update Axie Data")]
        static void UpdateAxieData()
        {
            try
            {
                AssetDatabase.StartAssetEditing();
                UpdateAxieBodyData();
                UpdateAxiePartData();
                AssetDatabase.SaveAssets();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                Resources.UnloadUnusedAssets();
            }
        }

        static void UpdateAxieBodyData()
        {
            var inputPath = Path.Combine(BaseResourcePath, "BuiltAssets", "Bodies");
            var outputPath = Path.Combine(BaseResourcePath, "Data", "Bodies");
            var subRigGUIDs = AssetDatabase.FindAssets($"t:{nameof(GameObject)}", new[] { inputPath });

            foreach (var subRigGUID in subRigGUIDs)
            {
                var subRigPath = AssetDatabase.GUIDToAssetPath(subRigGUID);

                if (
                    AxieBodyRegex.Match(subRigPath) is not { Success: true } match ||
                    !System.Enum.TryParse<AxieBodyType>(match.Groups["body"].Value, out var bodyType)
                ) continue;

                var bodyDataPath = Path.Combine(outputPath, $"{bodyType}.asset");

                if (AssetDatabase.LoadAssetAtPath<AxieBodyData>(bodyDataPath) is not { } bodyData)
                {
                    bodyData = ScriptableObject.CreateInstance<AxieBodyData>();
                    AssetDatabase.CreateAsset(bodyData, bodyDataPath);
                }

                bodyData.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(subRigPath);
                bodyData.animationClips.Clear();

                var animationClipGUIDs = AssetDatabase.FindAssets($"t:{nameof(AnimationClip)}", new[] { Path.Join(Path.GetDirectoryName(subRigPath), "Animations") });

                foreach (var animationClipGUID in animationClipGUIDs)
                {
                    var animationClipPath = AssetDatabase.GUIDToAssetPath(animationClipGUID);
                    var animationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animationClipPath);
                    bodyData.animationClips.Add(animationClip);
                }

                bodyData.animationClips.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
                EditorUtility.SetDirty(bodyData);
            }
        }

        static void UpdateAxiePartData()
        {
            var inputPath = Path.Combine(BaseResourcePath, "BuiltAssets", "Parts");
            var outputPath = Path.Combine(BaseResourcePath, "Data", "Parts");
            var subRigGUIDs = AssetDatabase.FindAssets($"t:{nameof(GameObject)}", new[] { inputPath });
            var partDataGUIDs = AssetDatabase.FindAssets($"t:{typeof(AxiePartData).FullName}", new[] { outputPath });
            var partDataMap = new Dictionary<string, AxiePartData>();

            foreach (var partDataGUID in partDataGUIDs)
            {
                var partDataPath = AssetDatabase.GUIDToAssetPath(partDataGUID);
                var partData = AssetDatabase.LoadAssetAtPath<AxiePartData>(partDataPath);
                partData.rigs.Clear();
                partDataMap.Add(partData.name, partData);
            }

            try
            {
                for (var i = 0; i < subRigGUIDs.Length; i++)
                {
                    var subRigGUID = subRigGUIDs[i];
                    var subRigPath = AssetDatabase.GUIDToAssetPath(subRigGUID);

                    if (EditorUtility.DisplayCancelableProgressBar("Updating part data", subRigPath, (float)i / subRigGUIDs.Length)) break;

                    if (
                        AxiePartRegex.Match(subRigPath) is not { Success: true } match ||
                        !int.TryParse(match.Groups["skin"].Value, out var skin) ||
                        !int.TryParse(match.Groups["variant"].Value, out var variant) ||
                        !int.TryParse(match.Groups["level"].Value, out var level) ||
                        !System.Enum.TryParse<AxieRigType>(match.Groups["rigType"].Value, out var rigType)
                    ) continue;

                    var @class = match.Groups["class"];
                    var subRigName = match.Groups["subRigName"];
                    var partDataName = $"S{skin:00}_{@class}{variant:00}_L{level}_{rigType.ToAxiePartType()}";

                    if (!partDataMap.TryGetValue(partDataName, out var partData))
                    {
                        partData = ScriptableObject.CreateInstance<AxiePartData>();
                        AssetDatabase.CreateAsset(partData, Path.Combine(outputPath, $"{partDataName}.asset"));
                        partDataMap.Add(partDataName, partData);
                    }

                    var rigData = new AxieRigData { type = rigType, prefab = AssetDatabase.LoadAssetAtPath<GameObject>(subRigPath) };
                    rigData.animationClips.Clear();

                    var animationClipGUIDs = AssetDatabase.FindAssets($"t:{nameof(AnimationClip)}", new[] { Path.Join(Path.GetDirectoryName(subRigPath), "Animations") });

                    foreach (var animationClipGUID in animationClipGUIDs)
                    {
                        var animationClipPath = AssetDatabase.GUIDToAssetPath(animationClipGUID);
                        var animationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animationClipPath);
                        rigData.animationClips.Add(animationClip);
                    }

                    rigData.animationClips.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
                    partData.rigs.Add(rigData);
                }

                EditorUtility.DisplayProgressBar("Updating part data", "Sorting...", 1f);

                foreach (var (_, partData) in partDataMap)
                {
                    partData.rigs.Sort((a, b) => EditorUtility.NaturalCompare(a.prefab.name, b.prefab.name));
                    EditorUtility.SetDirty(partData);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
