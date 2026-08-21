using UnityEngine;

public class SemanticObject : MonoBehaviour
{
    public string objectId;
    public string category;
    public Vector3 dimensions;

    public void Initialise(string id, string objectCategory)
    {
        objectId = id;
        category = objectCategory;

        dimensions = CalculateDimensions();
    }

    private Vector3 CalculateDimensions()
    {
        Renderer renderer = GetComponent<Renderer>();

        if (renderer != null)
        {
            return renderer.bounds.size;
        }

        return Vector3.zero;
    }
}
