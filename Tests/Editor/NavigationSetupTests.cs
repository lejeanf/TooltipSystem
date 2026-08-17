using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace jeanf.tooltip.tests
{
    /// <summary>
    /// Locks the navigation setup contract: the display chain's assets (marker shader + instanced
    /// materials), and the scene validator that tells an author why no path shows — every check must
    /// run, stay uniquely named, and carry a fix hint, and the specific navmesh/teleportation traps
    /// must stay detected.
    ///
    /// The scene-level tests spawn their own offenders and assert that the offender is NAMED in the
    /// result, so they hold whatever else happens to be in the scene the test runner opened.
    /// </summary>
    public class NavigationSetupTests
    {
        private const string UrpMaterialGuid = "6712bdfa68774946a84c09596df860cd";
        private const string HdrpMaterialGuid = "0d5a4d469eb24a01b2fae233868e2f2e";

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void DestroySpawned()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private GameObject Spawn(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        /// <summary>Runs the validator and returns one check by name (with a clear failure if it vanished).</summary>
        private static NavigationSetupValidation.CheckResult Check(string name)
        {
            var results = NavigationSetupValidation.RunAllChecks();
            var result = results.FirstOrDefault(r => r.Name == name);
            Assert.That(result.Name, Is.EqualTo(name),
                $"Check '{name}' did not run — it was removed or renamed. Ran: {string.Join(", ", results.Select(r => r.Name))}.");
            return result;
        }

        // ---------------------------------------------------------------- assets

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

        // ---------------------------------------------------------------- validator contract

        [Test]
        public void RunAllChecks_CoversTooltipNavMeshAndTeleportation()
        {
            var names = NavigationSetupValidation.RunAllChecks().Select(r => r.Name).ToList();
            Assert.That(names, Is.Not.Empty, "The validator returned no checks — its check list was emptied out.");

            string[] expected =
            {
                "Tooltip: component",
                "Tooltip: setup fields",
                "Tooltip: destination senders",
                "NavMesh: baked data",
                "NavMesh: surfaces baked",
                "NavMesh: collection scope",
                "NavMesh: agent type",
                "NavMesh: player start",
                "Teleport: areas present",
                "Teleport: area colliders",
                "Teleport: interaction layers",
                "Teleport: ray raycast mask",
                "Teleport: navmesh coverage",
                "SubScene: baked-away components",
            };
            foreach (var check in expected)
                Assert.That(names, Does.Contain(check),
                    $"'{check}' is no longer validated — that setup regression would go unnoticed; " +
                    "if the removal is intentional, update this test alongside it.");
        }

        [Test]
        public void EveryFailedOrWarnedCheck_HasAFixHint()
        {
            foreach (var result in NavigationSetupValidation.RunAllChecks()
                         .Where(r => r.Severity != NavigationSetupValidation.Severity.Pass))
            {
                // 'skipped' warnings state that a check could not run; real problems must say where to fix them.
                if (result.Message.Contains("skipped")) continue;
                Assert.That(result.Hint, Is.Not.Empty,
                    $"Check '{result.Name}' reported '{result.Message}' without a fix hint — " +
                    "every failure must tell the author where to fix it.");
            }
        }

        [Test]
        public void CheckNames_AreUnique_SoConsoleOutputIsUnambiguous()
        {
            var duplicates = NavigationSetupValidation.RunAllChecks()
                .GroupBy(r => r.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            Assert.That(duplicates, Is.Empty,
                $"Duplicate check names: {string.Join(", ", duplicates)} — rename them so console feedback is unambiguous.");
        }

        [Test]
        public void NavigationMenuItems_StayUnderToolsTooltipSystem()
        {
            foreach (var type in new[] { typeof(NavigationSetupValidation), typeof(NavigationFloorSetup) })
            {
                var paths = type
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .SelectMany(m => m.GetCustomAttributes<MenuItem>())
                    .Select(a => a.menuItem)
                    .ToList();

                Assert.That(paths, Is.Not.Empty, $"{type.Name} exposes no menu item — the tool became unreachable.");
                foreach (var path in paths)
                    Assert.That(path, Does.StartWith("Tools/TooltipSystem/"),
                        $"'{path}' breaks the menu convention (Tools/[PackageName]/[Function]).");
            }
        }

        // ---------------------------------------------------------------- navmesh checks

        [Test]
        public void NavMeshDataCheck_MatchesWhatIsActuallyBaked()
        {
            bool hasNavMesh = UnityEngine.AI.NavMesh.CalculateTriangulation().indices.Length > 0;
            var result = Check("NavMesh: baked data");

            Assert.That(result.Severity, Is.EqualTo(hasNavMesh
                    ? NavigationSetupValidation.Severity.Pass
                    : NavigationSetupValidation.Severity.Fail),
                "The 'baked data' check must report exactly what NavMesh.CalculateTriangulation sees — " +
                "it is the first thing an author looks at when no path is drawn.");
        }

        [Test]
        public void SurfaceCheck_FlagsAnUnbakedSurface()
        {
            var surface = Spawn("TestSurface_Unbaked").AddComponent<NavMeshSurface>();
            Assume.That(surface.navMeshData, Is.Null, "A freshly added NavMeshSurface should carry no baked data.");

            var result = Check("NavMesh: surfaces baked");
            Assert.That(result.Severity, Is.EqualTo(NavigationSetupValidation.Severity.Fail),
                "A NavMeshSurface with no baked NavMeshData contributes nothing at runtime and must fail the check.");
            Assert.That(result.Message, Does.Contain("TestSurface_Unbaked"),
                "The failing surface must be named, otherwise the author cannot find it in a 36-surface scene.");
        }

        [Test]
        public void AgentTypeCheck_FlagsASurfaceBakedForANonDefaultAgent()
        {
            var surface = Spawn("TestSurface_WrongAgent").AddComponent<NavMeshSurface>();
            surface.agentTypeID = UnityEngine.AI.NavMesh.GetSettingsByIndex(0).agentTypeID + 1;

            var result = Check("NavMesh: agent type");
            Assert.That(result.Severity, Is.EqualTo(NavigationSetupValidation.Severity.Fail),
                "NavigationTooltip paths with NavMesh.CalculatePath, which only queries the default agent — " +
                "a surface baked for another agent is invisible to it and must be flagged.");
            Assert.That(result.Message, Does.Contain("TestSurface_WrongAgent"));
        }

        [Test]
        public void CollectionScopeCheck_FlagsSeveralCollectAllSurfaces()
        {
            Spawn("TestSurface_All_A").AddComponent<NavMeshSurface>().collectObjects = CollectObjects.All;
            Spawn("TestSurface_All_B").AddComponent<NavMeshSurface>().collectObjects = CollectObjects.All;

            var result = Check("NavMesh: collection scope");
            Assert.That(result.Severity, Is.EqualTo(NavigationSetupValidation.Severity.Warning),
                "Several Collect Objects = All surfaces each bake the whole stage — loading two floors then stacks " +
                "overlapping navmeshes, which must be warned about.");
            Assert.That(result.Message, Does.Contain("TestSurface_All_"));
        }

        // ---------------------------------------------------------------- teleportation checks

        // The package deliberately has no compile-time XRI reference (the validator resolves the
        // component by name), so the tests build the components the same way.
        private Component SpawnTeleportArea(string name)
        {
            var type = TeleportAreaType();
            Assume.That(type, Is.Not.Null, "The XR Interaction Toolkit is not installed — teleportation checks cannot be exercised.");
            return Spawn(name).AddComponent(type);
        }

        private static Type TeleportAreaType() =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea", false))
                .FirstOrDefault(t => t != null);

        [Test]
        public void TeleportColliderCheck_FlagsAnAreaWithNoCollider()
        {
            SpawnTeleportArea("TestArea_NoCollider");

            var result = Check("Teleport: area colliders");
            Assert.That(result.Severity, Is.EqualTo(NavigationSetupValidation.Severity.Fail),
                "A TeleportationArea with no collider cannot be hit by the teleport ray at all.");
            Assert.That(result.Message, Does.Contain("TestArea_NoCollider"));
        }

        [Test]
        public void TeleportColliderCheck_FlagsATriggerOnlyArea()
        {
            var area = SpawnTeleportArea("TestArea_TriggerOnly");
            area.gameObject.AddComponent<BoxCollider>().isTrigger = true;

            var result = Check("Teleport: area colliders");
            Assert.That(result.Message, Does.Contain("TestArea_TriggerOnly"),
                "A trigger-only teleport area must be flagged — the teleport ray's Raycast Trigger Interaction " +
                "defaults to Ignore, so it passes straight through.");
        }

        [Test]
        public void TeleportInteractionLayerCheck_FlagsAnEmptyMask()
        {
            var area = SpawnTeleportArea("TestArea_NoLayers");
            var serialized = new SerializedObject(area);
            var bits = serialized.FindProperty("m_InteractionLayers.m_Bits");
            Assume.That(bits, Is.Not.Null, "XRBaseInteractable no longer serializes m_InteractionLayers.m_Bits — update the validator.");
            bits.uintValue = 0;
            serialized.ApplyModifiedProperties();

            var result = Check("Teleport: interaction layers");
            Assert.That(result.Severity, Is.EqualTo(NavigationSetupValidation.Severity.Fail),
                "An empty Interaction Layer Mask matches no interactor — the ray reads as blocked over a perfect floor.");
            Assert.That(result.Message, Does.Contain("TestArea_NoLayers"));
        }

        [Test]
        public void TeleportNavMeshCoverageCheck_FlagsAnAreaWithNoNavMeshAboveIt()
        {
            Assume.That(UnityEngine.AI.NavMesh.CalculateTriangulation().indices.Length, Is.GreaterThan(0),
                "No navmesh in the open scene — the coverage check correctly skips itself, nothing to assert.");

            var area = SpawnTeleportArea("TestArea_OffNavMesh");
            area.gameObject.AddComponent<BoxCollider>();
            area.transform.position = new Vector3(0f, 5000f, 0f); // far from any baked floor

            var result = Check("Teleport: navmesh coverage");
            Assert.That(result.Severity, Is.EqualTo(NavigationSetupValidation.Severity.Fail),
                "A teleport surface with no navmesh over it is a place the path can never route to — it must be flagged.");
            Assert.That(result.Message, Does.Contain("TestArea_OffNavMesh"));
        }
    }
}
