using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshCollider))]
public class MeshDeformer : MonoBehaviour
{
    [SerializeField] GameController gameController;
    Mesh deformingMesh;
    public Vector3[] displacedVertices;
    MeshCollider meshCollider;
    public float radius = 0.1f;
    public float depth = 0.02f;
    public float maxDepth = 0.2f;

    [Header("Region Mapping")]
    // Logs the indices of vertices each dent moves, for mapping head regions to vertices
    public bool logChangedVertices = false;
    // Indices moved by every dent since the last clear, so several clicks can paint out a region
    HashSet<int> recordedVertices = new HashSet<int>();

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

        List<int> changed = new List<int>();
        for (int i = 0; i < displacedVertices.Length; i++) {
            // Only vertices within radius (world units) are affected, fading to zero at the edge
            float distance = (displacedVertices[i] - point).magnitude * uniformScale;
            if (distance > radius) {
                continue;
            }
            float falloff = 1f - distance / radius;
            falloff *= falloff;
            displacedVertices[i] += push * falloff;
            changed.Add(i);
        }

        if (logChangedVertices) {
            recordedVertices.UnionWith(changed);
            Debug.Log($"Dent moved {changed.Count} vertices ({recordedVertices.Count} recorded total): {string.Join(", ", changed)}");
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

    // Right-click the component header in the Inspector to run these during Play mode
    [ContextMenu("Log Recorded Vertices")]
    void LogRecordedVertices()
    {
        Debug.Log($"{recordedVertices.Count} recorded vertices: {string.Join(", ", recordedVertices.OrderBy(i => i))}");
    }

    [ContextMenu("Clear Recorded Vertices")]
    void ClearRecordedVertices()
    {
        recordedVertices.Clear();
        Debug.Log("Cleared recorded vertices");
    }
}
