using System.Collections.Generic;
using SkyMavis.AxieMixer3D.Internal;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace SkyMavis.AxieMixer3D
{
    /// <summary>
    /// Full play specification. Build with an object initializer:
    ///   <c>new AnimPlayParams { ClipName = AnimNames.Run, StateName = "run", Fade = 0.15f }</c>
    ///   (weapon clips such as <c>WeaponAnimNames.AxeAttack</c> require the optional weapon-anim package)
    /// </summary>
    public sealed class AnimPlayParams
    {
        public string ClipName;
        public string StateName;
        public bool   Loop            = false;
        /// <summary>Crossfade seconds. &lt; 0 inherits the animator's global Fade; 0 = snap; &gt; 0 overrides.</summary>
        public float  Fade            = -1f;
        public float  TimeScale       = 1f;
        public float  NormalizedStart = 0f;
        public float  StartTime       = 0f;
        public System.Action OnComplete;
    }

    /// <summary>
    /// Internal abstraction over an animation backend, implemented by <see cref="AxiePlayable"/>
    /// (Playables) and the reference-only <see cref="AxieAnimator"/> (legacy Animation). Lets the
    /// shared handle types (<see cref="AnimTrack"/>, <see cref="AnimBlend"/>) drive either backend
    /// without a concrete reference. Not part of the public API.
    /// </summary>
    internal interface IAxieAnimBackend
    {
        bool IsPlaying { get; }
        bool IsActive(AnimTrack track);
        float GetTrackDuration(AnimTrack track);
        float GetTrackProgress(AnimTrack track);
        void SeekTrack(AnimTrack track, float normalizedTime);
        AnimTrack MakePendingTrack(AnimPlayParams data);
        void AdvanceOnComplete(bool fireCallbacks);
        bool IsBlendActive(AnimBlend blend);
        void StopBlend(AnimBlend blend);
    }

    /// <summary>Handle returned by <see cref="AxiePlayable.Play"/>. Use to queue follow-up clips.</summary>
    public sealed class AnimTrack
    {
        readonly IAxieAnimBackend _animator;
        readonly string _stateName;

        internal AnimPlayParams Params { get; }
        internal AnimPlayParams QueuedNext { get; private set; }
        internal string InternalStateName => _stateName;

        internal AnimTrack(IAxieAnimBackend animator, AnimPlayParams p, string stateName)
        {
            _animator = animator;
            Params = p;
            _stateName = stateName;
        }

        public string ClipName  => Params.ClipName;
        public string StateName => Params.StateName;
        public bool   Loop      => Params.Loop;
        public bool   IsPlaying => _animator.IsActive(this) && _animator.IsPlaying;
        public float  Duration  => _animator.GetTrackDuration(this);
        public float  Progress
        {
            get => _animator.GetTrackProgress(this);
            set => _animator.SeekTrack(this, value);
        }

        public AnimTrack Queue(AnimPlayParams data)
        {
            QueuedNext = data;
            return _animator.MakePendingTrack(data);
        }

        public AnimTrack Queue(string clipName, string stateName = null, bool loop = false, System.Action onComplete = null)
            => Queue(new AnimPlayParams { ClipName = clipName, StateName = stateName, Loop = loop, OnComplete = onComplete });

        public void Complete()
        {
            if (_animator.IsActive(this)) _animator.AdvanceOnComplete(fireCallbacks: true);
        }
    }

    /// <summary>
    /// Handle to a running 1D blend (e.g. Idle→Walk→Run). Drive it every frame by setting
    /// <see cref="Speed"/> to your locomotion parameter; the animator cross-weights the clips
    /// whose thresholds straddle that value. Starting any single-clip <see cref="AxiePlayable.Play"/>
    /// (or <see cref="AxiePlayable.Stop"/>) tears the blend down.
    /// </summary>
    public sealed class AnimBlend
    {
        internal struct Point
        {
            public string ClipName;
            public float  Threshold;
            public string StateName;   // registered state name (e.g. "Walk#loop")
            public float  Length;      // source clip length, seconds
        }

        readonly IAxieAnimBackend _animator;

        internal List<Point> Points  { get; }
        internal float[]      Weights { get; }   // reused scratch, one per point

        /// <summary>Blend parameter (same axis as the point thresholds). Set every frame.</summary>
        public float Speed { get; set; }

        /// <summary>True while this blend is the animator's active blend.</summary>
        public bool IsActive => _animator.IsBlendActive(this);

        internal AnimBlend(IAxieAnimBackend animator, List<Point> points)
        {
            _animator = animator;
            Points = points;
            Weights = new float[points.Count];
        }

        /// <summary>Tear this blend down; returns to the default clip if one is set, else stops.</summary>
        public void Stop() => _animator.StopBlend(this);
    }

    /// <summary>
    /// The official animator for Axie body clips — a Playables-API player accessed via
    /// <see cref="AxieCharacter3D.Playable"/>. Exposes the animation API through
    /// <see cref="AnimPlayParams"/>, <see cref="AnimTrack"/>, <see cref="AnimBlend"/>, and the members
    /// below (<see cref="Play(AnimPlayParams)"/>, <see cref="PlayBlend"/>, <see cref="SetDefault"/>, …).
    ///
    /// It drives the untouched, non-legacy clips through a <see cref="PlayableGraph"/> bound to a
    /// controller-less <see cref="Animator"/>, so clips stay Mecanim-compatible and animate correctly
    /// in player builds. Plain C# class — attach to nothing. Create one per character, call
    /// <see cref="Dispose"/> when the character is destroyed.
    /// </summary>
    public sealed class AxiePlayable : System.IDisposable, IAxieAnimBackend
    {
        enum ActiveKind { None, Single, Blend }

        readonly AxieCharacter3D _character;
        readonly Animator _animator;
        readonly bool _ownsAnimator;
        readonly AxieAnimatorUpdater _updater;
        // User-supplied clips registered via Register(); take precedence over the body's baked clips.
        readonly Dictionary<string, AnimationClip> _userClips = new(System.StringComparer.OrdinalIgnoreCase);

        PlayableGraph _graph;
        AnimationPlayableOutput _output;
        // 2-input crossfader: input[0] = ACTIVE source, input[1] = PREVIOUS source (only during a fade).
        AnimationMixerPlayable _rootMixer;

        // The currently-active source wired to _rootMixer input[0]. Exactly one kind is live at a time.
        ActiveKind _activeKind;
        AnimationClipPlayable _activeClip;    // valid when _activeKind == Single
        AnimationMixerPlayable _activeBlend;  // valid when _activeKind == Blend
        float _activeLength;                  // active single clip length, seconds

        // The outgoing source on _rootMixer input[1] while a crossfade is in flight.
        Playable _previous;
        bool _fading;
        float _fadeDuration;
        float _fadeElapsed;

        AnimTrack _current;
        AnimBlend _blend;
        string _defaultClipName;
        AnimBlend _defaultBlend;       // when non-null, this blend is the "return-to" state, not a clip
        float _timeScale = 1f;
        float _fade;
        bool _paused;

        public float     TimeScale       { get => _timeScale; set { _timeScale = value; ApplyLiveTimeScale(); } }
        public float     Fade            { get => _fade;      set => _fade = value; }
        /// <summary>
        /// Drive the active/default blend's parameter without holding the <see cref="AnimBlend"/> handle.
        /// Getter reads the current blend, else the default blend, else 0. See <see cref="SetSpeed"/>.
        /// </summary>
        public float     Speed           { get => (_blend ?? _defaultBlend)?.Speed ?? 0f; set => SetSpeed(value); }
        public AnimTrack CurrentTrack    => _current;
        public AnimBlend CurrentBlend    => _blend;
        public string    DefaultClipName => _defaultClipName;
        public bool      IsPlaying       => _current != null && _graph.IsValid() && _graph.IsPlaying();
        public bool      IsPaused        => _paused;

        public event System.Action<string> Completed;

        public AxiePlayable(AxieCharacter3D character)
        {
            if (character == null || character.Root == null)
                throw new System.ArgumentNullException(nameof(character));
            if (character.Root.GetComponent<Animation>() != null)
                throw new System.InvalidOperationException("A legacy Animation component is already present on this Axie's Root (only one animator may own the Root). AxiePlayable is the official animator — remove the legacy Animation component.");

            _character = character;

            // Reuse a controller-less Animator if one is already present; otherwise add (and own) one.
            var existing = character.Root.GetComponent<Animator>();
            if (existing != null)
            {
                if (existing.runtimeAnimatorController != null)
                    throw new System.InvalidOperationException("This Axie's Root already has an Animator with a runtimeAnimatorController; AxiePlayable needs an uncontrolled Animator. Remove the controller or use a different character.");
                _animator = existing;
                _ownsAnimator = false;
            }
            else
            {
                _animator = character.Root.AddComponent<Animator>();
                _ownsAnimator = true;
            }
            _animator.runtimeAnimatorController = null;
            // Spike-confirmed: a null Avatar samples the generic (non-legacy) clips onto the rig by
            // transform path in a player build. No avatar to build or destroy.
            _animator.avatar = null;

            _graph = PlayableGraph.Create($"AxiePlayable:{character.Root.name}");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _output = AnimationPlayableOutput.Create(_graph, "Axie", _animator);
            _rootMixer = AnimationMixerPlayable.Create(_graph, 2);
            _output.SetSourcePlayable(_rootMixer);
            _graph.Play();

            _updater = character.Root.AddComponent<AxieAnimatorUpdater>();
            _updater.OnUpdate = Tick;
        }

        /// <summary>
        /// Register a caller-supplied clip under <paramref name="name"/> so it can be driven through the
        /// normal <see cref="Play(string,string,bool,System.Action)"/> / <see cref="PlayBlend"/> /
        /// <see cref="SetDefault"/> APIs. User clips shadow a baked body clip of the same name.
        /// Re-registering a name replaces the previous clip.
        /// </summary>
        public void Register(string name, AnimationClip clip)
        {
            if (string.IsNullOrEmpty(name))
                throw new System.ArgumentException("Register requires a non-empty name.", nameof(name));
            if (clip == null)
                throw new System.ArgumentNullException(nameof(clip));

            if (!_userClips.ContainsKey(name) && _character.GetAnimClip(name) != null)
                Debug.LogWarning($"{nameof(AxiePlayable)}: Register('{name}') shadows a baked body clip of the same name.");

            _userClips[name] = clip;
        }

        /// <summary>Remove a clip previously added with <see cref="Register"/>. Returns false if it wasn't registered.</summary>
        public bool Unregister(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return _userClips.Remove(name);
        }

        /// <summary>True if <paramref name="name"/> resolves to a clip (registered or baked) on this body.</summary>
        public bool IsRegistered(string name) => ResolveSource(name) != null;

        /// <summary>Set the clip the animator returns to when one-shots finish. Clears any default blend.</summary>
        public void SetDefault(string clipName)
        {
            _defaultBlend = null;
            _defaultClipName = clipName;
        }

        public bool TrySetDefault(string clipName)
        {
            if (clipName == null) { _defaultClipName = null; _defaultBlend = null; return true; }
            if (ResolveSource(clipName) == null) return false;
            _defaultBlend = null;
            _defaultClipName = clipName;
            return true;
        }

        /// <summary>
        /// Set a 1D blend as the "return-to" state, so one-shot clips (cast, attack, …) auto-return to
        /// the blend when they finish — e.g.
        /// <c>SetDefaultBlend(new[] { (AnimNames.Idle, 0f), (AnimNames.Walk, 2f), (AnimNames.Run, 5f) })</c>.
        /// Returns the live handle whose <see cref="AnimBlend.Speed"/> you drive each frame; that same
        /// handle stays valid across cast/attack round-trips. When <paramref name="play"/> is true
        /// (default) the blend starts immediately. Returns null if no clip resolves on this body.
        /// </summary>
        public AnimBlend SetDefaultBlend(IReadOnlyList<(string clipName, float threshold)> points, float speed = 0f, bool play = true)
        {
            var blend = BuildBlend(points, speed);
            if (blend == null)
            {
                Debug.LogWarning($"{nameof(AxiePlayable)}: SetDefaultBlend found no valid clips; default unchanged.");
                return null;
            }

            _defaultClipName = null;
            _defaultBlend = blend;
            if (play) ArmBlend(blend);
            return blend;
        }

        /// <summary>
        /// Set the blend parameter on the active blend (applied live) and on the default blend so it
        /// carries across one-shot round-trips — so you can drive locomotion without keeping the
        /// <see cref="AnimBlend"/> handle. No-op if no blend is active or set as default.
        /// </summary>
        public void SetSpeed(float speed)
        {
            if (_blend != null) _blend.Speed = speed;
            if (_defaultBlend != null && _defaultBlend != _blend) _defaultBlend.Speed = speed;
        }

        public float GetDuration(string clipName) => ResolveSource(clipName)?.length ?? 0f;

        public bool TryGetDuration(string clipName, out float seconds)
        {
            var clip = ResolveSource(clipName);
            seconds = clip?.length ?? 0f;
            return clip != null;
        }

        public AnimTrack Play(AnimPlayParams data)
        {
            if (data == null || string.IsNullOrEmpty(data.ClipName))
                throw new System.ArgumentException("AnimPlayParams.ClipName is required.");

            var clip = ResolveSource(data.ClipName);
            if (clip == null)
            {
                Debug.LogWarning($"{nameof(AxiePlayable)}: clip '{data.ClipName}' not found on this body.");
                return null;
            }

            var fade = data.Fade >= 0f ? data.Fade : _fade;

            var cp = AnimationClipPlayable.Create(_graph, clip);
            // Loop: unbounded duration + manual wrap in Tick. One-shot: duration = length so the
            // playable clamps and holds its last frame (reproduces WrapMode.ClampForever).
            cp.SetDuration(data.Loop ? double.MaxValue : Mathf.Max(clip.length, 0.0001f));
            cp.SetSpeed(_paused ? 0f : _timeScale * data.TimeScale);

            double start = 0d;
            if (data.NormalizedStart > 0f) start = data.NormalizedStart * clip.length;
            else if (data.StartTime > 0f)  start = data.StartTime;
            if (start > 0d) cp.SetTime(start);

            SetActiveSource(cp, ActiveKind.Single, cp, default, fade);
            _blend = null;   // single-clip playback cancels any active blend (crossfaded out if fading)

            _activeLength = clip.length;
            _current = new AnimTrack(this, data, data.ClipName);
            return _current;
        }

        public AnimTrack Play(string clipName, string stateName = null, bool loop = false, System.Action onComplete = null)
            => Play(new AnimPlayParams { ClipName = clipName, StateName = stateName, Loop = loop, OnComplete = onComplete });

        /// <summary>
        /// Start a 1D blend across the given (clip, threshold) points — e.g.
        /// <c>PlayBlend(new[] { (AnimNames.Idle, 0f), (AnimNames.Walk, 2f), (AnimNames.Run, 5f) })</c>.
        /// Drive it by setting <see cref="AnimBlend.Speed"/> each frame. Clips are looped and
        /// time-scale synced so their cycles stay phase-locked while cross-fading. Missing clips are
        /// skipped with a warning. Returns null if no point resolves to a clip on this body.
        /// </summary>
        public AnimBlend PlayBlend(IReadOnlyList<(string clipName, float threshold)> points, float speed = 0f)
        {
            var blend = BuildBlend(points, speed);
            if (blend == null)
            {
                Debug.LogWarning($"{nameof(AxiePlayable)}: PlayBlend found no valid clips; nothing played.");
                return null;
            }
            ArmBlend(blend);
            return blend;
        }

        // Resolve the clips and produce an AnimBlend handle without starting it. Returns null if no
        // point resolves to a clip on this body.
        AnimBlend BuildBlend(IReadOnlyList<(string clipName, float threshold)> points, float speed)
        {
            if (points == null || points.Count == 0)
                throw new System.ArgumentException("A blend requires at least one (clipName, threshold) point.");

            var valid = new List<AnimBlend.Point>(points.Count);
            foreach (var (clipName, threshold) in points)
            {
                var clip = ResolveSource(clipName);
                if (clip == null)
                {
                    Debug.LogWarning($"{nameof(AxiePlayable)}: blend clip '{clipName}' not found on this body; skipping.");
                    continue;
                }
                valid.Add(new AnimBlend.Point
                {
                    ClipName  = clipName,
                    Threshold = threshold,
                    StateName = clipName,
                    Length    = clip.length,
                });
            }

            if (valid.Count == 0) return null;

            // Sort by threshold so weight interpolation walks adjacent points.
            valid.Sort((a, b) => a.Threshold.CompareTo(b.Threshold));
            return new AnimBlend(this, valid) { Speed = speed };
        }

        // Make the given blend the active source: build an N-input mixer of clip playables, wire it into
        // _rootMixer (crossfading in over the previous source when fade > 0 — this is the fade-in
        // envelope), then let Tick drive per-point weights/speeds each frame.
        void ArmBlend(AnimBlend blend, float fade = 0f, string fadeFromState = null)
        {
            _current = null;   // a blend cancels single-clip playback

            var bm = AnimationMixerPlayable.Create(_graph, blend.Points.Count);
            for (var i = 0; i < blend.Points.Count; i++)
            {
                var clip = ResolveSource(blend.Points[i].ClipName);
                if (clip == null) continue;   // BuildBlend already filtered these; guard anyway
                var cp = AnimationClipPlayable.Create(_graph, clip);
                cp.SetDuration(double.MaxValue);   // looped; wrapped manually in UpdateBlend
                cp.SetSpeed(0f);
                _graph.Connect(cp, 0, bm, i);
                bm.SetInputWeight(i, 0f);
            }

            _blend = blend;
            var crossfade = fade > 0f && _activeKind != ActiveKind.None;
            SetActiveSource(bm, ActiveKind.Blend, default, bm, crossfade ? fade : 0f);
            UpdateBlend();   // apply weights on the starting frame
        }

        public AnimTrack Queue(AnimPlayParams data)
        {
            if (_current != null && IsPlaying) return _current.Queue(data);
            return Play(data);
        }

        public AnimTrack Queue(string clipName, string stateName = null, bool loop = false, System.Action onComplete = null)
            => Queue(new AnimPlayParams { ClipName = clipName, StateName = stateName, Loop = loop, OnComplete = onComplete });

        public void Interrupt()
        {
            if (_current == null && _blend == null && _defaultClipName == null && _defaultBlend == null) return;

            var queued = _current?.QueuedNext;

            TeardownActive();
            _current = null;
            _blend = null;

            if (queued is { } next) Play(next);
            else                    PlayDefaultInternal();
        }

        public void Pause()
        {
            _paused = true;
            ApplyPauseSpeeds();
        }

        public void Resume()
        {
            _paused = false;
            if (_current != null && _activeKind == ActiveKind.Single && _activeClip.IsValid())
                _activeClip.SetSpeed(_timeScale * _current.Params.TimeScale);
            // A blend re-applies its per-point speeds on the next Tick via UpdateBlend.
        }

        public void Stop()
        {
            TeardownActive();
            _current = null;
            _blend = null;
        }

        public void Dispose()
        {
            _blend = null;
            _defaultBlend = null;
            if (_updater != null) { _updater.OnUpdate = null; Object.Destroy(_updater); }
            if (_graph.IsValid()) _graph.Destroy();   // destroys _rootMixer and every source playable
            if (_ownsAnimator && _animator != null) Object.Destroy(_animator);
            _userClips.Clear();
            _current = null;
            _activeKind = ActiveKind.None;
        }

        // --- IAxieAnimBackend (drives the shared AnimTrack / AnimBlend handles) ---

        bool IAxieAnimBackend.IsActive(AnimTrack track) => _current == track;

        bool IAxieAnimBackend.IsBlendActive(AnimBlend blend) => _blend != null && _blend == blend;

        void IAxieAnimBackend.StopBlend(AnimBlend blend) => StopBlend(blend);

        AnimTrack IAxieAnimBackend.MakePendingTrack(AnimPlayParams data) => MakePendingTrack(data);

        void IAxieAnimBackend.AdvanceOnComplete(bool fireCallbacks) => AdvanceOnComplete(fireCallbacks);

        float IAxieAnimBackend.GetTrackDuration(AnimTrack track) => ResolveSource(track.ClipName)?.length ?? 0f;

        float IAxieAnimBackend.GetTrackProgress(AnimTrack track)
        {
            if (_current != track || _activeKind != ActiveKind.Single || !_activeClip.IsValid()) return 0f;
            return _activeLength > 0f ? Mathf.Clamp01((float)_activeClip.GetTime() / _activeLength) : 0f;
        }

        void IAxieAnimBackend.SeekTrack(AnimTrack track, float normalizedTime)
        {
            if (_current != track || _activeKind != ActiveKind.Single || !_activeClip.IsValid()) return;
            if (_activeLength > 0f) _activeClip.SetTime(Mathf.Clamp01(normalizedTime) * _activeLength);
        }

        void StopBlend(AnimBlend blend)
        {
            if (_blend != blend) return;
            var wasDefault = blend == _defaultBlend;
            TeardownActive();
            _blend = null;
            _current = null;
            if (wasDefault) return;             // don't re-arm the blend we're stopping
            PlayDefaultInternal();              // returns false → already stopped
        }

        AnimTrack MakePendingTrack(AnimPlayParams data)
        {
            // Duration/Progress resolve by ClipName, so no active state is needed for a queued track.
            return new AnimTrack(this, data, data.ClipName);
        }

        void AdvanceOnComplete(bool fireCallbacks)
        {
            var finished = _current;
            _current = null;   // clear before callbacks so a Play() inside onComplete wins

            if (fireCallbacks)
            {
                finished?.Params.OnComplete?.Invoke();
                if (_current == null) Completed?.Invoke(finished?.ClipName);
            }

            if (_current != null) return;   // callback started a new clip; respect it

            if (finished?.QueuedNext is { } next)       Play(next);
            else                                        PlayDefaultInternal(finished);
            // PlayDefaultInternal false (no default) -> hold last frame (one-shot clamps at its duration)
        }

        // Return to whatever default is configured: a blend takes precedence over a clip. When a
        // one-shot just finished it is still the active source (clamped at its last frame); starting the
        // default with fade > 0 crossfades over it via _rootMixer. Returns false if neither a default
        // blend nor clip is set (caller then holds the last frame).
        bool PlayDefaultInternal(AnimTrack from = null)
        {
            var fade = _fade;
            if (_defaultBlend != null)
            {
                ArmBlend(_defaultBlend, fade);
                return true;
            }
            if (_defaultClipName != null)
            {
                Play(new AnimPlayParams { ClipName = _defaultClipName, Loop = true, Fade = fade });
                return true;
            }
            return false;
        }

        // --- private helpers ---

        void Tick()
        {
            // 1. Advance any in-flight crossfade on _rootMixer; destroy the previous source when done.
            if (_fading)
            {
                if (!_paused) _fadeElapsed += Time.deltaTime;
                var t = _fadeDuration > 0f ? Mathf.Clamp01(_fadeElapsed / _fadeDuration) : 1f;
                _rootMixer.SetInputWeight(0, t);
                if (_previous.IsValid()) _rootMixer.SetInputWeight(1, 1f - t);
                if (t >= 1f) FinalizePreviousImmediate();
            }

            // 2. A blend drives its own per-point weights/speeds each frame.
            if (_blend != null) { UpdateBlend(); return; }

            // 3. Single clip: manual loop wrap / one-shot completion.
            if (_current == null) return;
            if (_activeKind != ActiveKind.Single || !_activeClip.IsValid()) return;
            var length = _activeLength;
            if (length <= 0f) return;
            var time = (float)_activeClip.GetTime();

            if (_current.Loop)
            {
                if (time >= length) _activeClip.SetTime(Mathf.Repeat(time, length));
                return;
            }

            if (_paused) return;
            if (time >= length)
            {
                _activeClip.SetTime(length);       // clamp last frame (ClampForever)
                AdvanceOnComplete(fireCallbacks: true);
            }
        }

        // Fill blend.Weights from blend.Speed (piecewise-linear; only the two points straddling
        // Speed carry weight) and return the array.
        float[] ComputeWeights(AnimBlend blend)
        {
            var pts = blend.Points;
            var n   = pts.Count;
            var w   = blend.Weights;
            for (var i = 0; i < n; i++) w[i] = 0f;

            var speed = blend.Speed;
            if (n == 1 || speed <= pts[0].Threshold)
            {
                w[0] = 1f;
            }
            else if (speed >= pts[n - 1].Threshold)
            {
                w[n - 1] = 1f;
            }
            else
            {
                for (var i = 0; i < n - 1; i++)
                {
                    if (speed >= pts[i].Threshold && speed <= pts[i + 1].Threshold)
                    {
                        var span = pts[i + 1].Threshold - pts[i].Threshold;
                        var frac = span > 0f ? (speed - pts[i].Threshold) / span : 0f;
                        w[i]     = 1f - frac;
                        w[i + 1] = frac;
                        break;
                    }
                }
            }
            return w;
        }

        // Recompute per-clip weights and time-scales from the blend's Speed. Called every tick while a
        // blend is active. Only the two clips straddling Speed carry weight (piecewise-linear); all
        // active clips are phase-locked (advance at Length/refLength) so their cycles stay aligned.
        void UpdateBlend()
        {
            if (!_activeBlend.IsValid()) return;
            var pts = _blend.Points;
            var n = pts.Count;
            var w = ComputeWeights(_blend);

            // Blended cycle length: every active state advances at 1/refLength in normalized time, so
            // they stay phase-locked (no foot-slide) while their weights shift.
            var refLength = 0f;
            for (var i = 0; i < n; i++) if (w[i] > 0f) refLength += w[i] * pts[i].Length;
            if (refLength <= 0f) refLength = pts[0].Length > 0f ? pts[0].Length : 1f;

            // Phase (normalized) of an already-weighted clip, to align one that is only now gaining weight.
            var phase = 0f;
            var havePhase = false;
            for (var i = 0; i < n; i++)
            {
                var cp = (AnimationClipPlayable)_activeBlend.GetInput(i);
                if (cp.IsValid() && _activeBlend.GetInputWeight(i) > 0f && w[i] > 0f && pts[i].Length > 0f)
                {
                    phase = Mathf.Repeat((float)cp.GetTime(), pts[i].Length) / pts[i].Length;
                    havePhase = true;
                    break;
                }
            }

            for (var i = 0; i < n; i++)
            {
                var cp = (AnimationClipPlayable)_activeBlend.GetInput(i);
                if (!cp.IsValid()) continue;
                if (w[i] > 0f)
                {
                    if (_activeBlend.GetInputWeight(i) <= 0f)   // newly active → align to the shared phase
                        cp.SetTime(havePhase ? phase * pts[i].Length : 0d);

                    _activeBlend.SetInputWeight(i, w[i]);
                    cp.SetSpeed(_paused ? 0f : (pts[i].Length > 0f ? pts[i].Length / refLength * _timeScale : _timeScale));

                    // Manual loop wrap (duration is unbounded); keeps the clip time near [0,length).
                    var t = (float)cp.GetTime();
                    if (pts[i].Length > 0f && t >= pts[i].Length) cp.SetTime(Mathf.Repeat(t, pts[i].Length));
                }
                else
                {
                    _activeBlend.SetInputWeight(i, 0f);
                    cp.SetSpeed(0f);   // freeze zero-weight clips so they re-align cleanly when re-armed
                }
            }
        }

        // Wire newRoot as the active source on _rootMixer input[0]. When fade > 0 and a source is
        // already active, that source is moved to input[1] and cross-faded out; otherwise it is
        // destroyed and newRoot snaps in at full weight.
        void SetActiveSource(Playable newRoot, ActiveKind kind, AnimationClipPlayable clip, AnimationMixerPlayable blend, float fade)
        {
            FinalizePreviousImmediate();   // clear any older in-flight fade first

            var hasOld = _activeKind != ActiveKind.None;
            var oldRoot = hasOld ? ActiveRoot() : Playable.Null;

            if (fade > 0f)
            {
                if (hasOld)
                {
                    _graph.Disconnect(_rootMixer, 0);
                    _graph.Connect(oldRoot, 0, _rootMixer, 1);
                    _rootMixer.SetInputWeight(1, 1f);
                    _previous = oldRoot;
                }
                _graph.Connect(newRoot, 0, _rootMixer, 0);
                _rootMixer.SetInputWeight(0, 0f);
                _fading = true;
                _fadeDuration = fade;
                _fadeElapsed = 0f;
            }
            else
            {
                if (hasOld)
                {
                    _graph.Disconnect(_rootMixer, 0);
                    if (oldRoot.IsValid()) _graph.DestroySubgraph(oldRoot);
                }
                _graph.Connect(newRoot, 0, _rootMixer, 0);
                _rootMixer.SetInputWeight(0, 1f);
                _fading = false;
            }

            _activeKind = kind;
            _activeClip = clip;
            _activeBlend = blend;
        }

        // Destroy the outgoing crossfade source (if any) and end the fade.
        void FinalizePreviousImmediate()
        {
            if (_previous.IsValid())
            {
                _graph.Disconnect(_rootMixer, 1);
                _graph.DestroySubgraph(_previous);
                _previous = Playable.Null;
            }
            _fading = false;
            _fadeElapsed = 0f;
            _fadeDuration = 0f;
        }

        // Destroy the active and previous sources and clear the active slot (does not touch _current/_blend).
        void TeardownActive()
        {
            FinalizePreviousImmediate();
            if (_activeKind != ActiveKind.None)
            {
                var root = ActiveRoot();
                _graph.Disconnect(_rootMixer, 0);
                if (root.IsValid()) _graph.DestroySubgraph(root);
                _activeKind = ActiveKind.None;
                _activeClip = default;
                _activeBlend = default;
            }
        }

        Playable ActiveRoot()
        {
            switch (_activeKind)
            {
                case ActiveKind.Single: return _activeClip;
                case ActiveKind.Blend:  return _activeBlend;
                default:                return Playable.Null;
            }
        }

        void ApplyPauseSpeeds()
        {
            if (_activeKind == ActiveKind.Single && _activeClip.IsValid())
            {
                _activeClip.SetSpeed(0f);
            }
            else if (_activeKind == ActiveKind.Blend && _activeBlend.IsValid())
            {
                for (var i = 0; i < _activeBlend.GetInputCount(); i++)
                {
                    var cp = (AnimationClipPlayable)_activeBlend.GetInput(i);
                    if (cp.IsValid()) cp.SetSpeed(0f);
                }
            }
        }

        void ApplyLiveTimeScale()
        {
            if (_current == null || _paused) return;
            if (_activeKind == ActiveKind.Single && _activeClip.IsValid())
                _activeClip.SetSpeed(_timeScale * _current.Params.TimeScale);
            // A blend re-applies its per-point speeds on the next Tick via UpdateBlend.
        }

        // A registered user clip shadows the baked body clip of the same name.
        AnimationClip ResolveSource(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return null;
            if (_userClips.TryGetValue(clipName, out var user) && user != null) return user;
            return _character.GetAnimClip(clipName);
        }
    }
}
