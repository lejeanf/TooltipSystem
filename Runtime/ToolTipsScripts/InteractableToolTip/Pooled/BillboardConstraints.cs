using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// Per-axis limits on how a tooltip billboards toward the camera, evaluated relative to a <b>rest</b>
    /// orientation (the controller's / candidate anchor's authored facing).
    ///
    /// The look direction is decomposed, in rest space, into:
    /// <list type="bullet">
    /// <item><b>Yaw</b>   — horizontal turn around the rest up axis.</item>
    /// <item><b>Pitch</b> — vertical tilt around the rest right axis.</item>
    /// <item><b>Roll</b>  — spin around the view axis to follow the <i>camera's</i> tilt (0 = always upright,
    ///        which is the classic world-up billboard). Useful in VR so a panel can lean with the head.</item>
    /// </list>
    /// Each axis can be turned off (that component is frozen at the rest value) and/or clamped to a degree
    /// range. The defaults (free yaw + pitch, upright, no clamps) reproduce the original unconstrained
    /// "face the camera, stay world-upright" behaviour exactly — see <see cref="IsUnconstrained"/>.
    /// </summary>
    [System.Serializable]
    public class BillboardConstraints
    {
        [Tooltip("Allow horizontal turn (around the rest up axis) toward the camera.")]
        public bool yawAxis = true;
        [Tooltip("Allow vertical tilt (around the rest right axis) toward the camera.")]
        public bool pitchAxis = true;
        [Tooltip("Allow the panel to roll around its view axis to match the camera's tilt (off = always upright = classic billboard).")]
        public bool rollAxis = false;

        [Tooltip("Soften the approach to a clamp: over this many degrees before each limit, the billboard eases to a stop so the limit is 'felt' rather than hit hard. 0 = hard clamp.")]
        [Min(0f)] public float limitEaseDegrees = 8f;

        // Each clamp is a band of [center+min, center+max], evaluated in WRAPPED angle space so it can sit
        // anywhere on the circle (including across the ±180° seam, e.g. center 140° + [-60,60] = 80°→180°→-160°).
        // min/max are RELATIVE to center; center 0 reproduces the old absolute-from-rest-forward behaviour.
        [Tooltip("Limit the yaw to a band around a center, relative to the rest forward. Move the center to place the band anywhere (even across ±180°).")]
        public bool clampYaw = false;
        public float yawCenter = 0f;
        public float yawMin = -60f;
        public float yawMax = 60f;

        [Tooltip("Limit the pitch to a band around a center, relative to the rest forward.")]
        public bool clampPitch = false;
        public float pitchCenter = 0f;
        public float pitchMin = -30f;
        public float pitchMax = 30f;

        [Tooltip("Limit the roll (camera-tilt follow) to a band around a center.")]
        public bool clampRoll = false;
        public float rollCenter = 0f;
        public float rollMin = -20f;
        public float rollMax = 20f;

        /// <summary>True when nothing is actually constrained — yaw+pitch free, upright, no clamps. In this
        /// state <see cref="Apply"/> returns the plain world-up look rotation, i.e. the original behaviour.</summary>
        public bool IsUnconstrained =>
            yawAxis && pitchAxis && !rollAxis && !clampYaw && !clampPitch;

        /// <summary>
        /// The constrained billboard rotation for a view at <paramref name="worldLookDir"/> from the camera
        /// (= viewPos - cameraPos), given the <paramref name="rest"/> orientation and the camera's up vector
        /// (only used when <see cref="rollAxis"/> is on).
        /// </summary>
        public Quaternion Apply(Quaternion rest, Vector3 worldLookDir, Vector3 cameraUp)
        {
            float m = worldLookDir.sqrMagnitude;
            if (m < 1e-10f) return rest;
            worldLookDir /= Mathf.Sqrt(m);

            // Fast path: identical to the original FaceCamera (free, world-upright, no clamps).
            if (IsUnconstrained)
                return Quaternion.LookRotation(worldLookDir, Vector3.up);

            // Constrain the FACE the user actually sees. The readable side is the panel's local -Z, and the
            // billboard points transform.forward AWAY from the camera (LookRotation(viewPos - cameraPos)), so the
            // face points the opposite way. Measuring on `worldLookDir` would put the allowed arc 180° behind
            // where the tooltip visually looks. Measure on the face (= toward the camera) instead, with rest
            // forward as "home", so the angles match the gizmo and the visible orientation.
            Vector3 face = -worldLookDir;

            // Yaw/pitch from the face direction in rest space (independent of camera roll), so disabling roll
            // leaves an upright panel regardless of how the camera is tilted.
            Vector3 local = Quaternion.Inverse(rest) * face;
            float yawA = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitchA = -Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;

            // Roll = the camera's tilt about the view axis, measured against the rest up. Only when enabled.
            float rollA = 0f;
            if (rollAxis)
            {
                Vector3 refUp = Vector3.ProjectOnPlane(rest * Vector3.up, face);
                Vector3 camUp = Vector3.ProjectOnPlane(cameraUp, face);
                if (refUp.sqrMagnitude > 1e-8f && camUp.sqrMagnitude > 1e-8f)
                    rollA = Vector3.SignedAngle(refUp, camUp, face);
            }

            if (!yawAxis) yawA = 0f;
            else if (clampYaw) yawA = ClampAround(yawA, yawCenter, yawMin, yawMax, limitEaseDegrees);

            if (!pitchAxis) pitchA = 0f;
            else if (clampPitch) pitchA = ClampAround(pitchA, pitchCenter, pitchMin, pitchMax, limitEaseDegrees);

            if (clampRoll) rollA = ClampAround(rollA, rollCenter, rollMin, rollMax, limitEaseDegrees); // rollA already 0 when !rollAxis

            // faceRot's forward = the constrained face direction; flip 180° about up so the panel's -Z (the face)
            // points there and transform.forward points away from the camera, as the billboard convention expects.
            Quaternion faceRot = rest * Quaternion.Euler(pitchA, yawA, rollA);
            return faceRot * Quaternion.AngleAxis(180f, Vector3.up);
        }

        /// <summary>
        /// Soft-clamp the angle <paramref name="x"/> into the band [center+min, center+max], measured in
        /// WRAPPED space around <paramref name="center"/> (shortest signed delta), so the band can straddle the
        /// ±180° seam. The soft knee (<paramref name="ease"/>) is applied on the relative delta.
        /// </summary>
        public static float ClampAround(float x, float center, float min, float max, float ease)
        {
            float delta = Mathf.DeltaAngle(center, x); // shortest signed angle from center to x, -180..180
            return center + SoftClamp(delta, min, max, ease);
        }

        /// <summary>The eased sub-zone width (degrees) applied at each end of a [min,max] clamp, capped so the
        /// two ends can't overlap. Exposed so editor gizmos can mark exactly where the softening begins.</summary>
        public static float SoftZone(float min, float max, float ease)
        {
            if (ease <= 1e-4f || max <= min) return 0f;
            return Mathf.Min(ease, (max - min) * 0.5f);
        }

        /// <summary>
        /// Clamp <paramref name="x"/> to [min,max] but with a soft knee: within <paramref name="ease"/> degrees
        /// of each limit the response compresses via tanh, so the angle asymptotically approaches (never hard-
        /// hits) the boundary and the per-frame increments shrink to zero — the limit is "felt".
        /// </summary>
        public static float SoftClamp(float x, float min, float max, float ease)
        {
            float zone = SoftZone(min, max, ease);
            if (zone <= 1e-4f) return Mathf.Clamp(x, min, max);

            float upperStart = max - zone;
            if (x > upperStart) return upperStart + zone * (float)System.Math.Tanh((x - upperStart) / zone);

            float lowerStart = min + zone;
            if (x < lowerStart) return lowerStart - zone * (float)System.Math.Tanh((lowerStart - x) / zone);

            return x;
        }
    }
}
