using System.Collections.Generic;

namespace SkyMavis.AxieMixer3D
{
    [System.Serializable]
    public struct AxieDescriptor
    {
        static readonly AxiePartType[] GenesAxiePartTypes = new AxiePartType[]
        {
        AxiePartType.Eye, AxiePartType.Mouth, AxiePartType.Ear, AxiePartType.Horn, AxiePartType.Back, AxiePartType.Tail
        };

        public int colorVariant;
        public AxieBodyType body;
        public List<AxiePartDescriptor> parts;

        public static AxieDescriptor FromGenes(string genes)
        {
            var desc = new AxieDescriptor() { parts = new(GenesAxiePartTypes.Length) };
            var genesBitIndex = 0;
            System.Span<byte> genesBytes = stackalloc byte[512 / 8];
            ParseGenes(ref genesBytes);

            var mainClass = PopGenesBits(ref genesBytes, 5);
            var reservation = PopGenesBits(ref genesBytes, 45);
            var contribution = PopGenesBits(ref genesBytes, 5);

            var bodySkinInheritability = PopGenesBits(ref genesBytes, 1);
            var bodySkin = PopGenesBits(ref genesBytes, 9);
            var bodyDetail0 = PopGenesBits(ref genesBytes, 9);
            var bodyDetail1 = PopGenesBits(ref genesBytes, 9);
            var bodyDetail2 = PopGenesBits(ref genesBytes, 9);

            var primaryColor0 = PopGenesBits(ref genesBytes, 6);
            var primaryColor1 = PopGenesBits(ref genesBytes, 6);
            var primaryColor2 = PopGenesBits(ref genesBytes, 6);

            var secondaryColor0 = PopGenesBits(ref genesBytes, 6);
            var secondaryColor1 = PopGenesBits(ref genesBytes, 6);
            var secondaryColor2 = PopGenesBits(ref genesBytes, 6);

            if (bodySkin == 1)
            {
                desc.colorVariant = 48;
                // desc.body = "Frosty";
                desc.body = AxieBodyType.Normal;
            }
            else
            {
                desc.colorVariant = GetColorVariant(GetAxieClass(mainClass), primaryColor0);
                desc.body = bodyDetail0 switch
                {
                    // 1 => "Spiky",
                    2 => AxieBodyType.Fuzzy,
                    // 3 => "Curly",
                    // 256 => "Sumo",
                    // 257 => "Wetdog",
                    // 384 => "Bigyak",
                    _ => AxieBodyType.Normal,
                };
            }

            for (var partIndex = 0; partIndex < 6; partIndex++)
            {
                var partStage = PopGenesBits(ref genesBytes, 2);
                var partReservation = PopGenesBits(ref genesBytes, 13);
                var partSkinInheritability = PopGenesBits(ref genesBytes, 1);
                var partSkin = PopGenesBits(ref genesBytes, 9);

                var partClass0 = PopGenesBits(ref genesBytes, 5);
                var partValue0 = PopGenesBits(ref genesBytes, 8);

                var partClass1 = PopGenesBits(ref genesBytes, 5);
                var partValue1 = PopGenesBits(ref genesBytes, 8);

                var partClass2 = PopGenesBits(ref genesBytes, 5);
                var partValue2 = PopGenesBits(ref genesBytes, 8);

                var partType = GenesAxiePartTypes[partIndex];
                var partClass = GetAxieClass(partClass0);

                desc.parts.Add(new()
                {
                    type = partType,
                    skin = partSkin == 1 && partValue0 == 2 ? 1 : 0,
                    @class = partClass,
                    variant = partValue0,
                    level = 1,
                });
            }

            return desc;

            void ParseGenes(ref System.Span<byte> genesBytes)
            {
                for (var i = 0; i < genes.Length; i++)
                {
                    var ch = genes[^(i + 1)];
                    var d = ch switch
                    {
                        >= '0' and <= '9' => ch - '0',
                        >= 'a' and <= 'f' => ch - 'a' + 10,
                        >= 'A' and <= 'F' => ch - 'A' + 10,
                        _ => -1,
                    };

                    if (d < 0) break;

                    genesBytes[i / 2] += (byte)(d << (4 * (i % 2)));
                }
            }

            int PopGenesBits(ref System.Span<byte> genesBytes, int bitCount)
            {
                var value = 0;
                var byteOffset = System.Math.DivRem(genesBitIndex, 8, out var bitOffset);
                var byteCount = (bitOffset + bitCount + 7) / 8;
                genesBitIndex += bitCount;

                for (var byteIndex = 0; byteIndex < byteCount && byteOffset + byteIndex < genesBytes.Length; byteIndex++)
                {
                    value |= genesBytes[^(byteOffset + byteIndex + 1)] << (8 * (byteCount - byteIndex - 1));
                }

                return (value >> (8 * byteCount - (bitOffset + bitCount))) & ~(~0 << bitCount);
            }

            static string GetAxieClass(int classNumber) => classNumber switch
            {
                0 => "Beast",
                1 => "Bug",
                2 => "Bird",
                3 => "Plant",
                4 => "Aquatic",
                5 => "Reptile",
                16 => "Mech",
                17 => "Dawn",
                18 => "Dusk",
                _ => null,
            };

            static byte GetColorVariant(string bodyClass, int primaryColor0) => (bodyClass, primaryColor0) switch
            {
                ("Beast", 0) => 0,
                ("Beast", 1) => 1,
                ("Beast", 2) => 2,
                ("Beast", 3) => 3,
                ("Beast", 4) => 4,
                ("Beast", 6) => 5,
                ("Bug", 0) => 17,
                ("Bug", 1) => 18,
                ("Bug", 2) => 19,
                ("Bug", 3) => 20,
                ("Bug", 4) => 21,
                ("Bird", 0) => 22,
                ("Bird", 1) => 23,
                ("Bird", 2) => 24,
                ("Bird", 3) => 25,
                ("Bird", 4) => 26,
                ("Plant", 0) => 6,
                ("Plant", 1) => 7,
                ("Plant", 2) => 8,
                ("Plant", 3) => 9,
                ("Plant", 4) => 10,
                ("Aquatic", 0) => 11,
                ("Aquatic", 1) => 12,
                ("Aquatic", 2) => 13,
                ("Aquatic", 3) => 14,
                ("Aquatic", 4) => 15,
                ("Aquatic", 6) => 16,
                ("Reptile", 0) => 27,
                ("Reptile", 1) => 28,
                ("Reptile", 2) => 29,
                ("Reptile", 3) => 30,
                ("Reptile", 4) => 31,
                ("Reptile", 6) => 32,
                ("Mech", 0) => 43,
                ("Mech", 1) => 44,
                ("Mech", 2) => 45,
                ("Mech", 3) => 46,
                ("Mech", 4) => 47,
                ("Dawn", 0) => 33,
                ("Dawn", 1) => 34,
                ("Dawn", 2) => 35,
                ("Dawn", 3) => 36,
                ("Dawn", 4) => 37,
                ("Dusk", 0) => 38,
                ("Dusk", 1) => 39,
                ("Dusk", 2) => 40,
                ("Dusk", 3) => 41,
                ("Dusk", 4) => 42,
                _ => 0,
            };
        }
    }
}
