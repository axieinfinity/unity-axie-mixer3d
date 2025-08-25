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

        RenderTexture _realtimeAvatar;
        AxieAvatarRenderParams _renderParams;

        void Start()
        {
            StartCoroutine(DelayedStart());
        }

        void OnDestroy()
        {
            // Make sure to release resources when no longer needed
            if (_realtimeAvatar != null)
            {
                Destroy(_realtimeAvatar);
            }
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

            var animationClip = characterBehaviour.Character.GetLiteAnimationClip("Default.Run");
            animationClip = Instantiate(animationClip);
            animationClip.legacy = true;
            animationClip.wrapMode = WrapMode.Loop;

            var animation = characterBehaviour.Character.Root.AddComponent<Animation>();
            animation.AddClip(animationClip, "Run");
            animation.Play("Run");

            _realtimeAvatar = new RenderTexture(1, 1, 16, RenderTextureFormat.ARGB32);
            _renderParams = new AxieAvatarRenderParams
            {
                width = 512,
                height = 512,
                viewDirection = Quaternion.Euler(20f, 200f, 0f) * Vector3.forward,
            };

            avatarImage0.texture = characterBehaviour.Avatars[0];
            avatarImage1.texture = characterBehaviour.Avatars[1];
            renderImage.texture = _realtimeAvatar;
        }

        void Update()
        {
            if (_realtimeAvatar != null)
            {
                characterBehaviour.Character.RenderAvatar(_realtimeAvatar, _renderParams);
            }
        }
    }
}
