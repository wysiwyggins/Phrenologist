using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class GameController : MonoBehaviour
{
    public enum Stat { None, Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma }

    class Region
    {
        public string name;
        public Stat stat;
        public int[] vertices;
        // How many vertices the recording dent moved, before overlapping vertices were handed out
        public int footprintSize;
    }

    [System.Serializable]
    class RegionSlot
    {
        public string name;
        public TextMeshProUGUI statText;
    }

    [Header("Stats UI")]
    [SerializeField] GameObject uiPanel;
    [SerializeField] TextMeshProUGUI strText;
    [SerializeField] TextMeshProUGUI dexText;
    [SerializeField] TextMeshProUGUI conText;
    [SerializeField] TextMeshProUGUI intText;
    [SerializeField] TextMeshProUGUI wisText;
    [SerializeField] TextMeshProUGUI chaText;

    // Drag a stat's text field into a region to make dents there lower that stat
    [Header("Region Stats")]
    [SerializeField] List<RegionSlot> regionStats = new List<RegionSlot>();

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip[] bonks;

    [Header("Controls")]
    public KeyCode resetKey = KeyCode.R;

    [Header("Stat Rules")]
    public int minStartingStat = 15;
    public int maxStartingStat = 20;
    public bool logRegions = false;

    readonly List<Region> regions = new List<Region>();
    readonly Dictionary<Stat, int> startingStats = new Dictionary<Stat, int>();
    // Set once regions no longer share vertices; waits for the first punch so the mesh is loaded
    bool regionsPartitioned = false;

    // Indexed by Stat, so StatTexts[(int)Stat.Wisdom] is wisText
    TextMeshProUGUI[] StatTexts => new[] { null, strText, dexText, conText, intText, wisText, chaText };

    void Start()
    {
        LoadRegions();
        RollStats();
        uiPanel.SetActive(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(resetKey)) {
            ResetHead();
        }
    }

    // Keeps the region list in step with HeadRegions.txt, filling empty slots with the file's stat
    void OnValidate()
    {
        List<RegionSlot> synced = new List<RegionSlot>();
        foreach ((string name, Stat stat, string _) in ReadRegionFile(false)) {
            RegionSlot slot = regionStats.Find(s => s.name == name) ?? new RegionSlot { name = name };
            if (!slot.statText) {
                slot.statText = StatTexts[(int)stat];
            }
            synced.Add(slot);
        }
        if (synced.Count > 0) {
            regionStats = synced;
        }
    }

    // One "Name | Stat | 1, 2, 3" line per region in Assets/Resources/HeadRegions.txt
    static IEnumerable<(string name, Stat stat, string vertices)> ReadRegionFile(bool warn)
    {
        TextAsset file = Resources.Load<TextAsset>("HeadRegions");
        if (!file) {
            if (warn) {
                Debug.LogError("GameController couldn't find Resources/HeadRegions.txt");
            }
            yield break;
        }

        foreach (string rawLine in file.text.Split('\n')) {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#")) {
                continue;
            }
            string[] parts = line.Split('|');
            if (parts.Length != 3 || !System.Enum.TryParse(parts[1].Trim(), out Stat stat)) {
                if (warn) {
                    Debug.LogWarning($"Skipping malformed head region line: {line[..Mathf.Min(line.Length, 60)]}");
                }
                continue;
            }
            yield return (parts[0].Trim(), stat, parts[2]);
        }
    }

    void LoadRegions()
    {
        foreach ((string name, Stat fileStat, string vertexList) in ReadRegionFile(true)) {
            List<int> vertices = new List<int>();
            foreach (string index in vertexList.Split(',')) {
                if (int.TryParse(index.Trim(), out int i)) {
                    vertices.Add(i);
                }
            }
            if (vertices.Count == 0) {
                continue;
            }
            regions.Add(new Region { name = name, stat = InspectorStat(name, fileStat), vertices = vertices.ToArray(), footprintSize = vertices.Count });
        }
    }

    // The stat whose text field is dragged into this region's slot, or fileStat if there isn't one
    Stat InspectorStat(string regionName, Stat fileStat)
    {
        TextMeshProUGUI field = regionStats.Find(s => s.name == regionName)?.statText;
        if (!field) {
            return fileStat;
        }
        int index = System.Array.IndexOf(StatTexts, field);
        if (index <= 0) {
            Debug.LogWarning($"{regionName}'s text field {field.name} isn't one of the Stats UI text fields; keeping {fileStat}");
            return fileStat;
        }
        return (Stat)index;
    }

    // Undents every head and rolls fresh stats, as if a new subject sat down
    void ResetHead()
    {
        foreach (MeshDeformer deformer in FindObjectsByType<MeshDeformer>(FindObjectsSortMode.None)) {
            deformer.ResetMesh();
        }
        RollStats();
    }

    void RollStats()
    {
        foreach (Stat stat in System.Enum.GetValues(typeof(Stat))) {
            startingStats[stat] = Random.Range(minStartingStat, maxStartingStat + 1);
        }
        SetStats(new Dictionary<Stat, float>());
    }

    // Recorded regions overlap because each is a whole dent's footprint. This gives every shared vertex to
    // whichever of its regions has the nearest center, so a punch only bleeds into neighbors near their border.
    // Vertices no region recorded stay unassigned.
    void PartitionRegions(MeshDeformer deformer)
    {
        int vertexCount = deformer.displacedVertices.Length;
        foreach (Region region in regions) {
            region.vertices = System.Array.FindAll(region.vertices, i => i < vertexCount);
        }

        Vector3[] centers = new Vector3[regions.Count];
        for (int r = 0; r < regions.Count; r++) {
            Vector3 sum = Vector3.zero;
            foreach (int i in regions[r].vertices) {
                sum += deformer.OriginalPosition(i);
            }
            centers[r] = regions[r].vertices.Length > 0 ? sum / regions[r].vertices.Length : Vector3.zero;
        }

        int[] owner = new int[vertexCount];
        float[] ownerDistance = new float[vertexCount];
        System.Array.Fill(ownerDistance, float.MaxValue);
        for (int r = 0; r < regions.Count; r++) {
            foreach (int i in regions[r].vertices) {
                float distance = (deformer.OriginalPosition(i) - centers[r]).sqrMagnitude;
                if (distance < ownerDistance[i]) {
                    owner[i] = r;
                    ownerDistance[i] = distance;
                }
            }
        }

        for (int r = 0; r < regions.Count; r++) {
            int[] kept = System.Array.FindAll(regions[r].vertices, i => owner[i] == r);
            if (logRegions) {
                Debug.Log($"{regions[r].name} keeps {kept.Length} of {regions[r].vertices.Length} vertices");
            }
            regions[r].vertices = kept;
        }

        regionsPartitioned = true;
    }

    public void OnPunched(MeshDeformer deformer)
    {
        if (!regionsPartitioned) {
            PartitionRegions(deformer);
        }

        audioSource.PlayOneShot(bonks[Random.Range(0, bonks.Length)]);
        uiPanel.SetActive(true);

        float oneDentAverage = deformer.depth / 6f;

        Dictionary<Stat, float> pointsLost = new Dictionary<Stat, float>();
        foreach (Region region in regions) {
            float total = 0f;
            foreach (int i in region.vertices) {
                total += deformer.DisplacementAt(i);
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
        TextMeshProUGUI[] texts = StatTexts;
        for (int s = 1; s < texts.Length; s++) {
            pointsLost.TryGetValue((Stat)s, out float points);
            int value = Mathf.Max(0, startingStats[(Stat)s] - Mathf.RoundToInt(points));
            texts[s].text = value.ToString();
        }
    }
}
