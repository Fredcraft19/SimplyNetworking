#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class NetworkSetupWizard : EditorWindow
{
    private const string SETTINGS_PATH = "Assets/Resources/NetworkSettings.asset";
    private NetworkSettings _settings;

    static NetworkSetupWizard()
    {
        EditorApplication.delayCall += CheckFirstTimeSetup;
    }

    private static void CheckFirstTimeSetup()
    {
        if (Application.isPlaying) return;

        NetworkSettings settings = Resources.Load<NetworkSettings>("NetworkSettings");
        if (settings == null)
        {
            ShowWindow();
        }
    }

    [MenuItem("Tools/Networking/Setup Engine")]
    public static void ShowWindow()
    {
        NetworkSetupWizard window = GetWindow<NetworkSetupWizard>("Network Engine Setup");
        window.minSize = new Vector2(350, 700);
        window.maxSize = new Vector2(350, 700);
        window.Show();
    }

    private void OnEnable()
    {
        LoadOrCreateSettings();
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        EditorGUILayout.LabelField("Network Engine Configuration", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Configure your default server settings below. These will be used at runtime.", MessageType.Info);
        GUILayout.Space(10);

        if (_settings != null)
        {
            EditorGUI.BeginChangeCheck();

            _settings.serverIp = EditorGUILayout.TextField("Server IP:", _settings.serverIp);
            _settings.serverPort = (ushort)EditorGUILayout.IntField("Server Port:", _settings.serverPort);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_settings);
            }

            GUILayout.Space(15);

            if (GUILayout.Button("Save & Close", GUILayout.Height(30)))
            {
                AssetDatabase.SaveAssets();
                Close();
            }
        }
    }

    private void LoadOrCreateSettings()
    {
        _settings = Resources.Load<NetworkSettings>("NetworkSettings");

        if (_settings == null)
        {
            // Ensure Resources directory exists
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            _settings = CreateInstance<NetworkSettings>();
            AssetDatabase.CreateAsset(_settings, SETTINGS_PATH);
            AssetDatabase.SaveAssets();
            Debug.Log("[NetworkEngine] Created default NetworkSettings asset in Assets/Resources/");
        }
    }
}
#endif