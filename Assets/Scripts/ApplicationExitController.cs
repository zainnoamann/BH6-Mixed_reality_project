using UnityEngine;

public class ApplicationExitController : MonoBehaviour
{
    public void ExitApplication()
    {
        Debug.Log("Exiting CreativeTwin application.");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}