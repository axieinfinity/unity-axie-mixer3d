using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SkyMavis.AxieMixer3D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkyMavis.AxieMixer3D.Editor
{
    public static class AxieAssetSyncer
    {
        const string SourcePathPref = "AxieMixer3D.AssetSourcePath";
        const string StagingRoot = "Assets/_AxieDeliveryStaging";
        const string PackageRelRoot = "Packages/com.skymavis.axiemixer3d/AxieMixerAssets";

        static readonly string[] SourceDirs = { "BuiltAssets/Bodies", "DeliveryAssets/Parts", "AddonAssets" };

        static string PackageAssetRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/com.skymavis.axiemixer3d/AxieMixerAssets"));

        static string StagingRootAbs =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "_AxieDeliveryStaging"));

        static readonly HashSet<string> KnownPartTypes = new(StringComparer.OrdinalIgnoreCase)
            { "Back", "Ear", "Eye", "Horn", "Mouth", "Tail" };

        // -------------------------------------------------------------------------
        // Menu items
        // -------------------------------------------------------------------------

        [MenuItem("Tools/Axie Mixer 3D/Sync Assets from Source…")]
        static void MenuSync() => SyncAssetsFromSource();

        [MenuItem("Tools/Axie Mixer 3D/Validate Assets")]
        static void MenuValidate() => ValidateAssets();

        [MenuItem("Tools/Axie Mixer 3D/Sync & Validate")]
        static void MenuSyncAndValidate() => SyncAndValidate();

        // -------------------------------------------------------------------------
        // Public entry points
        // -------------------------------------------------------------------------

        public static void SyncAssetsFromSource()
        {
            string sourceRoot = ResolveSourceRoot();
            if (string.IsNullOrEmpty(sourceRoot)) return;

            int copied = 0, skipped = 0, errors = 0;
            try
            {
                DeleteBuiltParts();
                foreach (string relDir in SourceDirs)
                {
                    string srcDir = Path.Combine(sourceRoot, relDir);
                    if (!Directory.Exists(srcDir))
                    {
                        Debug.LogWarning($"[AxieAssetSyncer] Source dir not found, skipping: {srcDir}");
                        continue;
                    }

                    if (relDir == "DeliveryAssets/Parts")
                        ProcessDeliveryParts(srcDir, Path.Combine(sourceRoot, "BuiltAssets/Parts"),
                            PackageRelRoot + "/BuiltAssets/Parts", ref copied, ref skipped, ref errors);
                    else
                        CopyTree(srcDir, Path.Combine(PackageAssetRoot, relDir), ref copied, ref skipped, ref errors);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            Debug.Log($"[AxieAssetSyncer] Sync complete — copied: {copied}, skipped: {skipped}, errors: {errors}");

            if (errors == 0 && EditorUtility.DisplayDialog(
                    "Sync complete",
                    $"Copied {copied} files ({skipped} unchanged).\n\nRun Update Axie Data now?",
                    "Yes, update", "Skip"))
                AxieDataUpdater.UpdateAxieData();
        }

        public static void ValidateAssets()
        {
            string partsRoot = Path.Combine(PackageAssetRoot, "BuiltAssets/Parts");
            if (!Directory.Exists(partsRoot))
            {
                Debug.LogError($"[AxieAssetSyncer] Parts directory not found: {partsRoot}");
                return;
            }

            var report = BuildValidationReport(partsRoot);
            string reportPath = Path.Combine(Application.dataPath,
                $"AxieMixer3D_ValidationReport_{DateTime.Now:yyyy-MM-dd}.md");
            File.WriteAllText(reportPath, report);
            AssetDatabase.Refresh();
            Debug.Log($"[AxieAssetSyncer] Validation report written to {reportPath}");
            EditorUtility.RevealInFinder(reportPath);
        }

        public static void SyncAndValidate()
        {
            string sourceRoot = ResolveSourceRoot();
            if (string.IsNullOrEmpty(sourceRoot)) return;

            int copied = 0, skipped = 0, errors = 0;
            try
            {
                DeleteBuiltParts();
                foreach (string relDir in SourceDirs)
                {
                    string srcDir = Path.Combine(sourceRoot, relDir);
                    if (!Directory.Exists(srcDir)) continue;

                    if (relDir == "DeliveryAssets/Parts")
                        ProcessDeliveryParts(srcDir, Path.Combine(sourceRoot, "BuiltAssets/Parts"),
                            PackageRelRoot + "/BuiltAssets/Parts", ref copied, ref skipped, ref errors);
                    else
                        CopyTree(srcDir, Path.Combine(PackageAssetRoot, relDir), ref copied, ref skipped, ref errors);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            Debug.Log($"[AxieAssetSyncer] Sync complete — copied: {copied}, skipped: {skipped}, errors: {errors}");
            ValidateAssets();

            if (EditorUtility.DisplayDialog(
                    "Sync & Validate complete",
                    "Sync and validation finished. Run Update Axie Data now?",
                    "Yes, update", "Skip"))
                AxieDataUpdater.UpdateAxieData();
        }

        // -------------------------------------------------------------------------
        // Source path resolution
        // -------------------------------------------------------------------------

        static string ResolveSourceRoot()
        {
            string saved = EditorPrefs.GetString(SourcePathPref, "");
            if (!string.IsNullOrEmpty(saved) && Directory.Exists(saved)) return saved;

            string chosen = EditorUtility.OpenFolderPanel(
                "Select source project Assets/ folder (e.g. axie-potara-stuff-v2/Assets)", saved, "");

            if (string.IsNullOrEmpty(chosen))
            {
                Debug.LogWarning("[AxieAssetSyncer] No source folder selected — sync cancelled.");
                return null;
            }

            EditorPrefs.SetString(SourcePathPref, chosen);
            return chosen;
        }

        // -------------------------------------------------------------------------
        // Delete old Parts before sync
        // -------------------------------------------------------------------------

        static void DeleteBuiltParts()
        {
            var abs = Path.Combine(PackageAssetRoot, "BuiltAssets/Parts");
            if (!Directory.Exists(abs)) return;
            Directory.Delete(abs, recursive: true);
            var meta = abs + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
        }

        // -------------------------------------------------------------------------
        // CopyTree — Bodies, AddonAssets
        // -------------------------------------------------------------------------

        static void CopyTree(string srcDir, string dstDir, ref int copied, ref int skipped, ref int errors)
        {
            var files = Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string srcFile = files[i];
                if (EditorUtility.DisplayCancelableProgressBar("Syncing assets…", Path.GetFileName(srcFile), (float)i / files.Length))
                {
                    Debug.Log("[AxieAssetSyncer] Sync cancelled by user.");
                    return;
                }

                if (ShouldExclude(srcFile, srcDir)) { skipped++; continue; }

                string relative = srcFile.Substring(srcDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                string dstFile = Path.Combine(dstDir, relative);

                try
                {
                    if (IsIdentical(srcFile, dstFile)) { skipped++; continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dstFile));
                    File.Copy(srcFile, dstFile, overwrite: true);
                    copied++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AxieAssetSyncer] Failed to copy {srcFile}: {ex.Message}");
                    errors++;
                }
            }
        }

        static bool ShouldExclude(string filePath, string srcRoot)
        {
            string name = Path.GetFileName(filePath);
            if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return true;
            if (name == ".DS_Store" || name == "Thumbs.db") return true;

            // Check directory segments only — Aquatic FBX filenames start with "Aqua_" but are
            // not alias duplicates. Only folder names (not the file name) are alias candidates.
            string dirRelative = Path.GetDirectoryName(filePath.Substring(srcRoot.Length)) ?? "";
            foreach (string segment in dirRelative.Split(new[] { '/', Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
                if (segment.StartsWith("Aqua_", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        static bool IsIdentical(string srcFile, string dstFile)
        {
            if (!File.Exists(dstFile)) return false;
            var si = new FileInfo(srcFile);
            var di = new FileInfo(dstFile);
            return si.Length == di.Length && si.LastWriteTimeUtc == di.LastWriteTimeUtc;
        }

        // -------------------------------------------------------------------------
        // ProcessDeliveryParts — four phases
        // -------------------------------------------------------------------------

        static void ProcessDeliveryParts(string srcPartsDir, string srcBuiltPartsDir, string dstPartsRel, ref int copied, ref int skipped, ref int errors)
        {
            var dstPartsAbs = Path.Combine(PackageAssetRoot, "BuiltAssets/Parts");
            var stagingAbs = StagingRootAbs;

            // Phase A: stage FBX + PNG
            EditorUtility.DisplayProgressBar("Processing Parts", "Staging delivery assets…", 0f);
            StageDeliveryFiles(srcPartsDir, stagingAbs, ref skipped, ref errors);

            // Phase B: import + configure ModelImporters
            EditorUtility.DisplayProgressBar("Processing Parts", "Importing staged FBX…", 0.1f);
            AssetDatabase.Refresh();
            ConfigureStagedModelImporters();

            // Recreate Parts folder as Unity-tracked asset folder
            EnsureAssetFolder(PackageRelRoot + "/BuiltAssets", "Parts");

            // Find V5 ShaderGraph shader — t:Shader filter does NOT find .shadergraph assets
            Shader shader = null;
            foreach (var g in AssetDatabase.FindAssets("S_Axie_Mixer", new[] { PackageRelRoot + "/BuiltAssets" }))
            {
                shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g));
                if (shader != null) break;
            }
            if (shader == null)
                Debug.LogWarning("[AxieAssetSyncer] V5 shader not found in BuiltAssets; materials will have missing shader.");

            // Phase C: walk staging, extract meshes/prefabs/mats into flat destination
            EditorUtility.DisplayProgressBar("Processing Parts", "Extracting meshes and prefabs…", 0.2f);
            var previewScene = EditorSceneManager.NewPreviewScene();
            var emptyMesh = new Mesh();
            try
            {
                ProcessStagedParts(stagingAbs, srcBuiltPartsDir, dstPartsAbs, dstPartsRel, shader, previewScene, emptyMesh, ref copied, ref errors);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
                UnityEngine.Object.DestroyImmediate(emptyMesh);
            }

            AssetDatabase.SaveAssets();

            // Phase D: cleanup staging
            EditorUtility.DisplayProgressBar("Processing Parts", "Cleaning up staging…", 0.95f);
            AssetDatabase.DeleteAsset(StagingRoot);
        }

        static void StageDeliveryFiles(string srcPartsDir, string stagingAbs, ref int skipped, ref int errors)
        {
            if (Directory.Exists(stagingAbs))
                Directory.Delete(stagingAbs, recursive: true);

            foreach (var srcFile in Directory.GetFiles(srcPartsDir, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(srcFile).ToLowerInvariant();
                if (ext != ".fbx" && ext != ".png") continue;
                if (ShouldExclude(srcFile, srcPartsDir)) { skipped++; continue; }

                var rel = srcFile.Substring(srcPartsDir.Length).TrimStart('/', Path.DirectorySeparatorChar);
                if (rel.IndexOf("Animations", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                var dst = Path.Combine(stagingAbs, rel);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(srcFile, dst, overwrite: true);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AxieAssetSyncer] Stage copy failed {srcFile}: {ex.Message}");
                    errors++;
                }
            }
        }

        static void ConfigureStagedModelImporters()
        {
            var guids = AssetDatabase.FindAssets("t:GameObject", new[] { StagingRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (EditorUtility.DisplayCancelableProgressBar("Configuring importers", path, (float)i / guids.Length)) break;
                if (AssetImporter.GetAtPath(path) is ModelImporter mi)
                {
                    mi.isReadable = true;
                    mi.optimizeBones = false;
                    mi.preserveHierarchy = true;
                    mi.SaveAndReimport();
                }
            }
        }

        static void ProcessStagedParts(
            string stagingAbs, string srcBuiltPartsDir, string dstPartsAbs, string dstPartsRel,
            Shader shader, Scene previewScene, Mesh emptyMesh,
            ref int copied, ref int errors)
        {
            if (!Directory.Exists(stagingAbs)) return;

            foreach (var skinDir in Directory.GetDirectories(stagingAbs))
            {
                var skinName = Path.GetFileName(skinDir);
                if (!skinName.StartsWith("S", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var classDir in Directory.GetDirectories(skinDir))
                {
                    var className = Path.GetFileName(classDir);

                    foreach (var variantDir in Directory.GetDirectories(classDir))
                    {
                        var variantName = Path.GetFileName(variantDir);
                        var underscore = variantName.LastIndexOf('_');
                        if (underscore < 0) continue;
                        var mm = variantName.Substring(underscore + 1);

                        foreach (var levelDir in Directory.GetDirectories(variantDir))
                        {
                            var levelName = Path.GetFileName(levelDir);
                            if (!levelName.StartsWith("Lvl_", StringComparison.OrdinalIgnoreCase)) continue;
                            var levelNum = levelName.Substring("Lvl_".Length);

                            foreach (var partTypeDir in Directory.GetDirectories(levelDir))
                            {
                                var rawPartType = Path.GetFileName(partTypeDir);
                                var partType = NormalizePartType(rawPartType);
                                var flatName = $"{className}-{partType}-{mm}-{skinName}-LV{levelNum}";
                                var flatDirRel = $"{dstPartsRel}/{flatName}";

                                EnsureAssetFolder(dstPartsRel, flatName);

                                // Texture — DeliveryAssets has no PNGs; source is BuiltAssets hierarchy
                                Texture2D mainTex = null;
                                var srcTexPath = Path.Combine(srcBuiltPartsDir, skinName, className, variantName, $"Lvl_{levelNum}", rawPartType, "MainTex.png");
                                if (File.Exists(srcTexPath))
                                {
                                    var dstPngAbs = Path.Combine(dstPartsAbs, flatName, "MainTex.png");
                                    var dstPngRel = $"{flatDirRel}/MainTex.png";
                                    File.Copy(srcTexPath, dstPngAbs, overwrite: true);
                                    AssetDatabase.ImportAsset(dstPngRel, ImportAssetOptions.ForceSynchronousImport);
                                    ConfigureTexture(dstPngRel);
                                    mainTex = AssetDatabase.LoadAssetAtPath<Texture2D>(dstPngRel);
                                    copied++;
                                }

                                // Material
                                var dstMatRel = $"{flatDirRel}/{partType}.mat";
                                var outlineOn = !partType.Equals("Mouth", StringComparison.OrdinalIgnoreCase) &&
                                               !partType.Equals("Eye", StringComparison.OrdinalIgnoreCase);
                                var mat = CreateOrUpdateMaterial(dstMatRel, shader, mainTex, outlineOn);
                                copied++;

                                // Rig sub-folders
                                var writtenRigs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var rigTypeDir in Directory.GetDirectories(partTypeDir))
                                {
                                    var rawRig = Path.GetFileName(rigTypeDir);
                                    var rig = NormalizeRigType(partType, rawRig);
                                    if (rig == null)
                                    {
                                        Debug.Log($"[AxieAssetSyncer] Skipping rig '{rawRig}' in {flatName}");
                                        continue;
                                    }
                                    if (!writtenRigs.Add(rig))
                                    {
                                        Debug.LogWarning($"[AxieAssetSyncer] Slot collision: '{rig}' already written in {flatName} — '{rawRig}' skipped");
                                        continue;
                                    }

                                    var modelDir = Path.Combine(rigTypeDir, "Model");
                                    if (!Directory.Exists(modelDir)) modelDir = rigTypeDir;

                                    var fbxFiles = Directory.GetFiles(modelDir, "*.fbx", SearchOption.TopDirectoryOnly)
                                        .OrderByDescending(f => f).ToArray();
                                    if (fbxFiles.Length == 0) continue;

                                    var fbxRel = ToStagingRelPath(fbxFiles[0]);
                                    var inputModel = AssetDatabase.LoadAssetAtPath<GameObject>(fbxRel);
                                    if (inputModel == null)
                                    {
                                        Debug.LogWarning($"[AxieAssetSyncer] Could not load staged FBX: {fbxRel}");
                                        errors++;
                                        continue;
                                    }

                                    var meshes = GetLODMeshes(inputModel, emptyMesh);
                                    if (meshes == null || meshes.Count == 0) continue;

                                    var rigDirRel = $"{flatDirRel}/{rig}";
                                    EnsureAssetFolder(flatDirRel, rig);

                                    var meshRel = $"{rigDirRel}/Model.mesh";
                                    var prefabRel = $"{rigDirRel}/Model.prefab";

                                    var outputMesh = CreateOrUpdateMesh(meshRel, meshes);
                                    if (outputMesh == null) { errors++; continue; }

                                    var prefab = CreateStrippedPrefab(inputModel, prefabRel, previewScene);
                                    if (prefab == null) { errors++; continue; }

                                    AssignMeshAndMaterial(prefabRel, outputMesh, mat);
                                    copied += 2;
                                }
                            }
                        }
                    }
                }
            }
        }

        // converts absolute staging path to Unity-relative "Assets/_AxieDeliveryStaging/..."
        static string ToStagingRelPath(string absPath)
        {
            var stagingAbs = StagingRootAbs;
            var rel = Path.GetFullPath(absPath).Substring(stagingAbs.Length).TrimStart('/', Path.DirectorySeparatorChar);
            return StagingRoot + "/" + rel.Replace(Path.DirectorySeparatorChar, '/');
        }

        // -------------------------------------------------------------------------
        // Asset helpers
        // -------------------------------------------------------------------------

        static void EnsureAssetFolder(string parentRel, string folderName)
        {
            var full = parentRel + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(full))
                AssetDatabase.CreateFolder(parentRel, folderName);
        }

        static void ConfigureTexture(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter ti) return;
            ti.isReadable = false;
            ti.mipmapEnabled = false;
            ti.maxTextureSize = 512;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        static Material CreateOrUpdateMaterial(string assetPath, Shader shader, Texture2D tex, bool outlineEnabled)
        {
            Material mat;
            if (AssetDatabase.LoadAssetAtPath<Material>(assetPath) is { } existing)
            {
                mat = existing;
                if (shader != null) mat.shader = shader;
                if (tex != null) mat.mainTexture = tex;
            }
            else
            {
                mat = new Material(shader != null ? shader : Shader.Find("Standard")) { mainTexture = tex };
                AssetDatabase.CreateAsset(mat, assetPath);
            }

            var kw = mat.shader?.keywordSpace.FindKeyword("_OUTLINE_ON") ?? default;
            if (kw.isValid)
            {
                mat.SetFloat("_Outline", outlineEnabled ? 1f : 0f);
                mat.SetKeyword(kw, outlineEnabled);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Mesh CreateOrUpdateMesh(string meshRel, List<Mesh> inputMeshes)
        {
            Mesh root;
            var existing = new List<Mesh>();

            if (AssetDatabase.LoadAssetAtPath<Mesh>(meshRel) is { } existingRoot)
            {
                EditorUtility.CopySerialized(inputMeshes[0], existingRoot);
                existingRoot.name = "Model";
                root = existingRoot;
                existing.Add(existingRoot);
                existing.AddRange(AssetDatabase.LoadAllAssetRepresentationsAtPath(meshRel).OfType<Mesh>());
            }
            else
            {
                root = UnityEngine.Object.Instantiate(inputMeshes[0]);
                root.name = "Model";
                AssetDatabase.CreateAsset(root, meshRel);
                existing.Add(root);
            }

            for (int lod = 1; lod < inputMeshes.Count; lod++)
            {
                if (lod < existing.Count)
                    EditorUtility.CopySerialized(inputMeshes[lod], existing[lod]);
                else
                {
                    var sub = UnityEngine.Object.Instantiate(inputMeshes[lod]);
                    sub.name = $"LOD{lod}";
                    AssetDatabase.AddObjectToAsset(sub, root);
                }
            }
            for (int lod = inputMeshes.Count; lod < existing.Count; lod++)
                UnityEngine.Object.DestroyImmediate(existing[lod], true);

            EditorUtility.SetDirty(root);
            return root;
        }

        static GameObject CreateStrippedPrefab(GameObject inputModel, string prefabRel, Scene previewScene)
        {
            var instance = PrefabUtility.InstantiatePrefab(inputModel, previewScene) as GameObject;
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            if (instance.TryGetComponent<LODGroup>(out var lodGroup) &&
                instance.GetComponentsInChildren<SkinnedMeshRenderer>() is { Length: > 0 } renderers)
            {
                var r = renderers[0];
                const int lodxLen = 5; // "_LODx"
                if (r.name.Length > lodxLen) r.name = r.name[..^lodxLen];
                UnityEngine.Object.DestroyImmediate(lodGroup);
                for (int i = 1; i < renderers.Length; i++)
                    UnityEngine.Object.DestroyImmediate(renderers[i].gameObject);
            }

            var saved = PrefabUtility.SaveAsPrefabAsset(instance, prefabRel);
            UnityEngine.Object.DestroyImmediate(instance);
            return saved;
        }

        static void AssignMeshAndMaterial(string prefabRel, Mesh mesh, Material mat)
        {
            var contents = PrefabUtility.LoadPrefabContents(prefabRel);
            if (contents == null) return;
            try
            {
                var r = contents.GetComponentInChildren<SkinnedMeshRenderer>();
                if (r != null)
                {
                    r.sharedMesh = mesh;
                    r.sharedMaterial = mat;
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabRel);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static List<Mesh> GetLODMeshes(GameObject model, Mesh emptyMesh)
        {
            if (model.TryGetComponent<LODGroup>(out var lodGroup))
            {
                return lodGroup.GetLODs()
                    .Select(lod => lod.renderers.Length == 1 && lod.renderers[0] is SkinnedMeshRenderer r
                        ? r.sharedMesh : emptyMesh)
                    .ToList();
            }
            if (model.GetComponentInChildren<SkinnedMeshRenderer>() is { } smr)
                return new List<Mesh> { smr.sharedMesh };
            return null;
        }

        // -------------------------------------------------------------------------
        // Normalization helpers
        // -------------------------------------------------------------------------

        static string NormalizePartType(string raw) => raw switch
        {
            "Tai" => "Tail",
            _ => raw,
        };

        static string NormalizeRigType(string partType, string rigType)
        {
            if (Enum.TryParse<AxieRigType>(rigType, out _)) return rigType;
            if (string.Equals(rigType, "l_1", StringComparison.Ordinal)) return null; // lowercase dup

            string side = rigType.ToUpperInvariant() switch
            {
                "M_1" or "M_2"             => "_M",
                "L_1"                      => "_L",
                "R_1"                      => "_R",
                "T_1" or "T_2" or "T_RIG" => "_T",
                _                          => null,
            };
            if (side != null)
            {
                var candidate = partType + side;
                if (Enum.TryParse<AxieRigType>(candidate, out _)) return candidate;
            }

            return (partType, rigType) switch
            {
                ("Back", "Back")    => "Back_M",
                ("Back", "Back__M") => "Back_M",
                ("Mouth", "L_1")   => "Mouth_M",
                _                  => rigType, // keep as-is (e.g. Mouth_L)
            };
        }

        // -------------------------------------------------------------------------
        // Validation — flat folder walk
        // -------------------------------------------------------------------------

        static readonly Regex FlatFolderRegex = new(
            @"^(?<class>[^-]+)-(?<partType>[^-]+)-(?<variant>\d{2})-(?<skin>S\d{2})-LV(?<level>\d)$",
            RegexOptions.Compiled);

        enum Classification { Ok, MissingMat }

        struct PartEntry
        {
            public string FlatName;
            public string Skin;
            public Classification Classification;
        }

        static string BuildValidationReport(string partsRoot)
        {
            var entries = new List<PartEntry>();
            var structuralIssues = new List<string>();

            foreach (var flatDir in Directory.GetDirectories(partsRoot))
            {
                var name = Path.GetFileName(flatDir);
                var m = FlatFolderRegex.Match(name);
                if (!m.Success) { structuralIssues.Add($"UNRECOGNIZED_FOLDER: {name}"); continue; }

                foreach (var rigDir in Directory.GetDirectories(flatDir))
                {
                    var rigName = Path.GetFileName(rigDir);
                    if (!Enum.TryParse<AxieRigType>(rigName, out _))
                        structuralIssues.Add($"INVALID_RIG_TYPE: {name}/{rigName}");
                }

                bool hasMat = Directory.GetFiles(flatDir, "*.mat", SearchOption.TopDirectoryOnly).Length > 0;
                entries.Add(new PartEntry
                {
                    FlatName = name,
                    Skin = m.Groups["skin"].Value,
                    Classification = hasMat ? Classification.Ok : Classification.MissingMat,
                });
            }

            return FormatReport(entries, structuralIssues);
        }

        static string FormatReport(List<PartEntry> entries, List<string> structuralIssues)
        {
            int ok = 0, missing = 0;
            var missingBySkin = new SortedDictionary<string, List<string>>();
            var invalidRigs = new List<string>();
            var unrecognized = new List<string>();

            foreach (var issue in structuralIssues)
            {
                if (issue.StartsWith("INVALID_RIG_TYPE")) invalidRigs.Add(issue);
                else unrecognized.Add(issue);
            }

            foreach (var e in entries)
            {
                if (e.Classification == Classification.Ok) { ok++; continue; }
                missing++;
                if (!missingBySkin.TryGetValue(e.Skin, out var list))
                    missingBySkin[e.Skin] = list = new List<string>();
                list.Add(e.FlatName);
            }

            int totalStructural = invalidRigs.Count + unrecognized.Count;
            var sb = new StringBuilder();
            sb.AppendLine("# Axie Mixer 3D — Asset Validation Report");
            sb.AppendLine($"*Generated: {DateTime.Now:yyyy-MM-dd HH:mm}*");
            sb.AppendLine();
            sb.AppendLine("Scans `AxieMixerAssets/BuiltAssets/Parts/` (flat format).");
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine("| Category | Count |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| OK (has `.mat`) | **{ok}** |");
            sb.AppendLine($"| Missing mat | **{missing}** |");
            sb.AppendLine($"| Structural issues | **{totalStructural}** |");
            sb.AppendLine();

            if (missing > 0)
            {
                sb.AppendLine("## Missing mat");
                sb.AppendLine();
                foreach (var kv in missingBySkin)
                {
                    sb.AppendLine($"### {kv.Key} ({kv.Value.Count} parts)");
                    sb.AppendLine();
                    foreach (var p in kv.Value) sb.AppendLine($"- {p}");
                    sb.AppendLine();
                }
            }

            if (totalStructural > 0)
            {
                sb.AppendLine("## Structural issues");
                sb.AppendLine();
                if (unrecognized.Count > 0)
                {
                    sb.AppendLine("### UNRECOGNIZED_FOLDER");
                    sb.AppendLine();
                    sb.AppendLine("Folder does not match `{Class}-{PartType}-{MM}-S{NN}-LV{N}` pattern.");
                    sb.AppendLine();
                    foreach (var i in unrecognized) sb.AppendLine($"- {i}");
                    sb.AppendLine();
                }
                if (invalidRigs.Count > 0)
                {
                    sb.AppendLine("### INVALID_RIG_TYPE");
                    sb.AppendLine();
                    sb.AppendLine("Rig sub-folder name is not a valid `AxieRigType` enum value. `AxieDataUpdater` skips these.");
                    sb.AppendLine();
                    foreach (var i in invalidRigs) sb.AppendLine($"- {i}");
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
    }
}
