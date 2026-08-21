using UnityEngine;

public class InteractionSetup : MonoBehaviour
{
    [SerializeField] private Transform roomRoot;

    private void Start()
    {
        SetupObjects();
    }

    private void SetupObjects()
    {
        if (roomRoot == null)
        {
            Debug.LogError("Room Root has not been assigned.");
            return;
        }

        MeshRenderer[] meshRenderers =
            roomRoot.GetComponentsInChildren<MeshRenderer>();

        int count = 0;

        foreach (MeshRenderer meshRenderer in meshRenderers)
        {
            GameObject obj = meshRenderer.gameObject;

            // Add a Mesh Collider if the object does not already have one.
            if (obj.GetComponent<Collider>() == null)
            {
                MeshFilter meshFilter = obj.GetComponent<MeshFilter>();

                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    MeshCollider meshCollider =
                        obj.AddComponent<MeshCollider>();

                    meshCollider.sharedMesh = meshFilter.sharedMesh;
                }
            }

            // Add our interaction component.
            if (obj.GetComponent<ObjectInteraction>() == null)
            {
                obj.AddComponent<ObjectInteraction>();
            }

            count++;
        }

        Debug.Log(
            "Interaction setup complete. Objects prepared: "
            + count
        );
    }
}
