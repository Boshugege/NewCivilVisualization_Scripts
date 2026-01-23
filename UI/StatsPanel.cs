using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;

public class StatsPanel : MonoBehaviour
{
    private static CommonData commonData = CommonData.Instance;

    [SerializeField] private TextMeshProUGUI FPSText;
    [SerializeField] private TextMeshProUGUI CPUNameText;
    [SerializeField] private TextMeshProUGUI GPUNameText;
    [SerializeField] private TextMeshProUGUI DRAMText;
    [SerializeField] private TextMeshProUGUI VRAMText;
    private float deltaTime = 0.0f;
    private int frameCount = 0;

    void Awake()
    {
        commonData.RegisterUI("StatsPanel", gameObject);
        commonData.EnableUI("StatsPanel", false);
    }

    // Start is called before the first frame update
    void Start()
    {
        FPSText = transform.Find("FPSText").GetComponent<TextMeshProUGUI>();
        CPUNameText = transform.Find("CPUNameText").GetComponent<TextMeshProUGUI>();
        GPUNameText = transform.Find("GPUNameText").GetComponent<TextMeshProUGUI>();
        DRAMText = transform.Find("DRAMText").GetComponent<TextMeshProUGUI>();
        VRAMText = transform.Find("VRAMText").GetComponent<TextMeshProUGUI>();

        Assert.IsNotNull(FPSText);
        Assert.IsNotNull(CPUNameText);
        Assert.IsNotNull(GPUNameText);
        Assert.IsNotNull(DRAMText);
        Assert.IsNotNull(VRAMText);

        FPSText.text = $"FPS: ...";
        CPUNameText.text = $"CPU: {SystemInfo.processorType}";
        GPUNameText.text = $"GPU: {SystemInfo.graphicsDeviceName}";
        DRAMText.text = $"DRAM: {SystemInfo.systemMemorySize} MB";
        VRAMText.text = $"VRAM: {SystemInfo.graphicsMemorySize} MB";
    }

    // Update is called once per frame
    void Update()
    {
        deltaTime += Time.deltaTime;
        frameCount++;

        if (deltaTime >= CommonData.FPSUpdateInterval)
        {
            float fps = frameCount / deltaTime;
            FPSText.text = $"FPS: {fps:F2}";

            deltaTime = 0.0f;
            frameCount = 0;
        }
    }
}
