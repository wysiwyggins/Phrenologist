using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("Title")]
    public TextMeshProUGUI titleText;
    public string finishedTitle = "Character Created";
    public float fadeInDuration = 2f;
    public float titleFadeDuration = 0.6f;

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
    public AudioClip shatterSound;
    public AudioClip statTick;
    public AudioClip statSettle;
    public AudioClip finalizedSound;

    [Header("Stat Counting")]
    public float tickInterval = 0.04f;
    public float tickPitchMin = 0.9f;
    public float tickPitchMax = 1.3f;

    [Header("Round Rules")]
    // Punches allowed before the character is finalized (0 for no limit)
    public int maxPunches = 10;
    // Seconds after the first punch before the character is finalized (0 for no limit)
    public float timeLimit = 15f;
    public Color finalizedColor = new Color(1f, 0.78f, 0.25f);

    [Header("Floor")]
    // Drag the floor's collider here; the head hitting it plays shatterSound and finalizes the character
    [SerializeField] Collider floor;
    public float fullVolumeCrashSpeed = 3f;

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

    enum RoundState { FadingIn, Playing, Finished }
    RoundState state = RoundState.FadingIn;
    int punchCount = 0;
    float firstPunchTime = -1f;
    bool headFell = false;
    // Set once "Character Created" is showing, so a click mid-count doesn't skip the reveal
    bool characterCreated = false;

    // What each stat text currently shows and what it's counting toward, indexed by Stat
    readonly int[] shownStats = new int[7];
    readonly int[] targetStats = new int[7];
    float nextTickTime = 0f;
    bool statsSettled = true;
    Color[] statColors;

    AudioSource uiAudio;
    Image fadeImage;
    Coroutine titleFade;
    Coroutine finalizing;
    string startTitle = "";

    Rigidbody headBody;
    Vector3 headStartPosition;
    Quaternion headStartRotation;

    public bool AcceptingPunches => state == RoundState.Playing;

    // Indexed by Stat, so StatTexts[(int)Stat.Wisdom] is wisText
    TextMeshProUGUI[] StatTexts => new[] { null, strText, dexText, conText, intText, wisText, chaText };

    void Start()
    {
        LoadRegions();

        if (titleText) {
            startTitle = titleText.text;
        }
        statColors = System.Array.ConvertAll(StatTexts, t => t ? t.color : Color.white);

        // A separate 2D source so tick pitch changes don't bend the bonks
        uiAudio = gameObject.AddComponent<AudioSource>();
        uiAudio.playOnAwake = false;
        uiAudio.spatialBlend = 0f;

        CreateFadeOverlay();
        FindHead();
        if (!floor) {
            Debug.LogWarning("GameController has no floor collider assigned, so falls won't be detected");
        }

        StartCoroutine(BeginSubject(false));
    }

    void Update()
    {
        bool resetPressed = Input.GetKeyDown(resetKey) || (characterCreated && Input.GetMouseButtonDown(0));
        if (resetPressed && state != RoundState.FadingIn) {
            StartCoroutine(BeginSubject(true));
        }

        if (state == RoundState.Playing && timeLimit > 0f && firstPunchTime >= 0f && Time.time - firstPunchTime >= timeLimit) {
            Finalize();
        }

        TickStats();
    }

    // A black full-screen image just behind the title, so the title can show over the darkness
    void CreateFadeOverlay()
    {
        if (!titleText) {
            Debug.LogWarning("GameController has no title text, so there's no fade in");
            return;
        }
        GameObject overlay = new GameObject("Fade", typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)overlay.transform;
        rect.SetParent(titleText.canvas.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        if (titleText.transform.parent == rect.parent) {
            rect.SetSiblingIndex(titleText.transform.GetSiblingIndex());
        }
        fadeImage = overlay.GetComponent<Image>();
        fadeImage.color = Color.black;
        fadeImage.raycastTarget = false;
    }

    void FindHead()
    {
        MeshDeformer deformer = FindFirstObjectByType<MeshDeformer>();
        if (deformer) {
            headBody = System.Array.Find(deformer.GetComponentsInParent<Rigidbody>(), b => !b.isKinematic);
        }
        if (!headBody) {
            Debug.LogWarning("GameController couldn't find the head's dynamic Rigidbody");
            return;
        }
        headStartPosition = headBody.position;
        headStartRotation = headBody.rotation;
        headBody.gameObject.AddComponent<HeadImpact>().controller = this;
    }

    // Fades in on a fresh, undented head with new stats. fadeOutFirst fades to black before resetting.
    IEnumerator BeginSubject(bool fadeOutFirst)
    {
        state = RoundState.FadingIn;
        if (fadeOutFirst) {
            SetTitleAlpha(0f, titleFadeDuration);
            yield return Fade(a => SetFade(a), 0f, 1f, titleFadeDuration);
        }

        ResetHead();
        SetFade(1f);
        if (titleText) {
            titleText.text = startTitle;
            titleText.alpha = 0f;
        }

        SetTitleAlpha(1f, fadeInDuration);
        yield return Fade(a => SetFade(a), 1f, 0f, fadeInDuration);
        state = RoundState.Playing;
    }

    void SetFade(float alpha)
    {
        if (fadeImage) {
            fadeImage.color = new Color(0f, 0f, 0f, alpha);
            fadeImage.enabled = alpha > 0f;
        }
    }

    void SetTitleAlpha(float alpha, float duration)
    {
        if (!titleText) {
            return;
        }
        if (titleFade != null) {
            StopCoroutine(titleFade);
        }
        titleFade = StartCoroutine(Fade(a => titleText.alpha = a, titleText.alpha, alpha, duration));
    }

    static IEnumerator Fade(System.Action<float> set, float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime) {
            set(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration)));
            yield return null;
        }
        set(to);
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

    void ResetHead()
    {
        if (headBody) {
            headBody.linearVelocity = Vector3.zero;
            headBody.angularVelocity = Vector3.zero;
            headBody.position = headStartPosition;
            headBody.rotation = headStartRotation;
            headBody.transform.SetPositionAndRotation(headStartPosition, headStartRotation);
        }
        foreach (MeshDeformer deformer in FindObjectsByType<MeshDeformer>(FindObjectsSortMode.None)) {
            deformer.ResetMesh();
        }

        punchCount = 0;
        firstPunchTime = -1f;
        headFell = false;
        characterCreated = false;
        uiPanel.SetActive(false);
        TextMeshProUGUI[] texts = StatTexts;
        for (int s = 1; s < texts.Length; s++) {
            texts[s].color = statColors[s];
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

        if (!uiPanel.activeSelf) {
            uiPanel.SetActive(true);
            System.Array.Clear(shownStats, 0, shownStats.Length);
            ShowStats();
            SetTitleAlpha(0f, titleFadeDuration);
        }
        if (firstPunchTime < 0f) {
            firstPunchTime = Time.time;
        }
        punchCount++;

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

        if (maxPunches > 0 && punchCount >= maxPunches) {
            Finalize();
        }
    }

    void SetStats(Dictionary<Stat, float> pointsLost)
    {
        for (int s = 1; s < targetStats.Length; s++) {
            pointsLost.TryGetValue((Stat)s, out float points);
            targetStats[s] = Mathf.Max(0, startingStats[(Stat)s] - Mathf.RoundToInt(points));
        }
        statsSettled = false;
    }

    void TickStats()
    {
        if (statsSettled || !uiPanel.activeSelf || Time.time < nextTickTime) {
            return;
        }

        bool moved = false;
        for (int s = 1; s < shownStats.Length; s++) {
            if (shownStats[s] != targetStats[s]) {
                shownStats[s] += System.Math.Sign(targetStats[s] - shownStats[s]);
                moved = true;
            }
        }

        if (moved) {
            ShowStats();
            PlayUI(statTick, Random.Range(tickPitchMin, tickPitchMax));
            nextTickTime = Time.time + tickInterval;
        } else {
            statsSettled = true;
            PlayUI(statSettle, 1f);
        }
    }

    void ShowStats()
    {
        TextMeshProUGUI[] texts = StatTexts;
        for (int s = 1; s < texts.Length; s++) {
            texts[s].text = shownStats[s].ToString();
        }
    }

    void PlayUI(AudioClip clip, float pitch)
    {
        if (clip) {
            uiAudio.pitch = pitch;
            uiAudio.PlayOneShot(clip);
        }
    }

    // Called by HeadImpact on the head's Rigidbody
    public void OnHeadCollision(Collision collision)
    {
        if (headFell || !floor || collision.collider != floor || state == RoundState.FadingIn) {
            return;
        }
        headFell = true;
        if (shatterSound) {
            float volume = Mathf.Clamp(collision.relativeVelocity.magnitude / fullVolumeCrashSpeed, 0.3f, 1f);
            audioSource.PlayOneShot(shatterSound, volume);
        }
        Shatter();
    }

    // A broken bust has no character left: every stat snaps to zero and it's finalized on the spot,
    // even if the punch limit had already ended the round
    void Shatter()
    {
        state = RoundState.Finished;
        if (finalizing != null) {
            StopCoroutine(finalizing);
            finalizing = null;
        }

        System.Array.Clear(targetStats, 0, targetStats.Length);
        System.Array.Clear(shownStats, 0, shownStats.Length);
        statsSettled = true;
        uiPanel.SetActive(true);
        ShowStats();
        MarkFinalized();
    }

    // Stops punches and, once the stats finish counting, marks them as final
    void Finalize()
    {
        if (state != RoundState.Playing) {
            return;
        }
        state = RoundState.Finished;
        finalizing = StartCoroutine(ShowFinalized());
    }

    IEnumerator ShowFinalized()
    {
        yield return new WaitUntil(() => statsSettled);
        yield return new WaitForSeconds(0.3f);
        finalizing = null;
        MarkFinalized();
    }

    void MarkFinalized()
    {
        characterCreated = true;
        TextMeshProUGUI[] texts = StatTexts;
        for (int s = 1; s < texts.Length; s++) {
            texts[s].color = finalizedColor;
        }
        PlayUI(finalizedSound, 1f);
        if (titleText) {
            titleText.text = finishedTitle;
            SetTitleAlpha(1f, titleFadeDuration);
        }
    }
}
