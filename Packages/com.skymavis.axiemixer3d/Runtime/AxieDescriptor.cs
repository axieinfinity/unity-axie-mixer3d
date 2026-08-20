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
                desc.body = AxieBodyType.Frosty;
            }
            else
            {
                desc.colorVariant = GetColorVariant(GetAxieClass(mainClass), primaryColor0);
                desc.body = bodyDetail0 switch
                {
                    1 => AxieBodyType.Spiky,
                    2 => AxieBodyType.Fuzzy,
                    3 => AxieBodyType.Curly,
                    256 => AxieBodyType.Sumo,
                    257 => AxieBodyType.Wetdog,
                    384 => AxieBodyType.Bigyak,
                    _ => AxieBodyType.Normal,
                };
            }

            for (var partIndex = 0; partIndex < 6; partIndex++)
            {
                // Per-part layout (part-relative bits): reservation [0,12), stage [12,15),
                // skinInheritability [15,16), skin [16,25), then 3x (class 5 + value 8).
                // Stage is the 3-bit field immediately before the skin-inheritability bit —
                // NOT the first 2 bits of the group. See fantaxies AxieGenesBitMap (partStageOffset = 12/3).
                var partReservation = PopGenesBits(ref genesBytes, 12);
                var partStage = PopGenesBits(ref genesBytes, 3);
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
                    skin = partSkin,
                    @class = partClass,
                    variant = partValue0,
                    level = partStage + 1,
                });
            }

            return desc;

            void ParseGenes(ref System.Span<byte> genesBytes)
            {
                if (string.IsNullOrEmpty(genes)) return;
                var hex = (genes.StartsWith("0x") || genes.StartsWith("0X")) ? genes[2..] : genes;

                for (var i = 0; i < hex.Length && i / 2 < genesBytes.Length; i++)
                {
                    var ch = hex[^(i + 1)];
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

        /// <summary>
        /// Encodes this descriptor back into a 512-bit hex gene string — the exact inverse of
        /// <see cref="FromGenes"/> for the fields the descriptor carries (body, colorVariant, and
        /// per-part class/variant/skin/level). Recessive part genes (R1/R2) are written equal to the
        /// dominant gene and unused fields (reservation/contribution/secondary colors) are zeroed, so
        /// <c>FromGenes(desc.ToGenes())</c> round-trips to an equivalent descriptor. Result is
        /// "0x"-prefixed, 128 hex chars.
        /// </summary>
        public string ToGenes()
        {
            var genesBitIndex = 0;
            System.Span<byte> genesBytes = stackalloc byte[512 / 8];

            // Frosty is signalled by bodySkin==1 (which also forces colorVariant 48 on decode).
            var frosty = body == AxieBodyType.Frosty || colorVariant == 48;

            var mainClassNum = 0;
            var primaryColor0 = 0;
            if (!frosty)
                (mainClassNum, primaryColor0) = ColorVariantToClassColor(colorVariant);

            var bodySkin = frosty ? 1 : 0;
            var bodyDetail0 = frosty ? 0 : BodyToDetail(body);

            PushGenesBits(ref genesBytes, mainClassNum, 5);
            PushGenesBits(ref genesBytes, 0, 45);            // reservation
            PushGenesBits(ref genesBytes, 0, 5);             // contribution
            PushGenesBits(ref genesBytes, 0, 1);             // bodySkinInheritability
            PushGenesBits(ref genesBytes, bodySkin, 9);
            PushGenesBits(ref genesBytes, bodyDetail0, 9);
            PushGenesBits(ref genesBytes, 0, 9);             // bodyDetail1
            PushGenesBits(ref genesBytes, 0, 9);             // bodyDetail2
            PushGenesBits(ref genesBytes, primaryColor0, 6);
            PushGenesBits(ref genesBytes, primaryColor0, 6);
            PushGenesBits(ref genesBytes, primaryColor0, 6);
            PushGenesBits(ref genesBytes, 0, 6);             // secondaryColor0
            PushGenesBits(ref genesBytes, 0, 6);
            PushGenesBits(ref genesBytes, 0, 6);

            for (var partIndex = 0; partIndex < GenesAxiePartTypes.Length; partIndex++)
            {
                var type = GenesAxiePartTypes[partIndex];

                var stage = 0;
                var skin = 0;
                var classNum = 0;
                var value = 0;
                if (parts != null)
                {
                    foreach (var p in parts)
                    {
                        if (p.type != type) continue;
                        stage = Clamp(p.level - 1, 0, 7);
                        skin = p.skin & 0x1FF;
                        classNum = ClassToNumber(p.@class);
                        value = p.variant & 0xFF;
                        break;
                    }
                }

                PushGenesBits(ref genesBytes, 0, 12);        // partReservation
                PushGenesBits(ref genesBytes, stage, 3);
                PushGenesBits(ref genesBytes, 0, 1);         // partSkinInheritability
                PushGenesBits(ref genesBytes, skin, 9);
                PushGenesBits(ref genesBytes, classNum, 5);  // dominant class
                PushGenesBits(ref genesBytes, value, 8);     // dominant value
                PushGenesBits(ref genesBytes, classNum, 5);  // recessive1 (= dominant)
                PushGenesBits(ref genesBytes, value, 8);
                PushGenesBits(ref genesBytes, classNum, 5);  // recessive2 (= dominant)
                PushGenesBits(ref genesBytes, value, 8);
            }

            // Serialize least-significant-nibble-last, the inverse of ParseGenes.
            const string hexDigits = "0123456789abcdef";
            var chars = new char[128];
            for (var i = 0; i < 128; i++)
            {
                var nibble = (genesBytes[i / 2] >> (4 * (i % 2))) & 0xF;
                chars[128 - 1 - i] = hexDigits[nibble];
            }
            return "0x" + new string(chars);

            void PushGenesBits(ref System.Span<byte> genesBytes, int value, int bitCount)
            {
                var byteOffset = System.Math.DivRem(genesBitIndex, 8, out var bitOffset);
                var byteCount = (bitOffset + bitCount + 7) / 8;
                genesBitIndex += bitCount;

                var masked = value & ~(~0 << bitCount);
                long shifted = (long)masked << (8 * byteCount - (bitOffset + bitCount));

                for (var byteIndex = 0; byteIndex < byteCount && byteOffset + byteIndex < genesBytes.Length; byteIndex++)
                {
                    genesBytes[^(byteOffset + byteIndex + 1)] |= (byte)((shifted >> (8 * (byteCount - byteIndex - 1))) & 0xFF);
                }
            }

            static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

            static int ClassToNumber(string axieClass) => axieClass switch
            {
                "Beast" => 0,
                "Bug" => 1,
                "Bird" => 2,
                "Plant" => 3,
                "Aquatic" => 4,
                "Reptile" => 5,
                "Mech" => 16,
                "Dawn" => 17,
                "Dusk" => 18,
                _ => 0,
            };

            static int BodyToDetail(AxieBodyType body) => body switch
            {
                AxieBodyType.Spiky => 1,
                AxieBodyType.Fuzzy => 2,
                AxieBodyType.Curly => 3,
                AxieBodyType.Sumo => 256,
                AxieBodyType.Wetdog => 257,
                AxieBodyType.Bigyak => 384,
                _ => 0,
            };

            // Inverse of the decoder's GetColorVariant lookup: colorVariant -> (mainClass number, primaryColor0).
            static (int, int) ColorVariantToClassColor(int colorVariant) => colorVariant switch
            {
                0 => (0, 0), 1 => (0, 1), 2 => (0, 2), 3 => (0, 3), 4 => (0, 4), 5 => (0, 6),      // Beast
                6 => (3, 0), 7 => (3, 1), 8 => (3, 2), 9 => (3, 3), 10 => (3, 4),                  // Plant
                11 => (4, 0), 12 => (4, 1), 13 => (4, 2), 14 => (4, 3), 15 => (4, 4), 16 => (4, 6),// Aquatic
                17 => (1, 0), 18 => (1, 1), 19 => (1, 2), 20 => (1, 3), 21 => (1, 4),              // Bug
                22 => (2, 0), 23 => (2, 1), 24 => (2, 2), 25 => (2, 3), 26 => (2, 4),              // Bird
                27 => (5, 0), 28 => (5, 1), 29 => (5, 2), 30 => (5, 3), 31 => (5, 4), 32 => (5, 6),// Reptile
                33 => (17, 0), 34 => (17, 1), 35 => (17, 2), 36 => (17, 3), 37 => (17, 4),         // Dawn
                38 => (18, 0), 39 => (18, 1), 40 => (18, 2), 41 => (18, 3), 42 => (18, 4),         // Dusk
                43 => (16, 0), 44 => (16, 1), 45 => (16, 2), 46 => (16, 3), 47 => (16, 4),         // Mech
                _ => (0, 0),
            };
        }
    }
}
