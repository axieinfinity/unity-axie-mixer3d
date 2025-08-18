using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    public class AxieCharacter3DBehaviour : MonoBehaviour
    {
        public string axieGenes;
        public AxieDescriptor axieDescriptor;

        public AxieCharacter3D Character { get; private set; }

        void Start()
        {
            if (Character == null) Rebuild();
        }

        void OnDestroy()
        {
            Character?.Dispose();
            Character = null;
        }

        [System.Obsolete("Refresh() is obsolete. Use Rebuild() instead.")]
        public void Refresh()
        {
            Rebuild();
        }

        public void Rebuild()
        {
            Character?.Dispose();

            if (!string.IsNullOrWhiteSpace(axieGenes)) axieDescriptor = AxieDescriptor.FromGenes(axieGenes);

            Character = AxieFactory.Default.CreateCharacter(axieDescriptor);
            Character.Root.transform.SetParent(transform, false);
        }
    }
}
