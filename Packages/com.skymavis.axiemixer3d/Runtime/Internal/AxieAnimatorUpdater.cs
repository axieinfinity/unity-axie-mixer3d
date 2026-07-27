using UnityEngine;

namespace SkyMavis.AxieMixer3D.Internal
{
    internal sealed class AxieAnimatorUpdater : MonoBehaviour
    {
        internal System.Action OnUpdate;

        void Update() => OnUpdate?.Invoke();

        void OnDestroy() => OnUpdate = null;
    }
}
