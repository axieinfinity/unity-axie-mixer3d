using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SkyMavis.AxieMixer3D.Samples
{
    public class AxieAvatars : MonoBehaviour
    {
        public AxieCharacter3DBehaviour characterBehaviour;
        public RawImage avatarImage0;
        public RawImage avatarImage1;
        public RawImage renderImage;

        // Avatar rendering is an addon: create one AxieAvatarRenderer for the character and reuse it
        // for both the one-off static snapshots and the per-frame realtime avatar.
        AxieAvatarRenderer _avatarRenderer;
        RenderTexture _staticAvatar0;
        RenderTexture _staticAvatar1;
        RenderTexture _realtimeAvatar;
        AxieAvatarRenderParams _realtimeParams;

        void Start()
        {
            StartCoroutine(DelayedStart());
        }

        void OnDestroy()
        {
            _avatarRenderer?.Dispose();
            if (_staticAvatar0 != null) Destroy(_staticAvatar0);
            if (_staticAvatar1 != null) Destroy(_staticAvatar1);
            if (_realtimeAvatar != null) Destroy(_realtimeAvatar);
        }

        IEnumerator DelayedStart()
        {
            // Wait for the first few frames for the graphic device to get ready in the editor
            // This should not be a problem in a standalone build
            yield return null;
            yield return null;
            yield return null;
            characterBehaviour.enabled = true;
            yield return null;

            characterBehaviour.Playable.Play(AnimNames.Run, loop: true);

            _avatarRenderer = new AxieAvatarRenderer(characterBehaviour.Character);

            // Two static snapshots from different angles, rendered once.
            _staticAvatar0 = new RenderTexture(1, 1, 16, RenderTextureFormat.ARGB32);
            _staticAvatar1 = new RenderTexture(1, 1, 16, RenderTextureFormat.ARGB32);
            _avatarRenderer.Render(_staticAvatar0, new AxieAvatarRenderParams
            {
                width = 512,
                height = 512,
                viewDirection = Vector3.forward,
            });
            _avatarRenderer.Render(_staticAvatar1, new AxieAvatarRenderParams
            {
                width = 512,
                height = 512,
                modelHeading = 180f,
                viewDirection = Vector3.forward,
            });

            // Realtime avatar re-rendered every frame so it plays back the running animation.
            _realtimeAvatar = new RenderTexture(1, 1, 16, RenderTextureFormat.ARGB32);
            _realtimeParams = new AxieAvatarRenderParams
            {
                width = 512,
                height = 512,
                viewDirection = Quaternion.Euler(20f, 200f, 0f) * Vector3.forward,
            };

            avatarImage0.texture = _staticAvatar0;
            avatarImage1.texture = _staticAvatar1;
            renderImage.texture = _realtimeAvatar;
        }

        void Update()
        {
            if (_avatarRenderer != null && _realtimeAvatar != null)
            {
                _avatarRenderer.Render(_realtimeAvatar, _realtimeParams);
            }
        }
    }
}
