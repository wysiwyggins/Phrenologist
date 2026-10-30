using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class GameController : MonoBehaviour
{
    public enum Stat { None, Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma }

    // A named patch of the head and the stat that dents there lower
    class Region
    {
        public string name;
        public Stat stat;
        public int[] vertices;
        // How many vertices the recording dent moved, before overlapping vertices were handed out
        public int footprintSize;
    }

    [Header("Stats UI")]
    [SerializeField] private GameObject uiPanel;
    public int strVal;
    [SerializeField] private TextMeshProUGUI strText;
    public int dexVal;
    [SerializeField] private TextMeshProUGUI dexText;
    public int conVal;
    [SerializeField] private TextMeshProUGUI conText;
    public int intVal;
    [SerializeField] private TextMeshProUGUI intText;
    public int wisVal;
    [SerializeField] private TextMeshProUGUI wisText;
    public int chaVal;
    [SerializeField] private TextMeshProUGUI chaText;

    [Header("Controls")]
    public KeyCode resetKey = KeyCode.R;

    [Header("Stat Rules")]
    public int minStartingStat = 15;
    public int maxStartingStat = 20;
    // Logs each region's dent count after every punch, for tuning
    public bool logRegions = false;

    List<Region> regions = new List<Region>();
    Dictionary<Stat, int> startingStats = new Dictionary<Stat, int>();
    // Set once regions no longer share vertices; waits for the first punch so the mesh is loaded
    bool regionsPartitioned = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        LoadRegions();
        RollStats();
    }

    void Update()
    {
        if (Input.GetKeyDown(resetKey)) {
            ResetHead();
        }
    }

    // Undents every head and rolls fresh stats, as if a new subject sat down
    void ResetHead()
    {
        foreach (MeshDeformer deformer in FindObjectsByType<MeshDeformer>(FindObjectsSortMode.None)) {
            deformer.ResetMesh();
        }
        RollStats();
    }

    // Each head starts out a little different, then dents only ever subtract
    void RollStats()
    {
        foreach (Stat stat in System.Enum.GetValues(typeof(Stat))) {
            startingStats[stat] = Random.Range(minStartingStat, maxStartingStat + 1);
        }
        SetStats(new Dictionary<Stat, float>());
    }

    // Reads Assets/Resources/HeadRegions.txt: one "Name | Stat | 1, 2, 3" line per region
    void LoadRegions()
    {
        TextAsset file = Resources.Load<TextAsset>("HeadRegions");
        if (!file) {
            Debug.LogError("GameController couldn't find Resources/HeadRegions.txt");
            return;
        }

        foreach (string rawLine in file.text.Split('\n')) {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#")) {
                continue;
            }

            string[] parts = line.Split('|');
            if (parts.Length != 3 || !System.Enum.TryParse(parts[1].Trim(), out Stat stat)) {
                Debug.LogWarning($"Skipping malformed head region line: {line.Substring(0, Mathf.Min(line.Length, 60))}");
                continue;
            }

            List<int> vertices = new List<int>();
            foreach (string index in parts[2].Split(',')) {
                if (int.TryParse(index.Trim(), out int i)) {
                    vertices.Add(i);
                }
            }
            if (vertices.Count == 0) {
                continue;
            }
            regions.Add(new Region { name = parts[0].Trim(), stat = stat, vertices = vertices.ToArray(), footprintSize = vertices.Count });
        }
    }

    // Recorded regions overlap because each is a whole dent's footprint. This gives every shared vertex to
    // whichever of its regions has the nearest center, so a punch only bleeds into neighbors near their border.
    // Vertices no region recorded stay unassigned.
    void PartitionRegions(MeshDeformer deformer)
    {
        int vertexCount = deformer.displacedVertices.Length;

        Vector3[] centers = new Vector3[regions.Count];
        for (int r = 0; r < regions.Count; r++) {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (int i in regions[r].vertices) {
                if (i < vertexCount) {
                    sum += deformer.OriginalPosition(i);
                    count++;
                }
            }
            centers[r] = count > 0 ? sum / count : Vector3.zero;
        }

        // For each vertex, the nearest region among those that recorded it
        int[] owner = new int[vertexCount];
        float[] ownerDistance = new float[vertexCount];
        for (int i = 0; i < vertexCount; i++) {
            owner[i] = -1;
            ownerDistance[i] = float.MaxValue;
        }
        for (int r = 0; r < regions.Count; r++) {
            foreach (int i in regions[r].vertices) {
                if (i >= vertexCount) {
                    continue;
                }
                float distance = (deformer.OriginalPosition(i) - centers[r]).sqrMagnitude;
                if (distance < ownerDistance[i]) {
                    owner[i] = r;
                    ownerDistance[i] = distance;
                }
            }
        }

        for (int r = 0; r < regions.Count; r++) {
            List<int> kept = new List<int>();
            foreach (int i in regions[r].vertices) {
                if (i < vertexCount && owner[i] == r) {
                    kept.Add(i);
                }
            }
            if (logRegions) {
                Debug.Log($"{regions[r].name} keeps {kept.Count} of {regions[r].vertices.Length} vertices");
            }
            regions[r].vertices = kept.ToArray();
        }

        regionsPartitioned = true;
    }

    // Called by MeshDeformer after every dent
    public void OnPunched(MeshDeformer deformer)
    {
        if (!regionsPartitioned) {
            PartitionRegions(deformer);
        }

        // One dent's falloff is (1 - d/r)^2, which averages to about 1/6 of its peak depth across the circle it touches.
        // Dividing a region's total displacement by what one dent spreads over its original footprint gives about
        // how many dents it has taken. Most of a dent centered on the region lands in the vertices it kept.
        float oneDentAverage = deformer.depth / 6f;

        Dictionary<Stat, float> pointsLost = new Dictionary<Stat, float>();
        foreach (Region region in regions) {
            float total = 0f;
            foreach (int i in region.vertices) {
                if (i < deformer.displacedVertices.Length) {
                    total += deformer.DisplacementAt(i);
                }
            }
            float regionDents = total / region.footprintSize / oneDentAverage;

            pointsLost.TryGetValue(region.stat, out float soFar);
            pointsLost[region.stat] = soFar + regionDents * deformer.PointsPerDent(region.stat);

            if (logRegions) {
                Debug.Log($"{region.name} ({region.stat}): {regionDents:F2} dents");
            }
        }

        SetStats(pointsLost);
    }

    void SetStats(Dictionary<Stat, float> pointsLost)
    {
        strVal = StatValue(Stat.Strength, pointsLost);
        dexVal = StatValue(Stat.Dexterity, pointsLost);
        conVal = StatValue(Stat.Constitution, pointsLost);
        intVal = StatValue(Stat.Intelligence, pointsLost);
        wisVal = StatValue(Stat.Wisdom, pointsLost);
        chaVal = StatValue(Stat.Charisma, pointsLost);

        strText.text = strVal.ToString();
        dexText.text = dexVal.ToString();
        conText.text = conVal.ToString();
        intText.text = intVal.ToString();
        wisText.text = wisVal.ToString();
        chaText.text = chaVal.ToString();
    }

    int StatValue(Stat stat, Dictionary<Stat, float> pointsLost)
    {
        pointsLost.TryGetValue(stat, out float points);
        return Mathf.Clamp(startingStats[stat] - Mathf.RoundToInt(points), 0, 20);
    }
}
