using UnityEngine;
using UnityEngine.AI;

namespace jeanf.tooltip
{
    /// <summary>
    /// Navmesh query seam so the pure path math stays testable without a baked navmesh.
    /// </summary>
    public interface INavPathQuery
    {
        /// <summary>True when the straight segment from <paramref name="from"/> to <paramref name="to"/> stays on the navmesh.</summary>
        bool IsSegmentClear(Vector3 from, Vector3 to);
    }

    public sealed class NavMeshPathQuery : INavPathQuery
    {
        private NavMeshHit _hit;
        public bool IsSegmentClear(Vector3 from, Vector3 to) =>
            !NavMesh.Raycast(from, to, out _hit, NavMesh.AllAreas);
    }

    /// <summary>
    /// Preallocated buffers for the whole path pipeline. Allocated once, reused every repath — zero GC.
    /// </summary>
    public sealed class NavigationPathBuffers
    {
        /// <summary>Raw NavMesh corners (fill via NavMeshPath.GetCornersNonAlloc).</summary>
        public readonly Vector3[] Corners;
        public int CornerCount;

        internal readonly Vector3[] Ortho;
        internal int OrthoCount;

        /// <summary>Dense polyline after orthogonalization + corner fillets (feeds the LineRenderer).</summary>
        public readonly Vector3[] Points;
        /// <summary>Cumulative distance along <see cref="Points"/>.</summary>
        public readonly float[] PointDistances;
        public int PointCount;
        public float TotalLength;

        public readonly Vector3[] MarkerPositions;
        public readonly Vector3[] MarkerTangents;
        public readonly float[] MarkerDistances;
        public int MarkerCount;

        internal readonly Vector3[] ArcScratch;

        public NavigationPathBuffers(int maxCorners = 128, int maxPoints = 4096, int maxMarkers = 1023)
        {
            Corners = new Vector3[maxCorners];
            Ortho = new Vector3[maxCorners * 2];
            Points = new Vector3[maxPoints];
            PointDistances = new float[maxPoints];
            MarkerPositions = new Vector3[maxMarkers];
            MarkerTangents = new Vector3[maxMarkers];
            MarkerDistances = new float[maxMarkers];
            ArcScratch = new Vector3[NavigationPathProcessor.ArcSamples + 1];
        }
    }

    /// <summary>
    /// Pure path post-processing: NavMesh corners -> (optional) orthogonalization with leg re-centering
    /// -> navmesh-validated corner fillets -> uniform resampling into oriented markers.
    /// Static and buffer-based so it is edit-mode testable and allocation-free.
    /// </summary>
    public static class NavigationPathProcessor
    {
        internal const int ArcSamples = 10;
        private const float AxisEpsilon = 0.05f;
        // Segments more diagonal than this (min/max extent ratio) get an axis + 45° decomposition
        // instead of an L-shape — a full right-angle detour on a near-diagonal reads as silly.
        private const float OctilinearAspect = 0.5f;
        private const float MinLegLength = 0.4f;
        private const float MinTangentDistance = 0.03f;
        private const float ShrinkFactor = 0.8f;
        private const int MaxShrinkIterations = 14;
        private const float LegShiftStep = 0.25f;

        public static void Process(in NavigationPathSettings settings, INavPathQuery query, NavigationPathBuffers b)
        {
            b.PointCount = 0;
            b.MarkerCount = 0;
            b.TotalLength = 0f;
            if (b.CornerCount < 2) return;

            Vector3[] src;
            int srcCount;
            if (settings.mode == NavigationPathMode.Orthogonal)
            {
                Orthogonalize(settings, query, b);
                src = b.Ortho;
                srcCount = b.OrthoCount;
            }
            else
            {
                src = b.Corners;
                srcCount = b.CornerCount;
            }

            Fillet(src, srcCount, settings.cornerRadius, query, b);
            b.TotalLength = b.PointCount > 0 ? b.PointDistances[b.PointCount - 1] : 0f;
            Resample(in settings, b);
        }

        /// <summary>
        /// Closest-point projection of a world position onto the processed path, returning the distance along it.
        /// Searches a small window around <paramref name="segmentHint"/> so per-frame cost stays O(1);
        /// reset the hint to 0 after every repath.
        /// </summary>
        public static float ProjectDistance(NavigationPathBuffers b, Vector3 position, ref int segmentHint)
            => ProjectDistance(b, position, ref segmentHint, out _);

        /// <summary>Interpolated point at a given distance along the processed path.</summary>
        public static Vector3 PointAt(NavigationPathBuffers b, float distance)
        {
            if (b.PointCount == 0) return Vector3.zero;
            if (b.PointCount == 1 || distance <= 0f) return b.Points[0];
            if (distance >= b.TotalLength) return b.Points[b.PointCount - 1];
            int lo = 1, hi = b.PointCount - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (b.PointDistances[mid] < distance) lo = mid + 1;
                else hi = mid;
            }
            float segLen = b.PointDistances[lo] - b.PointDistances[lo - 1];
            float t = segLen > 1e-6f ? (distance - b.PointDistances[lo - 1]) / segLen : 0f;
            return Vector3.LerpUnclamped(b.Points[lo - 1], b.Points[lo], t);
        }

        /// <param name="deviationSqr">Squared distance from <paramref name="position"/> to the closest path point — how far off-path the player is.</param>
        public static float ProjectDistance(NavigationPathBuffers b, Vector3 position, ref int segmentHint, out float deviationSqr)
        {
            deviationSqr = 0f;
            if (b.PointCount < 2) return 0f;
            int start = Mathf.Clamp(segmentHint - 2, 0, b.PointCount - 2);
            int end = Mathf.Min(start + 12, b.PointCount - 2);
            float bestSqr = float.MaxValue;
            float bestDist = 0f;
            int bestSeg = start;
            for (int i = start; i <= end; i++)
            {
                Vector3 a = b.Points[i];
                Vector3 ab = b.Points[i + 1] - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(position - a, ab) / len2) : 0f;
                Vector3 toProj = position - (a + ab * t);
                float sq = toProj.sqrMagnitude;
                if (sq < bestSqr)
                {
                    bestSqr = sq;
                    bestSeg = i;
                    bestDist = b.PointDistances[i] + Mathf.Sqrt(len2) * t;
                }
            }
            segmentHint = bestSeg;
            deviationSqr = bestSqr;
            return bestDist;
        }

        /// <summary>
        /// Greedily drops corners that are directly reachable over the navmesh. The funnel inserts
        /// corners at tile seams and elevation changes even in open fields — without this pass those
        /// phantom corners would get stylized into bends where there is no obstacle at all.
        /// </summary>
        private static void SimplifyLineOfSight(NavigationPathBuffers b, INavPathQuery query)
        {
            if (b.CornerCount < 3) return;
            Vector3[] pts = b.Corners;
            int write = 1;
            int current = 0;
            while (current < b.CornerCount - 1)
            {
                int next = current + 1;
                for (int j = b.CornerCount - 1; j > next; j--)
                {
                    if (query.IsSegmentClear(pts[current], pts[j]))
                    {
                        next = j;
                        break;
                    }
                }
                pts[write++] = pts[next];
                current = next;
            }
            b.CornerCount = write;
        }

        private static void Orthogonalize(in NavigationPathSettings settings, INavPathQuery query, NavigationPathBuffers b)
        {
            SimplifyLineOfSight(b, query);

            Vector3[] src = b.Corners;
            Vector3[] dst = b.Ortho;
            int count = 0;
            Push(dst, ref count, src[0]);

            if (b.CornerCount == 2)
            {
                // No obstacle between player and target — a straight line reads better than stylized legs.
                Push(dst, ref count, src[1]);
                b.OrthoCount = count;
                return;
            }

            for (int i = 1; i < b.CornerCount; i++)
            {
                Vector3 a = dst[count - 1];
                Vector3 c = src[i];
                float dx = c.x - a.x;
                float dz = c.z - a.z;
                float adx = Mathf.Abs(dx);
                float adz = Mathf.Abs(dz);
                if (adx < AxisEpsilon || adz < AxisEpsilon || Mathf.Abs(adx - adz) < MinLegLength)
                {
                    Push(dst, ref count, c); // already axis-aligned or ~45° — keep straight
                    continue;
                }

                float yMid = (a.y + c.y) * 0.5f;
                bool xDominant = adx >= adz;
                // L-shape bends (dominant axis first, then the other order).
                Vector3 l1 = xDominant ? new Vector3(c.x, yMid, a.z) : new Vector3(a.x, yMid, c.z);
                Vector3 l2 = xDominant ? new Vector3(a.x, yMid, c.z) : new Vector3(c.x, yMid, a.z);
                // Octilinear bends: one dominant-axis leg + one 45° leg (axis-first / diagonal-first).
                float remainder = Mathf.Abs(adx - adz);
                Vector3 axisStep = xDominant
                    ? new Vector3(Mathf.Sign(dx) * remainder, 0f, 0f)
                    : new Vector3(0f, 0f, Mathf.Sign(dz) * remainder);
                Vector3 o1 = new Vector3(a.x + axisStep.x, yMid, a.z + axisStep.z);
                Vector3 o2 = new Vector3(c.x - axisStep.x, yMid, c.z - axisStep.z);

                // Near-axis segments prefer right angles (keeps doorway crossings centered);
                // strongly diagonal ones prefer the gentler 45° look. Neither valid -> keep raw.
                float aspect = Mathf.Min(adx, adz) / Mathf.Max(adx, adz);
                _ = aspect > OctilinearAspect
                    ? TryBend(a, o1, c, query, dst, ref count) || TryBend(a, o2, c, query, dst, ref count) ||
                      TryBend(a, l1, c, query, dst, ref count) || TryBend(a, l2, c, query, dst, ref count)
                    : TryBend(a, l1, c, query, dst, ref count) || TryBend(a, l2, c, query, dst, ref count) ||
                      TryBend(a, o1, c, query, dst, ref count) || TryBend(a, o2, c, query, dst, ref count);

                Push(dst, ref count, c);
            }

            b.OrthoCount = MergeCollinear(dst, count);
            if (settings.centerLegs && settings.maxLegShift > LegShiftStep * 0.5f)
                CenterLegs(settings, query, b);
        }

        private static bool TryBend(Vector3 a, Vector3 mid, Vector3 c, INavPathQuery query, Vector3[] dst, ref int count)
        {
            if (!query.IsSegmentClear(a, mid) || !query.IsSegmentClear(mid, c)) return false;
            Push(dst, ref count, mid);
            return true;
        }

        private static void Push(Vector3[] dst, ref int count, Vector3 p)
        {
            if (count < dst.Length) dst[count++] = p;
        }

        private static int MergeCollinear(Vector3[] pts, int count)
        {
            if (count < 3) return count;
            int write = 1;
            for (int i = 1; i < count - 1; i++)
            {
                Vector3 inDir = pts[i] - pts[write - 1];
                Vector3 outDir = pts[i + 1] - pts[i];
                float cross = inDir.x * outDir.z - inDir.z * outDir.x;
                bool collinear = Mathf.Abs(cross) < 1e-3f && Vector3.Dot(inDir, outDir) > 0f;
                if (!collinear) pts[write++] = pts[i];
            }
            pts[write++] = pts[count - 1];
            return write;
        }

        /// <summary>
        /// Slide each interior axis-aligned leg sideways to the middle of its free band
        /// (probed with navmesh raycasts) so the path crosses doorways/corridors centered.
        /// </summary>
        private static void CenterLegs(in NavigationPathSettings settings, INavPathQuery query, NavigationPathBuffers b)
        {
            Vector3[] v = b.Ortho;
            for (int i = 1; i + 2 < b.OrthoCount; i++)
            {
                Vector3 leg = v[i + 1] - v[i];
                bool alongX = Mathf.Abs(leg.x) >= Mathf.Abs(leg.z);
                if (alongX && Mathf.Abs(leg.z) > AxisEpsilon) continue; // only strictly axis-aligned legs
                if (!alongX && Mathf.Abs(leg.x) > AxisEpsilon) continue;
                Vector3 perp = alongX ? new Vector3(0f, 0f, 1f) : new Vector3(1f, 0f, 0f);

                float lo = 0f;
                for (float o = LegShiftStep; o <= settings.maxLegShift + 1e-4f; o += LegShiftStep)
                {
                    if (LegFits(v, i, perp * -o, query)) lo = -o;
                    else break;
                }
                float hi = 0f;
                for (float o = LegShiftStep; o <= settings.maxLegShift + 1e-4f; o += LegShiftStep)
                {
                    if (LegFits(v, i, perp * o, query)) hi = o;
                    else break;
                }

                float shift = (lo + hi) * 0.5f;
                if (Mathf.Abs(shift) > 1e-3f && LegFits(v, i, perp * shift, query))
                {
                    v[i] += perp * shift;
                    v[i + 1] += perp * shift;
                }
            }
        }

        private static bool LegFits(Vector3[] v, int i, Vector3 offset, INavPathQuery query)
        {
            Vector3 a = v[i] + offset;
            Vector3 c = v[i + 1] + offset;
            return query.IsSegmentClear(v[i - 1], a) &&
                   query.IsSegmentClear(a, c) &&
                   query.IsSegmentClear(c, v[i + 2]);
        }

        private static void Fillet(Vector3[] src, int count, float radius, INavPathQuery query, NavigationPathBuffers b)
        {
            AddPoint(b, src[0]);
            for (int i = 1; i < count - 1; i++)
            {
                Vector3 a = src[i - 1];
                Vector3 p = src[i];
                Vector3 c = src[i + 1];
                Vector3 v1 = p - a;
                Vector3 v2 = c - p;
                float l1 = v1.magnitude;
                float l2 = v2.magnitude;
                if (l1 < 1e-4f || l2 < 1e-4f) continue;
                Vector3 u1 = v1 / l1;
                Vector3 u2 = v2 / l2;
                float cosTheta = Mathf.Clamp(-Vector3.Dot(u1, u2), -1f, 1f);
                float theta = Mathf.Acos(cosTheta);
                if (theta > Mathf.PI - 0.05f || radius < 0.02f)
                {
                    AddPoint(b, p);
                    continue;
                }

                float d = Mathf.Min(radius / Mathf.Tan(theta * 0.5f), 0.45f * Mathf.Min(l1, l2));
                bool emitted = false;
                for (int it = 0; it < MaxShrinkIterations && d > MinTangentDistance; it++)
                {
                    if (TryAppendArc(p - u1 * d, p, p + u2 * d, query, b))
                    {
                        emitted = true;
                        break;
                    }
                    // Arc would leave the navmesh (e.g. rounding a door jamb into the wall) — shrink this corner.
                    d *= ShrinkFactor;
                }
                if (!emitted) AddPoint(b, p);
            }
            AddPoint(b, src[count - 1]);
        }

        private static bool TryAppendArc(Vector3 t1, Vector3 control, Vector3 t2, INavPathQuery query, NavigationPathBuffers b)
        {
            Vector3[] scratch = b.ArcScratch;
            scratch[0] = t1;
            Vector3 prev = t1;
            for (int k = 1; k <= ArcSamples; k++)
            {
                float t = (float)k / ArcSamples;
                float mt = 1f - t;
                Vector3 pt = mt * mt * t1 + 2f * mt * t * control + t * t * t2;
                if (!query.IsSegmentClear(prev, pt)) return false;
                scratch[k] = pt;
                prev = pt;
            }
            for (int k = 0; k <= ArcSamples; k++) AddPoint(b, scratch[k]);
            return true;
        }

        private static void AddPoint(NavigationPathBuffers b, Vector3 p)
        {
            if (b.PointCount >= b.Points.Length) return; // capacity clamp; surfaced by editor validation
            if (b.PointCount == 0)
            {
                b.Points[0] = p;
                b.PointDistances[0] = 0f;
                b.PointCount = 1;
                return;
            }
            int i = b.PointCount;
            b.PointDistances[i] = b.PointDistances[i - 1] + Vector3.Distance(b.Points[i - 1], p);
            b.Points[i] = p;
            b.PointCount = i + 1;
        }

        private static void Resample(in NavigationPathSettings settings, NavigationPathBuffers b)
        {
            float spacing = settings.spacing;
            if (b.PointCount < 2 || spacing <= 0.01f) return;
            // Marker distances are anchored to the TARGET end of the path: while the start
            // (the player) advances between repaths, markers keep their world positions and the
            // one nearest the player simply drops off, instead of the whole trail re-seeding
            // from the moving start every repath (which reads as swimming/flicker).
            float last = b.TotalLength - Mathf.Max(settings.endMargin, 0f);
            float first = Mathf.Max(settings.startMargin, 0f);
            if (last < first) return;
            int count = Mathf.FloorToInt((last - first) / spacing) + 1;
            float next = last - (count - 1) * spacing;
            int i = 1;
            while (next <= last + 1e-4f && b.MarkerCount < b.MarkerPositions.Length)
            {
                while (i < b.PointCount && b.PointDistances[i] < next) i++;
                if (i >= b.PointCount) break;
                float segLen = b.PointDistances[i] - b.PointDistances[i - 1];
                if (segLen < 1e-5f)
                {
                    i++;
                    continue;
                }
                Vector3 dir = (b.Points[i] - b.Points[i - 1]) / segLen;
                b.MarkerPositions[b.MarkerCount] = b.Points[i - 1] + dir * (next - b.PointDistances[i - 1]);
                b.MarkerTangents[b.MarkerCount] = dir;
                b.MarkerDistances[b.MarkerCount] = next;
                b.MarkerCount++;
                next += spacing;
            }
        }
    }
}
