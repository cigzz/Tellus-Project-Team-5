using UnityEngine;
using UnityEngine.SceneManagement;

//scene loading script for UI button actions
public class SceneLoader : MonoBehaviour
{
    public void Scene(string sceneName)
    {
        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError($"Scene '{sceneName}' could not be loaded, check build settings.");
        }
    }
}