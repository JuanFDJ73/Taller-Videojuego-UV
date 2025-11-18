using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEngine;

public class MeshCombinerTool
{
    [MenuItem("Tools/Mesh Tools/Combinar mallas de selección")]
    static void CombineSelectedMeshes()
    {
        if (Selection.gameObjects.Length == 0)
        {
            Debug.LogWarning("No hay objetos seleccionados.");
            return;
        }

        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

        // Determinar root y transforms objetivo (similar al script anterior)
        Transform root;
        List<Transform> targets = new List<Transform>();

        if (prefabStage != null)
        {
            root = prefabStage.prefabContentsRoot.transform;
            foreach (var go in Selection.gameObjects)
            {
                if (go == null) continue;
                if (go.transform.IsChildOf(root) || go.transform == root)
                    targets.Add(go.transform);
            }
            if (targets.Count == 0) targets.Add(root);
        }
        else
        {
            root = Selection.activeTransform;
            if (root == null) root = Selection.gameObjects[0].transform;
            foreach (var go in Selection.gameObjects)
                if (go != null) targets.Add(go.transform);
        }

        // Recolectar MeshFilters
        List<MeshFilter> meshFilters = new List<MeshFilter>();
        foreach (var t in targets)
            meshFilters.AddRange(t.GetComponentsInChildren<MeshFilter>(true));

        if (meshFilters.Count == 0)
        {
            Debug.LogWarning("La selección no contiene MeshFilters.");
            return;
        }

        // Mapeo de Material -> List<CombineInstance>
        Dictionary<Material, List<CombineInstance>> perMaterial = new Dictionary<Material, List<CombineInstance>>();
        Matrix4x4 rootWorldToLocal = root.worldToLocalMatrix;

        foreach (var mf in meshFilters)
        {
            if (mf == null || mf.sharedMesh == null) continue;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null) continue;

            Material[] mats = mr.sharedMaterials;
            int subMeshCount = mf.sharedMesh.subMeshCount;

            for (int sub = 0; sub < subMeshCount; sub++)
            {
                // Seleccionar material seguro (si falta, usar el primero)
                Material mat = (sub < mats.Length) ? mats[sub] : (mats.Length > 0 ? mats[0] : null);
                if (mat == null) mat = new Material(Shader.Find("Standard")); // fallback (opcional)

                if (!perMaterial.ContainsKey(mat))
                    perMaterial[mat] = new List<CombineInstance>();

                CombineInstance ci = new CombineInstance();
                ci.mesh = mf.sharedMesh;
                ci.subMeshIndex = sub;
                ci.transform = rootWorldToLocal * mf.transform.localToWorldMatrix; // espacio del root
                perMaterial[mat].Add(ci);
            }
        }

        if (perMaterial.Count == 0)
        {
            Debug.LogWarning("No se generaron CombineInstances por material.");
            return;
        }

        // 1) Crear una mesh temporal por cada material (mergeSubmeshes = true)
        List<Mesh> meshesPerMaterial = new List<Mesh>();
        List<Material> materialOrder = new List<Material>(); // guarda el orden de materiales

        foreach (var kv in perMaterial)
        {
            Material mat = kv.Key;
            var listCIs = kv.Value;

            Mesh tmp = new Mesh();
            tmp.name = "tmp_combined_" + mat.GetInstanceID();
            tmp.CombineMeshes(listCIs.ToArray(), true, true); // merge meshes dentro del mismo material
            meshesPerMaterial.Add(tmp);
            materialOrder.Add(mat);
        }

        // 2) Combinar las meshes temporales en la mesh final, sin fusionar submeshes (por eso mergeSubMeshes = false)
        CombineInstance[] finalCIs = new CombineInstance[meshesPerMaterial.Count];
        for (int i = 0; i < meshesPerMaterial.Count; i++)
        {
            finalCIs[i] = new CombineInstance();
            finalCIs[i].mesh = meshesPerMaterial[i];
            finalCIs[i].subMeshIndex = 0; // cada tmp tiene un único submesh
            finalCIs[i].transform = Matrix4x4.identity;
        }

        Mesh finalMesh = new Mesh();
        finalMesh.name = "CombinedMesh_" + root.GetInstanceID();
        finalMesh.CombineMeshes(finalCIs, false, false); // false = mantener submeshes (uno por material)

        // Crear objeto contenedor (hijo del root si estamos en prefab)
        GameObject parent;
        if (prefabStage != null)
        {
            parent = new GameObject("CombinedMesh");
            Undo.RegisterCreatedObjectUndo(parent, "Crear CombinedMesh");
            parent.transform.SetParent(root, false);
        }
        else
        {
            parent = new GameObject("CombinedMesh");
            Undo.RegisterCreatedObjectUndo(parent, "Crear CombinedMesh");
        }

        MeshFilter parentMF = parent.AddComponent<MeshFilter>();
        MeshRenderer parentMR = parent.AddComponent<MeshRenderer>();

        // Guardar la mesh final como asset para persistencia
        string folder = "Assets/CombinedMeshes";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "CombinedMeshes");

        string basePath = Path.Combine(folder, finalMesh.name + ".asset");
        string meshPath = AssetDatabase.GenerateUniqueAssetPath(basePath);
        AssetDatabase.CreateAsset(finalMesh, meshPath);
        AssetDatabase.SaveAssets();

        parentMF.sharedMesh = finalMesh;
        parentMR.sharedMaterials = materialOrder.ToArray();

        // Desactivar renderers originales
        foreach (var mf in meshFilters)
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Undo.RecordObject(mr, "Disable original MeshRenderer");
                mr.enabled = false;
            }
        }

        // Limpiar meshes temporales creadas en memoria (no assets)
        foreach (var tmp in meshesPerMaterial)
            Object.DestroyImmediate(tmp);

        // Marcar scene/prefab como sucio para que pida guardar
        if (prefabStage != null)
            EditorSceneManager.MarkSceneDirty(prefabStage.scene);
        else
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Selection.activeGameObject = parent;
        Debug.Log($" Mallas combinadas en: {parent.name} (asset: {meshPath})");
    }
}