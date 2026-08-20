using System.Collections.Generic;
using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    public class AxieCharacter3D : System.IDisposable
    {
        public AxieInstantiationParams InstantiationParams { get; private set; }
        public GameObject Root { get; private set; }
        public Transform RightWeaponAttachPoint { get; private set; }
        public Transform LeftWeaponAttachPoint { get; private set; }

        /// <summary>
        /// The descriptor this character was assembled from (post-coercion). Read-only; to change the
        /// character in place — same handle, same scene transform — call <see cref="ApplyDescriptor"/>
        /// or <see cref="ApplyGenes"/> instead of building a new one.
        /// </summary>
        public AxieDescriptor Descriptor { get; private set; }

        /// <summary>
        /// Part types deliberately left un-outlined by <see cref="SetOutlineLayer"/> so the
        /// Draw-Objects outline matches the reference art (potara), which draws no outline
        /// around the eyes and mouth.
        /// </summary>
        public static readonly IReadOnlyList<AxiePartType> OutlineExcludedPartTypes = new[]
        {
            AxiePartType.Eye,
            AxiePartType.Mouth,
        };

        /// <summary>
        /// Project-wide default Draw-Objects outline layer applied by
        /// <see cref="AxieFactory.CreateCharacter"/> to every character it builds. Set this once
        /// at bootstrap (e.g. via <see cref="AxieMixerInitializer"/>, or directly in the host's
        /// init code) to the GameObject layer your URP <em>Render Objects</em> outline feature
        /// filters on. A value below 0 (the default) disables the default outline. Individual
        /// characters override it any time by calling <see cref="SetOutlineLayer"/>.
        /// </summary>
        public static int DefaultOutlineLayer { get; set; } = -1;

        /// <summary>
        /// Layer the un-outlined parts (eyes/mouth, see <see cref="OutlineExcludedPartTypes"/>)
        /// are placed on when <see cref="DefaultOutlineLayer"/> is applied. Defaults to 0 (Default).
        /// </summary>
        public static int DefaultOutlineBaseLayer { get; set; } = 0;

        /// <summary>
        /// Project-wide default for mesh combining set by <see cref="AxieMixerInitializer"/> at
        /// bootstrap. When <c>true</c> (the default), <see cref="AxieFactory.CreateCharacter"/> merges
        /// the per-part <see cref="SkinnedMeshRenderer"/>s into two combined renderers to cut draw
        /// calls and skinning cost. Override per-call via <see cref="AxieInstantiationParams.combineMeshes"/>.
        /// </summary>
        public static bool DefaultCombineMeshes { get; set; } = true;

        AxieBodyData _bodyData;
        // GameObjects of the parts in OutlineExcludedPartTypes, captured at assembly so
        // SetOutlineLayer can keep them off the outline layer without fragile name matching.
        IReadOnlyList<GameObject> _outlineExcludedParts;
        // The exact per-instance materials AxieFactory.Colorize cloned for this character.
        // Dispose destroys only these — never the shared catalog/addon/prefab materials.
        IReadOnlyList<Material> _ownedMaterials;
        // Runtime meshes AxieMeshCombiner created for this character (empty when combineMeshes is off).
        // Dispose destroys these so combined meshes don't leak.
        IReadOnlyList<Mesh> _ownedMeshes;

        // Last-applied outline layers, tracked so an in-place rebuild (ApplyDescriptor) can re-apply
        // the same outline onto the freshly instantiated hierarchy. -1 = no outline applied.
        int _outlineLayer = -1;
        int _outlineBaseLayer = 0;

        // Lazily created on first Playable access. Owns a controller-less Animator + AxieAnimatorUpdater on Root.
        AxiePlayable _playable;

        /// <summary>
        /// The character's animator, created on first access. This is the official animation entry
        /// point: it owns a controller-less <see cref="Animator"/> component on <see cref="Root"/> and
        /// drives the body clips through a <see cref="UnityEngine.Playables.PlayableGraph"/>, so
        /// animation works in player builds. Disposed with the character; re-created on next access
        /// after an in-place <see cref="ApplyDescriptor"/>.
        /// </summary>
        public AxiePlayable Playable => _playable ??= new AxiePlayable(this);

        public static AxieCharacter3D FromDescriptor(AxieDescriptor axieDescriptor, AxieInstantiationParams instantiationParams = null)
        {
            var factory = AxieFactory.Default;
            if (factory == null)
            {
                Debug.LogError($"{nameof(AxieFactory)}.{nameof(AxieFactory.Default)} has not been assigned. Add an {nameof(AxieMixerInitializer)} to a bootstrap scene/prefab and assign your AxieFactory catalog asset to it.");
                return null;
            }
            return factory.CreateCharacter(axieDescriptor, instantiationParams);
        }

        public static AxieCharacter3D FromGenes(string genes, AxieInstantiationParams instantiationParams = null) => FromDescriptor(AxieDescriptor.FromGenes(genes), instantiationParams);

        internal AxieCharacter3D(in AxieFactory.AxieBuildResult buildResult)
        {
            ApplyBuild(buildResult);
        }

        /// <summary>
        /// Rebuilds this character from <paramref name="descriptor"/> in place: the same
        /// <see cref="AxieCharacter3D"/> handle, scene parent, and transform are preserved while the
        /// underlying <see cref="Root"/> GameObject is re-instantiated. Anything caching
        /// <see cref="Root"/>, the weapon attach points, or the <see cref="Playable"/> must re-read
        /// them afterwards — the animator is disposed and re-created on next access.
        /// </summary>
        public void ApplyDescriptor(AxieDescriptor descriptor)
        {
            var factory = AxieFactory.Default;
            if (factory == null)
            {
                Debug.LogError($"{nameof(AxieFactory)}.{nameof(AxieFactory.Default)} has not been assigned.");
                return;
            }

            var buildResult = factory.Build(descriptor, InstantiationParams);
            if (buildResult.root == null) return; // build failed — keep the current character intact
            ApplyBuild(buildResult);
        }

        /// <summary>Convenience overload of <see cref="ApplyDescriptor"/> that decodes a gene string first.</summary>
        public void ApplyGenes(string genes) => ApplyDescriptor(AxieDescriptor.FromGenes(genes));

        // Shared by the constructor (first build) and ApplyDescriptor (in-place rebuild). On a rebuild
        // it tears down the previous build's owned resources, transplants the new Root into the old
        // one's scene slot, then swaps in the new state and re-applies the outline layer.
        internal void ApplyBuild(in AxieFactory.AxieBuildResult buildResult)
        {
            var isRebuild = Root != null;
            var oldRoot = Root;

            if (isRebuild)
            {
                // Playable owns components on the OLD Root — dispose before that Root is destroyed.
                _playable?.Dispose();
                _playable = null;

                foreach (var material in _ownedMaterials) Object.Destroy(material);
                foreach (var mesh in _ownedMeshes) Object.Destroy(mesh);

                // Move the new Root into the old Root's place so the handle stays put in the scene.
                var oldTransform = oldRoot.transform;
                var newTransform = buildResult.root.transform;
                newTransform.SetParent(oldTransform.parent, false);
                newTransform.SetSiblingIndex(oldTransform.GetSiblingIndex());
                newTransform.localPosition = oldTransform.localPosition;
                newTransform.localRotation = oldTransform.localRotation;
                newTransform.localScale = oldTransform.localScale;
            }

            InstantiationParams = buildResult.mergedParams;
            Root = buildResult.root;
            RightWeaponAttachPoint = buildResult.rightWeaponAttachPoint;
            LeftWeaponAttachPoint = buildResult.leftWeaponAttachPoint;
            _bodyData = buildResult.bodyData;
            _outlineExcludedParts = buildResult.outlineExcludedParts ?? System.Array.Empty<GameObject>();
            _ownedMaterials = buildResult.ownedMaterials ?? System.Array.Empty<Material>();
            _ownedMeshes = buildResult.ownedMeshes ?? System.Array.Empty<Mesh>();
            Descriptor = buildResult.coercedDescriptor;

            if (isRebuild)
            {
                Object.Destroy(oldRoot);
                // Re-apply whatever outline this character was last using onto the new hierarchy.
                if (_outlineLayer >= 0) SetOutlineLayer(_outlineLayer, _outlineBaseLayer);
            }
        }

        public void Dispose()
        {
            if (Root == null) return;

            // Playable first — it destroys the Animator/updater components on Root.
            _playable?.Dispose();
            _playable = null;

            // Destroy only the per-instance materials the factory cloned for us; the
            // renderers otherwise point at shared catalog/addon assets we must not touch.
            foreach (var material in _ownedMaterials)
            {
                Object.Destroy(material);
            }

            foreach (var mesh in _ownedMeshes)
            {
                Object.Destroy(mesh);
            }

            Object.Destroy(Root);
            Root = null;
        }

        /// <summary>
        /// Moves the Axie onto <paramref name="outlineLayer"/> so URP's Draw-Objects outline
        /// (a Render Objects feature filtering on that layer, re-drawing with an inflating
        /// material) outlines it — while keeping the parts in <see cref="OutlineExcludedPartTypes"/>
        /// (eyes, mouth) on <paramref name="baseLayer"/> so they receive no outline, matching the
        /// reference art. The package owns <em>which</em> parts are excluded; the caller only
        /// supplies the project-specific layer indices. Call with equal layers (the default,
        /// <c>SetOutlineLayer(baseLayer)</c>) to remove the outline and return everything to
        /// <paramref name="baseLayer"/>.
        /// </summary>
        /// <param name="outlineLayer">Layer the Draw-Objects outline feature filters on.</param>
        /// <param name="baseLayer">Layer for un-outlined content (defaults to 0 = Default).</param>
        public void SetOutlineLayer(int outlineLayer, int baseLayer = 0)
        {
            if (Root == null) return;

            // Remembered so an in-place ApplyDescriptor can restore the same outline on the new Root.
            _outlineLayer = outlineLayer;
            _outlineBaseLayer = baseLayer;

            SetLayerRecursively(Root, outlineLayer);

            if (outlineLayer == baseLayer) return;
            foreach (var part in _outlineExcludedParts)
            {
                if (part != null) SetLayerRecursively(part, baseLayer);
            }
        }

        static void SetLayerRecursively(GameObject gameObject, int layer)
        {
            gameObject.layer = layer;
            foreach (Transform child in gameObject.transform) SetLayerRecursively(child.gameObject, layer);
        }

        /// <summary>
        /// Returns the animation clip with the given name for this character's body. The name is
        /// matched case-insensitively. Resolves against the body's baked clips first, then falls
        /// back to any clips registered on <see cref="AxieFactory.Default"/> for this body (e.g. the
        /// optional weapon-anim package). Returns null if the name is unknown on both.
        /// </summary>
        public AnimationClip GetAnimClip(string animationName)
        {
            if (_bodyData.Animations != null
                && _bodyData.Animations.TryGetValue(animationName, out var data)
                && data.clip != null)
                return data.clip;

            // Fallback: optional clips registered on the factory for this body. Uses
            // AxieFactory.Default — the common single-catalog setup driven by AxieMixerInitializer.
            return AxieFactory.Default != null
                ? AxieFactory.Default.GetRegisteredAnimClip(Descriptor.body, animationName)
                : null;
        }
    }
}
