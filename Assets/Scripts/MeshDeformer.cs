using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshCollider))]
public class MeshDeformer : MonoBehaviour
{
    [SerializeField] GameController gameController;
    Mesh deformingMesh;
    public Vector3[] displacedVertices;
    // Undented positions, so GameController can measure how far each vertex has been pushed
    Vector3[] originalVertices;
    MeshCollider meshCollider;
    public float radius = 0.1f;
    public float depth = 0.02f;
    public float maxDepth = 0.2f;

    [Header("Stat Sensitivity")]
    // Stat points lost for one dent landing squarely on a region
    public float pointsPerDent = 3f;
    // Scales pointsPerDent for each stat, so 2 makes that stat drop twice as fast and 0 makes it ignore dents
    public float strengthMultiplier = 1f;
    public float dexterityMultiplier = 1f;
    public float constitutionMultiplier = 1f;
    public float intelligenceMultiplier = 1f;
    public float wisdomMultiplier = 1f;
    public float charismaMultiplier = 1f;

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
        originalVertices = deformingMesh.vertices;
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

        if (gameController) {
            gameController.OnPunched(this);
        }
    }

    // Puts every vertex back where it started
    public void ResetMesh()
    {
        originalVertices.CopyTo(displacedVertices, 0);
        deformingMesh.vertices = displacedVertices;
        deformingMesh.RecalculateNormals();
        deformingMesh.RecalculateBounds();

        if (meshCollider) {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = deformingMesh;
        }
    }

    // Stat points lost per dent in a region tied to stat
    public float PointsPerDent(GameController.Stat stat)
    {
        switch (stat) {
            case GameController.Stat.Strength: return pointsPerDent * strengthMultiplier;
            case GameController.Stat.Dexterity: return pointsPerDent * dexterityMultiplier;
            case GameController.Stat.Constitution: return pointsPerDent * constitutionMultiplier;
            case GameController.Stat.Intelligence: return pointsPerDent * intelligenceMultiplier;
            case GameController.Stat.Wisdom: return pointsPerDent * wisdomMultiplier;
            case GameController.Stat.Charisma: return pointsPerDent * charismaMultiplier;
            default: return 0f;
        }
    }

    // Where vertex i sat before any dents, in the mesh's own space
    public Vector3 OriginalPosition(int i)
    {
        return originalVertices[i];
    }

    // How far vertex i has been pushed from where it started, in world units
    public float DisplacementAt(int i)
    {
        return (displacedVertices[i] - originalVertices[i]).magnitude * transform.localScale.x;
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
