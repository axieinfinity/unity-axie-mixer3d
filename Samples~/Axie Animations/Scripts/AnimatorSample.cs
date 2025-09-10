using System.Collections.Generic;
using UnityEngine;

namespace SkyMavis.AxieMixer3D.Samples
{
    public class AnimatorSample : MonoBehaviour
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

        public RuntimeAnimatorController animatorController;
        public AxieCharacter3DBehaviour liteCharacterBehaviour;
        public AxieCharacter3DBehaviour fullCharacterBehaviour;
        public GameObject[] weaponPrefabs;

        Animator _liteAnimator;
        Animator _fullAnimator;
        AnimatorOverrideController _liteAnimatorController;
        AnimatorOverrideController _fullAnimatorController;
        Vector2 _scrollPosition;
        float _moveSpeed;
        bool _stunned;
        string _axieClass = AxieClasses[0];
        int _axieVariant = AxieVariants[0];
        GameObject _currentWeaponPrefab;
        readonly List<GameObject> _weapons = new();

        void Start()
        {
            _liteAnimatorController = new AnimatorOverrideController(animatorController);
            _fullAnimatorController = new AnimatorOverrideController(animatorController);
            RebuildAxie();
        }

        void Update()
        {
            _liteAnimator.SetFloat("Move Speed", _moveSpeed);
            _fullAnimator.SetFloat("Move Speed", _moveSpeed);
            _liteAnimator.SetBool("Stunned", _stunned);
            _fullAnimator.SetBool("Stunned", _stunned);
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

                GUILayout.Label("Move Speed");
                _moveSpeed = GUILayout.HorizontalSlider(_moveSpeed, 0f, 3f);

                _stunned = GUILayout.Toggle(_stunned, "Stunned");

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Dead"))
                    {
                        _liteAnimator.SetTrigger("Dead");
                        _fullAnimator.SetTrigger("Dead");
                    }

                    if (GUILayout.Button("Restart"))
                    {
                        _liteAnimator.SetTrigger("Restart");
                        _fullAnimator.SetTrigger("Restart");
                    }
                }

                GUI.enabled = _weapons.Count > 0;

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Attack"))
                    {
                        _liteAnimator.SetTrigger("Attack");
                        _fullAnimator.SetTrigger("Attack");
                    }
                    if (GUILayout.Button("Skill"))
                    {
                        _liteAnimator.SetTrigger("Skill");
                        _fullAnimator.SetTrigger("Skill");
                    }
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

        void UpdateAnimations(string weaponName)
        {
            _liteAnimatorController["Idle"] = liteCharacterBehaviour.Character.GetLiteAnimationClip($"{weaponName ?? "Default"}.Idle");
            _liteAnimatorController["Walk"] = liteCharacterBehaviour.Character.GetLiteAnimationClip($"{weaponName ?? "Default"}.Walk");
            _liteAnimatorController["Run"] = liteCharacterBehaviour.Character.GetLiteAnimationClip($"{weaponName ?? "Default"}.Run");
            _liteAnimatorController["Stun"] = liteCharacterBehaviour.Character.GetLiteAnimationClip("Default.Stun");
            _liteAnimatorController["Dead"] = liteCharacterBehaviour.Character.GetLiteAnimationClip("Default.Dead");

            _fullAnimatorController["Idle"] = fullCharacterBehaviour.Character.GetFullAnimationClip($"{weaponName ?? "Default"}.Idle");
            _fullAnimatorController["Walk"] = fullCharacterBehaviour.Character.GetFullAnimationClip($"{weaponName ?? "Default"}.Walk");
            _fullAnimatorController["Run"] = fullCharacterBehaviour.Character.GetFullAnimationClip($"{weaponName ?? "Default"}.Run");
            _fullAnimatorController["Stun"] = fullCharacterBehaviour.Character.GetFullAnimationClip("Default.Stun");
            _fullAnimatorController["Dead"] = fullCharacterBehaviour.Character.GetFullAnimationClip("Default.Dead");

            if (weaponName != null)
            {
                _liteAnimatorController["Attack"] = liteCharacterBehaviour.Character.GetLiteAnimationClip($"{weaponName}.Attack");
                _liteAnimatorController["Skill"] = liteCharacterBehaviour.Character.GetLiteAnimationClip($"{weaponName}.Skill");

                _fullAnimatorController["Attack"] = fullCharacterBehaviour.Character.GetFullAnimationClip($"{weaponName}.Attack");
                _fullAnimatorController["Skill"] = fullCharacterBehaviour.Character.GetFullAnimationClip($"{weaponName}.Skill");
            }
            else
            {
                _liteAnimator.SetTrigger("Restart");
                _fullAnimator.SetTrigger("Restart");
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
            _liteAnimator = liteCharacterBehaviour.Character.Root.AddComponent<Animator>();
            _liteAnimator.runtimeAnimatorController = _liteAnimatorController;

            fullCharacterBehaviour.axieDescriptor = axieDescriptor;
            fullCharacterBehaviour.Rebuild();
            _fullAnimator = fullCharacterBehaviour.Character.Root.AddComponent<Animator>();
            _fullAnimator.runtimeAnimatorController = _fullAnimatorController;

            EquipWeapon(_currentWeaponPrefab);
        }
    }
}
