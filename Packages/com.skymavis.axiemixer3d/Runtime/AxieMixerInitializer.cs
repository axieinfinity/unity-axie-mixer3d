using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    /// <summary>
    /// Bootstrap component that assigns an <see cref="AxieFactory"/> catalog asset to
    /// <see cref="AxieFactory.Default"/> so that <see cref="AxieCharacter3D.FromGenes"/> and
    /// <see cref="AxieCharacter3D.FromDescriptor"/> can resolve it.
    /// <para>
    /// Drop this component on a long-lived bootstrap GameObject (e.g. a prefab loaded at
    /// app startup) and assign the catalog asset in the Inspector. The
    /// <c>DefaultExecutionOrder</c> ensures registration happens before any other
    /// MonoBehaviour calls into the mixer in <c>Awake</c>/<c>Start</c>.
    /// </para>
    /// <para>
    /// Because the catalog is referenced as a serialized field (rather than via
    /// <c>Resources.Load</c>), Unity only includes it in builds that actually reference
    /// this component. The heavy body/part/addon assets transitively referenced by the
    /// catalog are likewise included only when needed.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [AddComponentMenu("Axie Mixer 3D/Axie Mixer Initializer")]
    public class AxieMixerInitializer : MonoBehaviour
    {
        /// <summary>How every character is outlined by default (overridable per-object via
        /// <see cref="AxieCharacter3D.SetOutlineLayer"/>).</summary>
        public enum DefaultOutlineMode
        {
            /// <summary>No outline unless a caller opts in per-object.</summary>
            None,
            /// <summary>Move each new character onto <see cref="_outlineLayer"/> so a URP
            /// Render Objects feature filtering that layer draws the Draw-Objects outline.</summary>
            DrawObjects,
            /// <summary>Outline handled screen-space by <see cref="OutlinePostProcessRendererFeature"/>;
            /// no per-character layer work. Add the feature to your URP Renderer asset.</summary>
            PostProcess,
        }

        [SerializeField]
        AxieFactory _catalog;

        [Tooltip("If true, this instance survives scene loads via DontDestroyOnLoad.")]
        [SerializeField]
        bool _persistAcrossScenes = true;

        [Header("Default Outline")]
        [Tooltip("Outline applied to every character on creation. DrawObjects moves each character onto an outline layer (cost scales with Axie count). PostProcess uses a screen-space pass with fixed cost regardless of Axie count — prefer it for crowds. Individual characters override via AxieCharacter3D.SetOutlineLayer.")]
        [SerializeField]
        DefaultOutlineMode _defaultOutlineMode = DefaultOutlineMode.DrawObjects;

        [Tooltip("GameObject layer the URP Render Objects outline feature filters on. Only used when Default Outline Mode is Draw Objects.")]
        [SerializeField, LayerField]
        int _outlineLayer;

        [Tooltip("Layer for un-outlined parts (eyes/mouth). Usually Default (0).")]
        [SerializeField, LayerField]
        int _baseOutlineLayer;

        [Header("Mesh Combining")]
        [Tooltip("Merge the ~16 per-part SkinnedMeshRenderers into two combined renderers at character creation, cutting draw calls and skinning cost. Disable only for debugging or if your project needs the original per-part hierarchy.")]
        [SerializeField]
        bool _combineMeshes = true;

        public AxieFactory Catalog => _catalog;

        void Awake()
        {
            if (_catalog == null)
            {
                Debug.LogError($"{nameof(AxieMixerInitializer)} on '{name}' has no catalog assigned.", this);
                return;
            }

            AxieFactory.Default = _catalog;

            AxieCharacter3D.DefaultOutlineLayer = _defaultOutlineMode == DefaultOutlineMode.DrawObjects ? _outlineLayer : -1;
            AxieCharacter3D.DefaultOutlineBaseLayer = _baseOutlineLayer;
            AxieCharacter3D.DefaultCombineMeshes = _combineMeshes;

            if (_persistAcrossScenes && transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        void OnDestroy()
        {
            if (AxieFactory.Default == _catalog)
            {
                AxieFactory.Default = null;
                AxieCharacter3D.DefaultOutlineLayer = -1;
                AxieCharacter3D.DefaultCombineMeshes = true;
            }
        }
    }
}
