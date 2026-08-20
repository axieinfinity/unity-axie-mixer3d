using System.Collections.Generic;
using UnityEngine;

namespace SkyMavis.AxieMixer3D.WeaponAnims
{
    /// <summary>
    /// Entry point for enabling weapon/action animations. A consumer that has installed this package
    /// <b>must call <see cref="Register"/></b> (directly, or via <see cref="AxieWeaponAnimInitializer"/>)
    /// after <see cref="AxieFactory.Default"/> is assigned. Once registered, weapon clips resolve
    /// transparently through <c>AxieCharacter3D.GetAnimClip</c> — e.g. <c>character.Playable.Play("AxeAttack")</c>
    /// works for every character of that body with no per-instance wiring.
    /// </summary>
    public static class AxieWeaponAnims
    {
        /// <summary>
        /// Register every clip in <paramref name="catalog"/> with <paramref name="factory"/>
        /// (defaults to <see cref="AxieFactory.Default"/>). Idempotent — re-registering replaces
        /// clips of the same name.
        /// </summary>
        public static void Register(AxieWeaponAnimCatalog catalog, AxieFactory factory = null)
        {
            factory ??= AxieFactory.Default;
            if (catalog == null)
            {
                Debug.LogWarning("[AxieWeaponAnims] Register called with a null catalog.");
                return;
            }
            if (factory == null)
            {
                Debug.LogError("[AxieWeaponAnims] No AxieFactory to register with. Assign AxieFactory.Default " +
                               "(e.g. via AxieMixerInitializer) before registering weapon animations.");
                return;
            }

            foreach (var body in catalog.bodies)
            {
                if (body?.animations == null) continue;
                var clips = new List<AxieNamedClip>(body.animations.Count);
                foreach (var entry in body.animations)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.name) || entry.clip == null) continue;
                    clips.Add(new AxieNamedClip { name = entry.name, clip = entry.clip });
                }
                factory.RegisterAnimations(body.body, clips);
            }
        }

        /// <summary>Remove every clip in <paramref name="catalog"/> from the factory registry.</summary>
        public static void Unregister(AxieWeaponAnimCatalog catalog, AxieFactory factory = null)
        {
            factory ??= AxieFactory.Default;
            if (catalog == null || factory == null) return;

            foreach (var body in catalog.bodies)
            {
                if (body?.animations == null) continue;
                foreach (var entry in body.animations)
                    if (entry != null && !string.IsNullOrEmpty(entry.name))
                        factory.UnregisterAnimation(body.body, entry.name);
            }
        }
    }
}
