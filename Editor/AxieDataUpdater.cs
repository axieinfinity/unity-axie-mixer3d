using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // TODO: Don't expose to consumer
        [MenuItem("Tools/Update Axie Data")]
        static void UpdateAxieData()
        {
            try
            {
                AssetDatabase.StartAssetEditing();
                var partAnimationData = UpdateAxiePartData();
                UpdateAxieBodyData(partAnimationData);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.StopAssetEditing();
                Resources.UnloadUnusedAssets();
            }
        }

        static void UpdateAxieBodyData(List<(AxieRigType rigType, GameObject rigPrefab, string rootName, AnimationClip clip)> partAnimationData)
        {
            var inputPath = Path.Combine(BaseResourcePath, "BuiltAssets", "Bodies");
            var outputPath = Path.Combine(BaseResourcePath, "Data", "Bodies");
            var subRigGUIDs = AssetDatabase.FindAssets($"t:{nameof(GameObject)}", new[] { inputPath });
            var groupedAnimationData = partAnimationData.GroupBy(t => t.clip.name).ToDictionary(g => g.Key);

            for (var i = 0; i < subRigGUIDs.Length; i++)
            {
                var subRigGUID = subRigGUIDs[i];
                var subRigPath = AssetDatabase.GUIDToAssetPath(subRigGUID);

                if (EditorUtility.DisplayCancelableProgressBar("Updating body data", subRigPath, (float)i / subRigGUIDs.Length)) break;

                if (
                    AxieBodyRegex.Match(subRigPath) is not { Success: true } match ||
                    !System.Enum.TryParse<AxieBodyType>(match.Groups["body"].Value, out var bodyType)
                ) continue;

                var bodyDataPath = Path.Combine(outputPath, $"{bodyType}.asset");
                var fullBodyClips = System.Array.Empty<AnimationClip>();

                if (AssetDatabase.LoadAssetAtPath<AxieBodyData>(bodyDataPath) is { } bodyData)
                {
                    fullBodyClips = AssetDatabase.LoadAllAssetRepresentationsAtPath(bodyDataPath).Cast<AnimationClip>().ToArray();
                }
                else
                {
                    bodyData = ScriptableObject.CreateInstance<AxieBodyData>();
                    AssetDatabase.CreateAsset(bodyData, bodyDataPath);
                }

                bodyData.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(subRigPath);
                bodyData.liteAnimations.Clear();
                bodyData.fullAnimations.Clear();

                var attachPointPaths = GetAttachPointPaths(bodyData.prefab);
                var animationClipGUIDs = AssetDatabase.FindAssets($"t:{nameof(AnimationClip)}", new[] { Path.Join(Path.GetDirectoryName(subRigPath), "Animations") });

                foreach (var animationClipGUID in animationClipGUIDs)
                {
                    var animationClipPath = AssetDatabase.GUIDToAssetPath(animationClipGUID);
                    var animationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animationClipPath);

                    bodyData.liteAnimations.Add(new()
                    {
                        name = animationClip.name,
                        clip = animationClip,
                    });

                    if (fullBodyClips.FirstOrDefault(c => c.name == animationClip.name) is { } fullBodyClip)
                    {
                        EditorUtility.SetDirty(fullBodyClip);
                    }
                    else
                    {
                        fullBodyClip = new AnimationClip { name = animationClip.name };
                        AssetDatabase.AddObjectToAsset(fullBodyClip, bodyData);
                    }

                    BakeFullBodyAnimation(
                        animationClip,
                        groupedAnimationData.TryGetValue(animationClip.name, out var animationData)
                            ? animationData
                            : Enumerable.Empty<(AxieRigType rigType, GameObject rigPrefab, string rootName, AnimationClip clip)>(),
                        attachPointPaths,
                        fullBodyClip
                    );

                    bodyData.fullAnimations.Add(new()
                    {
                        name = animationClip.name,
                        clip = fullBodyClip,
                    });
                }

                bodyData.liteAnimations.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
                bodyData.fullAnimations.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
                EditorUtility.SetDirty(bodyData);
            }
        }

        static List<(AxieRigType rigType, GameObject rigPrefab, string rootName, AnimationClip clip)> UpdateAxiePartData()
        {
            var inputPath = Path.Combine(BaseResourcePath, "BuiltAssets", "Parts");
            var outputPath = Path.Combine(BaseResourcePath, "Data", "Parts");
            var subRigGUIDs = AssetDatabase.FindAssets($"t:{nameof(GameObject)}", new[] { inputPath });
            var partDataGUIDs = AssetDatabase.FindAssets($"t:{typeof(AxiePartData).FullName}", new[] { outputPath });
            var partDataMap = new Dictionary<string, AxiePartData>();
            var partAnimationData = new List<(AxieRigType rigType, GameObject rigPrefab, string rootName, AnimationClip clip)>();

            foreach (var partDataGUID in partDataGUIDs)
            {
                var partDataPath = AssetDatabase.GUIDToAssetPath(partDataGUID);
                var partData = AssetDatabase.LoadAssetAtPath<AxiePartData>(partDataPath);
                partData.rigs.Clear();
                partDataMap.Add(partData.name, partData);
            }

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
                var animationClipGUIDs = AssetDatabase.FindAssets($"t:{nameof(AnimationClip)}", new[] { Path.Join(Path.GetDirectoryName(subRigPath), "Animations") });

                foreach (var animationClipGUID in animationClipGUIDs)
                {
                    var animationClipPath = AssetDatabase.GUIDToAssetPath(animationClipGUID);
                    var animationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animationClipPath);
                    partAnimationData.Add((
                        rigType,
                        rigData.prefab,
                        $"{partDataName}_{rigData.prefab.GetInstanceID():X8}",
                        animationClip
                    ));
                }

                partData.rigs.Add(rigData);
            }

            EditorUtility.DisplayProgressBar("Updating part data", "Sorting...", 1f);

            foreach (var (_, partData) in partDataMap)
            {
                partData.rigs.Sort((a, b) => a.prefab.GetInstanceID().CompareTo(b.prefab.GetInstanceID()));
                EditorUtility.SetDirty(partData);
            }

            return partAnimationData;
        }

        static Dictionary<AxieRigType, string> GetAttachPointPaths(GameObject root)
        {
            var (attachPoints, _, _) = AxieFactory.CollectAttachPoints(root);
            return attachPoints.ToDictionary(p => p.Key, p => GetRelativePath(p.Value));

            string GetRelativePath(Transform child)
            {
                if (root.transform == child) return string.Empty;

                var relativePath = child.name;

                for (var transform = child.parent; transform != root.transform; transform = transform.parent)
                {
                    relativePath = $"{transform.name}/{relativePath}";
                }

                return relativePath;
            }
        }

        static void BakeFullBodyAnimation(
            AnimationClip bodyClip,
            IEnumerable<(AxieRigType rigType, GameObject rigPrefab, string rootName, AnimationClip clip)> partAnimationData,
            Dictionary<AxieRigType, string> attachPointPaths,
            AnimationClip outputClip
        )
        {
            var excessiveLengthPaths = new List<string>();
            var baseClipLength = bodyClip.length + 1f / 30f;
            EditorUtility.CopySerialized(bodyClip, outputClip);

            foreach (var (rigType, rigPrefab, rootName, clip) in partAnimationData)
            {
                var partBindings = AnimationUtility
                    .GetCurveBindings(clip)
                    .Where(b => rigPrefab.transform.Find(b.path))
                    .ToArray();
                var partCurves = partBindings.Select(b => AnimationUtility.GetEditorCurve(clip, b)).ToArray();

                for (var i = 0; i < partBindings.Length; i++)
                {
                    ref var partBinding = ref partBindings[i];
                    partBinding.path = $"{attachPointPaths[rigType]}/{rootName}/{partBinding.path}";
                }

                AnimationUtility.SetEditorCurves(outputClip, partBindings, partCurves);

                if (clip.length > baseClipLength)
                {
                    excessiveLengthPaths.Add(AssetDatabase.GetAssetPath(clip));
                }
            }

            if (excessiveLengthPaths.Count > 0)
            {
                Debug.LogWarning($"Animation length mismatch!\nBody animation: {AssetDatabase.GetAssetPath(bodyClip)}\nPart animations:{string.Concat(excessiveLengthPaths.Select(p => $"\n+ {p}"))}");
            }
        }
    }
}
