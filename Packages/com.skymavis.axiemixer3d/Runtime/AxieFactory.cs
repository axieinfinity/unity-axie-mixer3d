using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Profiling;
using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    /// <summary>
    /// A named animation clip handed to <see cref="AxieFactory.RegisterAnimations"/>. Lets an
    /// optional package (e.g. the weapon-anim package) register extra clips with the factory
    /// without depending on any internal catalog types.
    /// </summary>
    public struct AxieNamedClip
    {
        public string name;
        public AnimationClip clip;
    }

    public class AxieFactory : ScriptableObject
    {
        // The catalog must be assigned by the host application (e.g. via
        // AxieMixerInitializer) before any character is created. The package
        // intentionally does not auto-load this from Resources so that the
        // heavy body/part/addon assets it references are NOT force-included
        // in every build.
        public static AxieFactory Default { get; set; }

        // The raw output of the assembly pipeline (Build), consumed by AxieCharacter3D to populate or
        // rebuild itself. root == null signals a failed build.
        internal struct AxieBuildResult
        {
            public GameObject root;
            public Transform rightWeaponAttachPoint;
            public Transform leftWeaponAttachPoint;
            public AxieBodyData bodyData;
            public IReadOnlyList<GameObject> outlineExcludedParts;
            public IReadOnlyList<Material> ownedMaterials;
            public IReadOnlyList<Mesh> ownedMeshes;
            public AxieDescriptor coercedDescriptor;
            public AxieInstantiationParams mergedParams;
        }

        static readonly Regex AttachPointRegex = new(@"^Root_(?<rigType>\w+)_JNT$", RegexOptions.Compiled);

        [SerializeField]
        AxieColorVariant[] _colors = System.Array.Empty<AxieColorVariant>();
        [SerializeField]
        AxieInstantiationParams _defaultInstantiationParams;
        [SerializeField]
        AxieBodyEntry[] _bodies = System.Array.Empty<AxieBodyEntry>();
        [SerializeField]
        AxiePartEntry[] _parts = System.Array.Empty<AxiePartEntry>();
        [SerializeField]
        AxieAddonEntry[] _addons = System.Array.Empty<AxieAddonEntry>();
        // Shaders reached only by name at runtime (e.g. OutlinePostProcessRendererFeature does
        // CoreUtils.CreateEngineMaterial("Axie Mixer 3D/Outline/PostProcess") -> Shader.Find) have no hard
        // asset reference, so a player build would strip them. We force-include them by referencing a MATERIAL
        // that uses each such shader — not the Shader asset directly: a material anchors the shader AND the
        // exact variants it needs, whereas a bare Shader reference is prone to build-time variant stripping
        // (outline renders magenta / disappears in builds). This catalog ships in every build via the host's
        // AxieMixerInitializer, so the referenced materials keep their shaders alive and Shader.Find works at
        // runtime. Never read — the serialized reference is the entire purpose.
        [SerializeField]
        Material[] _forceIncludedMaterials = System.Array.Empty<Material>();

        Dictionary<AxieBodyType, AxieBodyData> _bodyMap;
        Dictionary<string, AxiePartEntry> _partMap;
        Dictionary<string, AxieAddonEntry> _addonMap;
        Dictionary<int, AxieColorVariant> _colorMap;

        // Extra clips registered at runtime by optional packages (e.g. weapon anims), keyed by
        // body then by case-insensitive clip name. Intentionally NOT serialized — baking these
        // into the catalog would re-couple the heavy clips into every build, which is exactly
        // what keeping them in a separate package avoids. Cleared on OnDisable / domain reload.
        Dictionary<AxieBodyType, Dictionary<string, AnimationClip>> _registeredAnimations;

        public void SetupEmpty()
        {
            _colors = System.Array.Empty<AxieColorVariant>();
        }

        void OnEnable()
        {
            BuildLookups();
        }

        void OnDisable()
        {
            _bodyMap = null;
            _partMap = null;
            _addonMap = null;
            _colorMap = null;
            _registeredAnimations = null;
        }

        /// <summary>
        /// Register (or replace) extra animation clips for a body. Idempotent per name — a later
        /// registration with the same (body, name) overwrites the earlier clip. Names are matched
        /// case-insensitively, consistent with the body's baked animation set. Null/empty names and
        /// null clips are skipped.
        /// </summary>
        public void RegisterAnimations(AxieBodyType body, IEnumerable<AxieNamedClip> clips)
        {
            if (clips == null) return;

            _registeredAnimations ??= new Dictionary<AxieBodyType, Dictionary<string, AnimationClip>>();
            if (!_registeredAnimations.TryGetValue(body, out var map))
            {
                map = new Dictionary<string, AnimationClip>(System.StringComparer.OrdinalIgnoreCase);
                _registeredAnimations[body] = map;
            }

            foreach (var namedClip in clips)
            {
                if (string.IsNullOrEmpty(namedClip.name) || namedClip.clip == null) continue;
                map[namedClip.name] = namedClip.clip;
            }
        }

        /// <summary>Convenience single-clip overload of <see cref="RegisterAnimations"/>.</summary>
        public void RegisterAnimation(AxieBodyType body, string name, AnimationClip clip)
        {
            if (string.IsNullOrEmpty(name) || clip == null) return;
            RegisterAnimations(body, new[] { new AxieNamedClip { name = name, clip = clip } });
        }

        /// <summary>Remove a single registered clip. Returns true if a clip was removed.</summary>
        public bool UnregisterAnimation(AxieBodyType body, string name)
            => !string.IsNullOrEmpty(name)
               && _registeredAnimations != null
               && _registeredAnimations.TryGetValue(body, out var map)
               && map.Remove(name);

        /// <summary>Remove all registered clips for every body.</summary>
        public void ClearRegisteredAnimations() => _registeredAnimations?.Clear();

        /// <summary>Remove all registered clips for a single body.</summary>
        public void ClearRegisteredAnimations(AxieBodyType body) => _registeredAnimations?.Remove(body);

        /// <summary>
        /// Looks up a runtime-registered clip for a body. Used by <see cref="AxieCharacter3D.GetAnimClip"/>
        /// as a fallback after the body's baked set misses. Case-insensitive; returns null on miss.
        /// </summary>
        internal AnimationClip GetRegisteredAnimClip(AxieBodyType body, string name)
            => !string.IsNullOrEmpty(name)
               && _registeredAnimations != null
               && _registeredAnimations.TryGetValue(body, out var map)
               && map.TryGetValue(name, out var clip)
               ? clip : null;

        void BuildLookups()
        {
            _bodyMap = new();
            foreach (var entry in _bodies)
            {
                if (entry.data != null) _bodyMap[entry.type] = entry.data;
            }

            _partMap = new();
            foreach (var entry in _parts)
            {
                if (entry.rigs != null && !string.IsNullOrEmpty(entry.name)) _partMap[entry.name] = entry;
            }

            _addonMap = new();
            foreach (var entry in _addons)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.name)) _addonMap[entry.name] = entry;
            }

            _colorMap = new();
            // First-wins to match the previous FirstOrDefault semantics if indices ever collide.
            foreach (var color in _colors) _colorMap.TryAdd(color.index, color);
        }

        public AxieCharacter3D CreateCharacter(AxieDescriptor axieDescriptor, AxieInstantiationParams instantiationParams = null)
        {
            var buildResult = Build(axieDescriptor, instantiationParams);
            if (buildResult.root == null) return null;

            var character = new AxieCharacter3D(buildResult);

            // Apply the project-wide default Draw-Objects outline (set once at bootstrap, e.g. by
            // AxieMixerInitializer). Callers override per-object via AxieCharacter3D.SetOutlineLayer.
            if (AxieCharacter3D.DefaultOutlineLayer >= 0)
            {
                character.SetOutlineLayer(AxieCharacter3D.DefaultOutlineLayer, AxieCharacter3D.DefaultOutlineBaseLayer);
            }

            return character;
        }

        // Runs the full assembly pipeline and returns the raw pieces, without constructing an
        // AxieCharacter3D. Shared by CreateCharacter (first build) and AxieCharacter3D.ApplyDescriptor
        // (in-place rebuild). Returns default (root == null) if the body/prefab can't be resolved.
        internal AxieBuildResult Build(AxieDescriptor axieDescriptor, AxieInstantiationParams instantiationParams = null)
        {
            if (_bodyMap == null) BuildLookups();

            axieDescriptor = CoerceDescriptor(axieDescriptor);
            // Capture before merge so null (no caller params) is distinguishable from explicit false.
            bool? callerCombineMeshes = instantiationParams?.combineMeshes;
            instantiationParams = (_defaultInstantiationParams ?? new()).Merge(instantiationParams);

            if (!_bodyMap.TryGetValue(axieDescriptor.body, out var bodyData) || bodyData == null)
            {
                Debug.LogError($"Cannot find body {axieDescriptor.body}.");
                return default;
            }

            var bodyPrefab = bodyData.prefab;
            if (bodyPrefab == null)
            {
                Debug.LogError($"Body prefab for {axieDescriptor.body} is missing.");
                return default;
            }

            var root = Instantiate(bodyPrefab);
            var (attachPoints, leftWeaponAttachPoint, rightWeaponAttachPoint) = CollectAttachPoints(root);
            var rigTypeSet = new HashSet<AxieRigType>();
            // Captured so AxieCharacter3D.SetOutlineLayer can keep eyes/mouth off the outline layer.
            var outlineExcludedParts = new List<GameObject>();

            foreach (var partDescriptor in axieDescriptor.parts)
            {
                // Skin (S00–S12) and level (1/2) come straight from the genes/descriptor;
                // TryResolvePart degrades to a shipped asset when this class+variant doesn't
                // ship the requested tier/level (e.g. a mystic bit on a non-mystic part).
                if (!TryResolvePart(partDescriptor, out var partEntry, out var partSkin, out var partLevel))
                {
                    Debug.LogWarning($"[AxieFactory] No asset for {partDescriptor.@class}{partDescriptor.variant:00} {partDescriptor.type} (skin={partDescriptor.skin}, level={partDescriptor.level}). Part skipped.");
                    continue;
                }

                var layerOverrideIndex = instantiationParams.partLayerOverrides.FindIndex(x => x.type == partDescriptor.type);
                var isOutlineExcluded = AxieCharacter3D.OutlineExcludedPartTypes.Contains(partDescriptor.type);
                rigTypeSet.Clear();

                foreach (var rigData in partEntry.rigs)
                {
                    if (!attachPoints.TryGetValue(rigData.type, out var attachPoint))
                    {
                        Debug.LogError($"Cannot find attach point for {rigData.type}.");
                        continue;
                    }

                    var rigPrefab = rigData.prefab;
                    if (rigPrefab == null) continue;

                    var part = Instantiate(rigPrefab, attachPoint);
                    part.name = $"{partEntry.name}_{rigData.type}";

                    if (isOutlineExcluded) outlineExcludedParts.Add(part);

                    if (layerOverrideIndex >= 0)
                    {
                        SetLayerRecursively(part, instantiationParams.partLayerOverrides[layerOverrideIndex].layer);
                    }

                    // Use the resolved skin/level (post-fallback) so the addon matches the
                    // part asset actually selected, not a tier this class+variant lacks.
                    var addonName = $"{partDescriptor.@class}-{rigData.type.ToAxiePartType()}-{partDescriptor.variant:00}-S{partSkin:00}-LV{partLevel}/{rigData.type}";
                    _addonMap.TryGetValue(addonName, out var addon);

                    if (addon != null && part.GetComponentInChildren<Renderer>() is { } renderer)
                    {
                        Material addonMaterial = null;
                        if (addon.materials.Length == 1)
                        {
                            addonMaterial = addon.materials[0].material;
                        }
                        else
                        {
                            var rigPrefabName = rigPrefab.name;
                            foreach (var entry in addon.materials)
                            {
                                if (entry.name == rigPrefabName && entry.material is { } m)
                                {
                                    addonMaterial = m;
                                    break;
                                }
                            }
                        }
                        if (addonMaterial != null)
                            renderer.sharedMaterial = addonMaterial;
                    }

                    if (rigTypeSet.Add(rigData.type) && addon != null)
                    {
                        foreach (var addonPrefabRef in addon.prefabs)
                        {
                            if (addonPrefabRef is { } addonPrefab)
                            {
                                Instantiate(addonPrefab, attachPoint);
                            }
                        }
                    }
                }
            }

            var ownedMaterials = Colorize();

            // Merge the per-part renderers (default on). Reads the colorized materials assigned just above, so
            // merged sub-meshes carry the right per-instance colors. Repoints outline exclusion at the merged
            // "excluded" renderer (eyes/mouth) and hands the runtime meshes to AxieCharacter3D for disposal.
            IReadOnlyList<GameObject> outlineParts = outlineExcludedParts;
            IReadOnlyList<Mesh> ownedMeshes = System.Array.Empty<Mesh>();
            if (callerCombineMeshes ?? AxieCharacter3D.DefaultCombineMeshes)
            {
                var merged = AxieMeshCombiner.Combine(root, outlineExcludedParts);
                var meshes = new List<Mesh>(merged.Count);
                var excluded = new List<GameObject>();
                foreach (var m in merged)
                {
                    meshes.Add(m.Mesh);
                    if (m.IsOutlineExcluded) excluded.Add(m.Renderer.gameObject);
                }
                ownedMeshes = meshes;
                outlineParts = excluded;
            }

            return new AxieBuildResult
            {
                root = root,
                rightWeaponAttachPoint = rightWeaponAttachPoint,
                leftWeaponAttachPoint = leftWeaponAttachPoint,
                bodyData = bodyData,
                outlineExcludedParts = outlineParts,
                ownedMaterials = ownedMaterials,
                ownedMeshes = ownedMeshes,
                coercedDescriptor = axieDescriptor,
                mergedParams = instantiationParams,
            };

            List<Material> Colorize()
            {
                var owned = new List<Material>();

                if (_colorMap == null || !_colorMap.TryGetValue(axieDescriptor.colorVariant, out var colorVariant))
                    return owned;

                var materialMap = new Dictionary<Material, Material>();
                var primaryColor = Color.white;
                var secondaryColor = Color.white;
                ParseColor(ref primaryColor, colorVariant.primary1);
                ParseColor(ref secondaryColor, colorVariant.primary2);

                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var material = renderer.sharedMaterial;
                    if (material == null) continue;

                    if (materialMap.TryGetValue(material, out var clonedMaterial))
                    {
                        renderer.sharedMaterial = clonedMaterial;
                        continue;
                    }

                    var clone = Instantiate(material);
                    clone.name = material.name;
                    clone.SetColor("_PrimaryColor", primaryColor);
                    clone.SetColor("_SecondaryColor", secondaryColor);
                    renderer.sharedMaterial = clone;
                    materialMap.Add(material, clone);
                    owned.Add(clone);
                }

                return owned;
            }

            static void ParseColor(ref Color color, string value)
            {
                if (ColorUtility.TryParseHtmlString($"#{value}", out var parsedColor))
                {
                    color = parsedColor;
                }
                else
                {
                    Debug.LogWarning($"Cannot parse color #{value}");
                }
            }
        }

        static void SetLayerRecursively(GameObject gameObject, int layer)
        {
            gameObject.layer = layer;
            foreach (Transform child in gameObject.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        static readonly ProfilerMarker CollectAttachPointsMarker = new($"{typeof(AxieFactory).FullName}.{nameof(CollectAttachPoints)}");

        internal static (Dictionary<AxieRigType, Transform> attachPoints, Transform leftWeaponAttachPoint, Transform rightWeaponAttachPoint) CollectAttachPoints(GameObject root)
        {
            using var _ = CollectAttachPointsMarker.Auto();
            var attachPoints = new Dictionary<AxieRigType, Transform>();
            var leftWeaponAttachPoint = default(Transform);
            var rightWeaponAttachPoint = default(Transform);
            foreach (var transform in root.GetComponentsInChildren<Transform>())
            {
                if (AttachPointRegex.Match(transform.name) is { Success: true } match)
                {
                    var rigTypeName = match.Groups["rigType"].Value;

                    if (System.Enum.TryParse<AxieRigType>(rigTypeName, out var rigType))
                    {
                        attachPoints[rigType] = transform;

                    }
                    else
                    {
                        switch (rigTypeName)
                        {
                            case "Weapon_R":
                                rightWeaponAttachPoint = transform;
                                break;
                            case "Weapon_L":
                                leftWeaponAttachPoint = transform;
                                break;
                        }
                    }
                }
            }

            return (attachPoints, leftWeaponAttachPoint, rightWeaponAttachPoint);
        }

        // Resolves the shipped part entry for a descriptor, degrading gracefully when the
        // requested skin tier (S00–S12) or level (1/2) isn't shipped for this class+variant.
        // Genes can carry a skin/level combo that only some parts have (e.g. a mystic bit on a
        // non-mystic part, or a stage with no Lvl_2 art) — fall back toward the base S00 / Lvl_1
        // entry that always exists rather than dropping the part. Outputs the resolved skin/level
        // so callers (addon lookup) stay consistent with the entry actually chosen.
        bool TryResolvePart(AxiePartDescriptor part, out AxiePartEntry partEntry, out int resolvedSkin, out int resolvedLevel)
        {
            var skin = part.skin < 0 ? 0 : part.skin;
            var level = part.level < 1 ? 1 : part.level;

            if (TryGetPart(skin, level, out partEntry)) { resolvedSkin = skin; resolvedLevel = level; return true; }
            if (level != 1 && TryGetPart(skin, 1, out partEntry)) { resolvedSkin = skin; resolvedLevel = 1; return true; }
            if (skin != 0 && TryGetPart(0, level, out partEntry)) { resolvedSkin = 0; resolvedLevel = level; return true; }
            if (skin != 0 && level != 1 && TryGetPart(0, 1, out partEntry)) { resolvedSkin = 0; resolvedLevel = 1; return true; }

            resolvedSkin = skin;
            resolvedLevel = level;
            return false;

            bool TryGetPart(int s, int l, out AxiePartEntry entry)
            {
                var name = $"S{s:00}_{part.@class}{part.variant:00}_L{l}_{part.type}";
                return _partMap.TryGetValue(name, out entry);
            }
        }

        internal static AxieDescriptor CoerceDescriptor(AxieDescriptor descriptor)
        {
            // Defensive copy so mixing never mutates the caller's list. Skin (S00–S12) and
            // level (1/2) flow straight through from the genes/descriptor — TryResolvePart
            // handles any tier/level a class+variant doesn't ship, so nothing is collapsed here.
            // AxieDescriptor is a public struct, so tolerate a null parts list from a hand-built one.
            descriptor.parts = descriptor.parts?.ToList() ?? new List<AxiePartDescriptor>();
            return descriptor;
        }

        /// <summary>
        /// Returns true if the catalog contains a part asset for exactly this skin/level — no fallback.
        /// Useful for pre-checking whether a skin tier is available before building a character.
        /// </summary>
        public bool HasPart(string className, int variant, int skin, int level, AxiePartType type)
        {
            if (_partMap == null) BuildLookups();
            var name = $"S{skin:00}_{className}{variant:00}_L{level}_{type}";
            return _partMap.ContainsKey(name);
        }

        [System.Obsolete("Addons are now pre-baked into the catalog. ClearCache() is a no-op.", false)]
        public void ClearCache() { }

#if UNITY_EDITOR
        // Colors are authored directly on the catalog asset (the _colors array) and are intentionally
        // NOT touched here, so re-baking bodies/parts/addons preserves them.
        internal void EditorAssign(AxieBodyEntry[] bodies, AxiePartEntry[] parts, AxieAddonEntry[] addons)
        {
            _bodies = bodies ?? System.Array.Empty<AxieBodyEntry>();
            _parts = parts ?? System.Array.Empty<AxiePartEntry>();
            _addons = addons ?? System.Array.Empty<AxieAddonEntry>();
            BuildLookups();
        }
#endif
    }
}
