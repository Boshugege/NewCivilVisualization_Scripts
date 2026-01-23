using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AutoLightRotater : MonoBehaviour
{
    private static CommonData commonData = CommonData.Instance;

    private const float speed = 100f;

    // Update is called once per frame
    void Update()
    {
        if (commonData.lightRotate)
        {
            transform.Rotate(Vector3.up, speed * Time.deltaTime, Space.World);
        }
    }
}
