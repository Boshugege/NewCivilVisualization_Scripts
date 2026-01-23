using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Threading;
using TMPro;
using UnityEngine.Assertions;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Linq;
using System.Collections.Generic;

// [RequireComponent(typeof(FPMController))]
public class ConfigMenu : MonoBehaviour
{
    private static CommonData commonData = CommonData.Instance;

    [Header("UI References")]
    // private GameObject configMenu;
    private FPMController playerController;
    private Seismic seismic;
    // private WaveInfo waveInfo;
    private Sensor sensor;

    [Header("UI Elements")]
    [SerializeField] private Slider flySpeedSlider;
    [SerializeField] private TextMeshProUGUI flySpeedValueText;
    [SerializeField] private Slider mouseSensitivitySlider;
    [SerializeField] private TextMeshProUGUI mouseSensitivityValueText;
    [SerializeField] private Slider walkSpeedSlider;
    [SerializeField] private TextMeshProUGUI walkSpeedValueText;
    [SerializeField] private Slider jumpForceSlider;
    [SerializeField] private TextMeshProUGUI jumpForceValueText;
    [SerializeField] private Slider gravitySlider;
    [SerializeField] private TextMeshProUGUI gravityValueText;
    [SerializeField] private Slider cameraFOVSlider;
    [SerializeField] private TextMeshProUGUI cameraFOVValueText;
    [SerializeField] private Slider playSpeedSlider;
    [SerializeField] private TextMeshProUGUI playSpeedValueText;
    [SerializeField] private Slider visualAmplitudeSlider;
    [SerializeField] private TextMeshProUGUI visualAmplitudeValueText;

    [SerializeField] private Button refreshButton;
    private TMP_Dropdown seismicDataDropdown;
    [SerializeField] private Button layerConfigButton;
    private TMP_Dropdown modelTypeDropdown;
    [SerializeField] private Slider alphaSlider;
    [SerializeField] private TextMeshProUGUI alphaValueText;
    [SerializeField] private Slider betaSlider;
    [SerializeField] private TextMeshProUGUI betaValueText;

    [HideInInspector] public Button solveButton;
    [HideInInspector] public TextMeshProUGUI solveText;
    [HideInInspector] public TextMeshProUGUI solveProgressText;

    private Toggle colorToggle;
    private Toggle statsToggle;
    private TMP_Dropdown sensorTypeDropdown;
    private Toggle markLineToggle;
    private Toggle waveFadeInToggle;
    private Toggle internalStructureToggle;
    private Toggle externalStructureToggle;
    private Toggle lightRotateToggle;

    void Awake()
    {
        commonData.RegisterUI("ConfigMenu", gameObject);
        commonData.EnableUI("ConfigMenu", false);
    }

    void Start()
    {
        playerController = GameObject.Find("Player").GetComponent<FPMController>();
        seismic = GameObject.Find("Seismic").GetComponent<Seismic>();
        // waveInfo = GameObject.Find("UICanvas").GetComponent<WaveInfo>();
        sensor = GameObject.Find("Sensor").GetComponent<Sensor>();
        Assert.IsNotNull(playerController);
        Assert.IsNotNull(seismic);
        // Assert.IsNotNull(waveInfo);
        Assert.IsNotNull(sensor);

        // flySpeedSlider = GameObject.Find("FlySpeedSlider").GetComponentInChildren<Slider>();
        // flySpeedValueText = GameObject.Find("FlySpeedValueText").GetComponent<TextMeshProUGUI>();
        // Assert.IsNotNull(flySpeedSlider);
        // Assert.IsNotNull(flySpeedValueText);
        // flySpeedSlider.minValue = 1f;
        // flySpeedSlider.maxValue = 300f;
        // flySpeedSlider.value = 100f;
        OnFlySpeedChanged(flySpeedSlider.value);
        flySpeedSlider.onValueChanged.AddListener(OnFlySpeedChanged);

        // mouseSensitivitySlider = GameObject.Find("MouseSensitivitySlider").GetComponentInChildren<Slider>();
        // mouseSensitivityValueText = GameObject.Find("MouseSensitivityValueText").GetComponent<TextMeshProUGUI>();
        // Assert.IsNotNull(mouseSensitivitySlider);
        // Assert.IsNotNull(mouseSensitivityValueText);
        // mouseSensitivitySlider.minValue = 0.5f;
        // mouseSensitivitySlider.maxValue = 5f;
        // mouseSensitivitySlider.value = 1.5f;
        OnMouseSensitivityChanged(mouseSensitivitySlider.value);
        mouseSensitivitySlider.onValueChanged.AddListener(OnMouseSensitivityChanged);

        // walkSpeedSlider = GameObject.Find("WalkSpeedSlider").GetComponentInChildren<Slider>();
        // walkSpeedValueText = GameObject.Find("WalkSpeedValueText").GetComponent<TextMeshProUGUI>();
        // Assert.IsNotNull(walkSpeedSlider);
        // Assert.IsNotNull(walkSpeedValueText);
        // walkSpeedSlider.minValue = 1f;
        // walkSpeedSlider.maxValue = 50f;
        // walkSpeedSlider.value = 5f;
        OnWalkSpeedChanged(walkSpeedSlider.value);
        walkSpeedSlider.onValueChanged.AddListener(OnWalkSpeedChanged);

        // jumpForceSlider = GameObject.Find("JumpForceSlider").GetComponentInChildren<Slider>();
        // jumpForceValueText = GameObject.Find("JumpForceValueText").GetComponent<TextMeshProUGUI>();
        // Assert.IsNotNull(jumpForceSlider);
        // Assert.IsNotNull(jumpForceValueText);
        // jumpForceSlider.minValue = 1f;
        // jumpForceSlider.maxValue = 50f;
        // jumpForceSlider.value = 10f;
        OnJumpForceChanged(jumpForceSlider.value);
        jumpForceSlider.onValueChanged.AddListener(OnJumpForceChanged);

        // gravitySlider = GameObject.Find("GravitySlider").GetComponentInChildren<Slider>();
        // gravityValueText = GameObject.Find("GravityValueText").GetComponent<TextMeshProUGUI>();
        // Assert.IsNotNull(gravitySlider);
        // Assert.IsNotNull(gravityValueText);
        // gravitySlider.minValue = -10f;
        // gravitySlider.maxValue = 30f;
        // gravitySlider.value = 9.8f;
        OnGravityChanged(gravitySlider.value);
        gravitySlider.onValueChanged.AddListener(OnGravityChanged);

        // cameraFOVSlider = GameObject.Find("CameraFOVSlider").GetComponentInChildren<Slider>();
        // cameraFOVValueText = GameObject.Find("CameraFOVValueText").GetComponent<TextMeshProUGUI>();
        // Assert.IsNotNull(cameraFOVSlider);
        // Assert.IsNotNull(cameraFOVValueText);
        // cameraFOVSlider.minValue = 30f;
        // cameraFOVSlider.maxValue = 120f;
        // cameraFOVSlider.value = 60f;
        OnCameraFOVChanged(cameraFOVSlider.value);
        cameraFOVSlider.onValueChanged.AddListener(OnCameraFOVChanged);

        // playSpeedSlider = GameObject.Find("PlaySpeedSlider").GetComponentInChildren<Slider>();
        // playSpeedValueText = GameObject.Find("PlaySpeedValueText").GetComponent<TextMeshProUGUI>();
        // Assert.IsNotNull(playSpeedSlider);
        // Assert.IsNotNull(playSpeedValueText);
        // playSpeedSlider.minValue = -10f;
        // playSpeedSlider.maxValue = 10f;
        // playSpeedSlider.value = 1f;
        OnPlaySpeedChanged(playSpeedSlider.value);
        playSpeedSlider.onValueChanged.AddListener(OnPlaySpeedChanged);

        OnVisualAmplitudeChanged(visualAmplitudeSlider.value);
        visualAmplitudeSlider.onValueChanged.AddListener(OnVisualAmplitudeChanged);

        seismicDataDropdown = GameObject.Find("SeismicDataDropdown").GetComponent<TMP_Dropdown>();
        Assert.IsNotNull(seismicDataDropdown);
        seismicDataDropdown.onValueChanged.AddListener((value) =>
        {
            seismic.dataFile = seismicDataDropdown.options[value].text;
        });
        RefreshSeismicDataDropdown();

        refreshButton.onClick.AddListener(RefreshSeismicDataDropdown);

        layerConfigButton.onClick.AddListener(OnLayerConfigButtonClick);

        modelTypeDropdown = GameObject.Find("ModelTypeDropdown").GetComponent<TMP_Dropdown>();
        Assert.IsNotNull(modelTypeDropdown);
        modelTypeDropdown.ClearOptions();
        modelTypeDropdown.AddOptions(new List<string> { "Shear", "FlexuralShear" , "Torsion" });
        modelTypeDropdown.value = 0;
        modelTypeDropdown.onValueChanged.AddListener((value) =>
        {
            if (value == 0)
            {
                seismic.config.modelType = ModelType.Shear;
            }
            else if (value == 1)
            {
                seismic.config.modelType = ModelType.FlexuralShear;
            }
            else if (value == 2)
            {
                seismic.config.modelType = ModelType.Torsion;
            }
            else Debug.LogError("Unknown model type!");
        });

        OnAlphaChanged(alphaSlider.value);
        alphaSlider.onValueChanged.AddListener(OnAlphaChanged);

        OnBetaChanged(betaSlider.value);
        betaSlider.onValueChanged.AddListener(OnBetaChanged);


        solveButton = GameObject.Find("SolveButton").GetComponentInChildren<Button>();
        solveText = GameObject.Find("SolveButton").GetComponentInChildren<TextMeshProUGUI>();
        solveProgressText = GameObject.Find("SolveProgress").GetComponentInChildren<TextMeshProUGUI>();
        Assert.IsNotNull(solveButton);
        Assert.IsNotNull(solveText);
        Assert.IsNotNull(solveProgressText);
        solveButton.onClick.AddListener(OnSolveButtonClick);

        colorToggle = GameObject.Find("ColorToggle").GetComponent<Toggle>();
        Assert.IsNotNull(colorToggle);
        colorToggle.isOn = false;
        colorToggle.onValueChanged.AddListener((value) =>
        {
            seismic.EnableColoring(value);
        });

        statsToggle = GameObject.Find("StatsToggle").GetComponent<Toggle>();
        Assert.IsNotNull(statsToggle);
        statsToggle.isOn = false;
        statsToggle.onValueChanged.AddListener((value) =>
        {
            commonData.EnableUI("StatsPanel", value);
        });

        sensorTypeDropdown = GameObject.Find("SensorTypeDropdown").GetComponent<TMP_Dropdown>();
        Assert.IsNotNull(sensorTypeDropdown);
        sensorTypeDropdown.ClearOptions();
        sensorTypeDropdown.AddOptions(System.Enum.GetNames(typeof(Sensor.SensorType)).ToList());
        sensorTypeDropdown.value = (int)sensor.sensorType;
        sensorTypeDropdown.onValueChanged.AddListener((value) =>
        {
            sensor.sensorType = (Sensor.SensorType)value;
        });

        markLineToggle = GameObject.Find("MarkLineToggle").GetComponent<Toggle>();
        Assert.IsNotNull(markLineToggle);
        markLineToggle.isOn = false;
        markLineToggle.onValueChanged.AddListener((value) =>
        {
            sensor.EnableMarkLine(value);
        });

        waveFadeInToggle = GameObject.Find("WaveFadeInToggle").GetComponent<Toggle>();
        Assert.IsNotNull(waveFadeInToggle);
        waveFadeInToggle.isOn = false;
        waveFadeInToggle.onValueChanged.AddListener((value) =>
        {
            sensor.EnableWaveFadeIn(value);
        });

        internalStructureToggle = GameObject.Find("InternalStructureToggle").GetComponent<Toggle>();
        Assert.IsNotNull(internalStructureToggle);
        internalStructureToggle.isOn = true;

        seismic.EnableInternalStructure(true); // wyh cannot find where to set default true

        internalStructureToggle.onValueChanged.AddListener((value) =>
        {
            seismic.EnableInternalStructure(value);
        });

        externalStructureToggle = GameObject.Find("ExternalStructureToggle").GetComponent<Toggle>();
        Assert.IsNotNull(externalStructureToggle);
        externalStructureToggle.isOn = true;
        externalStructureToggle.onValueChanged.AddListener((value) =>
        {
            seismic.EnableExternalStructure(value);
        });

        lightRotateToggle = GameObject.Find("LightRotateToggle").GetComponent<Toggle>();
        Assert.IsNotNull(lightRotateToggle);
        lightRotateToggle.isOn = false;
        lightRotateToggle.onValueChanged.AddListener((value) =>
        {
            commonData.lightRotate = value;
        });

        // gameObject.AddComponent<CanvasGroup>();
        // commonData.RegisterCanvasGroup("ConfigMenu", GetComponent<CanvasGroup>());
        // commonData.EnableCanvasGroup("ConfigMenu", false);
    }

    void OnFlySpeedChanged(float value)
    {
        commonData.flySpeed = value;
        flySpeedValueText.text = value.ToString("F1");
    }

    void OnMouseSensitivityChanged(float value)
    {
        commonData.mouseSensitivity = value;
        mouseSensitivityValueText.text = value.ToString("F1");
    }

    void OnWalkSpeedChanged(float value)
    {
        commonData.walkSpeed = value;
        walkSpeedValueText.text = value.ToString("F1");
    }

    void OnJumpForceChanged(float value)
    {
        commonData.jumpForce = value;
        jumpForceValueText.text = value.ToString("F1");
    }

    void OnGravityChanged(float value)
    {
        commonData.gravity = value;
        gravityValueText.text = value.ToString("F1");
    }

    void OnCameraFOVChanged(float value)
    {
        playerController.OnFOVChanged(value);
        cameraFOVValueText.text = value.ToString("F1");
    }

    void OnPlaySpeedChanged(float value)
    {
        commonData.playSpeed = value;
        playSpeedValueText.text = value.ToString("F1");
    }

    void OnVisualAmplitudeChanged(float value)
    {
        commonData.visualAmplitude = value;
        visualAmplitudeValueText.text = value.ToString("F1");
    }

    void OnLayerConfigButtonClick()
    {
        commonData.EnableUI("ConfigMenu", false);
        commonData.EnableUI("LayerConfig", true);
    }

    void OnAlphaChanged(float value)
    {
        seismic.config.alpha = value;
        alphaValueText.text = value.ToString("F3");
    }

    void OnBetaChanged(float value)
    {
        seismic.config.beta = value;
        betaValueText.text = value.ToString("F3");
    }

    void OnSolveButtonClick()
    {
        seismic.Stop();
        seismic.ApplyConfig();
        StartCoroutine(seismic.SolveCoroutine());

        // stopwatch.Start();
        // seismic.solvedCount = 0;
        // solveButton.interactable = false;
        // _ = seismic.StartSolving(true);

        // foreach (var meshDeformer in seismic.meshDeformers)
        // {
        //     meshDeformer.Solve();
        // }

        // Thread solveThread = new Thread(() =>
        // {
        //     foreach (var meshDeformer in seismic.meshDeformers)
        //     {
        //         meshDeformer.Solve();
        //         Interlocked.Increment(ref seismic.solvedCount);
        //     }
        // });
        // solveThread.Start();
    }

    // #region UI更新方法
    // void UpdateWalkSpeedUI(float value)
    // {
    //     walkSpeedInput.text = value.ToString("F1");
    // }

    // void UpdateFlySpeedUI(float value)
    // {
    //     flySpeedInput.text = value.ToString("F1");
    // }

    // void UpdateSensitivityUI(float value)
    // {
    //     sensitivityInput.text = value.ToString("F1");
    // }
    // #endregion

    public void RefreshSeismicDataDropdown()
    {
        // Get all available seismic data files from the data folder and populate the dropdown
        var files = System.IO.Directory.GetFiles(Seismic.dataFolderPath, "*.json")
            .Select(path => System.IO.Path.GetFileName(path));
        Assert.IsTrue(files.Count() > 0, "No seismic data file found!");
        seismicDataDropdown.ClearOptions();
        seismicDataDropdown.AddOptions(files.ToList());
        seismicDataDropdown.value = 0;
    }
}