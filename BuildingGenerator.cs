#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using static UnityEngine.Mathf;

using Config = Solver1D.Config;

public class BuildingGenerator : MonoBehaviour
{
    public Config newConfig = new Config();
    public float newPlaySpeed = 1f;
    public bool newEnableTexture = false;

    Config config;
    float playSpeed;
    bool enableTexture;

    public bool rebuild = true;
    public bool running = false;
    float currentTime;
    Texture2D texture;

    GameObject building;
    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    Mesh mesh;
    const int n = 4;
    Vector3[] baseVertices;

    Vector3[] vertices;
    int[] triangles;
    Vector2[] uv0;
    Vector4[] uv1;  // (xOffset, yOffset, zOffset, Red)

    Solver1D solver;

    void Awake()
    {
        building = new GameObject("Building");
        building.transform.SetParent(transform);
        meshFilter = building.AddComponent<MeshFilter>();
        meshRenderer = building.AddComponent<MeshRenderer>();
        mesh = new Mesh
        {
            name = "BuildingMesh",
        };
        meshFilter.mesh = mesh;

        meshRenderer.material = Resources.Load<Material>("BuildingGeneratorMaterial");
        texture = Resources.Load<Texture2D>("Wall");
    }

    void Update()
    {
        if (rebuild)
        {
            Rebuild();
            return;
        }
        if (running) UpdateVertices();
    }

    void Rebuild()
    {
        config = new Config(newConfig);
        playSpeed = newPlaySpeed;
        enableTexture = newEnableTexture;

        solver = new Solver1D(config);
        solver.Solve();

        baseVertices = new Vector3[] {
            // transform.TransformPoint(new Vector3(-config.xWidth / 2, 0, -config.zWidth / 2)),
            // transform.TransformPoint(new Vector3(config.xWidth / 2, 0, -config.zWidth / 2)),
            // transform.TransformPoint(new Vector3(config.xWidth / 2, 0, config.zWidth / 2)),
            // transform.TransformPoint(new Vector3(-config.xWidth / 2, 0, config.zWidth / 2))
            new Vector3(-config.xWidth / 2, 0, -config.zWidth / 2),
            new Vector3(config.xWidth / 2, 0, -config.zWidth / 2),
            new Vector3(config.xWidth / 2, 0, config.zWidth / 2),
            new Vector3(-config.xWidth / 2, 0, config.zWidth / 2)
        };

        vertices = new Vector3[config.levels * 2 * n + n];
        triangles = new int[(config.levels * 2 * n + n - 2) * 3];
        uv0 = new Vector2[vertices.Length];
        uv1 = new Vector4[vertices.Length];

        for (int level = 0; level < config.levels; ++level)
        {
            int vi = level * 2 * n, ti = level * 2 * n * 3;
            float lowerHeight = config.levelHeight * level;
            float upperHeight = config.levelHeight * (level + 1);

            for (int i = 0; i < n; ++i)
            {
                vertices[vi + i] = baseVertices[i] + new Vector3(0f, lowerHeight, 0f);
                vertices[vi + n + i] = baseVertices[i] + new Vector3(0f, upperHeight, 0f);

                uv0[vi + i] = new Vector2(i & 1, 0f);
                uv0[vi + n + i] = new Vector2(i & 1, 1f);

                uv1[vi + i] = Vector4.zero;
                uv1[vi + n + i] = Vector4.zero;

                int p00 = vi + i;
                int p10 = vi + (i + 1) % n;
                int p01 = p00 + n;
                int p11 = p10 + n;

                triangles[ti++] = p00;
                triangles[ti++] = p01;
                triangles[ti++] = p10;

                triangles[ti++] = p01;
                triangles[ti++] = p11;
                triangles[ti++] = p10;
            }
        }

        {
            // ceiling
            int vi = config.levels * 2 * n, ti = config.levels * 2 * n * 3;
            float height = config.levelHeight * config.levels;

            for (int i = 0; i < n; ++i)
            {
                vertices[vi + i] = baseVertices[i] + new Vector3(0f, height, 0f);
                uv0[vi + i] = Vector2.zero;
                uv1[vi + i] = Vector4.zero;
            }

            for (int i = 0; i < n - 2; ++i)
            {
                triangles[ti++] = vi;
                triangles[ti++] = vi + i + 2;
                triangles[ti++] = vi + i + 1;
            }
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv0;
        mesh.SetUVs(1, uv1);

        meshRenderer.material.SetTexture("_MainTex", enableTexture ? texture : Texture2D.whiteTexture);
        meshRenderer.material.SetInteger("_EnableTexture", enableTexture ? 1 : 0);

        // mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        rebuild = false;
        currentTime = 0f;
        // running = config.mode != ShakeMode.Static;
    }

    void UpdateVertices()
    {
        currentTime += Time.deltaTime * playSpeed;
        solver.SetTime(currentTime);

        for (int level = 1; level <= config.levels; ++level)
        {
            Vector3 response = solver.GetResponse(level);
            float displacement = response.x;
            float torsion = response.y;
            float red = response.z;

            int start = level * 2 * n - n;
            int end = level == config.levels ? vertices.Length : level * 2 * n + n;
            for (int i = start; i < end; ++i)
            {
                Vector3 v = vertices[i];
                Vector3 pos =
                    new Vector3(displacement, 0f, config.zCenterOffset)
                    + Quaternion.Euler(0f, -torsion / PI * 180, 0f)
                    * new Vector3(v.x, 0, v.z - config.zCenterOffset);
                uv1[i] = new Vector4(pos.x - v.x, 0f, pos.z - v.z, red);
            }
        }

        mesh.SetUVs(1, uv1);
        // mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        // if (config.mode == ShakeMode.Response && currentTime >= config.duration) running = false;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(BuildingGenerator))]
public class BuildingEditor : Editor
{
    BuildingGenerator generator;

    void OnEnable() => generator = target as BuildingGenerator;

    public override void OnInspectorGUI()
    {
        // base.OnInspectorGUI();
        generator.newPlaySpeed = EditorGUILayout.Slider("Play Speed", generator.newPlaySpeed, 0.1f, 10f);
        generator.newEnableTexture = EditorGUILayout.Toggle("Enable Texture", generator.newEnableTexture);
        generator.newConfig.SetInspectorGUI();

        if (GUILayout.Button("Load")) generator.rebuild = true;
        if (GUILayout.Button("Stop")) generator.running = false;
    }
}
#endif