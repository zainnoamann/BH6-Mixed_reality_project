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

        int count = 0;

        // Each direct child of Model is treated as one selectable object.
        foreach (Transform objectRoot in roomRoot)
        {
            if (objectRoot == null)
                continue;

            // Add interaction to the furniture/object parent.
            if (objectRoot.GetComponent<ObjectInteraction>() == null)
            {
                objectRoot.gameObject.AddComponent<ObjectInteraction>();
            }

            // Add colliders to the actual mesh objects.
            MeshFilter[] meshFilters =
                objectRoot.GetComponentsInChildren<MeshFilter>();

            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null)
                    continue;

                GameObject meshObject = meshFilter.gameObject;

                if (meshObject.GetComponent<Collider>() == null)
                {
                    MeshCollider meshCollider =
                        meshObject.AddComponent<MeshCollider>();

                    meshCollider.sharedMesh =
                        meshFilter.sharedMesh;
                }
            }

            count++;

            Debug.Log(
                "Selectable object prepared: " +
                objectRoot.name
            );
        }

        Debug.Log(
            "Interaction setup complete. Objects prepared: " +
            count
        );
    }
}
