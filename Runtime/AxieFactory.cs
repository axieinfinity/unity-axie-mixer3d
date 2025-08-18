using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Profiling;
using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    internal class AxieFactory : ScriptableObject
    {
        public static AxieFactory Default => Resources.Load<AxieFactory>("AxieMixer3D/AxieFactory");
        static readonly Regex AttachPointRegex = new(@"^Root_(?<rigType>\w+)_JNT$", RegexOptions.Compiled);

        [SerializeField]
        AxieMixerConfig _config;
        [SerializeField]
        string[] _addonPaths;

        readonly Dictionary<string, (Dictionary<string, Material> materials, List<GameObject> prefabs)> _addonCache = new();

        void OnDisable()
        {
            ClearCache();
        }

        public AxieCharacter3D CreateCharacter(AxieDescriptor axieDescriptor)
        {
            const string dataPath = "AxieMixer3D/Data";

            if (Resources.Load<AxieBodyData>(Path.Combine(dataPath, "Bodies", axieDescriptor.body.ToString())) is not { } bodyData)
            {
                Debug.LogError($"Cannot find body {axieDescriptor.body}.");
                return null;
            }

            var root = Instantiate(bodyData.prefab);
            var (attachPoints, leftWeaponAttachPoint, rightWeaponAttachPoint) = CollectAttachPoints(root);
            var rigTypeSet = new HashSet<AxieRigType>();

            foreach (var partDescriptor in axieDescriptor.parts)
            {
                var partName = $"S{partDescriptor.skin:00}_{partDescriptor.@class}{partDescriptor.variant:00}_L{partDescriptor.level}_{partDescriptor.type}";

                if (Resources.Load<AxiePartData>(Path.Combine(dataPath, "Parts", partName)) is not { } partData)
                {
                    // Debug.LogError($"Cannot find part {partName}.");
                    continue;
                }

                rigTypeSet.Clear();

                foreach (var rigData in partData.rigs)
                {
                    if (!attachPoints.TryGetValue(rigData.type, out var attachPoint))
                    {
                        Debug.LogError($"Cannot find attach point for {rigData.type}.");
                        continue;
                    }

                    var part = Instantiate(rigData.prefab, attachPoint);
                    part.name = $"{partData.name}_{rigData.prefab.GetInstanceID():X8}";

                    var addons = GetAddons(partDescriptor, rigData.type);

                    if (
                        addons.materials.TryGetValue(rigData.prefab.name, out var addonMaterial) &&
                        part.GetComponentInChildren<Renderer>() is { } renderer
                    )
                    {
                        renderer.sharedMaterial = addonMaterial;
                    }

                    if (rigTypeSet.Add(rigData.type))
                    {
                        foreach (var addonPrefab in addons.prefabs)
                        {
                            Instantiate(addonPrefab, attachPoint);
                        }
                    }
                }
            }

            Colorize();

            return new(root, rightWeaponAttachPoint, leftWeaponAttachPoint, bodyData);

            (Dictionary<string, Material> materials, List<GameObject> prefabs) GetAddons(AxiePartDescriptor partDescriptor, AxieRigType rigType)
            {
                var addonName = $"S{partDescriptor.skin:00}_{partDescriptor.@class}{partDescriptor.variant:00}_L{partDescriptor.level}_{rigType}";

                if (_addonCache.TryGetValue(addonName, out var addons))
                {
                    return addons;
                }

                var materials = new Dictionary<string, Material>();
                var prefabs = new List<GameObject>();

                foreach (var addonPath in _addonPaths)
                {
                    var assets = Resources.LoadAll(Path.Combine(addonPath, addonName));

                    foreach (var asset in assets)
                    {
                        switch (asset)
                        {
                            case GameObject prefab:
                                prefabs.Add(prefab);
                                break;
                            case Material material:
                                materials[material.name] = material;
                                break;
                        }
                    }
                }

                return _addonCache[addonName] = (materials, prefabs);
            }

            void Colorize()
            {
                if (
                    _config?.items.colors?.FirstOrDefault(c => c.index == axieDescriptor.colorVariant) is not { } colorVariant
                ) return;

                var materialMap = new Dictionary<Material, Material>();
                var primaryColor = Color.white;
                var secondaryColor = Color.white;
                ParseColor(ref primaryColor, colorVariant.primary1);
                ParseColor(ref secondaryColor, colorVariant.primary2);

                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    if (materialMap.TryGetValue(renderer.sharedMaterial, out var clonedMaterial))
                    {
                        renderer.sharedMaterial = clonedMaterial;
                        continue;
                    }

                    var material = renderer.sharedMaterial;
                    renderer.sharedMaterial = Instantiate(renderer.sharedMaterial);
                    renderer.sharedMaterial.name = material.name;
                    renderer.sharedMaterial.SetColor("_PrimaryColor", primaryColor);
                    renderer.sharedMaterial.SetColor("_SecondaryColor", secondaryColor);
                    materialMap.Add(material, renderer.sharedMaterial);
                }
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

        public void ClearCache()
        {
            _addonCache.Clear();
        }
    }
}
