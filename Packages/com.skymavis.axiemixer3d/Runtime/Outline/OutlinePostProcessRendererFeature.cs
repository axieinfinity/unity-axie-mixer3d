using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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
            [Range(1f, 10f)] public int thickness = 2;

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
        // Blit.hlsl's fullscreen Vert reads _BlitScaleBias to map the procedural triangle to [0,1].
        static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

        [SerializeField] Settings _settings = new();

        Material _material;
        OutlinePass _outlinePass;

        public override void Create()
        {
            _material = CoreUtils.CreateEngineMaterial(ShaderName);
            _outlinePass = new(_material, _settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null || renderingData.cameraData.cameraType == CameraType.Preview) return;

            _outlinePass.renderPassEvent = _settings.renderPassEvent;
            // Requesting depth + normals makes URP schedule the prepass that fills the
            // _CameraDepthTexture / _CameraNormalsTexture globals the outline shader samples.
            _outlinePass.ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(_outlinePass);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) CoreUtils.Destroy(_material);
        }

        class OutlinePass : ScriptableRenderPass
        {
            static readonly MaterialPropertyBlock s_PropertyBlock = new();

            readonly Material _material;
            readonly Settings _settings;

            public OutlinePass(Material material, Settings settings)
            {
                _material = material;
                _settings = settings;
            }

            void ApplyMaterialSettings()
            {
                _material.SetFloat(ThicknessId, _settings.thickness);
                _material.SetColor(ColorId, _settings.outlineColor);
                _material.SetFloat(DepthScaleId, _settings.depthScale);
                _material.SetFloat(DepthBiasId, _settings.depthBias);
                _material.SetFloat(NormalScaleId, _settings.normalScale);
                _material.SetFloat(NormalBiasId, _settings.normalBias);
            }

            class PassData
            {
                public Material material;
            }

            // RenderGraph path (URP 17, RenderGraph enabled — the project default). The shader
            // alpha-blends the outline over the scene and samples depth/normals from globals,
            // so we only need to draw a fullscreen triangle into the active color target.
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null) return;

                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (cameraData.cameraType == CameraType.Preview) return;
                if (!resourceData.activeColorTexture.IsValid()) return;

                ApplyMaterialSettings();

                using var builder = renderGraph.AddRasterRenderPass<PassData>(
                    $"{nameof(AxieMixer3D)}.{nameof(OutlinePostProcessRendererFeature)}", out var passData);

                passData.material = _material;

                if (resourceData.cameraDepthTexture.IsValid()) builder.UseTexture(resourceData.cameraDepthTexture);
                if (resourceData.cameraNormalsTexture.IsValid()) builder.UseTexture(resourceData.cameraNormalsTexture);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    s_PropertyBlock.Clear();
                    s_PropertyBlock.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, s_PropertyBlock);
                });
            }
        }
    }
}
