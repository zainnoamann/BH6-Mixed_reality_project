using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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

            Prepare(objectRoot);

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

    /// <summary>
    /// Makes one object selectable: interaction component plus colliders on its meshes.
    /// Extracted from SetupObjects so objects created at runtime get the same treatment.
    /// </summary>
    public void Prepare(Transform objectRoot)
    {
        if (objectRoot == null)
            return;

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

                // Physics ignores the back of a face, and exported rooms often have
                // some faces turned inside out: the player walks straight through
                // those walls and ceilings. A second collider with every face flipped
                // blocks from both sides. Raycasts still only report the face that
                // points at the ray, so surface normals stay correct.
                Mesh flipped = FlippedCopy(meshFilter.sharedMesh);

                if (flipped != null)
                {
                    meshObject.AddComponent<MeshCollider>().sharedMesh = flipped;
                }
            }
        }
    }

    // One flipped copy per imported mesh, however many objects share it.
    private static readonly Dictionary<Mesh, Mesh> flippedMeshes =
        new Dictionary<Mesh, Mesh>();

    private static Mesh FlippedCopy(Mesh source)
    {
        if (flippedMeshes.TryGetValue(source, out Mesh cached) && cached != null)
            return cached;

        if (!source.isReadable)
        {
            Debug.LogWarning(
                "Mesh '" + source.name + "' is not readable, so its colliders are " +
                "one-sided. Enable Read/Write in the model's import settings."
            );

            return null;
        }

        List<int> triangles = new List<int>();

        for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            if (source.GetTopology(subMesh) != MeshTopology.Triangles)
                continue;

            int[] indices = source.GetTriangles(subMesh);

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                triangles.Add(indices[i]);
                triangles.Add(indices[i + 2]);
                triangles.Add(indices[i + 1]);
            }
        }

        Mesh flipped = new Mesh
        {
            name = source.name + " (flipped collider)",
            indexFormat = source.vertexCount > 65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16
        };

        flipped.vertices = source.vertices;
        flipped.SetTriangles(triangles, 0);

        flippedMeshes[source] = flipped;

        return flipped;
    }
}
