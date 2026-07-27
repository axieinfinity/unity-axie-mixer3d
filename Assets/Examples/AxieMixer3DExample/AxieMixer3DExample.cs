using System.Collections;
using System.Reflection;
using SkyMavis.AxieMixer3D;
using SkyMavis.AxieMixer3D.WeaponAnims;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SkyMavis.AxieMixer3D.Example
{
    /// <summary>
    /// End-to-end example that assembles a 3D Axie and demonstrates the animation API:
    /// looping defaults, one-shot clips, and queued sequences.
    ///
    /// Attach to an empty GameObject in <c>AxieMixer3DExample.unity</c> and press Play.
    /// Use the runtime GUI panel (top-left) to switch animations without touching the
    /// inspector.
    ///
    /// <b>Default Blend</b> — an Idle→Walk→Run 1D blend set as the return-to state; the slider
    /// drives its loco speed via <see cref="AxiePlayable.SetSpeed"/>.<br/>
    /// <b>One-shot</b> — plays once then returns to the default blend.<br/>
    /// <b>Sequence</b> — chains clips with <see cref="AnimTrack.Queue"/>; returns to the default
    /// blend after the last clip finishes.
    /// </summary>
    public class AxieMixer3DExample : MonoBehaviour
    {
        /// <summary>The two outline approaches the package ships (plus off).</summary>
        public enum OutlineMode
        {
            None,
            PostProcess,
            DrawObjects,
        }

        [Header("Option A — build from a gene string (leave empty to use fields below)")]
        [Tooltip("512-bit hex gene string. When set, overrides the descriptor fields.")]
        public string axieGenes = "";

        [Header("Option B — build from an explicit descriptor")]
        public AxieBodyType body = AxieBodyType.Normal;
        public AxieClassOption axieClass = AxieClassOption.Beast;
        public AxieVariant variant = AxieVariant.V02;
        public byte colorVariant = 3;
        public int skin = 0;
        public int level = 1;

        [Header("Outline")]
        [Tooltip("Switchable at runtime. Requires the outline features on URP-Balanced-Renderer.")]
        public OutlineMode outlineMode = OutlineMode.None;

        [Header("Scene setup (created at runtime if missing)")]
        public bool createCameraAndLight = true;

        const string OutlineLayerName = "Outline";

        static readonly string[]       s_classNames  = {"Aquatic","Beast","Bird","Bug","Plant","Reptile"};
        static readonly int[]          s_variantVals = {2,4,6,8,10,12};
        static readonly byte[]         s_classColors = {14,3,25,20,9,30}; // matches s_classNames order
        static readonly AxieBodyType[] s_bodyTypes   = (AxieBodyType[])System.Enum.GetValues(typeof(AxieBodyType));
        static readonly AxiePartType[] s_partTypes   = (AxiePartType[])System.Enum.GetValues(typeof(AxiePartType));
        const int SkinCount = 14;

        // Idle→Walk→Run 1D blend used as the default (return-to) locomotion state.
        static readonly (string clipName, float threshold)[] s_locoBlend =
        {
            (AnimNames.Idle, 0f),
            (AnimNames.Walk, 1f),
            (AnimNames.Run,  3.5f),
        };
        const float LocoSpeedMax = 5f;

        struct CharacterSnapshot
        {
            public string Genes;
            public AxieBodyType Body;
            public AxieClassOption Class;
            public AxieVariant Variant;
            public byte ColorVariant;
            public int Skin;
            public int Level;

            public static CharacterSnapshot Of(AxieMixer3DExample e) => new()
            {
                Genes = e.axieGenes, Body = e.body, Class = e.axieClass,
                Variant = e.variant, ColorVariant = e.colorVariant, Skin = e.skin, Level = e.level,
            };

            public bool Equals(CharacterSnapshot o) =>
                Genes == o.Genes && Body == o.Body && Class == o.Class &&
                Variant == o.Variant && ColorVariant == o.ColorVariant && Skin == o.Skin && Level == o.Level;
        }

        AxieCharacter3D _character;
        AxiePlayable _playable;
        float _locoSpeed = 0f;   // slider-driven blend speed (0 = Idle … 5 = Run)
        OutlineMode _appliedMode = (OutlineMode)(-1);
        CharacterSnapshot _charSnapshot;

        string[] _allAnimNames;
        Vector2  _animListScroll;

        // Customization UI state
        int     _uiBodyIdx      = 0;
        int     _uiBodyClassIdx = 1;  // Beast — drives colorVariant only (independent of parts)
        int     _uiColorVariant = 3;  // actual colorVariant (kept exact when decoded from genes)
        int[]   _uiPartClass;
        int[]   _uiPartVariant;
        int[]   _uiPartSkin;
        int[]   _uiPartLevel;
        Vector2 _customizeScroll;

        const int MaxLevel = 4;

        // Genes paste field
        string _uiGenes      = "";
        string _appliedGenes = "";

        // Rotation drag
        float _axieYaw       = 180f;
        bool  _isDraggingAxie;
        Rect  _leftPanelRect;
        Rect  _rightPanelRect;

        void Start()
        {
            _allAnimNames = CollectAnimNames();
            InitCustomizationState();
            if (createCameraAndLight) EnsureCameraAndLight();
            _charSnapshot = CharacterSnapshot.Of(this);
            BuildCharacter();
        }

        void InitCustomizationState()
        {
            int bi = System.Array.IndexOf(s_bodyTypes, body);
            _uiBodyIdx = bi >= 0 ? bi : 0;

            _uiColorVariant = s_classColors[_uiBodyClassIdx];

            _uiPartClass   = new int[s_partTypes.Length];
            _uiPartVariant = new int[s_partTypes.Length];
            _uiPartSkin    = new int[s_partTypes.Length];
            _uiPartLevel   = new int[s_partTypes.Length];

            for (int i = 0; i < s_partTypes.Length; i++)
            {
                _uiPartClass[i]   = _uiBodyClassIdx;
                _uiPartVariant[i] = 0; // V02
                _uiPartSkin[i]    = 0;
                _uiPartLevel[i]   = 1;
            }

            // If a gene string was set in the inspector, decode it into the UI.
            if (!string.IsNullOrWhiteSpace(axieGenes))
                PopulateUIFromGenes(axieGenes);
        }

        // Reflect the string constants off a *Names holder (AnimNames / WeaponAnimNames) into names.
        static void AddConstNames(System.Type namesType, System.Collections.Generic.ICollection<string> names)
        {
            foreach (var f in namesType.GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.IsLiteral && f.FieldType == typeof(string))
                    names.Add((string)f.GetValue(null));
        }

        // Merge the main-package Default clip names (AnimNames) with the optional weapon-anim
        // package's Action clip names (WeaponAnimNames) so the list shows both sets. Weapon clips
        // only actually play once the weapon package's catalog is registered (AxieWeaponAnims.Register
        // / the initializer); until then PlayOneShot warns and no-ops for those entries.
        static string[] CollectAnimNames()
        {
            var set = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            AddConstNames(typeof(AnimNames), set);
            AddConstNames(typeof(WeaponAnimNames), set);
            var list = new System.Collections.Generic.List<string>(set);
            list.Sort(System.StringComparer.OrdinalIgnoreCase);
            return list.ToArray();
        }

        void Update()
        {
            HandleAxieRotation();

            var current = CharacterSnapshot.Of(this);
            if (!current.Equals(_charSnapshot))
            {
                _charSnapshot = current;
                int bi = System.Array.IndexOf(s_bodyTypes, body);
                if (bi >= 0) _uiBodyIdx = bi;
                BuildCharacter();
                return;
            }

            if (outlineMode != _appliedMode) ApplyOutlineMode(outlineMode);
        }

        void HandleAxieRotation()
        {
            if (_character?.Root == null) return;

            // Flip Y so GUI-space and Input.mousePosition match
            var mp = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            bool overPanel = _leftPanelRect.Contains(mp) || _rightPanelRect.Contains(mp);

            if (Input.GetMouseButtonDown(0) && !overPanel) _isDraggingAxie = true;
            if (Input.GetMouseButtonUp(0))                 _isDraggingAxie = false;

            if (_isDraggingAxie)
            {
                // Reversed direction, doubled speed (was +2 → now -4).
                _axieYaw += Input.GetAxis("Mouse X") * -4f;
                _character.Root.transform.localRotation = Quaternion.Euler(0f, _axieYaw, 0f);
            }
        }

        void OnGUI()
        {
            var btn = ButtonStyle();
            var lbl = LabelStyle();
            var h   = GUILayout.Height(63f);

            var cst  = CustomStyle();
            var cLbl = CustomLblStyle();
            var cVal = CustomValStyle();
            var gen  = GenesStyle();
            const float cH = 54f;
            const float aW = 46f; // arrow button width

            const float leftW  = 442f; // half the previous 884
            const float rightW = 435f;
            _leftPanelRect  = new Rect(10f, 10f, leftW, Screen.height - 20f);
            _rightPanelRect = new Rect(Screen.width - rightW - 10f, 10f, rightW, Screen.height - 20f);

            // ── Left panel — Back + Genes + Customize ────────────────────────────
            GUILayout.BeginArea(_leftPanelRect);

            if (GUILayout.Button("← Back", btn, h)) LoadAxieCollectionScene();

            GUILayout.Space(15f);
            GUILayout.Label("<b>Axie ID / Genes</b>", lbl);
            GUILayout.BeginHorizontal();
            _uiGenes = GUILayout.TextField(_uiGenes ?? "", gen, GUILayout.Height(cH));
            GUI.enabled = !_fetching;
            if (GUILayout.Button(_fetching ? "…" : "Load", btn, GUILayout.Width(120f), GUILayout.Height(cH)))
                LoadInput(_uiGenes);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_fetchStatus))
                GUILayout.Label(_fetchStatus, lbl);

            // Auto-load a pasted gene string; Axie IDs load on the button (avoids a request per keystroke).
            string genesTrimmed = (_uiGenes ?? "").Trim();
            if (genesTrimmed != _appliedGenes && LooksLikeGenes(genesTrimmed))
                ApplyGenes(genesTrimmed);

            GUILayout.Space(15f);
            GUILayout.Label("<b>Customize</b>", lbl);

            const float lblW = 200f;
            const float pad  = 14f; // left/right inner padding for the scroll content
            _customizeScroll = GUILayout.BeginScrollView(_customizeScroll);

            GUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            GUILayout.BeginVertical();

            // One control per line. Body type / Body class are independent of parts.
            int bd = PickerRow("Body type", s_bodyTypes[_uiBodyIdx].ToString(), cLbl, cVal, cst, lblW, aW, cH);
            if (bd != 0) { _uiBodyIdx = Wrap(_uiBodyIdx + bd, s_bodyTypes.Length); ApplyUIChanges(); }

            int bcd = PickerRow("Body class", s_classNames[_uiBodyClassIdx], cLbl, cVal, cst, lblW, aW, cH);
            if (bcd != 0) SetBodyClass(Wrap(_uiBodyClassIdx + bcd, s_classNames.Length));

            for (int i = 0; i < s_partTypes.Length; i++)
            {
                int pi = i;
                string t = s_partTypes[i].ToString();

                GUILayout.Space(18f); // gap before each part group

                int cd = PickerRow($"{t} class", s_classNames[_uiPartClass[pi]], cLbl, cVal, cst, lblW, aW, cH);
                if (cd != 0) { _uiPartClass[pi] = Wrap(_uiPartClass[pi] + cd, s_classNames.Length); ApplyUIChanges(); }

                int vd = PickerRow($"{t} value", $"V{s_variantVals[_uiPartVariant[pi]]:D2}", cLbl, cVal, cst, lblW, aW, cH);
                if (vd != 0) { _uiPartVariant[pi] = Wrap(_uiPartVariant[pi] + vd, s_variantVals.Length); ApplyUIChanges(); }

                int sd = PickerRow($"{t} skin", $"S{_uiPartSkin[pi]:D2}", cLbl, cVal, cst, lblW, aW, cH);
                if (sd != 0) { _uiPartSkin[pi] = Wrap(_uiPartSkin[pi] + sd, SkinCount); ApplyUIChanges(); }

                int ld = PickerRow($"{t} level", _uiPartLevel[pi].ToString(), cLbl, cVal, cst, lblW, aW, cH);
                if (ld != 0) { _uiPartLevel[pi] = Mathf.Clamp(_uiPartLevel[pi] + ld, 1, MaxLevel); ApplyUIChanges(); }
            }

            GUILayout.EndVertical();
            GUILayout.Space(pad);
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();

            GUILayout.EndArea();

            // ── Right panel — Default Blend + Sequence + All Animations ─────────
            GUILayout.BeginArea(_rightPanelRect);

            GUILayout.Label("<b>Default Blend</b>", lbl);
            GUILayout.Label("(Idle → Walk → Run)", lbl);
            GUILayout.Label($"Loco speed: {_locoSpeed:0.0}", lbl);

            // Reserve a fixed-height row and draw the track ourselves, vertically centered. IMGUI
            // top-aligns the slider thumb to the row (see HorizontalThumbRect), so a full-row-height
            // thumb over an invisible track style keeps the knob centered on the drawn groove.
            const float sliderRowH = 48f;
            const float sliderTrackH = 12f;
            Rect sliderRow   = GUILayoutUtility.GetRect(1f, sliderRowH, GUILayout.ExpandWidth(true));
            Rect sliderTrack = new Rect(sliderRow.x, sliderRow.y + (sliderRowH - sliderTrackH) * 0.5f,
                                        sliderRow.width, sliderTrackH);
            if (Event.current.type == EventType.Repaint)
                GUI.skin.horizontalSlider.Draw(sliderTrack, GUIContent.none, false, false, false, false);
            float newSpeed = GUI.HorizontalSlider(sliderRow, _locoSpeed, 0f, LocoSpeedMax,
                                                  GUIStyle.none, SliderThumbStyle());
            if (!Mathf.Approximately(newSpeed, _locoSpeed))
            {
                _locoSpeed = newSpeed;
                _playable?.SetSpeed(_locoSpeed);   // drive the live/default blend without holding the handle
            }

            GUILayout.Space(18f);
            GUILayout.Label("<b>Sequence</b>", lbl);
            if (GUILayout.Button("Skill → Dead → Default", btn, h)) PlaySkillDeadSequence();

            GUILayout.Space(18f);
            GUILayout.Label("<b>All Animations</b>", lbl);
            _animListScroll = GUILayout.BeginScrollView(_animListScroll);
            if (_allAnimNames != null)
                foreach (var name in _allAnimNames)
                    if (GUILayout.Button(name, btn, h))
                        PlayOneShot(name);
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        void ApplyUIChanges()
        {
            // Editing any customize control switches to descriptor mode. Without this,
            // a non-empty gene string (inspector or a prior paste) makes BuildCharacter
            // always rebuild from genes and silently ignore the UI descriptor.
            // (BuildCharacter re-encodes the descriptor back into _uiGenes/_appliedGenes.)
            axieGenes = "";
            body = s_bodyTypes[_uiBodyIdx];
            _charSnapshot = CharacterSnapshot.Of(this);
            BuildCharacter();
        }

        const string GraphqlURL = "https://graphql-gateway.axieinfinity.com/graphql";
        bool   _fetching;
        string _fetchStatus = "";

        /// <summary>
        /// Routes the input box: a numeric Axie ID is queried from the gateway (then decoded), a
        /// hex gene string is decoded directly. Mirrors the AxieGenesDecoder editor tool.
        /// </summary>
        void LoadInput(string input)
        {
            string s = (input ?? "").Trim();
            if (string.IsNullOrEmpty(s)) return;

            if (IsAxieId(s))            StartCoroutine(FetchGenesById(s));
            else if (LooksLikeGenes(s)) ApplyGenes(s);
            else                        _fetchStatus = "Enter an Axie ID (number) or a gene hex string.";
        }

        /// <summary>Short all-digit strings are Axie IDs; long hex strings are genes.</summary>
        static bool IsAxieId(string s)
        {
            if (s.Length == 0 || s.Length > 12) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        static bool LooksLikeGenes(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.StartsWith("0x") || s.StartsWith("0X")) s = s.Substring(2);
            if (s.Length < 40) return false;
            foreach (char c in s)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>Queries the Axie Infinity GraphQL gateway for an Axie's genes, then applies them.</summary>
        IEnumerator FetchGenesById(string id)
        {
            _fetching = true;
            _fetchStatus = $"Fetching Axie #{id}…";

            var query = $"{{ axie (axieId: \"{id}\") {{ id, genes, newGenes }} }}";
            var json = JsonUtility.ToJson(new FetchGenesRequest { query = query });
            var payload = System.Text.Encoding.UTF8.GetBytes(json);
            using var req = new UnityWebRequest(GraphqlURL, "POST")
            {
                uploadHandler   = new UploadHandlerRaw(payload) { contentType = "application/json" },
                downloadHandler = new DownloadHandlerBuffer(),
            };

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<FetchGenesResponse>(req.downloadHandler.text);
                var genes = response.data.axie?.newGenes;
                if (!string.IsNullOrEmpty(genes))
                {
                    _uiGenes = genes;                 // reflect the loaded genes in the box
                    _fetchStatus = $"Loaded Axie #{id}.";
                    ApplyGenes(genes);
                }
                else
                {
                    _fetchStatus = $"Axie #{id} not found.";
                }
            }
            else
            {
                _fetchStatus = $"Fetch failed: {req.error}";
            }

            _fetching = false;
        }

        [System.Serializable]
        struct FetchGenesRequest { public string query; }

        [System.Serializable]
        struct FetchGenesResponse
        {
            public Data data;

            [System.Serializable]
            public struct Data
            {
                public Axie axie;

                [System.Serializable]
                public class Axie { public string newGenes; }
            }
        }

        /// <summary>Decodes a gene string into the UI, then builds from it.</summary>
        void ApplyGenes(string genes)
        {
            _appliedGenes = genes;
            PopulateUIFromGenes(genes);
            axieGenes = genes;                       // build faithfully from the genes
            body      = s_bodyTypes[_uiBodyIdx];
            _charSnapshot = CharacterSnapshot.Of(this);
            BuildCharacter();
        }

        /// <summary>Maps a decoded descriptor onto the customize UI arrays.</summary>
        void PopulateUIFromGenes(string genes)
        {
            AxieDescriptor d;
            try { d = AxieDescriptor.FromGenes(genes); }
            catch { return; }

            int bi = System.Array.IndexOf(s_bodyTypes, d.body);
            if (bi >= 0) _uiBodyIdx = bi;

            _uiColorVariant = d.colorVariant;
            _uiBodyClassIdx = ClassIdxFromColorVariant(d.colorVariant);

            if (d.parts != null)
            {
                foreach (var p in d.parts)
                {
                    int ti = System.Array.IndexOf(s_partTypes, p.type);
                    if (ti < 0) continue;

                    int ci = System.Array.IndexOf(s_classNames, p.@class);
                    if (ci >= 0) _uiPartClass[ti] = ci;

                    int vi = System.Array.IndexOf(s_variantVals, p.variant);
                    if (vi >= 0) _uiPartVariant[ti] = vi;

                    _uiPartSkin[ti]  = Mathf.Clamp(p.skin, 0, SkinCount - 1);
                    _uiPartLevel[ti] = Mathf.Clamp(p.level, 1, MaxLevel);
                }
            }
        }

        static int ClassIdxFromColorVariant(int cv)
        {
            string cls = cv switch
            {
                <= 5  => "Beast",
                <= 10 => "Plant",
                <= 16 => "Aquatic",
                <= 21 => "Bug",
                <= 26 => "Bird",
                <= 32 => "Reptile",
                _     => "Beast",
            };
            int idx = System.Array.IndexOf(s_classNames, cls);
            return idx < 0 ? 1 : idx;
        }

        void LoadAxieCollectionScene()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                "Assets/Examples/Axie Collection/Axie Collection.unity",
                new UnityEngine.SceneManagement.LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene("Axie Collection");
#endif
        }

        /// <summary>
        /// Body class only drives the body's <see cref="AxieDescriptor.colorVariant"/>.
        /// Part classes are edited independently per row and are left untouched here.
        /// </summary>
        void SetBodyClass(int idx)
        {
            _uiBodyClassIdx = idx;
            _uiColorVariant = s_classColors[idx];
            ApplyUIChanges();
        }

        /// <summary>
        /// Draws a single "<paramref name="label"/>  [&lt;] value [&gt;]" line and returns
        /// -1 / +1 when an arrow is pressed this event, or 0 otherwise.
        /// </summary>
        static int PickerRow(string label, string value, GUIStyle lblS, GUIStyle valS, GUIStyle btnS,
                             float lblW, float aW, float cH)
        {
            var hgt = GUILayout.Height(cH);
            int delta = 0;
            GUILayout.BeginHorizontal(hgt);
            GUILayout.Label(label, lblS, GUILayout.Width(lblW));
            if (GUILayout.Button("<", btnS, GUILayout.Width(aW), hgt)) delta = -1;
            GUILayout.Label(value, valS);
            if (GUILayout.Button(">", btnS, GUILayout.Width(aW), hgt)) delta = 1;
            GUILayout.EndHorizontal();
            return delta;
        }

        static int Wrap(int value, int count) => ((value % count) + count) % count;

        // ── animation helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Sets the Idle→Walk→Run 1D blend as the default (return-to) state and starts it at the
        /// current slider speed. One-shot clips auto-return to this blend when they finish.
        /// </summary>
        void SetupDefaultBlend()
        {
            if (_playable == null) return;

            var blend = _playable.SetDefaultBlend(s_locoBlend, _locoSpeed);
            if (blend == null)
            {
                Debug.LogWarning("[AxieMixer3DExample] locomotion clips not available — falling back to looping Idle.");
                _playable.SetDefault(AnimNames.Idle);
                _playable.Play(AnimNames.Idle, loop: true);
            }
        }

        /// <summary>
        /// Plays <paramref name="clipName"/> once. When it finishes the animator
        /// automatically returns to the current default looping clip.
        /// </summary>
        void PlayOneShot(string clipName)
        {
            if (_playable == null) return;
            var track = _playable.Play(clipName, loop: false);
            if (track == null)
                Debug.LogWarning($"[AxieMixer3DExample] '{clipName}' not available on this body.");
        }

        /// <summary>
        /// Demonstrates <see cref="AnimTrack.Queue"/>: plays AttackCombo, then queues
        /// Dead, then the animator auto-returns to the current default.
        /// </summary>
        void PlaySkillDeadSequence()
        {
            if (_playable == null) return;
            // AttackCombo ships in the optional weapon-anim package. It plays only if that package
            // is installed and its catalog registered (AxieWeaponAnims.Register / the initializer);
            // otherwise Play returns null and the ?. short-circuits (graceful no-op + warning).
            _playable.Play(WeaponAnimNames.AttackCombo, loop: false)
                     ?.Queue(AnimNames.Dead, loop: false);
        }

        // ── character building ───────────────────────────────────────────────────

        void OnDestroy()
        {
            SetPostProcessOutlineActive(false);
            _playable?.Dispose();
            _playable = null;
            _character?.Dispose();
            _character = null;
        }

        void BuildCharacter()
        {
            SetPostProcessOutlineActive(false);
            _playable?.Dispose();
            _playable = null;
            _character?.Dispose();
            _character = null;

            bool descriptorMode = string.IsNullOrWhiteSpace(axieGenes);
            AxieDescriptor descriptor = descriptorMode ? BuildDescriptor() : default;
            _character = descriptorMode
                ? AxieCharacter3D.FromDescriptor(descriptor)
                : AxieCharacter3D.FromGenes(axieGenes);

            // Keep the genes box in sync with whatever was actually built: encode the descriptor
            // in descriptor mode, echo the source string in genes mode. Mark it already-applied so
            // the OnGUI auto-decode doesn't re-trigger a rebuild from it.
            _uiGenes = descriptorMode ? descriptor.ToGenes() : axieGenes;
            _appliedGenes = _uiGenes;

            if (_character == null) return;

            _character.Root.name = "Example Axie";
            _character.Root.transform.SetParent(transform, false);
            _character.Root.transform.localRotation = Quaternion.Euler(0f, _axieYaw, 0f);

            _playable = new AxiePlayable(_character) { Fade = 0.2f };
            _appliedMode = (OutlineMode)(-1);

            // Arm the Idle→Walk→Run blend as the default state on the new character.
            SetupDefaultBlend();
            ApplyOutlineMode(outlineMode);
        }

        void ApplyOutlineMode(OutlineMode mode)
        {
            _appliedMode = mode;
            if (_character?.Root == null) return;

            int outlineLayer = LayerMask.NameToLayer(OutlineLayerName);
            if (outlineLayer < 0)
            {
                Debug.LogWarning($"[AxieMixer3DExample] No '{OutlineLayerName}' layer found — Draw Objects outline won't render.");
                outlineLayer = 0;
            }

            _character.SetOutlineLayer(mode == OutlineMode.DrawObjects ? outlineLayer : 0);
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
                {
                    if (feature is OutlinePostProcessRendererFeature) feature.SetActive(active);
                }
            }
        }

        AxieDescriptor BuildDescriptor()
        {
            var descriptor = new AxieDescriptor
            {
                body         = s_bodyTypes[_uiBodyIdx],
                colorVariant = _uiColorVariant,
                parts        = new(),
            };

            for (int i = 0; i < s_partTypes.Length; i++)
            {
                descriptor.parts.Add(new AxiePartDescriptor
                {
                    @class  = s_classNames[_uiPartClass[i]],
                    variant = s_variantVals[_uiPartVariant[i]],
                    type    = s_partTypes[i],
                    skin    = _uiPartSkin[i],
                    level   = _uiPartLevel[i],
                });
            }

            return descriptor;
        }

        void EnsureCameraAndLight()
        {
            if (Camera.main == null)
            {
                var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(0f, 1.2f, -3.5f), Quaternion.Euler(10f, 0f, 0f));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.18f, 0.2f, 0.24f, 1f);
            }

            if (FindAnyObjectByType<Light>() == null)
            {
                var lightObject = new GameObject("Directional Light");
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        // ── GUI helpers ──────────────────────────────────────────────────────────

        GUIStyle _labelStyle;
        GUIStyle _buttonStyle;
        GUIStyle _customStyle;
        GUIStyle _customLblStyle;
        GUIStyle _customValStyle;
        GUIStyle _genesStyle;
        GUIStyle _sliderThumbStyle;

        GUIStyle SliderThumbStyle()
        {
            if (_sliderThumbStyle == null)
                _sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
                {
                    fixedWidth  = 48f,
                    fixedHeight = 48f,
                };
            return _sliderThumbStyle;
        }

        GUIStyle LabelStyle()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 32,
                };
            }
            return _labelStyle;
        }

        GUIStyle ButtonStyle()
        {
            if (_buttonStyle == null)
                _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 30 };
            return _buttonStyle;
        }

        GUIStyle CustomStyle()
        {
            if (_customStyle == null)
                _customStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 26,
                    margin   = new RectOffset(2, 2, 2, 2),
                };
            return _customStyle;
        }

        GUIStyle CustomLblStyle()
        {
            if (_customLblStyle == null)
                _customLblStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 26,
                    alignment = TextAnchor.MiddleLeft,
                    margin    = new RectOffset(2, 2, 2, 2),
                };
            return _customLblStyle;
        }

        GUIStyle CustomValStyle()
        {
            if (_customValStyle == null)
                _customValStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 26,
                    alignment = TextAnchor.MiddleCenter,
                    margin    = new RectOffset(2, 2, 2, 2),
                };
            return _customValStyle;
        }

        GUIStyle GenesStyle()
        {
            if (_genesStyle == null)
                _genesStyle = new GUIStyle(GUI.skin.textField)
                {
                    fontSize = 24,
                    margin   = new RectOffset(2, 2, 2, 2),
                };
            return _genesStyle;
        }
    }
}
