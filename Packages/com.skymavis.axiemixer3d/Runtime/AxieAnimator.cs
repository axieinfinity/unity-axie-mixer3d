using System.Collections.Generic;
using SkyMavis.AxieMixer3D.Internal;
using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    /// <summary>
    /// <b>Reference implementation only — not the official animator. Use <see cref="AxiePlayable"/>
    /// (via <see cref="AxieCharacter3D.Playable"/>) instead.</b>
    /// <para>
    /// Legacy-<see cref="Animation"/>-based player for Axie body clips. It is retained purely as a
    /// readable reference for how the shared animation handles (<see cref="AnimTrack"/>,
    /// <see cref="AnimBlend"/>, <see cref="AnimPlayParams"/>) map onto a backend — nothing in the
    /// package instantiates it. Legacy <see cref="Animation"/> playback silently does nothing in
    /// player builds (only in the Editor), which is why <see cref="AxiePlayable"/> superseded it.
    /// </para>
    /// Plain C# class — attach to nothing. Create one per character, call <see cref="Dispose"/> when
    /// the character is destroyed.
    /// </summary>
    public sealed class AxieAnimator : System.IDisposable, IAxieAnimBackend
    {
        readonly AxieCharacter3D _character;
        readonly Animation _animation;
        readonly AxieAnimatorUpdater _updater;
        readonly Dictionary<(string name, bool loop), AnimationClip> _cloneCache = new();
        readonly List<AnimationClip> _ownedClones = new();
        // User-supplied clips registered via Register(); take precedence over the body's baked clips.
        readonly Dictionary<string, AnimationClip> _userClips = new(System.StringComparer.OrdinalIgnoreCase);

        AnimTrack _current;
        AnimBlend _blend;
        string _defaultClipName;
        AnimBlend _defaultBlend;       // when non-null, this blend is the "return-to" state, not a clip
        float _timeScale = 1f;
        float _fade;
        bool _paused;
        // >0 while a blend is fading in over a finishing one-shot: UpdateBlend ramps every active
        // blend clip's weight from 0 to target over this window so the pose eases in, not snaps.
        float _blendFadeDuration;
        float _blendFadeElapsed;

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
        public bool      IsPlaying       => _current != null && _animation != null && _animation.isPlaying;
        public bool      IsPaused        => _paused;

        public event System.Action<string> Completed;

        public AxieAnimator(AxieCharacter3D character)
        {
            if (character == null || character.Root == null)
                throw new System.ArgumentNullException(nameof(character));
            if (character.Root.GetComponent<Animation>() != null)
                throw new System.InvalidOperationException("An Animation component is already present on this character's Root. Only one AxieAnimator may own it.");

            _character = character;
            _animation = character.Root.AddComponent<Animation>();
            _animation.playAutomatically = false;
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

            // Drop any cached clones for this name so the next Play/Blend picks up the new clip.
            InvalidateClones(name);

            if (!_userClips.ContainsKey(name) && _character.GetAnimClip(name) != null)
                Debug.LogWarning($"{nameof(AxieAnimator)}: Register('{name}') shadows a baked body clip of the same name.");

            _userClips[name] = clip;
        }

        /// <summary>Remove a clip previously added with <see cref="Register"/>. Returns false if it wasn't registered.</summary>
        public bool Unregister(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            InvalidateClones(name);
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
                Debug.LogWarning($"{nameof(AxieAnimator)}: SetDefaultBlend found no valid clips; default unchanged.");
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

        public float GetDuration(string clipName) => EnsureClipRegistered(clipName, false)?.length ?? 0f;

        public bool TryGetDuration(string clipName, out float seconds)
        {
            var clip = EnsureClipRegistered(clipName, false);
            seconds = clip?.length ?? 0f;
            return clip != null;
        }

        public AnimTrack Play(AnimPlayParams data)
        {
            if (data == null || string.IsNullOrEmpty(data.ClipName))
                throw new System.ArgumentException("AnimPlayParams.ClipName is required.");

            var clone = EnsureClipRegistered(data.ClipName, data.Loop);
            if (clone == null)
            {
                Debug.LogWarning($"{nameof(AxieAnimator)}: clip '{data.ClipName}' not found on this body.");
                return null;
            }

            var stateName = clone.name;
            var fade = data.Fade >= 0f ? data.Fade : _fade;

            if (fade > 0f)
            {
                // Soft-cancel any active blend: stop driving its weights but leave its states
                // playing so CrossFade fades them out smoothly instead of snapping them off.
                _blend = null;
                _blendFadeDuration = 0f;
                _animation.CrossFade(stateName, fade);
            }
            else
            {
                TeardownBlend();   // single-clip playback cancels any active blend
                _animation.Play(stateName);
            }

            var state = _animation[stateName];
            if (state != null)
            {
                state.speed = _timeScale * data.TimeScale;
                if (data.NormalizedStart > 0f) state.normalizedTime = data.NormalizedStart;
                else if (data.StartTime > 0f)  state.time = data.StartTime;
            }

            _current = new AnimTrack(this, data, stateName);
            if (_paused) ApplyPause();
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
                Debug.LogWarning($"{nameof(AxieAnimator)}: PlayBlend found no valid clips; nothing played.");
                return null;
            }
            ArmBlend(blend);
            return blend;
        }

        // Resolve/register the clips and produce an AnimBlend handle without starting it. Returns null
        // if no point resolves to a clip on this body.
        AnimBlend BuildBlend(IReadOnlyList<(string clipName, float threshold)> points, float speed)
        {
            if (points == null || points.Count == 0)
                throw new System.ArgumentException("A blend requires at least one (clipName, threshold) point.");

            var valid = new List<AnimBlend.Point>(points.Count);
            foreach (var (clipName, threshold) in points)
            {
                var clone = EnsureClipRegistered(clipName, loop: true);
                if (clone == null)
                {
                    Debug.LogWarning($"{nameof(AxieAnimator)}: blend clip '{clipName}' not found on this body; skipping.");
                    continue;
                }
                valid.Add(new AnimBlend.Point
                {
                    ClipName  = clipName,
                    Threshold = threshold,
                    StateName = clone.name,
                    Length    = clone.length,
                });
            }

            if (valid.Count == 0) return null;

            // Sort by threshold so weight interpolation walks adjacent points.
            valid.Sort((a, b) => a.Threshold.CompareTo(b.Threshold));
            return new AnimBlend(this, valid) { Speed = speed };
        }

        // Make the given blend the active state: cancel single-clip playback / any prior blend, wire up
        // the states, kick the Animation component, then let the tick drive weights each frame.
        // When fade > 0 and fadeFromState names a still-playing one-shot, the blend crossfades in over
        // that clip instead of snapping (UpdateBlend ramps the weights via _blendFadeDuration).
        void ArmBlend(AnimBlend blend, float fade = 0f, string fadeFromState = null)
        {
            // Cancel a *different* active blend cleanly. When crossfading we keep the outgoing
            // one-shot state alive so CrossFade can fade it out under the incoming blend.
            if (_blend != null && _blend != blend) TeardownBlend();
            _current = null;

            foreach (var p in blend.Points)
            {
                var st = _animation[p.StateName];
                if (st == null) continue;
                st.layer          = 0;
                st.blendMode      = AnimationBlendMode.Blend;
                st.weight         = 0f;
                st.enabled        = false;
                st.normalizedTime = 0f;
            }

            _blend = blend;

            var crossfade = fade > 0f && fadeFromState != null && _animation[fadeFromState] != null;
            if (crossfade)
            {
                // CrossFade the dominant blend clip in — this fades the outgoing one-shot out and,
                // by formally starting a blend state, keeps the Animation component playing. The
                // actual per-clip weights are then owned by UpdateBlend, ramped over _blendFadeDuration.
                ComputeWeights(blend);
                _animation.CrossFade(DominantStateName(blend), fade);
                _blendFadeDuration = fade;
                _blendFadeElapsed  = 0f;
            }
            else
            {
                _animation.Play(blend.Points[0].StateName);
                _blendFadeDuration = 0f;
            }

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

            _animation.Stop();
            _current = null;
            TeardownBlend();

            if (queued is { } next) Play(next);
            else                    PlayDefaultInternal();
        }

        public void Pause()
        {
            _paused = true;
            ApplyPause();
        }

        public void Resume()
        {
            _paused = false;
            if (_current != null)
            {
                var state = _animation[CurrentStateName()];
                if (state != null) state.speed = _timeScale * _current.Params.TimeScale;
            }
        }

        public void Stop()
        {
            TeardownBlend();
            _animation.Stop();
            _current = null;
        }

        public void Dispose()
        {
            _blend = null;
            _defaultBlend = null;
            if (_updater != null) { _updater.OnUpdate = null; Object.Destroy(_updater); }
            foreach (var clone in _ownedClones) if (clone != null) Object.Destroy(clone);
            _ownedClones.Clear();
            _cloneCache.Clear();
            _userClips.Clear();
            if (_animation != null) Object.Destroy(_animation);
            _current = null;
        }

        // --- internals used by AnimTrack ---

        internal bool IsActive(AnimTrack track) => _current == track;

        internal bool IsBlendActive(AnimBlend blend) => _blend != null && _blend == blend;

        internal void StopBlend(AnimBlend blend)
        {
            if (_blend != blend) return;
            var wasDefault = blend == _defaultBlend;
            TeardownBlend();
            if (wasDefault) { _animation.Stop(); return; }   // don't re-arm the blend we're stopping
            if (!PlayDefaultInternal()) _animation.Stop();
        }

        internal float StateDuration(string stateName)
        {
            var state = _animation?[stateName];
            return state?.clip != null ? state.clip.length : 0f;
        }

        internal float StateProgress(string stateName)
        {
            var state = _animation?[stateName];
            return state != null ? Mathf.Clamp01(state.normalizedTime) : 0f;
        }

        internal void SeekState(string stateName, float normalizedTime)
        {
            var state = _animation?[stateName];
            if (state != null) state.normalizedTime = Mathf.Clamp01(normalizedTime);
        }

        float IAxieAnimBackend.GetTrackDuration(AnimTrack track) => StateDuration(track.InternalStateName);
        float IAxieAnimBackend.GetTrackProgress(AnimTrack track) => StateProgress(track.InternalStateName);
        void IAxieAnimBackend.SeekTrack(AnimTrack track, float normalizedTime) => SeekState(track.InternalStateName, normalizedTime);

        // Internal members cannot implicitly implement an interface (C# requires public for implicit implementation).
        // These explicit implementations bridge the interface to the existing internal methods without changing their accessibility.
        bool IAxieAnimBackend.IsActive(AnimTrack track) => IsActive(track);
        AnimTrack IAxieAnimBackend.MakePendingTrack(AnimPlayParams data) => MakePendingTrack(data);
        void IAxieAnimBackend.AdvanceOnComplete(bool fireCallbacks) => AdvanceOnComplete(fireCallbacks);
        bool IAxieAnimBackend.IsBlendActive(AnimBlend blend) => IsBlendActive(blend);
        void IAxieAnimBackend.StopBlend(AnimBlend blend) => StopBlend(blend);

        internal AnimTrack MakePendingTrack(AnimPlayParams data)
        {
            // Register the clip now so the caller gets a valid Duration etc. immediately.
            var clip = EnsureClipRegistered(data.ClipName, data.Loop);
            var stateName = clip?.name ?? data.ClipName;
            return new AnimTrack(this, data, stateName);
        }

        internal void AdvanceOnComplete(bool fireCallbacks)
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
            // PlayDefaultInternal false (no default) -> hold last frame (ClampForever)
        }

        // Return to whatever default is configured: a blend takes precedence over a clip. When
        // <paramref name="from"/> is the just-finished one-shot (still holding its last frame), the
        // return crossfades over the animator's Fade instead of snapping. Returns false if neither a
        // default blend nor clip is set (caller then holds the last frame).
        bool PlayDefaultInternal(AnimTrack from = null)
        {
            var fade = _fade;
            string fromState = null;
            if (from != null)
            {
                var clip = EnsureClipRegistered(from.ClipName, from.Loop);
                fromState = clip?.name;
            }

            if (_defaultBlend != null)
            {
                ArmBlend(_defaultBlend, fade, fromState);
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
            if (_blend != null) { UpdateBlend(); return; }   // blend drives its own weights each frame
            if (_current == null || _paused || _current.Loop) return;
            var stateName = CurrentStateName();
            if (stateName == null) return;
            var state = _animation[stateName];
            if (state == null) return;
            if (state.normalizedTime >= 1f) AdvanceOnComplete(fireCallbacks: true);
        }

        string CurrentStateName()
        {
            if (_current == null) return null;
            var clip = EnsureClipRegistered(_current.ClipName, _current.Loop);
            return clip?.name;
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

        // State carrying the greatest current weight (used as the CrossFade lead when arming a blend).
        // Assumes ComputeWeights ran just before.
        string DominantStateName(AnimBlend blend)
        {
            var w    = blend.Weights;
            var best = 0;
            for (var i = 1; i < blend.Points.Count; i++) if (w[i] > w[best]) best = i;
            return blend.Points[best].StateName;
        }

        // Recompute per-clip weights and time-scales from the blend's Speed. Called every tick while
        // a blend is active. Only the two clips straddling Speed carry weight (piecewise-linear).
        void UpdateBlend()
        {
            var pts = _blend.Points;
            var n = pts.Count;
            var w = ComputeWeights(_blend);

            // Crossfade-in envelope: while arming over a finishing one-shot, ramp every blend clip's
            // weight from 0 to its target over _blendFadeDuration so the pose eases in, not snaps.
            var fadeIn = 1f;
            if (_blendFadeDuration > 0f)
            {
                if (!_paused) _blendFadeElapsed += Time.deltaTime;
                fadeIn = Mathf.Clamp01(_blendFadeElapsed / _blendFadeDuration);
                if (fadeIn >= 1f) _blendFadeDuration = 0f;
            }

            // Blended cycle length: every active state advances at 1/refLength in normalized time,
            // so they stay phase-locked (no foot-slide) while their weights shift.
            var refLength = 0f;
            for (var i = 0; i < n; i++) if (w[i] > 0f) refLength += w[i] * pts[i].Length;
            if (refLength <= 0f) refLength = pts[0].Length > 0f ? pts[0].Length : 1f;

            // Phase of an already-active state, to align a state that is only now gaining weight.
            var phase = 0f;
            var havePhase = false;
            for (var i = 0; i < n; i++)
            {
                var st = _animation[pts[i].StateName];
                if (st != null && st.enabled && w[i] > 0f) { phase = st.normalizedTime; havePhase = true; break; }
            }

            for (var i = 0; i < n; i++)
            {
                var st = _animation[pts[i].StateName];
                if (st == null) continue;
                if (w[i] > 0f)
                {
                    if (!st.enabled)
                    {
                        st.enabled = true;
                        st.normalizedTime = havePhase ? phase : 0f;
                    }
                    st.weight = w[i] * fadeIn;
                    st.speed  = _paused ? 0f : (pts[i].Length > 0f ? pts[i].Length / refLength * _timeScale : _timeScale);
                }
                else
                {
                    st.enabled = false;
                    st.weight  = 0f;
                }
            }
        }

        void TeardownBlend()
        {
            _blendFadeDuration = 0f;
            if (_blend == null) return;
            foreach (var p in _blend.Points)
            {
                var st = _animation?[p.StateName];
                if (st != null) { st.enabled = false; st.weight = 0f; st.speed = 1f; }
            }
            _blend = null;
        }

        AnimationClip EnsureClipRegistered(string clipName, bool loop)
        {
            if (string.IsNullOrEmpty(clipName)) return null;
            var key = (clipName, loop);
            if (_cloneCache.TryGetValue(key, out var cached)) return cached;

            var source = ResolveSource(clipName);
            if (source == null) return null;

            var clone = Object.Instantiate(source);
            clone.legacy = true;
            clone.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            clone.name = $"{clipName}{(loop ? "#loop" : "#once")}";

            _animation.AddClip(clone, clone.name);
            _cloneCache[key] = clone;
            _ownedClones.Add(clone);
            return clone;
        }

        // A registered user clip shadows the baked body clip of the same name.
        AnimationClip ResolveSource(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return null;
            if (_userClips.TryGetValue(clipName, out var user) && user != null) return user;
            return _character.GetAnimClip(clipName);
        }

        // Drop cached clones (both loop modes) for a name so a re-registered clip is picked up next Play.
        void InvalidateClones(string name)
        {
            RemoveClone((name, false));
            RemoveClone((name, true));
        }

        void RemoveClone((string name, bool loop) key)
        {
            if (!_cloneCache.TryGetValue(key, out var clone)) return;
            _cloneCache.Remove(key);
            if (clone == null) return;
            if (_animation != null) _animation.RemoveClip(clone.name);
            _ownedClones.Remove(clone);
            Object.Destroy(clone);
        }

        void ApplyPause()
        {
            var stateName = CurrentStateName();
            if (stateName == null) return;
            var state = _animation[stateName];
            if (state != null) state.speed = 0f;
        }

        void ApplyLiveTimeScale()
        {
            if (_current == null || _paused) return;
            var stateName = CurrentStateName();
            if (stateName == null) return;
            var state = _animation[stateName];
            if (state != null) state.speed = _timeScale * _current.Params.TimeScale;
        }
    }
}
