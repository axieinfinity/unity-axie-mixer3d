namespace SkyMavis.AxieMixer3D.Editor
{
    /// <summary>
    /// Shared clip-name normalization used by both bake tools (the main <see cref="AxieDataUpdater"/>
    /// and the weapon-anim package's updater). Both must produce byte-identical bare keys so the
    /// runtime lookup in <c>AxieCharacter3D.GetAnimClip</c> resolves clips from either source.
    /// </summary>
    internal static class AxieAnimNaming
    {
        /// <summary>
        /// Strips the "Default." / "Action." prefix and fixes the single-n "Canon" bake spelling to
        /// "Cannon". Idempotent; leaves already-bare names untouched.
        /// </summary>
        public static string NormalizeKey(string name)
        {
            var bare =
                name.StartsWith("Default.", System.StringComparison.OrdinalIgnoreCase) ? name.Substring(8) :
                name.StartsWith("Action.",  System.StringComparison.OrdinalIgnoreCase) ? name.Substring(7) :
                name;

            if (bare.StartsWith("Canon", System.StringComparison.Ordinal) &&
                !bare.StartsWith("Cannon", System.StringComparison.Ordinal))
                bare = "Cannon" + bare.Substring("Canon".Length);

            return bare;
        }
    }
}
