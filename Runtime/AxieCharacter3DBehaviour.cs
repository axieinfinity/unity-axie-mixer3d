using System.Linq;
using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    public class AxieCharacter3DBehaviour : MonoBehaviour
    {
        public string axieGenes;
        public AxieDescriptor axieDescriptor;
        public AxieAvatarRenderParams[] avatarRenderParams;

        public AxieCharacter3D Character { get; private set; }
        public RenderTexture[] Avatars { get; private set; } = new RenderTexture[0];

        void Start()
        {
            if (Character == null) Rebuild();
        }

        void OnDestroy()
        {
            Cleanup();
        }

        [System.Obsolete("Refresh() is obsolete. Use Rebuild() instead.")]
        public void Refresh()
        {
            Rebuild();
        }

        public void Rebuild()
        {
            Cleanup();

            if (!string.IsNullOrWhiteSpace(axieGenes)) axieDescriptor = AxieDescriptor.FromGenes(axieGenes);

            Character = AxieFactory.Default.CreateCharacter(axieDescriptor);
            Character.Root.transform.SetParent(transform, false);

            Avatars = avatarRenderParams.Select(renderParams =>
            {
                var avatar = new RenderTexture(1, 1, 16, RenderTextureFormat.ARGB32);
                Character.RenderAvatar(avatar, renderParams);
                return avatar;
            }).ToArray();
        }

        void Cleanup()
        {
            Character?.Dispose();
            Character = null;

            foreach (var avatar in Avatars)
            {
                Destroy(avatar);
            }
        }
    }
}
