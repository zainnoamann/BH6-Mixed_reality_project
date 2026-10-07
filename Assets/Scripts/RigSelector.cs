using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

/// <summary>
/// Picks which body the player uses when a scene has both rigs (QuestScene does).
///
/// The XR Origin is a floating camera: no capsule, no gravity, no collision. Its camera also
/// draws on top of the desktop Player's, so in the editor, where no headset runs, the view
/// glided through walls while the real Player walked around unseen. With a headset running
/// the XR Origin is used; otherwise the desktop Player (WASD, gravity, walls) is.
///
/// Runs by itself after every scene load; nothing needs adding to the scene.
/// </summary>
public static class RigSelector
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Choose()
    {
        XROrigin xrOrigin = Object.FindFirstObjectByType<XROrigin>();
        PlayerMovement desktopPlayer = Object.FindFirstObjectByType<PlayerMovement>();

        // Only one rig in the scene: nothing to choose.
        if (xrOrigin == null || desktopPlayer == null)
        {
            return;
        }

        bool headsetRunning = XRSettings.isDeviceActive
            || (XRGeneralSettings.Instance != null
                && XRGeneralSettings.Instance.Manager != null
                && XRGeneralSettings.Instance.Manager.activeLoader != null);

        xrOrigin.gameObject.SetActive(headsetRunning);
        desktopPlayer.gameObject.SetActive(!headsetRunning);

        Debug.Log(headsetRunning
            ? "Headset running: using the XR Origin."
            : "No headset: using the desktop Player (WASD, gravity, collisions).");
    }
}
