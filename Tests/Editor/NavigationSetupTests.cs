using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace jeanf.tooltip.tests
{
    /// <summary>
    /// Locks the display chain's assets: the marker shader for the active pipeline must compile,
    /// and the shipped materials must keep GPU instancing on (RenderMeshInstanced needs it).
    /// </summary>
    public class NavigationSetupTests
    {
        private const string UrpMaterialGuid = "6712bdfa68774946a84c09596df860cd";
        private const string HdrpMaterialGuid = "0d5a4d469eb24a01b2fae233868e2f2e";

        [Test]
        public void ActivePipeline_MarkerShader_Compiles()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            Assume.That(pipeline, Is.Not.Null, "Built-in pipeline: no marker shader is shipped for it.");

            bool hdrp = pipeline.GetType().FullName.Contains("HighDefinition");
            string shaderName = hdrp ? "jeanf/Tooltip/NavigationMarker HDRP" : "jeanf/Tooltip/NavigationMarker URP";
            Shader shader = Shader.Find(shaderName);

            Assert.That(shader, Is.Not.Null, $"Shader '{shaderName}' not found — was the .shader asset imported?");
            Assert.That(shader.isSupported, Is.True,
                $"Shader '{shaderName}' failed to compile for the active pipeline — the navigation path cannot render. " +
                "Check the shader for errors in the inspector; for HDRP see the fallback in Documentation~/navigation-path-redesign.md §11.");
        }

        [TestCase(UrpMaterialGuid, "URP")]
        [TestCase(HdrpMaterialGuid, "HDRP")]
        public void ShippedMarkerMaterials_KeepGpuInstancingEnabled(string guid, string label)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Assert.That(path, Is.Not.Empty, $"NavigationMarker_{label} material asset is missing.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.That(material, Is.Not.Null, $"Could not load material at '{path}'.");
            Assert.That(material.enableInstancing, Is.True,
                $"'{path}' must keep 'Enable GPU Instancing' ticked — dots/arrows are drawn with RenderMeshInstanced.");
            Assert.That(material.shader, Is.Not.Null, $"'{path}' lost its shader reference.");
        }
    }
}
