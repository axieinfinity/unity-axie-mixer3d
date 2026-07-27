using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    public class AxieCharacter3DBehaviour : MonoBehaviour
    {
        public string axieGenes;
        public AxieDescriptor axieDescriptor;

        public AxieCharacter3D Character { get; private set; }

        /// <summary>The underlying character's animator (created on first access). See <see cref="AxieCharacter3D.Playable"/>.</summary>
        public AxiePlayable Playable => Character?.Playable;

        void Start()
        {
            if (Character == null) Rebuild();
        }

        void OnDestroy()
        {
            Cleanup();
        }

        [System.Obsolete("Refresh() is obsolete. Use Rebuild() instead.")]
        public void Refresh()
        {
            Rebuild();
        }

        public void Rebuild()
        {
            if (!string.IsNullOrWhiteSpace(axieGenes)) axieDescriptor = AxieDescriptor.FromGenes(axieGenes);

            // Swap in place when we already have a character so the transform/parent (and this
            // GameObject's children) are preserved; otherwise build a fresh one and parent it.
            if (Character != null)
            {
                Character.ApplyDescriptor(axieDescriptor);
                return;
            }

            Character = AxieCharacter3D.FromDescriptor(axieDescriptor);
            if (Character == null) return;
            Character.Root.transform.SetParent(transform, false);
        }

        void Cleanup()
        {
            // Character.Dispose() also disposes the lazily-created Animator.
            Character?.Dispose();
            Character = null;
        }
    }
}
