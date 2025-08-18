using System.Collections.Generic;
using SkyMavis.AxieMixer3D;
using UnityEngine;

public class AxieCollection : MonoBehaviour
{
    static readonly Dictionary<string, byte> ClassColorMap = new()
    {
        { "Aquatic", 14 },
        { "Beast", 3 },
        { "Bird", 25 },
        { "Bug", 20 },
        { "Plant", 9 },
        { "Reptile", 30 },
    };

    public AxieBodyType body = AxieBodyType.Normal;
    public string[] classes = new[] {
        "Aquatic",
        "Beast",
        "Bird",
        "Bug",
        "Plant",
        "Reptile",
    };
    public int[] variants = new[] { 2, 4, 6, 8, 10, 12 };
    public int skin;
    public int level = 1;

    void Start()
    {
        var col = 0;

        foreach (var @class in classes)
        {
            var row = 0;
            var colorVariant = ClassColorMap.GetValueOrDefault(@class);

            foreach (var variant in variants)
            {
                var axieDescriptor = new AxieDescriptor
                {
                    body = body,
                    colorVariant = colorVariant,
                    parts = new(),
                };

                foreach (AxiePartType type in System.Enum.GetValues(typeof(AxiePartType)))
                {
                    axieDescriptor.parts.Add(
                        new()
                        {
                            @class = @class,
                            variant = variant,
                            type = type,
                            skin = skin,
                            level = level,
                        }
                    );
                }

                var character = AxieCharacter3D.FromDescriptor(axieDescriptor);
                character.Root.name = $"{body}-{@class}-{variant:00}";
                character.Root.transform.SetPositionAndRotation(
                    new(2f * col, 0f, 2f * row),
                    Quaternion.Euler(0f, 180f, 0f)
                );

                var idleClip = Instantiate(character.GetLiteAnimationClip("Default.Idle"));
                idleClip.legacy = true;
                idleClip.wrapMode = WrapMode.Loop;

                var animation = character.Root.AddComponent<Animation>();
                animation.AddClip(idleClip, "Default.Idle");
                animation.Play("Default.Idle");

                row++;
            }

            col++;
        }
    }
}
