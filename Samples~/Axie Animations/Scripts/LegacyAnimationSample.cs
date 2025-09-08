using System.Collections.Generic;
using UnityEngine;

namespace SkyMavis.AxieMixer3D.Samples.AxieAnimations
{
    public class LegacyAnimationSample : MonoBehaviour
    {
        static readonly string[] AxieClasses = new[] {
            "Aquatic",
            "Beast",
            "Bird",
            "Bug",
            "Plant",
            "Reptile",
        };
        static readonly int[] AxieVariants = new[] { 2, 4, 6, 8, 10, 12 };
        static readonly string[] AnimationNames = new[] {
            "Idle",
            "Walk",
            "Run",
            "Stun",
            "Dead",
        };

        public AxieCharacter3DBehaviour liteCharacterBehaviour;
        public AxieCharacter3DBehaviour fullCharacterBehaviour;
        public GameObject[] weaponPrefabs;

        Animation _liteAnimation;
        Animation _fullAnimation;
        string _currentAnimation = "Idle";
        Vector2 _scrollPosition;
        string _axieClass = AxieClasses[0];
        int _axieVariant = AxieVariants[0];
        GameObject _currentWeaponPrefab;
        readonly List<GameObject> _weapons = new();
        readonly Dictionary<string, AnimationClip> _liteClipMap = new();
        readonly Dictionary<string, AnimationClip> _fullClipMap = new();

        void Start()
        {
            RebuildAxie();
        }

        void OnGUI()
        {
            var rect = new Rect(0f, 0f, 200f, Screen.height);
            GUI.Box(rect, default(string));

            using (new GUILayout.AreaScope(rect))
            using (var scrollView = new GUILayout.ScrollViewScope(_scrollPosition))
            {
                _scrollPosition = scrollView.scrollPosition;

                GUILayout.Label("Time Scale");
                Time.timeScale = GUILayout.HorizontalSlider(Time.timeScale, 0f, 1f);

                GUILayout.Label("Animations");

                foreach (var animationName in AnimationNames)
                {
                    if (GUILayout.Button(animationName)) PlayAnimation(animationName);
                }

                GUI.enabled = _weapons.Count > 0;

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Attack")) PlayAnimation("Attack");
                    if (GUILayout.Button("Skill")) PlayAnimation("Skill");
                }

                GUI.enabled = true;

                GUILayout.Label("Axie Class");

                for (var i = 0; i < AxieClasses.Length; i++)
                {
                    if (GUILayout.Button(AxieClasses[i]))
                    {
                        _axieClass = AxieClasses[i];
                        RebuildAxie();
                    }
                }

                GUILayout.Label("Axie Variant");

                for (var i = 0; i < AxieVariants.Length; i++)
                {
                    if (GUILayout.Button(AxieVariants[i].ToString("D2")))
                    {
                        _axieVariant = AxieVariants[i];
                        RebuildAxie();
                    }
                }

                GUILayout.Label("Weapon Selection");

                for (var i = -1; i < weaponPrefabs.Length; i++)
                {
                    var weaponPrefab = i < 0 ? null : weaponPrefabs[i];
                    var weaponName = weaponPrefab?.name?.Split('_')?[1];

                    if (GUILayout.Button(weaponName ?? "None"))
                    {
                        EquipWeapon(weaponPrefab);
                    }
                }
            }
        }

        void PlayAnimation(string animationName)
        {
            _currentAnimation = animationName;
            _liteAnimation.Play(animationName);
            _fullAnimation.Play(animationName);
        }

        void UpdateAnimations(string weaponName)
        {
            ReplaceClip(_liteAnimation, _liteClipMap, "Idle", liteCharacterBehaviour.Character.GetLiteAnimationClip($"{weaponName ?? "Default"}.Idle"));
            ReplaceClip(_liteAnimation, _liteClipMap, "Walk", liteCharacterBehaviour.Character.GetLiteAnimationClip($"{weaponName ?? "Default"}.Walk"));
            ReplaceClip(_liteAnimation, _liteClipMap, "Run", liteCharacterBehaviour.Character.GetLiteAnimationClip($"{weaponName ?? "Default"}.Run"));
            ReplaceClip(_liteAnimation, _liteClipMap, "Stun", liteCharacterBehaviour.Character.GetLiteAnimationClip("Default.Stun"));
            ReplaceClip(_liteAnimation, _liteClipMap, "Dead", liteCharacterBehaviour.Character.GetLiteAnimationClip("Default.Dead"));

            ReplaceClip(_fullAnimation, _fullClipMap, "Idle", fullCharacterBehaviour.Character.GetFullAnimationClip($"{weaponName ?? "Default"}.Idle"));
            ReplaceClip(_fullAnimation, _fullClipMap, "Walk", fullCharacterBehaviour.Character.GetFullAnimationClip($"{weaponName ?? "Default"}.Walk"));
            ReplaceClip(_fullAnimation, _fullClipMap, "Run", fullCharacterBehaviour.Character.GetFullAnimationClip($"{weaponName ?? "Default"}.Run"));
            ReplaceClip(_fullAnimation, _fullClipMap, "Stun", fullCharacterBehaviour.Character.GetFullAnimationClip("Default.Stun"));
            ReplaceClip(_fullAnimation, _fullClipMap, "Dead", fullCharacterBehaviour.Character.GetFullAnimationClip("Default.Dead"));

            if (weaponName != null)
            {
                ReplaceClip(_liteAnimation, _liteClipMap, "Attack", liteCharacterBehaviour.Character.GetLiteAnimationClip($"{weaponName}.Attack"));
                ReplaceClip(_liteAnimation, _liteClipMap, "Skill", liteCharacterBehaviour.Character.GetLiteAnimationClip($"{weaponName}.Skill"));

                ReplaceClip(_fullAnimation, _fullClipMap, "Attack", fullCharacterBehaviour.Character.GetFullAnimationClip($"{weaponName}.Attack"));
                ReplaceClip(_fullAnimation, _fullClipMap, "Skill", fullCharacterBehaviour.Character.GetFullAnimationClip($"{weaponName}.Skill"));
            }
            else if (_currentAnimation == "Attack" || _currentAnimation == "Skill")
            {
                _currentAnimation = "Idle";
            }

            PlayAnimation(_currentAnimation);

            static void ReplaceClip(Animation animation, Dictionary<string, AnimationClip> clipMap, string clipName, AnimationClip clip)
            {
                clip = Instantiate(clip);
                clip.legacy = true;
                animation.AddClip(clip, clipName);

                if (clipMap.TryGetValue(clipName, out var oldClip))
                {
                    Destroy(oldClip);
                }

                clipMap[clipName] = clip;
            }
        }

        void EquipWeapon(GameObject prefab)
        {
            _currentWeaponPrefab = prefab;

            foreach (var weapon in _weapons)
            {
                Destroy(weapon);
            }

            _weapons.Clear();
            var weaponName = prefab?.name?.Split('_')?[1];

            if (weaponName != null)
            {
                CreateWeapon(liteCharacterBehaviour.Character);
                CreateWeapon(fullCharacterBehaviour.Character);
            }

            UpdateAnimations(weaponName);

            void CreateWeapon(AxieCharacter3D character)
            {
                if (weaponName != "Bow")
                {
                    var rightWeapon = Instantiate(prefab, character.RightWeaponAttachPoint);
                    _weapons.Add(rightWeapon);
                }

                if (weaponName == "Bow" || weaponName == "Gauntlet")
                {
                    var leftWeapon = Instantiate(prefab, character.LeftWeaponAttachPoint);
                    _weapons.Add(leftWeapon);

                    if (weaponName == "Gauntlet")
                    {
                        leftWeapon.transform.localScale = new Vector3(1f, 1f, -1f);
                    }
                }
            }
        }

        void RebuildAxie()
        {
            var axieDescriptor = new AxieDescriptor
            {
                body = AxieBodyType.Normal,
                parts = new(),
            };

            foreach (AxiePartType type in System.Enum.GetValues(typeof(AxiePartType)))
            {
                axieDescriptor.parts.Add(
                    new()
                    {
                        @class = _axieClass,
                        variant = _axieVariant,
                        type = type,
                        skin = 0,
                        level = 1,
                    }
                );
            }

            liteCharacterBehaviour.axieDescriptor = axieDescriptor;
            liteCharacterBehaviour.Rebuild();
            _liteAnimation = liteCharacterBehaviour.Character.Root.AddComponent<Animation>();
            _liteAnimation.wrapMode = WrapMode.Loop;

            fullCharacterBehaviour.axieDescriptor = axieDescriptor;
            fullCharacterBehaviour.Rebuild();
            _fullAnimation = fullCharacterBehaviour.Character.Root.AddComponent<Animation>();
            _fullAnimation.wrapMode = WrapMode.Loop;

            EquipWeapon(_currentWeaponPrefab);
        }
    }
}
