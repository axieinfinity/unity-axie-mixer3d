using System.Collections.Generic;
using SkyMavis.AxieMixer3D;

namespace SkyMavis.AxieMixer3D.Example
{
    /// <summary>The Axie classes that actually ship part assets (Mech/Dawn/Dusk have no meshes yet).</summary>
    public enum AxieClassOption
    {
        Beast,
        Bug,
        Bird,
        Plant,
        Aquatic,
        Reptile,
    }

    /// <summary>Valid part variants for the shipped S00 assets. The numeric value is the variant number.</summary>
    public enum AxieVariant
    {
        V02 = 2,
        V04 = 4,
        V06 = 6,
        V08 = 8,
        V10 = 10,
        V12 = 12,
    }

    /// <summary>
    /// Body animations available on every body. The enum name maps to the
    /// clip name via <c>"Default." + name</c> (e.g. <see cref="Idle"/> → <c>Default.Idle</c>).
    /// </summary>
    public enum AxieAnimation
    {
        Idle,
        Run,
        Walk,
        RunAttack,
        WalkAttack,
        IdleGetHit,
        IdleCarryItem,
        RunCarryItem,
        WalkCarryItem,
        Dead,
        Stun,
    }

    /// <summary>
    /// Shared lookup tables so the example and the spawner agree on what a "valid" Axie is.
    /// Mirrors the class/variant/color tables the mixer decodes from genes.
    /// </summary>
    public static class AxieMixerExampleOptions
    {
        public static string ToClassName(AxieClassOption option) => option.ToString();

        public static int ToVariant(AxieVariant variant) => (int)variant;

        public static string ToClipName(AxieAnimation animation) => animation.ToString();

        public static readonly AxieClassOption[] AllClasses =
            (AxieClassOption[])System.Enum.GetValues(typeof(AxieClassOption));

        public static readonly AxieVariant[] AllVariants =
            (AxieVariant[])System.Enum.GetValues(typeof(AxieVariant));

        public static readonly AxieBodyType[] AllBodies =
            (AxieBodyType[])System.Enum.GetValues(typeof(AxieBodyType));

        // Valid color-variant indices per class (from AxieDescriptor.GetColorVariant).
        static readonly Dictionary<AxieClassOption, byte[]> ColorVariants = new()
        {
            [AxieClassOption.Beast] = new byte[] { 0, 1, 2, 3, 4, 5 },
            [AxieClassOption.Bug] = new byte[] { 17, 18, 19, 20, 21 },
            [AxieClassOption.Bird] = new byte[] { 22, 23, 24, 25, 26 },
            [AxieClassOption.Plant] = new byte[] { 6, 7, 8, 9, 10 },
            [AxieClassOption.Aquatic] = new byte[] { 11, 12, 13, 14, 15, 16 },
            [AxieClassOption.Reptile] = new byte[] { 27, 28, 29, 30, 31, 32 },
        };

        /// <summary>The color-variant indices that colorize correctly for the given class.</summary>
        public static IReadOnlyList<byte> ColorVariantsFor(AxieClassOption option) => ColorVariants[option];
    }
}
