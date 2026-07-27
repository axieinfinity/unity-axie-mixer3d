using System.Collections.Generic;
using SkyMavis.AxieMixer3D;
using UnityEngine;

namespace SkyMavis.AxieMixer3D.Example
{
    /// <summary>
    /// Spawns a grid of randomly-generated (but always valid) Axies from the embedded
    /// <c>com.skymavis.axiemixer3d</c> package. Each Axie gets a random body, class, variant
    /// and a class-appropriate color, is spaced out on a grid, and is renamed so you can
    /// tell which is which (e.g. <c>Axie_00_Beast04_Curly_c3</c>).
    ///
    /// Attach it to an empty GameObject and press Play.
    /// </summary>
    public class AxieMixer3DSpawner : MonoBehaviour
    {
        [Header("Grid (spawns rows × columns Axies)")]
        [Tooltip("Number of rows. Total Axies = rows × columns.")]
        [Min(1)] public int rows = 3;
        [Tooltip("Number of columns. Total Axies = rows × columns.")]
        [Min(1)] public int columns = 3;
        [Tooltip("Distance between adjacent Axies, in world units.")]
        public float spacing = 2.5f;

        [Header("Randomness")]
        [Tooltip("Use a fixed seed for reproducible layouts. Otherwise every Play is different.")]
        public bool useSeed = false;
        public int seed = 12345;

        [Header("Animation")]
        public AxieAnimation animation = AxieAnimation.Idle;

        [Header("Scene setup (created at runtime if missing)")]
        public bool createCameraAndLight = true;

        readonly List<AxieCharacter3D> _characters = new();
        readonly List<AxiePlayable> _playables = new();

        void Start()
        {
            if (createCameraAndLight) EnsureCameraAndLight();

            var rng = useSeed ? new System.Random(seed) : new System.Random();

            int count = rows * columns;
            for (int i = 0; i < count; i++)
            {
                var axieClass = AxieMixerExampleOptions.AllClasses[rng.Next(AxieMixerExampleOptions.AllClasses.Length)];
                var variant = AxieMixerExampleOptions.AllVariants[rng.Next(AxieMixerExampleOptions.AllVariants.Length)];
                var body = AxieMixerExampleOptions.AllBodies[rng.Next(AxieMixerExampleOptions.AllBodies.Length)];
                var colors = AxieMixerExampleOptions.ColorVariantsFor(axieClass);
                var colorVariant = colors[rng.Next(colors.Count)];

                var descriptor = BuildDescriptor(body, axieClass, variant, colorVariant);
                var character = AxieCharacter3D.FromDescriptor(descriptor);
                _characters.Add(character);

                var variantNumber = AxieMixerExampleOptions.ToVariant(variant);
                var name = $"Axie_{i:00}_{axieClass}{variantNumber:00}_{body}_c{colorVariant}";
                character.Root.name = name;

                var root = character.Root.transform;
                root.SetParent(transform, false);
                root.localPosition = GridPosition(i);
                root.localRotation = Quaternion.Euler(0f, 180f, 0f);

                var playable = new AxiePlayable(character);
                _playables.Add(playable);
                PlayAnimation(playable, animation);
            }

            Debug.Log($"[AxieMixer3DSpawner] Spawned {_characters.Count} Axies.");
        }

        void OnDestroy()
        {
            foreach (var playable in _playables) playable?.Dispose();
            _playables.Clear();
            // AxieCharacter3D is IDisposable — dispose to free per-instance materials and the root GameObject.
            foreach (var character in _characters) character?.Dispose();
            _characters.Clear();
        }

        /// <summary>Lays the grid out centered on this GameObject, on the XZ plane.</summary>
        Vector3 GridPosition(int index)
        {
            int row = index / columns;
            int col = index % columns;

            float xOffset = (columns - 1) * 0.5f;
            float zOffset = (rows - 1) * 0.5f;

            return new Vector3((col - xOffset) * spacing, 0f, -(row - zOffset) * spacing);
        }

        static AxieDescriptor BuildDescriptor(AxieBodyType body, AxieClassOption axieClass, AxieVariant variant, byte colorVariant)
        {
            var descriptor = new AxieDescriptor
            {
                body = body,
                colorVariant = colorVariant,
                parts = new(),
            };

            foreach (AxiePartType type in System.Enum.GetValues(typeof(AxiePartType)))
            {
                descriptor.parts.Add(new AxiePartDescriptor
                {
                    @class = AxieMixerExampleOptions.ToClassName(axieClass),
                    variant = AxieMixerExampleOptions.ToVariant(variant),
                    type = type,
                    skin = 0,
                    level = 1,
                });
            }

            return descriptor;
        }

        static void PlayAnimation(AxiePlayable playable, AxieAnimation anim)
        {
            var clipName = AxieMixerExampleOptions.ToClipName(anim);
            if (playable.Play(clipName, loop: true) == null)
                playable.Play(AnimNames.Idle, loop: true);
        }

        void EnsureCameraAndLight()
        {
            if (Camera.main == null)
            {
                // Pull the camera back and up so the whole grid is in frame.
                float span = Mathf.Max(columns, rows) * spacing;
                var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(
                    new Vector3(0f, span * 0.6f + 1.2f, -span - 2f),
                    Quaternion.Euler(20f, 0f, 0f));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.18f, 0.2f, 0.24f, 1f);
            }

            if (FindAnyObjectByType<Light>() == null)
            {
                var lightObject = new GameObject("Directional Light");
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }
    }
}
