using System.Collections.Generic;
using UnityEngine;

namespace SkyMavis.AxieMixer3D.WeaponAnims
{
    /// <summary>A single named weapon/action clip for a body. Name is bare (no "Action." prefix).</summary>
    [System.Serializable]
    public class AxieWeaponAnimEntry
    {
        public string name;
        public AnimationClip clip;
    }

    /// <summary>The weapon/action clips available for one body type.</summary>
    [System.Serializable]
    public class AxieWeaponAnimBody
    {
        public AxieBodyType body;
        public List<AxieWeaponAnimEntry> animations = new();
    }

    /// <summary>
    /// Serialized catalog of optional weapon/action animation clips, grouped by body. Baked by the
    /// editor tool <c>Tools → Axie Mixer 3D → Update Weapon Anim Catalog</c>. Because this asset
    /// holds direct references to the clips, adding this package and referencing the catalog (e.g.
    /// via <see cref="AxieWeaponAnimInitializer"/>) is exactly the opt-in that pulls the weapon
    /// clips into a build. Register it with the factory through <see cref="AxieWeaponAnims.Register"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "AxieWeaponAnimCatalog", menuName = "Axie Mixer 3D/Weapon Anim Catalog")]
    public class AxieWeaponAnimCatalog : ScriptableObject
    {
        public List<AxieWeaponAnimBody> bodies = new();
    }
}
