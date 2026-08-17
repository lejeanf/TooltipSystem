#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace jeanf.tooltip
{
    /// <summary>
    /// Tools/TooltipSystem/Validate Setup — checks the whole "show the player where to go, and let
    /// them get there" chain in the loaded scenes, and prints one actionable line per problem: the
    /// NavigationTooltip itself, the navmesh the path is computed on, and the XRI teleportation
    /// areas the VR player lands on.
    ///
    /// The navmesh rules encode the post-Unity-6 setup: room geometry (floor + walls) is baked into
    /// ECS SubScenes, and NEITHER a NavMeshSurface NOR a TeleportationArea survives that bake — both
    /// are plain MonoBehaviours that are dropped when a SubScene is converted to entities, and the
    /// XRI teleport ray uses Physics.Raycast, which never hits entity colliders. Both therefore
    /// belong in the floor's additively-loaded dependency scene: the surface is baked there at edit
    /// time (with the SubScenes open, so their geometry is in the stage) and re-adds its baked
    /// NavMeshData when that scene loads at runtime.
    ///
    /// Also runs automatically before entering Play mode when the scene uses navigation paths.
    /// Tools/TooltipSystem/Setup Navigation Floor applies the fixes.
    /// </summary>
    [InitializeOnLoad]
    public static class NavigationSetupValidation
    {
        public enum Severity { Pass, Warning, Fail }

        public readonly struct CheckResult
        {
            public readonly string Name;
            public readonly Severity Severity;
            public readonly string Message;
            public readonly string Hint;

            public CheckResult(string name, Severity severity, string message, string hint = "")
            {
                Name = name;
                Severity = severity;
                Message = message;
                Hint = hint;
            }
        }

        private const string LogPrefix = "[TooltipSystem.Navigation]";

        /// <summary>Vertical tolerance when testing whether an authored surface sits on the navmesh.</summary>
        private const float NavMeshProbeDistance = 2f;

        static NavigationSetupValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;
            if (Find<NavigationTooltip>().Length == 0 && Find<NavigationTooltipTester>().Length == 0)
                return; // scene doesn't use navigation paths
            var results = RunAllChecks();
            if (results.Any(r => r.Severity != Severity.Pass)) LogResults(results);
        }

        [MenuItem("Tools/TooltipSystem/Validate Setup")]
        private static void ValidateFromMenu()
        {
            var results = RunAllChecks();
            LogResults(results);

            int fails = results.Count(r => r.Severity == Severity.Fail);
            int warnings = results.Count(r => r.Severity == Severity.Warning);
            EditorUtility.DisplayDialog("Navigation setup validation",
                fails == 0 && warnings == 0
                    ? $"All {results.Count} checks passed — the navigation path and teleportation setup look good."
                    : $"{fails} failure(s) and {warnings} warning(s) across {results.Count} checks — see the Console for the fix for each.",
                "OK");
        }

        /// <summary>
        /// Every check, in the order the chain breaks down. UI-free so the tests (and any future
        /// build step) can assert on the results instead of parsing the console.
        /// </summary>
        public static List<CheckResult> RunAllChecks()
        {
            var subScenePaths = SubScenePaths();
            var surfaces = Find<NavMeshSurface>();
            var teleportAreas = FindByTypeName(
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.BaseTeleportationInteractable");

            var results = new List<CheckResult>
            {
                CheckTooltipComponent(),
                CheckTooltipFields(),
                CheckDestinationSenders(),
                CheckNavMeshData(),
                CheckSurfacesBaked(surfaces),
                CheckCollectionScope(surfaces, subScenePaths.Count > 0),
                CheckAgentTypes(surfaces),
                CheckPlayerStart(),
                CheckTeleportAreasPresent(teleportAreas),
                CheckTeleportColliders(teleportAreas),
                CheckTeleportInteractionLayers(teleportAreas),
                CheckTeleportRaycastMask(teleportAreas),
                CheckTeleportNavMeshCoverage(teleportAreas),
                CheckBakedAwayComponents(subScenePaths, surfaces, teleportAreas),
            };
            return results;
        }

        // ---------------------------------------------------------------- tooltip

        private static CheckResult CheckTooltipComponent()
        {
            const string check = "Tooltip: component";
            var tooltip = Find<NavigationTooltip>().FirstOrDefault();
            if (tooltip == null)
            {
                bool hasTester = Find<NavigationTooltipTester>().Length > 0;
                return new CheckResult(check, hasTester ? Severity.Fail : Severity.Warning,
                    hasTester
                        ? "A NavigationTooltipTester is in the scene but there is no NavigationTooltip — nothing can draw the path."
                        : "No NavigationTooltip in the loaded scenes — no navigation path can be drawn.",
                    "Add the project's NavigationTooltip prefab (it carries the component, its LineRenderer and the marker material) " +
                    "to an always-loaded scene.");
            }

            if (!tooltip.gameObject.activeInHierarchy)
                return new CheckResult(check, Severity.Fail,
                    $"'{tooltip.name}' is inactive — the path will not render.",
                    "Activate the GameObject (or the parent that holds it).");
            if (!tooltip.enabled)
                return new CheckResult(check, Severity.Fail,
                    $"The NavigationTooltip component on '{tooltip.name}' is disabled — the path will not render.",
                    "Tick the component's checkbox in the inspector.");

            return new CheckResult(check, Severity.Pass, $"Active NavigationTooltip on '{tooltip.name}'.");
        }

        private static CheckResult CheckTooltipFields()
        {
            const string check = "Tooltip: setup fields";
            var tooltip = Find<NavigationTooltip>().FirstOrDefault();
            if (tooltip == null)
                return new CheckResult(check, Severity.Warning, "No NavigationTooltip — field checks skipped.");

            var problems = new List<string>();
            tooltip.ValidateSetup(problems);
            if (problems.Count == 0)
                return new CheckResult(check, Severity.Pass, "Marker material, LineRenderer, thresholds and Player tag are all set.");

            return new CheckResult(check, Severity.Fail, string.Join(" ", problems),
                $"Select '{tooltip.name}' — the inspector shows each problem inline under the field that causes it.");
        }

        // The senders broadcast their own transform as the destination. A sender that is off the
        // navmesh (floating, inside a wall, on a floor whose surface was never baked) makes
        // CalculatePath snap to some other point or fail outright, with no visible cause.
        private static CheckResult CheckDestinationSenders()
        {
            const string check = "Tooltip: destination senders";
            var senders = Find<NavigationDestinationSender>();
            if (senders.Length == 0)
                return new CheckResult(check, Severity.Pass,
                    "No NavigationDestinationSender in the loaded scenes (destinations come from script or the tester).");

            var offNavMesh = senders
                .Where(s => !NavMesh.SamplePosition(s.transform.position, out _, NavMeshProbeDistance, NavMesh.AllAreas))
                .Select(s => $"'{s.name}'")
                .ToList();

            if (offNavMesh.Count == 0)
                return new CheckResult(check, Severity.Pass,
                    $"All {senders.Length} destination sender(s) sit on the navmesh.");

            return new CheckResult(check, Severity.Fail,
                $"{offNavMesh.Count} of {senders.Length} destination sender(s) are further than {NavMeshProbeDistance}m from any " +
                $"navmesh: {Names(offNavMesh)} — the path to them cannot be computed.",
                "Move each sender onto the walkable floor, or bake the NavMeshSurface that should cover it " +
                "(Tools/TooltipSystem/Setup Navigation Floor).");
        }

        // ---------------------------------------------------------------- navmesh

        private static CheckResult CheckNavMeshData()
        {
            const string check = "NavMesh: baked data";
            if (NavMesh.CalculateTriangulation().indices.Length > 0)
                return new CheckResult(check, Severity.Pass, "The loaded scenes provide a navmesh.");

            return new CheckResult(check, Severity.Fail,
                "No navmesh at all in the loaded scenes — NavMesh.CalculatePath can never return a path, so nothing is ever drawn.",
                "Open the floor's dependency scene plus the room SubScenes that hold its geometry, then run " +
                "Tools/TooltipSystem/Setup Navigation Floor on the floor object (it adds and bakes the NavMeshSurface).");
        }

        private static CheckResult CheckSurfacesBaked(NavMeshSurface[] surfaces)
        {
            const string check = "NavMesh: surfaces baked";
            if (surfaces.Length == 0)
                return new CheckResult(check, Severity.Fail,
                    "No NavMeshSurface in the loaded scenes — nothing adds a navmesh at runtime, whatever is baked on disk.",
                    "Put one NavMeshSurface per floor in that floor's additively-loaded dependency scene (NOT in a SubScene: " +
                    "the component is dropped when the SubScene is baked to entities) and bake it.");

            var unbaked = surfaces.Where(s => s.navMeshData == null).Select(s => $"'{s.name}'").ToList();
            var inactive = surfaces.Where(s => s.navMeshData != null && !s.isActiveAndEnabled)
                .Select(s => $"'{s.name}'").ToList();

            if (unbaked.Count == 0 && inactive.Count == 0)
                return new CheckResult(check, Severity.Pass, $"All {surfaces.Length} NavMeshSurface(s) carry baked data and are enabled.");

            var message = new StringBuilder();
            if (unbaked.Count > 0)
                message.Append($"{unbaked.Count} NavMeshSurface(s) have no baked NavMeshData and contribute nothing at runtime: {Names(unbaked)}. ");
            if (inactive.Count > 0)
                message.Append($"{inactive.Count} baked NavMeshSurface(s) are inactive/disabled, so they never AddData(): {Names(inactive)}. ");

            return new CheckResult(check, Severity.Fail, message.ToString().TrimEnd(),
                "Select each surface and press Bake (or run Tools/TooltipSystem/Setup Navigation Floor), and make sure the " +
                "GameObject holding it is active in the scene that loads with that floor.");
        }

        // At edit time a surface collects geometry from the whole STAGE, not just its own scene
        // (NavMeshBuilder.CollectSourcesInStage) — which is what lets a surface in a dependency
        // scene bake the floor/wall meshes of the open SubScenes. The flip side: with Collect
        // Objects = All, every surface bakes EVERY loaded floor, so loading two of them at runtime
        // stacks two copies of the building's navmesh on top of each other.
        private static CheckResult CheckCollectionScope(NavMeshSurface[] surfaces, bool hasSubScenes)
        {
            const string check = "NavMesh: collection scope";
            if (surfaces.Length == 0)
                return new CheckResult(check, Severity.Warning, "No NavMeshSurface — collection scope checks skipped.");

            var collectAll = surfaces
                .Where(s => s.collectObjects == CollectObjects.All)
                .Select(s => $"'{s.name}'")
                .ToList();

            if (collectAll.Count == 0)
                return new CheckResult(check, Severity.Pass,
                    $"All {surfaces.Length} surface(s) bake a bounded set of objects (Volume/Children/Marked).");

            if (surfaces.Length == 1 && collectAll.Count == 1)
                return new CheckResult(check, Severity.Pass,
                    $"The single surface '{surfaces[0].name}' collects everything in the stage — fine while it is the only one.");

            return new CheckResult(check, Severity.Warning,
                $"{collectAll.Count} of {surfaces.Length} surface(s) use Collect Objects = All: {Names(collectAll)}. 'All' means " +
                "\"bake everything that is open in the editor right now\", so all of them bake the SAME geometry — at runtime you " +
                $"get {collectAll.Count} copies of the same walkable area stacked on top of each other (wasted memory, and a path " +
                "query can land on any of the duplicates).",
                "Pick one of two shapes. (a) One surface for the whole area: keep a single Collect Objects = All surface and delete " +
                "the extra ones — simplest when everything loads together. (b) One surface per area: set Collect Objects = Volume on " +
                "each and size its box to just its own part of the level, so each asset holds only that piece and streams with it — " +
                "select the objects and run Tools/TooltipSystem/Setup Navigation Floor to get this. Either way, re-bake afterwards" +
                (hasSubScenes
                    ? ", with the SubScenes that hold the geometry OPEN — a closed SubScene contributes nothing to the bake."
                    : "."));
        }

        // NavigationTooltip calls NavMesh.CalculatePath, which always uses the DEFAULT agent type.
        // A surface baked for another agent is invisible to it — the navmesh is there, the path is not.
        private static CheckResult CheckAgentTypes(NavMeshSurface[] surfaces)
        {
            const string check = "NavMesh: agent type";
            var defaultAgentTypeId = NavMesh.GetSettingsByIndex(0).agentTypeID;
            var wrongAgent = surfaces
                .Where(s => s.agentTypeID != defaultAgentTypeId)
                .Select(s => $"'{s.name}' ({NavMesh.GetSettingsNameFromID(s.agentTypeID)})")
                .ToList();

            if (wrongAgent.Count == 0)
                return new CheckResult(check, Severity.Pass,
                    $"All surfaces are baked for the default agent type ('{NavMesh.GetSettingsNameFromID(defaultAgentTypeId)}').");

            return new CheckResult(check, Severity.Fail,
                $"{wrongAgent.Count} surface(s) are baked for a non-default agent type: {Names(wrongAgent)} — NavigationTooltip " +
                "computes its path with NavMesh.CalculatePath, which only ever queries the default agent, so those surfaces are " +
                "invisible to it even though the navmesh exists.",
                $"Set Agent Type = '{NavMesh.GetSettingsNameFromID(defaultAgentTypeId)}' on those surfaces and re-bake " +
                "(or bake a second surface for the default agent over the same floor).");
        }

        private static CheckResult CheckPlayerStart()
        {
            const string check = "NavMesh: player start";
            GameObject player;
            try
            {
                player = GameObject.FindGameObjectWithTag("Player");
            }
            catch (UnityException)
            {
                return new CheckResult(check, Severity.Fail,
                    "The 'Player' tag does not exist in this project — NavigationTooltip resolves the path origin by that tag.",
                    "Add a 'Player' tag in the Tags & Layers settings and put it on the player root.");
            }

            if (player == null)
                return new CheckResult(check, Severity.Warning,
                    "No GameObject tagged 'Player' in the loaded scenes — the path origin cannot be checked " +
                    "(fine if the player spawns at runtime).",
                    "Open the scene that contains the player, or tag the player root 'Player'.");

            if (NavMesh.SamplePosition(player.transform.position, out _, NavMeshProbeDistance, NavMesh.AllAreas))
                return new CheckResult(check, Severity.Pass, $"'{player.name}' starts on the navmesh.");

            return new CheckResult(check, Severity.Fail,
                $"'{player.name}' starts further than {NavMeshProbeDistance}m from any navmesh — the path has no origin, so " +
                "nothing is drawn until the player walks onto a baked floor.",
                "Move the player's spawn onto the walkable floor, or bake the surface that should cover it. " +
                "(NavigationTooltip's own 'Navmesh Detection Distance' widens the runtime snap, but a spawn that far off " +
                "is usually a missing bake.)");
        }

        // ---------------------------------------------------------------- teleportation

        private static CheckResult CheckTeleportAreasPresent(Component[] areas)
        {
            const string check = "Teleport: areas present";
            if (XriTeleportType() == null)
                return new CheckResult(check, Severity.Warning,
                    "The XR Interaction Toolkit is not in this project — VR teleportation checks skipped.");

            if (areas.Length == 0)
                return new CheckResult(check, Severity.Fail,
                    "No TeleportationArea/TeleportationAnchor in the loaded scenes — the VR player's teleport ray has nothing to " +
                    "land on, so the navigation path leads somewhere they cannot reach.",
                    "Add a TeleportationArea to the floor plate in each floor's dependency scene " +
                    "(Tools/TooltipSystem/Setup Navigation Floor adds and wires it).");

            var inactive = areas.Where(a => !a.gameObject.activeInHierarchy).Select(a => $"'{a.name}'").ToList();
            if (inactive.Count > 0)
                return new CheckResult(check, Severity.Warning,
                    $"{areas.Length} teleport surface(s) found, but {inactive.Count} are inactive: {Names(inactive)}.",
                    "Activate them, or remove them if the floor is deliberately not teleportable.");

            return new CheckResult(check, Severity.Pass, $"{areas.Length} active teleport surface(s).");
        }

        // XRBaseInteractable falls back to the colliders on its own GameObject when the list is
        // empty, so an empty list is only fatal without a local collider. Trigger colliders are a
        // separate trap: the teleport ray's Raycast Trigger Interaction defaults to Ignore.
        private static CheckResult CheckTeleportColliders(Component[] areas)
        {
            const string check = "Teleport: area colliders";
            if (areas.Length == 0)
                return new CheckResult(check, Severity.Warning, "No teleport surfaces — collider checks skipped.");

            var brokenReference = new List<string>();
            var noCollider = new List<string>();
            var triggerOnly = new List<string>();
            foreach (var area in areas)
            {
                var colliders = TeleportColliders(area);
                if (colliders.Count == 0)
                {
                    // A list entry that resolves to null is a different (and much more confusing)
                    // problem than an area nobody ever gave a collider — say which one it is.
                    if (HasBrokenColliderReference(area)) brokenReference.Add($"'{area.name}'");
                    else noCollider.Add($"'{area.name}'");
                    continue;
                }
                if (colliders.All(c => c.isTrigger)) triggerOnly.Add($"'{area.name}'");
            }

            if (brokenReference.Count == 0 && noCollider.Count == 0 && triggerOnly.Count == 0)
                return new CheckResult(check, Severity.Pass, $"All {areas.Length} teleport surface(s) expose a solid collider.");

            var message = new StringBuilder();
            var hint = new StringBuilder();
            if (brokenReference.Count > 0)
            {
                message.Append($"{brokenReference.Count} teleport surface(s) list a collider that NO LONGER EXISTS: {Names(brokenReference)} — " +
                               "the Colliders slot shows 'Missing' or 'None'. Usually the child object holding the collider was deleted, " +
                               "or removed on this prefab instance. The ray has nothing to hit. ");
                hint.Append($"Select {Names(brokenReference, 3)} in the Hierarchy. If it is a prefab instance, the quickest fix is the " +
                            "Overrides dropdown > Revert All, which brings the deleted collider child back. Otherwise add a child (or a " +
                            "collider on the object itself) and drag it into the TeleportationArea's Colliders list. ");
            }
            if (noCollider.Count > 0)
            {
                message.Append($"{noCollider.Count} teleport surface(s) have no collider anywhere on them or their children — " +
                               $"the ray cannot hit them: {Names(noCollider)}. ");
                hint.Append("Add a non-trigger collider (a thin BoxCollider matching the walkable surface, or the floor's MeshCollider) " +
                            "on the area or a child of it. ");
            }
            if (triggerOnly.Count > 0)
            {
                message.Append($"{triggerOnly.Count} teleport surface(s) only have TRIGGER colliders: {Names(triggerOnly)} — the teleport ray's " +
                               "Raycast Trigger Interaction defaults to Ignore, so it passes straight through them. ");
                hint.Append("Untick 'Is Trigger' on those colliders, or set the teleport XRRayInteractor's Raycast Trigger Interaction " +
                            "to Collide. ");
            }

            var severity = triggerOnly.Count > 0 && brokenReference.Count == 0 && noCollider.Count == 0
                ? Severity.Warning
                : Severity.Fail;
            return new CheckResult(check, severity, message.ToString().TrimEnd(), hint.ToString().TrimEnd());
        }

        private static CheckResult CheckTeleportInteractionLayers(Component[] areas)
        {
            const string check = "Teleport: interaction layers";
            if (areas.Length == 0)
                return new CheckResult(check, Severity.Warning, "No teleport surfaces — interaction layer checks skipped.");

            var noLayer = areas
                .Where(a => new SerializedObject(a).FindProperty("m_InteractionLayers.m_Bits")?.uintValue == 0)
                .Select(a => $"'{a.name}'")
                .ToList();

            if (noLayer.Count == 0)
                return new CheckResult(check, Severity.Pass, $"All {areas.Length} teleport surface(s) declare an interaction layer.");

            return new CheckResult(check, Severity.Fail,
                $"{noLayer.Count} teleport surface(s) have an EMPTY Interaction Layer Mask: {Names(noLayer)} — no interactor can " +
                "ever match them, so the ray shows as blocked over an otherwise perfect floor.",
                "Set the same interaction layer the player's teleport interactor uses (the XRI default is 'Teleport') on the " +
                "TeleportationArea's Interaction Layer Mask.");
        }

        // The teleport ray is a plain Physics raycast: an area on a layer outside the interactor's
        // Raycast Mask is invisible to it, no matter how it is configured.
        private static CheckResult CheckTeleportRaycastMask(Component[] areas)
        {
            const string check = "Teleport: ray raycast mask";
            if (areas.Length == 0)
                return new CheckResult(check, Severity.Warning, "No teleport surfaces — raycast mask checks skipped.");

            var rays = FindByTypeName("UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor")
                .Where(r => new SerializedObject(r).FindProperty("m_LineType")?.enumValueIndex == 1) // ProjectileCurve
                .ToList();
            if (rays.Count == 0)
                return new CheckResult(check, Severity.Warning,
                    "No projectile-curve XRRayInteractor in the loaded scenes — the teleport ray's layer mask could not be " +
                    "checked (open the scene containing the player).",
                    "Open a scene with the Player prefab and validate again.");

            int mask = rays.Aggregate(0, (acc, r) =>
                acc | (new SerializedObject(r).FindProperty("m_RaycastMask")?.intValue ?? ~0));
            var unreachable = areas
                .Where(a => (mask & (1 << a.gameObject.layer)) == 0)
                .Select(a => $"'{a.name}' (layer '{LayerMask.LayerToName(a.gameObject.layer)}')")
                .ToList();

            if (unreachable.Count == 0)
                return new CheckResult(check, Severity.Pass,
                    "Every teleport surface sits on a layer the teleport ray casts against.");

            return new CheckResult(check, Severity.Fail,
                $"{unreachable.Count} teleport surface(s) are on a layer EXCLUDED from the teleport ray's Raycast Mask: " +
                $"{Names(unreachable)} — the ray flies through them and the player can never land there.",
                "Either move those surfaces to a layer in the mask, or add their layer to the Raycast Mask of the player's " +
                "projectile-curve XRRayInteractor.");
        }

        // The two halves have to agree: the path is drawn on the navmesh, the player travels by
        // landing on teleport surfaces. A teleport surface with no navmesh over it is a place the
        // path can never route through; it is the single most common cause of "the arrows stop
        // halfway" after a floor is re-baked.
        private static CheckResult CheckTeleportNavMeshCoverage(Component[] areas)
        {
            const string check = "Teleport: navmesh coverage";
            if (areas.Length == 0)
                return new CheckResult(check, Severity.Warning, "No teleport surfaces — navmesh coverage checks skipped.");
            if (NavMesh.CalculateTriangulation().indices.Length == 0)
                return new CheckResult(check, Severity.Warning,
                    "No navmesh in the loaded scenes — coverage check skipped.", "See the 'NavMesh: baked data' result.");

            var uncovered = new List<string>();
            foreach (var area in areas)
            {
                var colliders = TeleportColliders(area);
                if (colliders.Count == 0) continue; // already reported by the collider check

                var bounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Count; i++) bounds.Encapsulate(colliders[i].bounds);

                var top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
                float reach = bounds.size.y + NavMeshProbeDistance;
                if (!NavMesh.SamplePosition(top, out _, reach, NavMesh.AllAreas)) uncovered.Add($"'{area.name}'");
            }

            if (uncovered.Count == 0)
                return new CheckResult(check, Severity.Pass,
                    $"All {areas.Length} teleport surface(s) are covered by navmesh — the path can route onto every one of them.");

            return new CheckResult(check, Severity.Fail,
                $"{uncovered.Count} teleport surface(s) have no navmesh above them: {Names(uncovered)} — the VR player can " +
                "teleport there, but the navigation path can never be drawn to or across them.",
                "Re-bake the floor's NavMeshSurface with those plates included (check its Collect Objects volume and Include " +
                "Layers), or run Tools/TooltipSystem/Setup Navigation Floor on the floor object.");
        }

        // ---------------------------------------------------------------- SubScenes

        // A SubScene is baked to entities: MonoBehaviours without a baker simply do not exist at
        // runtime. A NavMeshSurface in there never calls AddData (no navmesh), a TeleportationArea
        // never registers (and its GameObject collider is gone, so Physics.Raycast misses it), and a
        // NavigationTooltip never runs. This is silent — everything looks right in the editor with
        // the SubScene open, and nothing works in a build.
        private static CheckResult CheckBakedAwayComponents(
            HashSet<string> subScenePaths, NavMeshSurface[] surfaces, Component[] teleportAreas)
        {
            const string check = "SubScene: baked-away components";
            if (subScenePaths.Count == 0)
                return new CheckResult(check, Severity.Pass, "No SubScenes in the loaded scenes.");

            var offenders = new List<string>();
            offenders.AddRange(surfaces.Where(s => subScenePaths.Contains(s.gameObject.scene.path))
                .Select(s => $"NavMeshSurface '{s.name}'"));
            offenders.AddRange(teleportAreas.Where(a => subScenePaths.Contains(a.gameObject.scene.path))
                .Select(a => $"{a.GetType().Name} '{a.name}'"));
            offenders.AddRange(Find<NavigationTooltip>().Where(t => subScenePaths.Contains(t.gameObject.scene.path))
                .Select(t => $"NavigationTooltip '{t.name}'"));

            if (offenders.Count == 0)
                return new CheckResult(check, Severity.Pass,
                    $"None of the navigation components live inside one of the {subScenePaths.Count} SubScene(s).");

            return new CheckResult(check, Severity.Fail,
                $"{offenders.Count} navigation component(s) live INSIDE a SubScene: {Names(offenders)} — SubScenes are baked to " +
                "entities, so these MonoBehaviours (and their GameObject colliders) do not exist at runtime. They work in the " +
                "editor while the SubScene is open and silently do nothing in a build.",
                "Move them out into the floor's additively-loaded dependency scene. Keep the geometry in the SubScene: the " +
                "surface bakes against it at edit time while the SubScene is open, then re-adds its baked data at runtime.");
        }

        // ---------------------------------------------------------------- helpers

        private static T[] Find<T>() where T : Component =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        /// <summary>
        /// Instances of a type this assembly does not reference (XRI, Entities). Keeps the package
        /// free of an XR Interaction Toolkit dependency — the same trick the project's other
        /// validators use for pipeline-specific components.
        /// </summary>
        private static Component[] FindByTypeName(string fullName)
        {
            var type = FindType(fullName);
            if (type == null) return Array.Empty<Component>();
            return Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OfType<Component>()
                .ToArray();
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        internal static Type XriTeleportType() =>
            FindType("UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea");

        /// <summary>
        /// The colliders XRI will actually use, mirroring XRBaseInteractable.Awake: the serialized
        /// Colliders list whenever it has entries, otherwise the colliders found in CHILDREN with
        /// triggers discarded. (Checking only the area's own GameObject would falsely condemn the
        /// standard Teleport Area prefab, whose collider sits on a child.)
        /// </summary>
        public static List<Collider> TeleportColliders(Component area)
        {
            var listed = ListedColliders(area, out _, out int entryCount);
            if (entryCount > 0) return listed; // XRI does not fall back while the list is non-empty

            var children = new List<Collider>();
            area.GetComponentsInChildren(children);
            children.RemoveAll(c => c.isTrigger); // XRI drops triggers from the automatic fallback
            return children;
        }

        /// <summary>True when the Colliders list has an entry whose collider no longer exists.</summary>
        public static bool HasBrokenColliderReference(Component area)
        {
            ListedColliders(area, out bool broken, out _);
            return broken;
        }

        private static List<Collider> ListedColliders(Component area, out bool broken, out int entryCount)
        {
            var listed = new List<Collider>();
            broken = false;
            entryCount = 0;

            var property = new SerializedObject(area).FindProperty("m_Colliders");
            if (property == null || !property.isArray) return listed;

            entryCount = property.arraySize;
            for (int i = 0; i < entryCount; i++)
            {
                if (property.GetArrayElementAtIndex(i).objectReferenceValue is Collider collider) listed.Add(collider);
                else broken = true;
            }
            return listed;
        }

        /// <summary>Scene paths that are authored as ECS SubScenes among the loaded scenes.</summary>
        internal static HashSet<string> SubScenePaths()
        {
            var paths = new HashSet<string>();
            foreach (var subScene in FindByTypeName("Unity.Scenes.SubScene"))
            {
                var asset = new SerializedObject(subScene).FindProperty("_SceneAsset")?.objectReferenceValue;
                if (asset != null) paths.Add(AssetDatabase.GetAssetPath(asset));
            }
            return paths;
        }

        private static string Names(IReadOnlyList<string> names, int max = 6)
        {
            string shown = string.Join(", ", names.Take(max));
            return names.Count > max ? $"{shown}, … (+{names.Count - max} more)" : shown;
        }

        private static void LogResults(List<CheckResult> results)
        {
            int fails = results.Count(r => r.Severity == Severity.Fail);
            int warnings = results.Count(r => r.Severity == Severity.Warning);

            var sb = new StringBuilder();
            sb.AppendLine($"{LogPrefix} {results.Count} checks — {fails} failed, {warnings} warning(s). " +
                          $"Loaded scenes: {LoadedSceneNames()}");
            foreach (var result in results)
            {
                string icon = result.Severity switch
                {
                    Severity.Pass => "✓",
                    Severity.Warning => "⚠",
                    _ => "✗"
                };
                sb.AppendLine($"{icon} {result.Name}: {result.Message}");
                if (!string.IsNullOrEmpty(result.Hint)) sb.AppendLine($"   → Fix: {result.Hint}");
            }

            if (fails > 0) Debug.LogError(sb.ToString());
            else if (warnings > 0) Debug.LogWarning(sb.ToString());
            else Debug.Log(sb.ToString());
        }

        private static string LoadedSceneNames()
        {
            var names = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++) names.Add(SceneManager.GetSceneAt(i).name);
            return string.Join(", ", names);
        }
    }
}
#endif
