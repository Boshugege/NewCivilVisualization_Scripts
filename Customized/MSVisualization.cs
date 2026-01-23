using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

using Config = Solver1D.Config;

public class MSVisualization : MonoBehaviour
{
    List<GameObject> buildings = new List<GameObject>();

    void Awake()
    {
        {
            var b1 = new GameObject(
                "剪切-ModeShape1",
                typeof(BuildingGenerator)
            );
            var b2 = new GameObject(
                "剪切-ModeShape2",
                typeof(BuildingGenerator)
            );
            var b3 = new GameObject(
                "剪切-ModeShape3",
                typeof(BuildingGenerator)
            );

            b1.transform.SetParent(this.transform);
            b2.transform.SetParent(this.transform);
            b3.transform.SetParent(this.transform);

            b1.transform.localPosition = new Vector3(0, 0, 0);
            b2.transform.localPosition = new Vector3(25f, 0, 0);
            b3.transform.localPosition = new Vector3(50f, 0, 0);

            var g1 = b1.GetComponent<BuildingGenerator>();
            g1.newConfig = new Config()
            {
                mode = ShakeMode.ModeShape,
                modelType = ModelType.Shear,
                modeShape = 1,
                coloringType = ColoringType.Displacement,
            };

            var g2 = b2.GetComponent<BuildingGenerator>();
            g2.newConfig = new Config()
            {
                mode = ShakeMode.ModeShape,
                modelType = ModelType.Shear,
                modeShape = 2,
                coloringType = ColoringType.Displacement,
            };

            var g3 = b3.GetComponent<BuildingGenerator>();
            g3.newConfig = new Config()
            {
                mode = ShakeMode.ModeShape,
                modelType = ModelType.Shear,
                modeShape = 3,
                coloringType = ColoringType.Displacement,
            };

            g1.newPlaySpeed = 0.2f;
            g2.newPlaySpeed = 0.2f;
            g3.newPlaySpeed = 0.2f;

            g1.rebuild = true;
            g2.rebuild = true;
            g3.rebuild = true;

            // Debug.Log(b1.transform.position);
            // Debug.Log(b2.transform.position);
            // Debug.Log(b3.transform.position);
            buildings.Add(b1);
            buildings.Add(b2);
            buildings.Add(b3);
        }

        {
            var b1 = new GameObject(
                "扭转-ModeShape1",
                typeof(BuildingGenerator)
            );
            var b2 = new GameObject(
                "扭转-ModeShape2",
                typeof(BuildingGenerator)
            );
            var b3 = new GameObject(
                "扭转-ModeShape3",
                typeof(BuildingGenerator)
            );

            b1.transform.SetParent(this.transform);
            b2.transform.SetParent(this.transform);
            b3.transform.SetParent(this.transform);

            b1.transform.localPosition = new Vector3(100f, 0, 0);
            b2.transform.localPosition = new Vector3(125f, 0, 0);
            b3.transform.localPosition = new Vector3(150f, 0, 0);

            var g1 = b1.GetComponent<BuildingGenerator>();
            g1.newConfig = new Config()
            {
                mode = ShakeMode.ModeShape,
                modelType = ModelType.Torsion,
                modeShape = 1,
                zCenterOffset = 0.5f,
                coloringType = ColoringType.Displacement,
            };

            var g2 = b2.GetComponent<BuildingGenerator>();
            g2.newConfig = new Config()
            {
                mode = ShakeMode.ModeShape,
                modelType = ModelType.Torsion,
                modeShape = 3,
                zCenterOffset = 0.5f,
                coloringType = ColoringType.Displacement,
            };

            var g3 = b3.GetComponent<BuildingGenerator>();
            g3.newConfig = new Config()
            {
                mode = ShakeMode.ModeShape,
                modelType = ModelType.Torsion,
                modeShape = 5,
                zCenterOffset = 0.5f,
                coloringType = ColoringType.Displacement,
            };

            g1.newPlaySpeed = 0.2f;
            g2.newPlaySpeed = 0.2f;
            g3.newPlaySpeed = 0.2f;

            g1.rebuild = true;
            g2.rebuild = true;
            g3.rebuild = true;

            buildings.Add(b1);
            buildings.Add(b2);
            buildings.Add(b3);
        }

        {
            var b1 = new GameObject(
                "弯剪-ModeShape1",
                typeof(BuildingGenerator)
            );
            var b2 = new GameObject(
                "弯剪-ModeShape2",
                typeof(BuildingGenerator)
            );
            var b3 = new GameObject(
                "弯剪-ModeShape3",
                typeof(BuildingGenerator)
            );

            b1.transform.SetParent(this.transform);
            b2.transform.SetParent(this.transform);
            b3.transform.SetParent(this.transform);

            b1.transform.localPosition = new Vector3(200f, 0, 0);
            b2.transform.localPosition = new Vector3(225f, 0, 0);
            b3.transform.localPosition = new Vector3(250f, 0, 0);

            var g1 = b1.GetComponent<BuildingGenerator>();
            g1.newConfig = new Config()
            {
                mode = ShakeMode.ModeShape,
                modelType = ModelType.FlexuralShear,
                modeShape = 1,
                coloringType = ColoringType.Displacement,
            };

            var g2 = b2.GetComponent<BuildingGenerator>();
            g2.newConfig = new Config()
            {
                mode = ShakeMode.ModeShape,
                modelType = ModelType.FlexuralShear,
                modeShape = 2,
                coloringType = ColoringType.Displacement,
            };

            var g3 = b3.GetComponent<BuildingGenerator>();
            g3.newConfig = new Config()
            {
                mode = ShakeMode.ModeShape,
                modelType = ModelType.FlexuralShear,
                modeShape = 3,
                coloringType = ColoringType.Displacement,
            };

            g1.newPlaySpeed = 0.2f;
            g2.newPlaySpeed = 0.2f;
            g3.newPlaySpeed = 0.2f;

            g1.rebuild = true;
            g2.rebuild = true;
            g3.rebuild = true;

            buildings.Add(b1);
            buildings.Add(b2);
            buildings.Add(b3);
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(MSVisualization))]
public class MSVisualizationEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        // var t = (MSVisualization)target;
        // if (GUILayout.Button("Play"))
        // {
        //     foreach (Transform child in t.transform)
        //     {
        //         var g = child.GetComponent<BuildingGenerator>();
        //         g.rebuild = true;
        //     }
        // }
    }
}
#endif