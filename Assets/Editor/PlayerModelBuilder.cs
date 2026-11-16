using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds simple blocky Runner and Granny bodies onto Assets/Prefabs/OnlinePlayer.prefab.
/// Re-runnable: replaces the "Avatar" child each time. Units are the prefab's local space:
/// the CharacterController spans y -1.1..1.1 (scaled to 1.60 m), forward is +z.
/// </summary>
public static class PlayerModelBuilder
{
    private const string PrefabPath = "Assets/Prefabs/OnlinePlayer.prefab";
    private const string MaterialFolder = "Assets/Prefabs/PlayerModel";

    [MenuItem("Tools/Player/Build Player Models")]
    public static void Build()
    {
        if (!Directory.Exists(MaterialFolder))
        {
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform old = root.transform.Find("Avatar");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            // The old capsule stays as a fallback but is hidden once the bodies exist.
            MeshRenderer capsule = root.GetComponent<MeshRenderer>();
            if (capsule != null)
                capsule.enabled = false;

            GameObject avatarRoot = new("Avatar");
            avatarRoot.transform.SetParent(root.transform, false);
            GameObject runner = BuildRunner(avatarRoot.transform);
            GameObject granny = BuildGranny(avatarRoot.transform);
            granny.SetActive(false);

            PlayerAvatar avatar = root.GetComponent<PlayerAvatar>() ?? root.AddComponent<PlayerAvatar>();
            avatar.Configure(runner, granny);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[PlayerModelBuilder] Runner and Granny bodies built on OnlinePlayer.prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
    }

    private static GameObject BuildRunner(Transform parent)
    {
        Material skin = Mat("M_Player_Skin", new Color(0.86f, 0.66f, 0.52f), 0.7f);
        Material hoodie = Mat("M_Player_Hoodie", new Color(0.13f, 0.42f, 0.78f), 0.8f);
        Material jeans = Mat("M_Player_Jeans", new Color(0.12f, 0.15f, 0.24f), 0.85f);
        Material shoes = Mat("M_Player_Shoes", new Color(0.92f, 0.92f, 0.9f), 0.6f);
        Material hair = Mat("M_Player_HairDark", new Color(0.12f, 0.08f, 0.06f), 0.9f);
        Material bag = Mat("M_Player_Backpack", new Color(0.85f, 0.45f, 0.1f), 0.8f);
        Material eye = Mat("M_Player_Eye", new Color(0.05f, 0.05f, 0.06f), 0.3f);

        GameObject body = new("Runner Model");
        body.transform.SetParent(parent, false);
        Transform t = body.transform;

        Leg(t, "Leg L", -0.14f, jeans, shoes);
        Leg(t, "Leg R", 0.14f, jeans, shoes);
        Part(t, "Torso", PrimitiveType.Cube, new Vector3(0f, 0.25f, 0f), new Vector3(0.56f, 0.8f, 0.32f), hoodie);
        Part(t, "Hood", PrimitiveType.Cube, new Vector3(0f, 0.62f, -0.1f), new Vector3(0.42f, 0.14f, 0.2f), hoodie);
        Part(t, "Pocket", PrimitiveType.Cube, new Vector3(0f, 0.05f, 0.165f), new Vector3(0.36f, 0.16f, 0.02f),
            Mat("M_Player_HoodieDark", new Color(0.09f, 0.3f, 0.58f), 0.8f));
        Part(t, "Backpack", PrimitiveType.Cube, new Vector3(0f, 0.3f, -0.26f), new Vector3(0.44f, 0.5f, 0.2f), bag);
        Arm(t, "Arm L", -0.37f, hoodie, skin);
        Arm(t, "Arm R", 0.37f, hoodie, skin);
        Head(t, skin, eye);
        Part(t, "Hair", PrimitiveType.Sphere, new Vector3(0f, 0.97f, -0.03f), new Vector3(0.4f, 0.22f, 0.4f), hair);
        return body;
    }

    private static GameObject BuildGranny(Transform parent)
    {
        Material skin = Mat("M_Player_SkinOld", new Color(0.88f, 0.72f, 0.62f), 0.75f);
        Material cardigan = Mat("M_Player_Cardigan", new Color(0.45f, 0.18f, 0.32f), 0.9f);
        Material dress = Mat("M_Player_Dress", new Color(0.32f, 0.3f, 0.36f), 0.9f);
        Material stockings = Mat("M_Player_Stockings", new Color(0.7f, 0.6f, 0.55f), 0.8f);
        Material shoes = Mat("M_Player_ShoesBlack", new Color(0.06f, 0.05f, 0.05f), 0.5f);
        Material hair = Mat("M_Player_HairGrey", new Color(0.82f, 0.82f, 0.84f), 0.9f);
        Material eye = Mat("M_Player_EyeRed", new Color(0.6f, 0.05f, 0.05f), 0.3f, new Color(1.2f, 0.05f, 0.05f));
        Material wood = Mat("M_Player_Cane", new Color(0.35f, 0.2f, 0.1f), 0.7f);
        Material glasses = Mat("M_Player_Glasses", new Color(0.75f, 0.65f, 0.3f), 0.3f, null, 0.8f);

        GameObject body = new("Granny Model");
        body.transform.SetParent(parent, false);
        Transform t = body.transform;

        Leg(t, "Leg L", -0.12f, stockings, shoes);
        Leg(t, "Leg R", 0.12f, stockings, shoes);
        Part(t, "Skirt", PrimitiveType.Cylinder, new Vector3(0f, -0.3f, 0f), new Vector3(0.66f, 0.32f, 0.5f), dress);
        Part(t, "Torso", PrimitiveType.Cube, new Vector3(0f, 0.22f, 0.02f), new Vector3(0.56f, 0.76f, 0.36f), cardigan);
        Part(t, "Hunch", PrimitiveType.Sphere, new Vector3(0f, 0.52f, -0.12f), new Vector3(0.5f, 0.3f, 0.3f), cardigan);
        Arm(t, "Arm L", -0.37f, cardigan, skin);
        Transform armRight = Arm(t, "Arm R", 0.37f, cardigan, skin);
        Part(armRight, "Cane", PrimitiveType.Cylinder, new Vector3(0f, -0.55f, 0.12f), new Vector3(0.05f, 0.42f, 0.05f), wood);
        Head(t, skin, eye);
        Part(t, "Hair", PrimitiveType.Sphere, new Vector3(0f, 0.96f, -0.03f), new Vector3(0.42f, 0.24f, 0.42f), hair);
        Part(t, "Bun", PrimitiveType.Sphere, new Vector3(0f, 1.0f, -0.2f), new Vector3(0.2f, 0.2f, 0.2f), hair);
        Part(t, "Glasses", PrimitiveType.Cube, new Vector3(0f, 0.9f, 0.19f), new Vector3(0.3f, 0.05f, 0.02f), glasses);
        return body;
    }

    private static void Head(Transform parent, Material skin, Material eye)
    {
        Part(parent, "Neck", PrimitiveType.Cylinder, new Vector3(0f, 0.68f, 0f), new Vector3(0.14f, 0.05f, 0.14f), skin);
        Part(parent, "Head", PrimitiveType.Sphere, new Vector3(0f, 0.88f, 0f), new Vector3(0.38f, 0.4f, 0.38f), skin);
        Part(parent, "Eye L", PrimitiveType.Sphere, new Vector3(-0.08f, 0.9f, 0.17f), new Vector3(0.06f, 0.06f, 0.04f), eye);
        Part(parent, "Eye R", PrimitiveType.Sphere, new Vector3(0.08f, 0.9f, 0.17f), new Vector3(0.06f, 0.06f, 0.04f), eye);
    }

    /// <summary>A leg pivoting at the hip (y -0.12) down to the sole (y -1.1).</summary>
    private static void Leg(Transform parent, string name, float x, Material cloth, Material shoe)
    {
        Transform hip = new GameObject(name).transform;
        hip.SetParent(parent, false);
        hip.localPosition = new Vector3(x, -0.12f, 0f);
        Part(hip, "Thigh", PrimitiveType.Capsule, new Vector3(0f, -0.42f, 0f), new Vector3(0.21f, 0.42f, 0.21f), cloth);
        Part(hip, "Shoe", PrimitiveType.Cube, new Vector3(0f, -0.92f, 0.06f), new Vector3(0.2f, 0.12f, 0.34f), shoe);
    }

    /// <summary>An arm pivoting at the shoulder (y 0.6).</summary>
    private static Transform Arm(Transform parent, string name, float x, Material sleeve, Material skin)
    {
        Transform shoulder = new GameObject(name).transform;
        shoulder.SetParent(parent, false);
        shoulder.localPosition = new Vector3(x, 0.58f, 0f);
        Part(shoulder, "Sleeve", PrimitiveType.Capsule, new Vector3(0f, -0.32f, 0f), new Vector3(0.16f, 0.34f, 0.16f), sleeve);
        Part(shoulder, "Hand", PrimitiveType.Sphere, new Vector3(0f, -0.7f, 0f), new Vector3(0.13f, 0.13f, 0.13f), skin);
        return shoulder;
    }

    private static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part;
    }

    private static Material Mat(string name, Color color, float roughness, Color? emission = null, float metallic = 0f)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", color);
        material.color = color;
        material.SetFloat("_Smoothness", 1f - roughness);
        material.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
        }

        EditorUtility.SetDirty(material);
        return material;
    }
}
