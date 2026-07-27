using UnityEngine;

namespace SkyMavis.AxieMixer3D
{
    /// <summary>
    /// Parameters used to configure how an Axie avatar is rendered into a texture.
    /// Includes output resolution, model orientation, and orthographic camera settings.
    /// </summary>
    [System.Serializable]
    public class AxieAvatarRenderParams
    {
        /// <summary>
        /// Output texture width in pixels.
        /// </summary>
        [Min(1), Delayed]
        public int width = 128;

        /// <summary>
        /// Output texture height in pixels.
        /// </summary>
        [Min(1), Delayed]
        public int height = 128;

        /// <summary>
        /// Model heading in world-space degrees.  
        /// Controls the facing direction and affects how lighting is applied.
        /// </summary>
        [Range(0f, 360f)]
        public float modelHeading = 180f;

        /// <summary>
        /// The orthographic camera’s focal point in the model’s local space.
        /// </summary>
        public Vector3 viewCenter = new(0f, 0.75f, 0f);

        /// <summary>
        /// The orthographic camera’s viewing direction in the model’s local space.
        /// </summary>
        public Vector3 viewDirection = -Vector3.one;
    }
}
