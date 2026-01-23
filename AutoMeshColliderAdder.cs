using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AutoMeshColliderAdder : MonoBehaviour
{
    public void AddMeshColliders()
    {
        // Find all mesh renderers in this GameObject and its children
        MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        Debug.Log($"AutoMeshColliderAdder.cs - Found {meshRenderers.Length} mesh renderers");

        foreach (MeshRenderer renderer in meshRenderers)
        {
            GameObject meshObject = renderer.gameObject;

            if (meshObject.GetComponent<MeshCollider>() == null)
            {
                MeshFilter meshFilter = meshObject.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    MeshCollider collider = meshObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = meshFilter.sharedMesh;

                    // bool convex = false, bool isTrigger = false
                    // collider.convex = convex;
                    // collider.isTrigger = isTrigger;
                }
            }
        }
    }

    void Start()
    {
        AddMeshColliders();
    }
}
