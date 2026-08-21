using UnityEngine;

public class SemanticSceneBuilder : MonoBehaviour
{
    [SerializeField] private Transform roomRoot;

    private void Start()
    {
        BuildSemanticScene();
    }

    public void BuildSemanticScene()
    {
        if (roomRoot == null)
        {
            Debug.LogError("Room root has not been assigned.");
            return;
        }

        SemanticObject[] existingObjects =
            roomRoot.GetComponentsInChildren<SemanticObject>();

        foreach (SemanticObject semanticObject in existingObjects)
        {
            Destroy(semanticObject);
        }

        Transform[] objects = roomRoot.GetComponentsInChildren<Transform>();

        foreach (Transform currentObject in objects)
        {
            if (currentObject == roomRoot)
            {
                continue;
            }

            ProcessObject(currentObject);
        }
    }

   private void ProcessObject(Transform currentObject)
{
    Debug.Log("Checking object: " + currentObject.name);

    string objectName = currentObject.name;

    if (!TryParseName(objectName, out string category, out string id))
    {
        Debug.LogWarning(
            "Unable to parse semantic object name: " + objectName
        );

        return;
    }

    SemanticObject semanticObject =
        currentObject.gameObject.GetComponent<SemanticObject>();

    if (semanticObject == null)
    {
        semanticObject =
            currentObject.gameObject.AddComponent<SemanticObject>();
    }

    semanticObject.Initialise(id, category);

    Debug.Log(
        "Semantic object created: " +
        objectName +
        " | Category: " +
        category +
        " | ID: " +
        id
    );
}

    private bool TryParseName(
        string objectName,
        out string category,
        out string id)
    {
        category = "";
        id = "";

        string[] parts = objectName.Split('_');

        if (parts.Length < 2)
        {
            return false;
        }

        category = parts[0];
        id = objectName;

        return true;
    }
}
