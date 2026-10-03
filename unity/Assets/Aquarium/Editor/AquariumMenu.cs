using UnityEditor;
using UnityEditor.SceneManagement;

namespace Aquarium.Editor
{
    public static class AquariumMenu
    {
        [MenuItem("Aquarium/Open First Reef")]
        public static void OpenDemo()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene("Assets/Aquarium/Scenes/AquariumDemo.unity");
        }
    }
}
