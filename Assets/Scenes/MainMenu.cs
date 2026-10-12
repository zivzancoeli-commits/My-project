using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public static bool editorMode = false;
    public void Play()
    {
        editorMode = false;
        SceneManager.LoadScene("Game");
    }

    public void OpenLevelEditor()
    {
        editorMode = true;
        SceneManager.LoadScene("Game");
    }

    public void OpenSettings()
    {
        Debug.Log("Settings coming soon");
    }

    public void Quit()
    {
        Debug.Log("Quit pressed");
        Application.Quit();
    }
}