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
        // After the migration the package looks like:
        //   Packages/com.skymavis.axiemixer3d/AxieMixerAssets/AxieFactory.asset (the catalog)
        //   Packages/com.skymavis.axiemixer3d/AxieMixerAssets/{AddonAssets,BuiltAssets,Data}
        // The legacy Resources layout is still tolerated until the migration runs.
        const string MigratedBasePath = "Packages/com.skymavis.axiemixer3d/AxieMixerAssets";
        const string LegacyBasePath = "Packages/com.skymavis.axiemixer3d/Resources/AxieMixer3D";

        static string ResolveBasePath()
            => AssetDatabase.IsValidFolder(MigratedBasePath) ? MigratedBasePath : LegacyBasePath;

        const string MigratedCatalogPath = "Packages/com.skymavis.axiemixer3d/AxieMixerAssets/AxieFactory.asset";
        const string LegacyCatalogPath = "Packages/com.skymavis.axiemixer3d/Resources/AxieMixer3D/AxieFactory.asset";

        static string ResolveCatalogPath()
        {
            if (AssetDatabase.LoadAssetAtPath<AxieFactory>(MigratedCatalogPath) != null) return MigratedCatalogPath;
            if (AssetDatabase.LoadAssetAtPath<AxieFactory>(LegacyCatalogPath) != null) return LegacyCatalogPath;
            return MigratedCatalogPath;
        }

        static readonly Regex AxieBodyRegex = new(@"(?<body>[^/]+)/Model/Model\.prefab$", RegexOptions.Compiled);
        static readonly Regex AxiePartRegex = new(@"/(?<class>[^-/]+)-[^-/]+-(?<variant>\d\d)-S(?<skin>\d\d)-LV(?<level>\d)/(?<rigType>[^/]+)/[^/]+\.prefab$", RegexOptions.Compiled);

        [MenuItem("Tools/Axie Mixer 3D/Update Axie Data")]
        public static void UpdateAxieData()
        {
            try
            {
                AssetDatabase.StartAssetEditing();
                var basePath = ResolveBasePath();
                DeleteObsoletePartDataAssets(basePath);
                UpdateAxieBodyData(basePath);
                GenerateAnimNames(basePath);
                UpdateAxieFactoryCatalog(basePath);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.StopAssetEditing();
                Resources.UnloadUnusedAssets();
            }
        }

        static void UpdateAxieBodyData(string basePath)
        {
            var inputPath = Path.Combine(basePath, "BuiltAssets", "Bodies");
            var outputPath = Path.Combine(basePath, "Data", "Bodies");
            var subRigGUIDs = AssetDatabase.FindAssets($"t:{nameof(GameObject)}", new[] { inputPath });

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

                if (AssetDatabase.LoadAssetAtPath<AxieBodyData>(bodyDataPath) is not { } bodyData)
                {
                    bodyData = ScriptableObject.CreateInstance<AxieBodyData>();
                    AssetDatabase.CreateAsset(bodyData, bodyDataPath);
                }

                // Clean up leftovers from the old two-tier (lite/full) bake: strip any clips
                // previously embedded as sub-assets of the body, and delete the external "Full"
                // clip folder that an earlier bake may have produced.
                foreach (var embedded in AssetDatabase.LoadAllAssetRepresentationsAtPath(bodyDataPath).OfType<AnimationClip>())
                    Object.DestroyImmediate(embedded, true);

                var fullDir = Path.Combine(outputPath, bodyType.ToString(), "Full");
                if (AssetDatabase.IsValidFolder(fullDir)) AssetDatabase.DeleteAsset(fullDir);

                var bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(subRigPath);
                bodyData.prefab = bodyPrefab;
                bodyData.animations.Clear();

                var animationClipGUIDs = AssetDatabase.FindAssets($"t:{nameof(AnimationClip)}", new[] { Path.Join(Path.GetDirectoryName(subRigPath), "Animations") });
                var seenKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

                foreach (var animationClipGUID in animationClipGUIDs)
                {
                    var animationClipPath = AssetDatabase.GUIDToAssetPath(animationClipGUID);
                    var animationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animationClipPath);

                    // Weapon/action clips live in the optional weapon-anim package now. Skip any that
                    // are still physically present in the main package so they never re-couple into
                    // the main body data (belt-and-suspenders — the migration also moves the files out).
                    if (animationClip.name.StartsWith("Action.", System.StringComparison.OrdinalIgnoreCase)) continue;

                    var key = AxieAnimNaming.NormalizeKey(animationClip.name);
                    if (string.IsNullOrEmpty(key))
                    {
                        Debug.LogWarning($"{nameof(AxieDataUpdater)}: body '{bodyType}' clip '{animationClip.name}' " +
                                         $"({animationClipPath}) normalizes to an empty name; skipping.");
                        continue;
                    }
                    if (!seenKeys.Add(key))
                    {
                        Debug.LogWarning($"{nameof(AxieDataUpdater)}: body '{bodyType}' has two clips normalizing " +
                                         $"to '{key}' (e.g. '{animationClip.name}'). Keeping the first, skipping this one.");
                        continue;
                    }

                    bodyData.animations.Add(new()
                    {
                        name = key,
                        clip = animationClip,
                    });
                }

                bodyData.animations.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
                EditorUtility.SetDirty(bodyData);
            }
        }

        // Part data used to be baked into ~570 standalone AxiePartData .asset files under
        // Data/Parts. It now lives inline in the catalog (see CollectPartEntries), so the old
        // folder is pure dead weight — remove it if a pre-migration bake left it behind.
        static void DeleteObsoletePartDataAssets(string basePath)
        {
            var partsFolder = Path.Combine(basePath, "Data", "Parts");
            if (AssetDatabase.IsValidFolder(partsFolder))
                AssetDatabase.DeleteAsset(partsFolder);
        }

        static void UpdateAxieFactoryCatalog(string basePath)
        {
            EditorUtility.DisplayProgressBar("Updating catalog", "Loading factory asset...", 0f);

            var catalogPath = ResolveCatalogPath();
            var factory = AssetDatabase.LoadAssetAtPath<AxieFactory>(catalogPath);
            if (factory == null)
            {
                EnsureFolderExists(Path.GetDirectoryName(catalogPath));
                factory = ScriptableObject.CreateInstance<AxieFactory>();
                AssetDatabase.CreateAsset(factory, catalogPath);
            }

            var bodyEntries = CollectBodyEntries(basePath);
            var partEntries = CollectPartEntries(basePath);
            var addonEntries = CollectAddonEntries(basePath);

            // Colors are authored directly on AxieFactory.asset (its _colors array); the catalog
            // build only regenerates bodies/parts/addons and leaves the colors untouched.
            factory.EditorAssign(bodyEntries, partEntries, addonEntries);
            EditorUtility.SetDirty(factory);
        }

        static AxieBodyEntry[] CollectBodyEntries(string basePath)
        {
            var bodiesFolder = Path.Combine(basePath, "Data", "Bodies");
            var entries = new List<AxieBodyEntry>();
            if (!AssetDatabase.IsValidFolder(bodiesFolder)) return entries.ToArray();

            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(AxieBodyData).FullName}", new[] { bodiesFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<AxieBodyData>(path);
                if (data == null) continue;
                if (!System.Enum.TryParse<AxieBodyType>(data.name, out var type))
                {
                    Debug.LogWarning($"Skipping body data {path}: name '{data.name}' is not a valid AxieBodyType.");
                    continue;
                }
                entries.Add(new AxieBodyEntry { type = type, data = data });
            }
            entries.Sort((a, b) => a.type.CompareTo(b.type));
            return entries.ToArray();
        }

        // Builds the part catalog inline from the rig prefabs under BuiltAssets/Parts. Rigs
        // sharing an S{skin}_{class}{variant}_L{level}_{partType} name collapse into one
        // AxiePartEntry that is serialized directly into the AxieFactory catalog — no
        // intermediate Data/Parts .asset files. Only the prefab references are kept
        // (AxieRigData.prefab is a direct GameObject reference).
        static AxiePartEntry[] CollectPartEntries(string basePath)
        {
            var inputPath = Path.Combine(basePath, "BuiltAssets", "Parts");
            if (!AssetDatabase.IsValidFolder(inputPath)) return System.Array.Empty<AxiePartEntry>();

            var subRigGUIDs = AssetDatabase.FindAssets($"t:{nameof(GameObject)}", new[] { inputPath });
            var rigsByName = new Dictionary<string, List<AxieRigData>>();

            for (var i = 0; i < subRigGUIDs.Length; i++)
            {
                var subRigPath = AssetDatabase.GUIDToAssetPath(subRigGUIDs[i]);

                if (EditorUtility.DisplayCancelableProgressBar("Collecting part data", subRigPath, (float)i / subRigGUIDs.Length)) break;

                if (
                    AxiePartRegex.Match(subRigPath) is not { Success: true } match ||
                    !int.TryParse(match.Groups["skin"].Value, out var skin) ||
                    !int.TryParse(match.Groups["variant"].Value, out var variant) ||
                    !int.TryParse(match.Groups["level"].Value, out var level) ||
                    !System.Enum.TryParse<AxieRigType>(match.Groups["rigType"].Value, out var rigType)
                ) continue;

                var @class = match.Groups["class"];
                var partName = $"S{skin:00}_{@class}{variant:00}_L{level}_{rigType.ToAxiePartType()}";

                if (!rigsByName.TryGetValue(partName, out var rigs))
                {
                    rigs = new List<AxieRigData>();
                    rigsByName.Add(partName, rigs);
                }

                var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(subRigPath);
                rigs.Add(new AxieRigData { type = rigType, prefab = rigPrefab });
            }

            var entries = new List<AxiePartEntry>(rigsByName.Count);
            foreach (var (name, rigs) in rigsByName)
            {
                rigs.Sort((a, b) => string.CompareOrdinal(
                    a.prefab != null ? a.prefab.name : "",
                    b.prefab != null ? b.prefab.name : ""));
                entries.Add(new AxiePartEntry { name = name, rigs = rigs });
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return entries.ToArray();
        }

        // Matches parent addon folders: {Class}-{PartType}-{variant:02}-S{skin:02}-LV{level}
        // e.g. "Beast-Horn-02-S01-LV1"
        static readonly Regex AddonParentFolderRegex = new(@"^[A-Za-z]+-[A-Za-z]+-\d{2}-S\d{2}-LV\d+$", RegexOptions.Compiled);

        static AxieAddonEntry[] CollectAddonEntries(string basePath)
        {
            var addonRoot = Path.Combine(basePath, "AddonAssets");
            var entries = new List<AxieAddonEntry>();
            if (!AssetDatabase.IsValidFolder(addonRoot)) return entries.ToArray();

            var visitedParents = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:DefaultAsset", new[] { addonRoot }))
            {
                var parentPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!AssetDatabase.IsValidFolder(parentPath)) continue;
                if (!visitedParents.Add(parentPath)) continue;

                var parentName = Path.GetFileName(parentPath);
                if (string.IsNullOrEmpty(parentName) || parentName.StartsWith("_")) continue;
                if (!AddonParentFolderRegex.IsMatch(parentName)) continue;

                // Each immediate child folder is a rig-type slot (e.g. Horn_L, Horn_R, Back_M).
                var visitedChildren = new HashSet<string>();
                foreach (var childGuid in AssetDatabase.FindAssets("t:DefaultAsset", new[] { parentPath }))
                {
                    var childPath = AssetDatabase.GUIDToAssetPath(childGuid);
                    if (!AssetDatabase.IsValidFolder(childPath)) continue;
                    if (!visitedChildren.Add(childPath)) continue;
                    if (Path.GetDirectoryName(childPath)?.Replace('\\', '/') != parentPath.Replace('\\', '/')) continue;

                    var childName = Path.GetFileName(childPath);
                    var materials = new List<AxieAddonMaterial>();
                    var prefabs = new List<GameObject>();

                    foreach (var assetGuid in AssetDatabase.FindAssets(string.Empty, new[] { childPath }))
                    {
                        var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                        if (AssetDatabase.IsValidFolder(assetPath)) continue;

                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                        switch (asset)
                        {
                            case GameObject prefab:
                                prefabs.Add(prefab);
                                break;
                            case Material material:
                                materials.Add(new AxieAddonMaterial { name = material.name, material = material });
                                break;
                        }
                    }

                    if (materials.Count == 0 && prefabs.Count == 0) continue;

                    entries.Add(new AxieAddonEntry
                    {
                        name = $"{parentName}/{childName}",
                        materials = materials.ToArray(),
                        prefabs = prefabs.ToArray(),
                    });
                }
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return entries.ToArray();
        }

        static void EnsureFolderExists(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath)) return;
            var parent = Path.GetDirectoryName(folderPath);
            EnsureFolderExists(parent);
            var leaf = Path.GetFileName(folderPath);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(leaf) && !AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        static void GenerateAnimNames(string basePath)
        {
            var bodiesFolder = Path.Combine(basePath, "Data", "Bodies");
            if (!AssetDatabase.IsValidFolder(bodiesFolder)) return;

            var names = new SortedDictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(AxieBodyData).FullName}", new[] { bodiesFolder }))
            {
                var data = AssetDatabase.LoadAssetAtPath<AxieBodyData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                foreach (var anim in data.animations)
                    if (!string.IsNullOrEmpty(anim.name))
                        names.TryAdd(anim.name, anim.name);
            }

            var outputPath = "Packages/com.skymavis.axiemixer3d/Runtime/AnimNames.cs";
            var newSource = BuildAnimNamesSource(names.Values);
            if (File.Exists(outputPath) && File.ReadAllText(outputPath) == newSource) return;
            File.WriteAllText(outputPath, newSource);
            AssetDatabase.ImportAsset(outputPath);
        }

        static string ToIdentifier(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }

        static string BuildAnimNamesSource(IEnumerable<string> bareNames)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//     Generated by Tools -> Axie Mixer 3D -> Update Axie Data (AxieDataUpdater).");
            sb.AppendLine("//     Do NOT edit by hand; re-run the data-update tool to regenerate.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("namespace SkyMavis.AxieMixer3D");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Typed body-animation clip names (bare — no \"Default.\"/\"Action.\" prefix).");
            sb.AppendLine("    /// Note: the Cannon weapon is baked as \"Canon\" in the source art; the data-update");
            sb.AppendLine("    /// tool normalizes it, so the constants read \"Cannon*\" (double n).");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class AnimNames");
            sb.AppendLine("    {");
            var emitted = new HashSet<string>();
            foreach (var name in bareNames)
            {
                var id = ToIdentifier(name);
                if (!emitted.Add(id))
                {
                    Debug.LogWarning($"{nameof(AxieDataUpdater)}: identifier collision for '{name}' -> '{id}'. Skipping.");
                    continue;
                }
                if (id != name)
                    Debug.LogWarning($"{nameof(AxieDataUpdater)}: clip name '{name}' is not a valid C# identifier; emitting as '{id}'.");
                sb.AppendLine($"        public const string {id} = \"{name}\";");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
