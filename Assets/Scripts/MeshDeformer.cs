using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshCollider))]
public class MeshDeformer : MonoBehaviour
{
    Mesh deformingMesh;
    Vector3[] displacedVertices;
    MeshCollider meshCollider;
    public float radius = 0.1f;
    public float depth = 0.02f;
    public float maxDepth = 0.2f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        deformingMesh = GetComponent<MeshFilter>().mesh;
        displacedVertices = deformingMesh.vertices;
        meshCollider = GetComponent<MeshCollider>();
    }

    // Instantly and permanently pushes vertices near point (world space) along direction (world space).
    // force multiplies depth, so 2 makes a dent twice as deep.
    public void AddDent(Vector3 point, Vector3 direction, float force)
    {
        Debug.DrawLine(Camera.main.transform.position, point, Color.red, 1f);
        float uniformScale = transform.localScale.x;
        point = transform.InverseTransformPoint(point);
        Vector3 push = transform.InverseTransformDirection(direction.normalized) * (Mathf.Min(depth * force, maxDepth) / uniformScale);

        for (int i = 0; i < displacedVertices.Length; i++) {
            // Only vertices within radius (world units) are affected, fading to zero at the edge
            float distance = (displacedVertices[i] - point).magnitude * uniformScale;
            if (distance > radius) {
                continue;
            }
            float falloff = 1f - distance / radius;
            falloff *= falloff;
            displacedVertices[i] += push * falloff;
        }

        deformingMesh.vertices = displacedVertices;
        deformingMesh.RecalculateNormals();
        deformingMesh.RecalculateBounds();

        // Rebuild the collider so later clicks land on the dented surface
        if (meshCollider) {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = deformingMesh;
        }
    }
}
