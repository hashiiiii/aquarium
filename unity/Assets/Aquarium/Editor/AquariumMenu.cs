using UnityEditor;
using UnityEditor.SceneManagement;

namespace Aquarium.Editor
{
    public static class AquariumMenu
    {
        // Preserve the original shortcut for existing users while naming both modes explicitly.
        [MenuItem("Aquarium/Open First Reef")]
        [MenuItem("Aquarium/Open Offline First Reef")]
        public static void OpenDemo() => Open("Assets/Aquarium/Scenes/AquariumDemo.unity");

        [MenuItem("Aquarium/Open Online First Reef")]
        public static void OpenOnlineDemo() => Open("Assets/Aquarium/Scenes/AquariumOnline.unity");

        private static void Open(string path)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(path);
        }
    }
}
