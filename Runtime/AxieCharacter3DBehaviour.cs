using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    public class AxieCharacter3DBehaviour : MonoBehaviour
    {
        public string axieGenes;
        public AxieDescriptor axieDescriptor;

        AxieCharacter3D _character;

        void OnEnable()
        {
            Refresh();
        }

        void OnDisable()
        {
            _character?.Dispose();
            _character = null;
        }

        public void Refresh()
        {
            _character?.Dispose();

            if (!string.IsNullOrWhiteSpace(axieGenes)) axieDescriptor = AxieDescriptor.FromGenes(axieGenes);

            _character = AxieFactory.Default.CreateCharacter(axieDescriptor);
            _character.Root.transform.SetParent(transform, false);
        }
    }
}
