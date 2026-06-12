using UnityEngine;
using UnityEditor;

public class PopupWindowNeon : EditorWindow
{
    private static readonly bool AutoOpenOnEditorLoad = false;

    private Texture2D imageTop;
    private Texture2D image1;
    private Texture2D image2;
    private Texture2D image3;
    private Texture2D image4;
    private Texture2D imageBottom;

    [MenuItem("Window/Daelonik Artworks/Neon City Bundle")]
    public static void ShowWindow()
    {
        PopupWindowNeon window = GetWindow<PopupWindowNeon>("Enlaces Web");
        window.minSize = new Vector2(900, 1200);
    }

    private void OnEnable()
    {
        imageTop = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_DLNK/Neon City/BundleSource/images/TopImage.png");
        image1 = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_DLNK/Neon City/BundleSource/images/asset00.png");
        image2 = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_DLNK/Neon City/BundleSource/images/asset01.png");
        image3 = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_DLNK/Neon City/BundleSource/images/asset02.png");
        image4 = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_DLNK/Neon City/BundleSource/images/asset03.png");
        imageBottom = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_DLNK/Neon City/BundleSource/images/BottomImage.png");
    }

    private void OnGUI()
    {
        GUILayout.Space(30);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (imageTop)
        {
            GUILayout.Label(imageTop, GUILayout.Width(500), GUILayout.Height(100));
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("NEON CITY BUNDLE ASSET INFO", GUILayout.Width(500), GUILayout.Height(30)))
        {
            Application.OpenURL("http://www.daelonik.com/asset-store/environments/neon-city-bundle");
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(30);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label("With NEON CITY BUNDLE you gain access to the Neon City asset, Neon Buildings Premium blueprints and Expansion packs: High City and Underground.\n\nClick on the images below for a quick link to the asset store. Once you've purchased the bundle all them must appear as FREE.\n\nFor a QUICK START GUIDE click on the NEON CITY BUNDLE image above. You can find there tips to import correctly using every pipeline.", EditorStyles.wordWrappedLabel, GUILayout.Width(800));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(30);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (image1 && GUILayout.Button(image1, GUILayout.Width(420), GUILayout.Height(280)))
        {
            Application.OpenURL("https://assetstore.unity.com/packages/3d/environments/sci-fi/scifi-neon-city-118580");
        }
        GUILayout.FlexibleSpace();
        if (image2 && GUILayout.Button(image2, GUILayout.Width(420), GUILayout.Height(280)))
        {
            Application.OpenURL("https://assetstore.unity.com/packages/3d/environments/sci-fi/scifi-neon-buildings-258220");
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("ASSET INFO", GUILayout.Width(420), GUILayout.Height(30)))
        {
            Application.OpenURL("http://www.daelonik.com/asset-store/environments/neon-city/");
        }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("ASSET INFO", GUILayout.Width(420), GUILayout.Height(30)))
        {
            Application.OpenURL("http://www.daelonik.com/asset-store/environments/neon-buildings/");
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(30);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (image3 && GUILayout.Button(image3, GUILayout.Width(420), GUILayout.Height(280)))
        {
            Application.OpenURL("https://assetstore.unity.com/packages/3d/environments/sci-fi/scifi-neon-high-city-274229");
        }
        GUILayout.FlexibleSpace();
        if (image4 && GUILayout.Button(image4, GUILayout.Width(420), GUILayout.Height(280)))
        {
            Application.OpenURL("https://assetstore.unity.com/packages/3d/environments/sci-fi/scifi-neon-underground-296561");
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("ASSET INFO", GUILayout.Width(420), GUILayout.Height(30)))
        {
            Application.OpenURL("http://www.daelonik.com/asset-store/environments/neon-high-city/");
        }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("ASSET INFO", GUILayout.Width(420), GUILayout.Height(30)))
        {
            Application.OpenURL("http://www.daelonik.com/asset-store/environments/neon-underground/");
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(30);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("CONTACT AND SUPPORT FROM DAELONIK", GUILayout.Width(500), GUILayout.Height(30)))
        {
            Application.OpenURL("http://www.daelonik.com/contact/");
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(30);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (imageBottom && GUILayout.Button(imageBottom, GUILayout.Width(500), GUILayout.Height(100)))
        {
            Application.OpenURL("https://assetstore.unity.com/publishers/3021");
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    [InitializeOnLoadMethod]
    private static void InitOnLoad()
    {
        if (AutoOpenOnEditorLoad)
        {
            EditorApplication.delayCall += ShowWindow;
        }
    }
}
