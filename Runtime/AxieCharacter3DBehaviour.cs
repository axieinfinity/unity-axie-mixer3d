using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    public class AxieCharacter3DBehaviour : MonoBehaviour
    {
        public string axieGenes;
        public AxieDescriptor axieDescriptor;

        public AxieCharacter3D Character { get; private set; }

        void OnEnable()
        {
            Refresh();
        }

        void OnDisable()
        {
            Character?.Dispose();
            Character = null;
        }

        public void Refresh()
        {
            Character?.Dispose();

            if (!string.IsNullOrWhiteSpace(axieGenes)) axieDescriptor = AxieDescriptor.FromGenes(axieGenes);

            Character = AxieFactory.Default.CreateCharacter(axieDescriptor);
            Character.Root.transform.SetParent(transform, false);
        }
    }
}
