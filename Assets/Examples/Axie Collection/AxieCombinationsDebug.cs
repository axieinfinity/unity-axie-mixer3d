using System.Collections.Generic;
using System.Reflection;
using SkyMavis.AxieMixer3D;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Debug/validation scene: renders every (class x partType x variant x level) combination
/// in a grid so art and data bakes can be visually inspected at a glance.
/// Each row = one class; each column = one (partType, variant, level) triple with a single part shown.
/// Runtime controls: left-click drag to look, WASD/QE to move, scroll wheel to zoom.
/// </summary>
public class AxieCombinationsDebug : MonoBehaviour
{
    public enum OutlineMode { None, DrawObjects, PostProcess }

    // Mirrors AnimNames constants — ToString() gives the clip name directly.
    public enum AnimClip
    {
        AttackCombo, AttackHead, AttackRange,
        AxeAttack, AxeIdle, AxeRun, AxeSkill, AxeWalk,
        BowAttack, BowIdle, BowRun, BowSkill, BowWalk,
        BrushAttack, BrushIdle, BrushRun, BrushWalk,
        CannonAttack, CannonIdle, CannonRun, CannonSkill, CannonWalk,
        CutTree,
        DaggerAttack, DaggerIdle, DaggerRun, DaggerSkill, DaggerWalk,
        Dead,
        FlagAttack, FlagIdle, FlagRun, FlagSkill, FlagWalk,
        FluteAttack, FluteIdle, FluteRun, FluteSkill, FluteWalk,
        GauntletAttack, GauntletIdle, GauntletRun, GauntletSkill, GauntletWalk,
        HammerAttack, HammerIdle, HammerRun, HammerSkill, HammerWalk, HammerWalkl,
        HitTree,
        Idle, IdleCarryItem, IdleGetHit,
        LanternAttack, LanternIdle, LanternRun, LanternSkill, LanternWalk,
        LootItem,
        MalaAttack, MalaIdle, MalaRun, MalaSkill, MalaWalk,
        PourWater,
        Run, RunAttack, RunCarryItem,
        Shoveling,
        SpearAttack, SpearIdle, SpearRun, SpearSkill, SpearWalk,
        StaffAttack, StaffIdle, StaffRun, StaffSkill, StaffWalk,
        StoneHarvest,
        Stun,
        SwordAttack, SwordAttackv2, SwordIdle, SwordRun, SwordSkill, SwordSkill2, SwordWalk,
        TalismanAttack, TalismanIdle, TalismanRun, TalismanSkill, TalismanWalk,
        TomeAttack, TomeIdle, TomeRun, TomeSkill, TomeWalk,
        Walk, WalkAttack, WalkCarryItem,
        WhipAttack, WhipIdle, WhipRun, WhipSkill, WhipWalk,
    }

    static readonly Dictionary<string, byte> ClassColorMap = new()
    {
        { "Aquatic", 14 },
        { "Beast",   3  },
        { "Bird",    25 },
        { "Bug",     20 },
        { "Plant",   9  },
        { "Reptile", 30 },
    };

    // S00–S13 skin names from texture mapping doc.
    static readonly string[] SkinNames =
    {
        "0 - Base",
        "1 - Mystic",
        "2 - AgamoGenesis",
        "3 - Japanese",
        "4 - Xmas2018",
        "5 - Xmas2019",
        "6 - Summer2022",
        "7 - Summer2022",
        "8 - Summer2022",
        "9 - Shiny2022",
        "10 - Shiny2022",
        "11 - Shiny2022",
        "12 - Nightmare",
        "13 - NightmareShiny",
    };

    static readonly AxieBodyType[] s_bodyTypes = (AxieBodyType[])System.Enum.GetValues(typeof(AxieBodyType));

    const string OutlineLayerName = "Outline";

    [Header("Body")]
    public AxieBodyType body = AxieBodyType.Normal;

    [Header("Animation")]
    public AnimClip defaultAnimation = AnimClip.Idle;
    public bool loopAnimation = true;

    [Header("Grid")]
    public string[] classes = { "Aquatic", "Beast", "Bird", "Bug", "Plant", "Reptile" };
    public int[] variants = { 2, 4, 6, 8, 10, 12 };
    public int skin;
    public float spacing = 2f;

    [Header("Outline")]
    public OutlineMode outlineMode = OutlineMode.None;

    [Header("Camera")]
    public float cameraMoveSpeed = 10f;
    public float cameraLookSpeed = 2f;
    public float cameraScrollSpeed = 5f;

    readonly List<AxieCharacter3D> _characters = new();
    readonly List<AxiePlayable> _playables = new();

    AxieBodyType _appliedBody = (AxieBodyType)(-1);
    AnimClip _appliedAnim = (AnimClip)(-1);
    bool _appliedLoop;
    OutlineMode _appliedMode = (OutlineMode)(-1);
    int _appliedSkin = -1;

    Camera _camera;
    float _yaw;
    float _pitch;
    // Updated each OnGUI call so HandleCamera can test mouse-over in screen space.
    Rect _panelScreenRect;

    void Start()
    {
        _camera = Camera.main;
        if (_camera != null)
        {
            var euler = _camera.transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x;
        }
        BuildAll();
    }

    void Update()
    {
        HandleCamera();

        if (body != _appliedBody || skin != _appliedSkin)
        {
            DisposeAll();
            BuildAll();
        }
        else if (defaultAnimation != _appliedAnim || loopAnimation != _appliedLoop)
        {
            ReplayAll();
        }

        if (outlineMode != _appliedMode)
            ApplyOutlineMode(outlineMode);
    }

    void OnDestroy()
    {
        SetPostProcessOutlineActive(false);
        DisposeAll();
    }

    void HandleCamera()
    {
        if (_camera == null) return;

        // Left-click drag to look — suppressed when the pointer is over the UI panel.
        if (Input.GetMouseButton(0))
        {
            var mp = Input.mousePosition;
            bool overPanel = _panelScreenRect.Contains(new Vector2(mp.x, Screen.height - mp.y));
            if (!overPanel)
            {
                _yaw   += Input.GetAxis("Mouse X") * cameraLookSpeed;
                _pitch -= Input.GetAxis("Mouse Y") * cameraLookSpeed;
                _pitch  = Mathf.Clamp(_pitch, -89f, 89f);
                _camera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
        }

        // WASD = move, Q/E = up/down.
        var move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += _camera.transform.forward;
        if (Input.GetKey(KeyCode.S)) move -= _camera.transform.forward;
        if (Input.GetKey(KeyCode.A)) move -= _camera.transform.right;
        if (Input.GetKey(KeyCode.D)) move += _camera.transform.right;
        if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
        if (Input.GetKey(KeyCode.E)) move += Vector3.up;
        if (move != Vector3.zero)
            _camera.transform.position += move.normalized * (cameraMoveSpeed * Time.deltaTime);

        // Scroll wheel = zoom.
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f)
        {
            if (_camera.orthographic)
                _camera.orthographicSize = Mathf.Max(0.5f, _camera.orthographicSize - scroll * cameraScrollSpeed);
            else
                _camera.transform.position += _camera.transform.forward * (scroll * cameraScrollSpeed);
        }
    }

    void OnGUI()
    {
        // 2× the original dimensions.
        const float panelW = 620f;   // 310 × 2
        const float rowH   = 52f;    //  26 × 2
        const float pad    = 16f;    //   8 × 2
        const float labelW = 96f;    //  48 × 2
        const float arrowW = 48f;    //  24 × 2
        const float valueW = 340f;   // 170 × 2
        const float spacer = 8f;     //   4 × 2
        // 3 rows (Back + Body + Skin) + 2 spacers between them.
        const float panelH = pad * 2 + rowH * 3 + spacer * 2;

        _panelScreenRect = new Rect(10, 10, panelW, panelH);
        GUI.Box(_panelScreenRect, GUIContent.none);
        GUILayout.BeginArea(new Rect(10 + pad, 10 + pad, panelW - pad * 2, panelH - pad * 2));

        var btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 24 };
        var lblStyle = new GUIStyle(GUI.skin.label)  { fontSize = 24, alignment = TextAnchor.MiddleLeft };
        var valStyle = new GUIStyle(GUI.skin.label)  { fontSize = 24, alignment = TextAnchor.MiddleCenter };

        // ── Back ──────────────────────────────────────────────────────────
        if (GUILayout.Button("Back", btnStyle, GUILayout.Height(rowH)))
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                "Assets/Examples/AxieMixer3DExample/AxieMixer3DExample.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene("AxieMixer3DExample");
#endif
        }

        GUILayout.Space(spacer);

        // ── Body ──────────────────────────────────────────────────────────
        GUILayout.BeginHorizontal(GUILayout.Height(rowH));
        GUILayout.Label("Body:", lblStyle, GUILayout.Width(labelW));
        int bodyIdx = System.Array.IndexOf(s_bodyTypes, body);
        if (bodyIdx < 0) bodyIdx = 0;
        if (GUILayout.Button("<", btnStyle, GUILayout.Width(arrowW), GUILayout.Height(rowH)))
            body = s_bodyTypes[(bodyIdx - 1 + s_bodyTypes.Length) % s_bodyTypes.Length];
        GUILayout.Label(body.ToString(), valStyle, GUILayout.Width(valueW));
        if (GUILayout.Button(">", btnStyle, GUILayout.Width(arrowW), GUILayout.Height(rowH)))
            body = s_bodyTypes[(bodyIdx + 1) % s_bodyTypes.Length];
        GUILayout.EndHorizontal();

        GUILayout.Space(spacer);

        // ── Skin ──────────────────────────────────────────────────────────
        GUILayout.BeginHorizontal(GUILayout.Height(rowH));
        GUILayout.Label("Skin:", lblStyle, GUILayout.Width(labelW));
        int skinIdx = Mathf.Clamp(skin, 0, SkinNames.Length - 1);
        if (GUILayout.Button("<", btnStyle, GUILayout.Width(arrowW), GUILayout.Height(rowH)))
            skin = (skin - 1 + SkinNames.Length) % SkinNames.Length;
        GUILayout.Label(SkinNames[skinIdx], valStyle, GUILayout.Width(valueW));
        if (GUILayout.Button(">", btnStyle, GUILayout.Width(arrowW), GUILayout.Height(rowH)))
            skin = (skin + 1) % SkinNames.Length;
        GUILayout.EndHorizontal();

        GUILayout.EndArea();

        // ── Camera controls hint ───────────────────────────────────────────
        const string hint = "Move: WASD / Q E     Look: Left-click drag     Zoom: Scroll wheel";
        var hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 22,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = new Color(1f, 1f, 1f, 0.75f) },
        };
        float hintH = 36f;
        GUI.Label(new Rect(0f, Screen.height - hintH - 8f, Screen.width, hintH), hint, hintStyle);
    }

    void BuildAll()
    {
        _appliedBody = body;
        _appliedSkin = skin;

        var factory = skin != 0 ? AxieFactory.Default : null;
        var partTypes = (AxiePartType[])System.Enum.GetValues(typeof(AxiePartType));
        int rowIndex = 0;

        foreach (var @class in classes)
        {
            var colorVariant = ClassColorMap.GetValueOrDefault(@class);
            int colIndex = 0;

            foreach (var variant in variants)
            {
                for (int level = 1; level <= 2; level++)
                {
                    var descriptor = new AxieDescriptor
                    {
                        body = body,
                        colorVariant = colorVariant,
                        parts = new(),
                    };

                    foreach (var partType in partTypes)
                    {
                        // When skin != 0, only include parts that actually ship that skin.
                        // Parts that don't have it are omitted entirely rather than falling back to skin 0.
                        if (factory == null || factory.HasPart(@class, variant, skin, level, partType))
                            descriptor.parts.Add(new() { @class = @class, variant = variant, type = partType, skin = skin, level = level });
                    }

                    var character = AxieCharacter3D.FromDescriptor(descriptor);
                    _characters.Add(character);
                    character.Root.name = $"{@class}_{variant:D2}_L{level}";
                    character.Root.transform.SetPositionAndRotation(
                        new Vector3(spacing * colIndex, 0f, spacing * rowIndex),
                        Quaternion.Euler(0f, 180f, 0f)
                    );

                    if (skin != 0 && descriptor.parts.Count == 0)
                        character.Root.SetActive(false);

                    var playable = new AxiePlayable(character);
                    _playables.Add(playable);

                    colIndex++;
                }
            }

            rowIndex++;
        }

        ReplayAll();
        ApplyOutlineMode(outlineMode);
    }

    void ReplayAll()
    {
        _appliedAnim = defaultAnimation;
        _appliedLoop = loopAnimation;
        var clipName = defaultAnimation.ToString();
        foreach (var playable in _playables)
            playable.Play(clipName, loop: loopAnimation);
    }

    void DisposeAll()
    {
        foreach (var p in _playables) p.Dispose();
        foreach (var c in _characters) c.Dispose();
        _playables.Clear();
        _characters.Clear();
    }

    void ApplyOutlineMode(OutlineMode mode)
    {
        _appliedMode = mode;

        int outlineLayer = LayerMask.NameToLayer(OutlineLayerName);
        if (outlineLayer < 0)
        {
            Debug.LogWarning($"[AxieCombinationsDebug] No '{OutlineLayerName}' layer found — Draw Objects outline won't render.");
            outlineLayer = 0;
        }

        foreach (var character in _characters)
            character.SetOutlineLayer(mode == OutlineMode.DrawObjects ? outlineLayer : 0);

        SetPostProcessOutlineActive(mode == OutlineMode.PostProcess);
    }

    static void SetPostProcessOutlineActive(bool active)
    {
        if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset urp) return;
        var field = typeof(UniversalRenderPipelineAsset).GetField(
            "m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(urp) is not ScriptableRendererData[] dataList) return;
        foreach (var data in dataList)
        {
            if (data == null) continue;
            foreach (var feature in data.rendererFeatures)
                if (feature is OutlinePostProcessRendererFeature) feature.SetActive(active);
        }
    }
}
