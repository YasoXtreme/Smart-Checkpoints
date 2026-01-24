using UnityEngine;

public static class SplineMath
{
    // Returns a position on a Catmull-Rom spline between p1 and p2
    // t is the progress (0 to 1)
    // p0 is the point BEFORE p1 (control point)
    // p3 is the point AFTER p2 (control point)
    public static Vector3 GetCatmullRomPosition(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        Vector3 a = 2f * p1;
        Vector3 b = p2 - p0;
        Vector3 c = 2f * p0 - 5f * p1 + 4f * p2 - p3;
        Vector3 d = -p0 + 3f * p1 - 3f * p2 + p3;

        Vector3 pos = 0.5f * (a + (b * t) + (c * t * t) + (d * t * t * t));

        return pos;
    }

    // Helper to get rotation along the curve (tangent)
    public static Quaternion GetCatmullRomRotation(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        // Sample a tiny bit ahead to look at the tangent
        Vector3 currentPos = GetCatmullRomPosition(t, p0, p1, p2, p3);
        Vector3 futurePos = GetCatmullRomPosition(t + 0.01f, p0, p1, p2, p3);

        Vector3 direction = (futurePos - currentPos).normalized;
        if (direction == Vector3.zero) return Quaternion.identity;
        return Quaternion.LookRotation(direction);
    }
}