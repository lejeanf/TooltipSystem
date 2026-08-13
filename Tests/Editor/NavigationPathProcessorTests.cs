using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace jeanf.tooltip.tests
{
    /// <summary>
    /// Edit-mode tests for the pure path pipeline. Navmesh access is faked through
    /// <see cref="INavPathQuery"/> so no scene or baked navmesh is required.
    /// </summary>
    public class NavigationPathProcessorTests
    {
        private sealed class OpenQuery : INavPathQuery
        {
            public bool IsSegmentClear(Vector3 from, Vector3 to) => true;
        }

        /// <summary>Blocks any probe that crosses one of the registered XZ wall segments.</summary>
        private sealed class WallQuery : INavPathQuery
        {
            private readonly List<(Vector2 a, Vector2 b)> _walls = new List<(Vector2, Vector2)>();

            public void AddWall(Vector2 a, Vector2 b) => _walls.Add((a, b));

            public bool IsSegmentClear(Vector3 from, Vector3 to)
            {
                var p = new Vector2(from.x, from.z);
                var q = new Vector2(to.x, to.z);
                foreach (var (a, b) in _walls)
                    if (SegmentsIntersect(p, q, a, b))
                        return false;
                return true;
            }

            private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
            {
                float d1 = Cross(p4 - p3, p1 - p3);
                float d2 = Cross(p4 - p3, p2 - p3);
                float d3 = Cross(p2 - p1, p3 - p1);
                float d4 = Cross(p2 - p1, p4 - p1);
                return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                       ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
            }

            private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        }

        private static NavigationPathBuffers MakeBuffers(params Vector3[] corners)
        {
            var buffers = new NavigationPathBuffers();
            for (int i = 0; i < corners.Length; i++) buffers.Corners[i] = corners[i];
            buffers.CornerCount = corners.Length;
            return buffers;
        }

        private static NavigationPathSettings Settings(NavigationPathMode mode, float radius, float spacing = 0.75f, bool center = false)
        {
            var s = NavigationPathSettings.Default;
            s.mode = mode;
            s.cornerRadius = radius;
            s.spacing = spacing;
            s.centerLegs = center;
            return s;
        }

        [Test]
        public void Shortest_WithoutRadius_KeepsRawCorners()
        {
            var b = MakeBuffers(new Vector3(0, 0, 0), new Vector3(4, 0, 1), new Vector3(9, 0, 5));
            NavigationPathProcessor.Process(Settings(NavigationPathMode.Shortest, 0f), new OpenQuery(), b);

            Assert.That(b.PointCount, Is.EqualTo(3));
            Assert.That(b.Points[1], Is.EqualTo(new Vector3(4, 0, 1)));
            Assert.That(b.TotalLength, Is.EqualTo(Vector3.Distance(b.Corners[0], b.Corners[1]) + Vector3.Distance(b.Corners[1], b.Corners[2])).Within(1e-4f));
        }

        [Test]
        public void Orthogonal_OpenFieldSeamCorners_CollapseToStraight()
        {
            // Phantom corners from tile seams in an open field: line-of-sight simplification
            // must collapse them so the path draws as a single straight segment.
            var b = MakeBuffers(new Vector3(0, 0, 0), new Vector3(3, 0, 2), new Vector3(7, 0, 5), new Vector3(10, 0, 7));
            NavigationPathProcessor.Process(Settings(NavigationPathMode.Orthogonal, 0f), new OpenQuery(), b);

            Assert.That(b.PointCount, Is.EqualTo(2));
            Assert.That(b.Points[1], Is.EqualTo(new Vector3(10, 0, 7)));
        }

        [Test]
        public void Orthogonal_OpenWorld_ProducesOctilinearLegs()
        {
            // Small wall blocks the direct start->target line so the corners survive
            // line-of-sight simplification and the segments get stylized.
            var walls = new WallQuery();
            walls.AddWall(new Vector2(4.9f, 2.7f), new Vector2(5.1f, 2.3f));
            var b = MakeBuffers(new Vector3(0, 0, 0), new Vector3(4, 0, 3), new Vector3(10, 0, 5));
            NavigationPathProcessor.Process(Settings(NavigationPathMode.Orthogonal, 0f), walls, b);

            Assert.That(b.PointCount, Is.GreaterThanOrEqualTo(4));
            for (int i = 1; i < b.PointCount; i++)
            {
                float dx = Mathf.Abs(b.Points[i].x - b.Points[i - 1].x);
                float dz = Mathf.Abs(b.Points[i].z - b.Points[i - 1].z);
                bool axisAligned = dx < 0.06f || dz < 0.06f;
                bool diagonal45 = Mathf.Abs(dx - dz) < 0.06f;
                Assert.That(axisAligned || diagonal45, $"segment {i} is neither axis-aligned nor 45° ({dx:F2}, {dz:F2})");
            }
            Assert.That(b.Points[0], Is.EqualTo(b.Corners[0]));
            Assert.That(b.Points[b.PointCount - 1], Is.EqualTo(b.Corners[2]));
        }

        [Test]
        public void Orthogonal_DirectPath_StaysStraight()
        {
            // Two corners = no obstacle: the stylized legs would be pointless, keep the straight line.
            var b = MakeBuffers(new Vector3(0, 0, 0), new Vector3(5, 0, 4));
            NavigationPathProcessor.Process(Settings(NavigationPathMode.Orthogonal, 0f), new OpenQuery(), b);

            Assert.That(b.PointCount, Is.EqualTo(2));
            Assert.That(b.Points[1], Is.EqualTo(new Vector3(5, 0, 4)));
        }

        [Test]
        public void Orthogonal_WhenAllBendsBlocked_KeepsDiagonal()
        {
            // Query rejects every axis-aligned probe (no L or octilinear leg validates) and the
            // direct start->target diagonal (so LOS simplification keeps the middle corner).
            var query = new BlockAxisAlignedAndDirectQuery(new Vector3(0, 0, 0), new Vector3(9, 0, 9));
            var b = MakeBuffers(new Vector3(0, 0, 0), new Vector3(5, 0, 4), new Vector3(9, 0, 9));
            NavigationPathProcessor.Process(Settings(NavigationPathMode.Orthogonal, 0f), query, b);

            Assert.That(b.PointCount, Is.EqualTo(3));
            Assert.That(b.Points[1], Is.EqualTo(new Vector3(5, 0, 4)));
        }

        private sealed class BlockAxisAlignedAndDirectQuery : INavPathQuery
        {
            private readonly Vector3 _from;
            private readonly Vector3 _to;

            public BlockAxisAlignedAndDirectQuery(Vector3 from, Vector3 to)
            {
                _from = from;
                _to = to;
            }

            public bool IsSegmentClear(Vector3 from, Vector3 to)
            {
                if (Mathf.Abs(to.x - from.x) < 0.01f || Mathf.Abs(to.z - from.z) < 0.01f) return false;
                bool isDirect = (from - _from).sqrMagnitude < 1e-6f && (to - _to).sqrMagnitude < 1e-6f;
                return !isDirect;
            }
        }

        [Test]
        public void Fillet_ClampsTangentDistanceOnShortLegs()
        {
            var b = MakeBuffers(new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(2, 0, 2));
            NavigationPathProcessor.Process(Settings(NavigationPathMode.Shortest, 2f), new OpenQuery(), b);

            // Tangent distance is clamped to 0.45 * min leg (0.9m): the arc must start after x = 1.1.
            Assert.That(b.PointCount, Is.GreaterThan(4));
            Assert.That(b.Points[1].x, Is.GreaterThan(1.09f));
            for (int i = 1; i < b.PointCount; i++)
                Assert.That(b.PointDistances[i], Is.GreaterThan(b.PointDistances[i - 1]), "cumulative distance must be strictly increasing");
        }

        /// <summary>Blocks any probe passing within <see cref="_radius"/> of a pillar (models a wall corner).</summary>
        private sealed class PillarQuery : INavPathQuery
        {
            private readonly Vector2 _pillar;
            private readonly float _radius;

            public PillarQuery(Vector2 pillar, float radius)
            {
                _pillar = pillar;
                _radius = radius;
            }

            public bool IsSegmentClear(Vector3 from, Vector3 to)
            {
                var a = new Vector2(from.x, from.z);
                var ab = new Vector2(to.x, to.z) - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(_pillar - a, ab) / len2) : 0f;
                return (a + ab * t - _pillar).magnitude >= _radius;
            }
        }

        [Test]
        public void Fillet_ShrinksArcAwayFromObstacles()
        {
            // 90° corner at (2,0). The unshrunk arc (tangent distance 0.9) has its midpoint at
            // (1.775, 0.225); a pillar there forces the validator to shrink the corner radius.
            var pillar = new PillarQuery(new Vector2(1.775f, 0.225f), 0.08f);
            var b = MakeBuffers(new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(2, 0, 8));
            NavigationPathProcessor.Process(Settings(NavigationPathMode.Shortest, 2f), pillar, b);

            Assert.That(b.PointCount, Is.GreaterThan(4), "corner should still be rounded, just tighter");
            Assert.That(b.Points[1].x, Is.GreaterThan(1.4f), "arc should have shrunk toward the corner");
            for (int i = 1; i < b.PointCount; i++)
                Assert.That(pillar.IsSegmentClear(b.Points[i - 1], b.Points[i]),
                    $"segment {i} still clips the obstacle after fillet validation");
        }

        [Test]
        public void Orthogonal_CenterLegs_CentersDoorwayCrossing()
        {
            var walls = new WallQuery();
            // Wall along z = 1.5 with a doorway gap x in [3.6, 4.6] (center 4.1), positioned so
            // the direct start->target line hits the wall (no line-of-sight collapse).
            walls.AddWall(new Vector2(-10f, 1.5f), new Vector2(3.6f, 1.5f));
            walls.AddWall(new Vector2(4.6f, 1.5f), new Vector2(20f, 1.5f));

            var b = MakeBuffers(new Vector3(0, 0, 0.2f), new Vector3(3.8f, 0, 1.7f), new Vector3(10f, 0, 2.8f));
            NavigationPathProcessor.Process(Settings(NavigationPathMode.Orthogonal, 0f, center: true), walls, b);

            // Find where the processed path crosses the wall line and check it is near the gap center.
            float crossingX = float.NaN;
            for (int i = 1; i < b.PointCount; i++)
            {
                float z0 = b.Points[i - 1].z, z1 = b.Points[i].z;
                if ((z0 - 1.5f) * (z1 - 1.5f) < 0f)
                {
                    float t = (1.5f - z0) / (z1 - z0);
                    crossingX = Mathf.Lerp(b.Points[i - 1].x, b.Points[i].x, t);
                    break;
                }
            }
            Assert.That(crossingX, Is.Not.NaN, "path never crosses the doorway line");
            Assert.That(crossingX, Is.EqualTo(4.1f).Within(0.2f), "doorway crossing is not centered");
        }

        [Test]
        public void Resample_ProducesUniformOrientedMarkers()
        {
            var b = MakeBuffers(new Vector3(0, 0, 0), new Vector3(10, 0, 0));
            NavigationPathProcessor.Process(Settings(NavigationPathMode.Shortest, 0f, spacing: 1f), new OpenQuery(), b);

            Assert.That(b.MarkerCount, Is.GreaterThanOrEqualTo(8));
            for (int i = 0; i < b.MarkerCount; i++)
            {
                Assert.That(b.MarkerTangents[i].magnitude, Is.EqualTo(1f).Within(1e-3f));
                Assert.That(b.MarkerTangents[i].x, Is.EqualTo(1f).Within(1e-3f));
                if (i > 0)
                    Assert.That(b.MarkerDistances[i] - b.MarkerDistances[i - 1], Is.EqualTo(1f).Within(1e-3f));
            }
            // Default start/end margins (0.79 / 1.13 m) keep markers off the player and the target ring.
            Assert.That(b.MarkerDistances[0], Is.GreaterThanOrEqualTo(0.79f - 1e-3f));
            Assert.That(b.MarkerDistances[b.MarkerCount - 1], Is.LessThanOrEqualTo(b.TotalLength - 1.13f + 1e-3f));
        }

        [Test]
        public void ProjectDistance_AdvancesMonotonicallyAlongPath()
        {
            var b = MakeBuffers(new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(10, 0, 10));
            NavigationPathProcessor.Process(Settings(NavigationPathMode.Shortest, 0f), new OpenQuery(), b);

            int hint = 0;
            float previous = -1f;
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                Vector3 probe = t < 0.5f
                    ? Vector3.Lerp(new Vector3(0, 0, 0.3f), new Vector3(10, 0, 0.3f), t * 2f)
                    : Vector3.Lerp(new Vector3(9.7f, 0, 0), new Vector3(9.7f, 0, 10f), (t - 0.5f) * 2f);
                float d = NavigationPathProcessor.ProjectDistance(b, probe, ref hint);
                Assert.That(d, Is.GreaterThanOrEqualTo(previous - 0.3f), $"projection went backwards at t={t:F2}");
                previous = d;
            }
            Assert.That(previous, Is.GreaterThan(15f));
        }

        [Test]
        public void Process_DoesNotAllocate()
        {
            var query = new OpenQuery();
            var b = MakeBuffers(new Vector3(0, 0, 0), new Vector3(4, 0, 3), new Vector3(10, 0, 5), new Vector3(12, 0, 12));
            var settings = Settings(NavigationPathMode.Orthogonal, 1.5f, center: true);

            // Warmup (JIT etc.), then assert the steady-state repath is allocation-free.
            NavigationPathProcessor.Process(settings, query, b);
            Assert.That(() => NavigationPathProcessor.Process(settings, query, b), Is.Not.AllocatingGCMemory());
        }
    }
}
