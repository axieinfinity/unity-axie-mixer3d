using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using SkyMavis.AxieMixer3D.WeaponAnims;

namespace SkyMavis.AxieMixer3D.Editor
{
    /// <summary>
    /// Bakes the optional weapon-anim package: scans its per-body <c>Action.*</c> clips into
    /// <see cref="AxieWeaponAnimCatalog"/> and regenerates <c>WeaponAnimNames.cs</c>. Mirrors the body
    /// bake in <see cref="AxieDataUpdater"/> and shares <see cref="AxieAnimNaming.NormalizeKey"/> so the
    /// runtime keys match exactly. This is a dev/authoring tool (lives in the project's Editor
    /// assembly, not shipped in the package) — consumers just install the pre-baked catalog.
    /// </summary>
    public static class AxieWeaponAnimUpdater
    {
        const string PackageRoot = "Packages/com.skymavis.axiemixer3d.weaponanims";
        const string BasePath = PackageRoot + "/WeaponAnimAssets";
        const string CatalogPath = BasePath + "/Catalog/AxieWeaponAnimCatalog.asset";
        const string AnimNamesPath = PackageRoot + "/Runtime/WeaponAnimNames.cs";

        [MenuItem("Tools/Axie Mixer 3D/Update Weapon Anim Catalog")]
        public static void UpdateWeaponAnimCatalog()
        {
            try
            {
                AssetDatabase.StartAssetEditing();
                var catalog = LoadOrCreateCatalog();
                BuildCatalog(catalog);
                GenerateWeaponAnimNames(catalog);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.StopAssetEditing();
                Resources.UnloadUnusedAssets();
            }
        }

        static AxieWeaponAnimCatalog LoadOrCreateCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AxieWeaponAnimCatalog>(CatalogPath);
            if (catalog == null)
            {
                EnsureFolderExists(Path.GetDirectoryName(CatalogPath));
                catalog = ScriptableObject.CreateInstance<AxieWeaponAnimCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            return catalog;
        }

        static void BuildCatalog(AxieWeaponAnimCatalog catalog)
        {
            catalog.bodies.Clear();

            var bodiesRoot = Path.Combine(BasePath, "Bodies");
            if (!AssetDatabase.IsValidFolder(bodiesRoot))
            {
                Debug.LogWarning($"[{nameof(AxieWeaponAnimUpdater)}] No Bodies folder at '{bodiesRoot}'. " +
                                 "Move the Action.*.anim clips into the weapon package first.");
                EditorUtility.SetDirty(catalog);
                return;
            }

            foreach (AxieBodyType body in System.Enum.GetValues(typeof(AxieBodyType)))
            {
                var animDir = Path.Combine(bodiesRoot, body.ToString(), "Animations");
                if (!AssetDatabase.IsValidFolder(animDir)) continue;

                var bodyEntry = new AxieWeaponAnimBody { body = body };
                var seenKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

                foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(AnimationClip)}", new[] { animDir }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip == null) continue;

                    var key = AxieAnimNaming.NormalizeKey(clip.name);
                    if (string.IsNullOrEmpty(key))
                    {
                        Debug.LogWarning($"[{nameof(AxieWeaponAnimUpdater)}] body '{body}' clip '{clip.name}' ({path}) " +
                                         "normalizes to an empty name; skipping (unusable as an animation key).");
                        continue;
                    }
                    if (!seenKeys.Add(key))
                    {
                        Debug.LogWarning($"[{nameof(AxieWeaponAnimUpdater)}] body '{body}' has two clips normalizing " +
                                         $"to '{key}' (e.g. '{clip.name}'). Keeping the first, skipping this one.");
                        continue;
                    }

                    bodyEntry.animations.Add(new AxieWeaponAnimEntry { name = key, clip = clip });
                }

                bodyEntry.animations.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
                if (bodyEntry.animations.Count > 0) catalog.bodies.Add(bodyEntry);
            }

            catalog.bodies.Sort((a, b) => a.body.CompareTo(b.body));
            EditorUtility.SetDirty(catalog);
        }

        static void GenerateWeaponAnimNames(AxieWeaponAnimCatalog catalog)
        {
            var names = new SortedDictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var body in catalog.bodies)
                foreach (var entry in body.animations)
                    if (!string.IsNullOrEmpty(entry.name))
                        names.TryAdd(entry.name, entry.name);

            var newSource = BuildSource(names.Values);
            if (File.Exists(AnimNamesPath) && File.ReadAllText(AnimNamesPath) == newSource) return;
            File.WriteAllText(AnimNamesPath, newSource);
            AssetDatabase.ImportAsset(AnimNamesPath);
        }

        static string ToIdentifier(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }

        static string BuildSource(IEnumerable<string> bareNames)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//     Generated by Tools -> Axie Mixer 3D -> Update Weapon Anim Catalog (AxieWeaponAnimUpdater).");
            sb.AppendLine("//     Do NOT edit by hand; re-run the weapon data-update tool to regenerate.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("namespace SkyMavis.AxieMixer3D.WeaponAnims");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Typed weapon/action animation clip names (bare — no \"Action.\" prefix). These are the names");
            sb.AppendLine("    /// that leave the main package's <c>AnimNames</c> when the weapon clips are split out.");
            sb.AppendLine("    /// Note: the Cannon weapon is baked as \"Canon\" in the source art; the tool normalizes it, so the");
            sb.AppendLine("    /// constants read \"Cannon*\" (double n).");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class WeaponAnimNames");
            sb.AppendLine("    {");
            var emitted = new HashSet<string>();
            foreach (var name in bareNames)
            {
                var id = ToIdentifier(name);
                if (!emitted.Add(id))
                {
                    Debug.LogWarning($"{nameof(AxieWeaponAnimUpdater)}: identifier collision for '{name}' -> '{id}'. Skipping.");
                    continue;
                }
                if (id != name)
                    Debug.LogWarning($"{nameof(AxieWeaponAnimUpdater)}: clip name '{name}' is not a valid C# identifier; emitting as '{id}'.");
                sb.AppendLine($"        public const string {id} = \"{name}\";");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
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
    }
}
