using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LogWindow : MonoBehaviour
{
    private static CommonData commonData = CommonData.Instance;

    [SerializeField] private GameObject logPrefab;

    void Awake()
    {
        // commonData.RegisterUI("LogWindow", gameObject);
        // commonData.EnableUI("LogWindow", false);

        // // 生成3个log
        // for (int i = 0; i < 3; i++)
        // {
        //     GameObject log = Instantiate(logPrefab, transform);
        // }
    }

    void Start()
    {
        
    }

    void Update()
    {
        
    }
}
