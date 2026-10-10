#if UNITY_EDITOR
using System.IO;
using Aquarium.Outgame.Client;
using Aquarium.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Aquarium.Editor
{
    public static class OutgameSceneBuilder
    {
        private const string SceneFolder = "Assets/Aquarium/Scenes";
        private const string ConnectionSettingsPath = "Assets/Settings/AquariumConnectionSettings.asset";

        [MenuItem("Aquarium/Build Title and Home Scenes")]
        public static void Build()
        {
            Directory.CreateDirectory(SceneFolder);
            var connectionSettings = GetOrCreateConnectionSettings();
            BuildOutgame(Path.Combine(SceneFolder, "Title.unity"), "Title", connectionSettings, includeGlobal: true);
            BuildOutgame(Path.Combine(SceneFolder, "Home.unity"), "Home", connectionSettings, includeGlobal: false);
            AddAquariumSceneScope(Path.Combine(SceneFolder, "AquariumOnline.unity"), connectionSettings);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(Path.Combine(SceneFolder, "Title.unity"), true),
                new EditorBuildSettingsScene(Path.Combine(SceneFolder, "Home.unity"), true),
                new EditorBuildSettingsScene(Path.Combine(SceneFolder, "AquariumOnline.unity"), true)
            };
            AssetDatabase.SaveAssets();
        }

        private static AquariumConnectionSettings GetOrCreateConnectionSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<AquariumConnectionSettings>(ConnectionSettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<AquariumConnectionSettings>();
            AssetDatabase.CreateAsset(settings, ConnectionSettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private static void BuildOutgame(string path, string sceneName,
            AquariumConnectionSettings connectionSettings, bool includeGlobal)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = sceneName;
            if (includeGlobal)
            {
                var global = new GameObject("Global Lifetime Scope", typeof(GlobalLifetimeScope));
                global.GetComponent<GlobalLifetimeScope>().Configure(connectionSettings);
            }
            var sceneRoot = new GameObject(sceneName + " Scene Root", typeof(OutgameSceneRoot));
            sceneRoot.GetComponent<OutgameSceneRoot>().UseGlobalParent();
            EditorSceneManager.SaveScene(scene, path);
        }

        private static void AddAquariumSceneScope(string path, AquariumConnectionSettings connectionSettings)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            if (scene.GetRootGameObjects().Length == 0)
                throw new IOException("AquariumOnline scene was unexpectedly empty; it was left unchanged.");
            AquariumSceneLifetimeScope existingScope = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                existingScope = root.GetComponent<AquariumSceneLifetimeScope>();
                if (existingScope != null) break;
            }
            if (existingScope == null)
            {
                var scopeObject = new GameObject("Aquarium Scene Lifetime Scope", typeof(AquariumSceneLifetimeScope));
                existingScope = scopeObject.GetComponent<AquariumSceneLifetimeScope>();
                existingScope.UseGlobalParent();
            }
            foreach (var root in scene.GetRootGameObjects())
            foreach (var game in root.GetComponentsInChildren<OnlineAquariumGame>(true))
                game.Configure(connectionSettings);
            EditorSceneManager.SaveScene(scene, path);
        }
    }
}
#endif
