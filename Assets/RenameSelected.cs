using UnityEditor;
using UnityEngine;

public class RenameSelected : MonoBehaviour
{
    [MenuItem("Tools/Renombrar Pisos")]
    static void RenameObjects()
    {
        var objs = Selection.gameObjects;

        for (int i = 0; i < objs.Length; i++)
        {
            objs[i].name = "Piso_" + (i + 1);
        }
    }
}