using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SkyMavis.AxieMixer3D
{
    public class OutlinePostProcessRendererFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Header("Outline Appearance")]
            public Color outlineColor = Color.black;
            [Range(1f, 10f)] public int thickness = 1;

            [Header("Depth Settings")]
            [Min(0f)] public float depthScale = 50f;
            [Min(0f)] public float depthBias = 50f;

            [Header("Normal Settings")]
            [Min(0f)] public float normalScale = .7f;
            [Min(0f)] public float normalBias = 10f;

            [Header("Render Settings")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        const string ShaderName = "Axie Mixer 3D/Outline/PostProcess";

        static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int DepthScaleId = Shader.PropertyToID("_DepthScale");
        static readonly int DepthBiasId = Shader.PropertyToID("_DepthBias");
        static readonly int NormalScaleId = Shader.PropertyToID("_NormalScale");
        static readonly int NormalBiasId = Shader.PropertyToID("_NormalBias");

        [SerializeField] Settings _settings = new();

        Material _material;
        OutlinePass _outlinePass;

        public override void Create()
        {
            _material = CoreUtils.CreateEngineMaterial(ShaderName);
            _outlinePass = new(_material);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null || renderingData.cameraData.cameraType == CameraType.Preview) return;

            _material.SetFloat(ThicknessId, _settings.thickness);
            _material.SetColor(ColorId, _settings.outlineColor);
            _material.SetFloat(DepthScaleId, _settings.depthScale);
            _material.SetFloat(DepthBiasId, _settings.depthBias);
            _material.SetFloat(NormalScaleId, _settings.normalScale);
            _material.SetFloat(NormalBiasId, _settings.normalBias);
            _outlinePass.renderPassEvent = _settings.renderPassEvent;
            _outlinePass.ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(_outlinePass);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) CoreUtils.Destroy(_material);
        }

        class OutlinePass : ScriptableRenderPass
        {
            readonly Material _material;

            public OutlinePass(Material material) => _material = material;

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                CommandBuffer cmd = CommandBufferPool.Get($"{nameof(AxieMixer3D)}.{nameof(OutlinePostProcessRendererFeature)}");

                try
                {
                    var source = new RenderTargetIdentifier(BuiltinRenderTextureType.None);
                    var target = renderingData.cameraData.renderer.cameraColorTarget;
                    Blit(cmd, source, target, _material, 0);
                    context.ExecuteCommandBuffer(cmd);
                }
                finally
                {
                    CommandBufferPool.Release(cmd);
                }
            }
        }
    }
}
