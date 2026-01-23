using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Building
{
    public class Level
    {
        public int numVertices;
        public int[] meshIndices;
        public int[] vertexIndices;
        public Vector3[] originalVertices;
    }

    public int numLevels;
    public Level[] levels;
    public Mesh[] meshes;

    // levelId: 1 ~ numLevels
    public Vector3 GetVertex(int levelId, int vertexIndex)
    {
        return levels[levelId - 1].originalVertices[vertexIndex];
    }

    public void SetVertex(int levelId, int vertexIndex, Vector3 vertex)
    {
        var level = levels[levelId - 1];
        int meshIndex = level.meshIndices[vertexIndex];
        int vertexIndexInMesh = level.vertexIndices[vertexIndex];
        meshes[meshIndex].vertices[vertexIndexInMesh] = vertex;
    }
}