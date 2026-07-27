using UnityEngine;

namespace SkyMavis.AxieMixer3D.WeaponAnims
{
    /// <summary>
    /// Optional convenience bootstrap that registers a weapon-anim catalog with the factory on
    /// <c>Awake</c> and unregisters it on <c>OnDestroy</c>. Drop it on the same bootstrap object as
    /// <c>AxieMixerInitializer</c> and assign the catalog.
    /// <para>
    /// The execution order (-9000) runs this <b>after</b> <c>AxieMixerInitializer</c> (-10000) has
    /// assigned <see cref="AxieFactory.Default"/>, so <see cref="AxieWeaponAnims.Register"/> finds a
    /// valid factory. For code-only bootstraps, call <c>AxieWeaponAnims.Register(catalog)</c> yourself
    /// after setting <c>AxieFactory.Default</c>.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-9000)]
    [AddComponentMenu("Axie Mixer 3D/Axie Weapon Anim Initializer")]
    public class AxieWeaponAnimInitializer : MonoBehaviour
    {
        [SerializeField]
        AxieWeaponAnimCatalog _catalog;

        public AxieWeaponAnimCatalog Catalog => _catalog;

        void Awake()
        {
            if (_catalog == null)
            {
                Debug.LogError($"{nameof(AxieWeaponAnimInitializer)} on '{name}' has no catalog assigned.", this);
                return;
            }
            AxieWeaponAnims.Register(_catalog);
        }

        void OnDestroy()
        {
            if (_catalog != null) AxieWeaponAnims.Unregister(_catalog);
        }
    }
}
