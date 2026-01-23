using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static ChartPainter;

public class Sensor : MonoBehaviour
{
    public class Chart
    {
        public RawImage axis;
        public Image series;
        public RawImage markLine;
        public int width;
        public int height;
        public Vector4 plotRect; // plotRect: (top, bottom, left, right) pixels, left < right, top < bottom

        public void InitTrigger(Seismic seismic)
        {
            EventTrigger trigger = series.gameObject.AddComponent<EventTrigger>();
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.PointerClick;
            entry.callback.AddListener((data) =>
            {
                if (axis.texture == null) return;
                PointerEventData pointerData = (PointerEventData)data;

                RectTransform seriesRect = series.gameObject.GetComponent<RectTransform>();
                Vector2 localPoint;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    seriesRect, pointerData.position, pointerData.pressEventCamera, out localPoint))
                {
                    float normalizedX = Mathf.InverseLerp(0, seriesRect.rect.width, localPoint.x + seriesRect.rect.width / 2);
                    normalizedX = Mathf.Clamp01(normalizedX);
                    // Debug.Log("Chart clicked at normalized position: " + normalizedX);
                    seismic.SetTime(normalizedX * seismic.duration);
                }
            });
            trigger.triggers.Add(entry);
        }

        public void ClearTexture()
        {
            axis.texture = null;
            series.sprite = null;

            RectTransform seriesRect = series.GetComponent<RectTransform>();
            seriesRect.anchorMin = new Vector2(0f, 0f);
            seriesRect.anchorMax = new Vector2(1f, 1f);

            // Remove click event trigger from the series image

        }

        public void SetTexture(Texture2D axisTexture, Texture2D seriesTexture)
        {
            axis.texture = axisTexture;
            series.sprite = Sprite.Create(seriesTexture, new Rect(0f, 0f, seriesTexture.width, seriesTexture.height), new Vector2(0.5f, 0.5f));
            // series.type = Image.Type.Filled;
            // series.fillMethod = Image.FillMethod.Horizontal;

            float top = plotRect.x / height;
            float bottom = plotRect.y / height;
            float left = plotRect.z / width;
            float right = plotRect.w / width;

            RectTransform seriesRect = series.GetComponent<RectTransform>();

            seriesRect.anchorMin = new Vector2(left, 1 - bottom);
            seriesRect.anchorMax = new Vector2(right, 1 - top);
            // seriesRect.offsetMin = new Vector2(0f, 0f);
            // seriesRect.offsetMax = new Vector2(0f, 0f);
        }

        public void SetProgress(float progress)
        {
            // Assert.IsTrue(progress >= 0f && progress <= 1f, $"Progress must be between 0 and 1. Received: {progress}");
            // todo
            series.fillAmount = progress;

            RectTransform markLineRect = markLine.GetComponent<RectTransform>();
            markLineRect.anchorMin = new Vector2(Mathf.Clamp01(progress - 0.001f), 0f);
            markLineRect.anchorMax = new Vector2(Mathf.Clamp01(progress + 0.001f), 1f);
            // markLineRect.offsetMin = new Vector2(0f, 0f);
            // markLineRect.offsetMax = new Vector2(0f, 0f);
        }

        public void EnableMarkLine(bool enable)
        {
            markLine.enabled = enable;
        }

        public void EnableWaveFadeIn(bool enable)
        {
            if (enable)
            {
                series.type = Image.Type.Filled;
                series.fillMethod = Image.FillMethod.Horizontal;
            }
            else
            {
                series.type = Image.Type.Simple;
            }
        }
    }

    public enum SensorType
    {
        NorthDisplacement,
        EastDisplacement,
        NorthVelocity,
        EastVelocity,
        NorthAcceleration,
        EastAcceleration,
    }

    // [Header("UI Elements")]
    // [SerializeField] private RawImage responseAxis;
    // [SerializeField] private RawImage responseSeries;
    // [SerializeField] private RawImage responseMarkLine;
    // [SerializeField] private RawImage inputAccAxis;
    // [SerializeField] private RawImage inputAccSeries;
    // [SerializeField] private RawImage inputAccMarkLine;

    private static CommonData commonData = CommonData.Instance;
    private static readonly Color markLineColor = Color.red;

    public Chart responseChart = new Chart();
    public Chart inputAccChart = new Chart();
    private SensorType _internalSensorType = SensorType.NorthDisplacement;
    public SensorType sensorType
    {
        get => _internalSensorType;
        set
        {
            if (value == _internalSensorType) return;
            _internalSensorType = value;
            UpdateResponse();
        }
    }

    private Seismic seismic;
    private MeshDeformer building;
    private float dt;
    // private bool enableMarkLine = false;
    // private bool enableWaveFadeIn = false;

    void Awake()
    {
        commonData.RegisterUI("Sensor", gameObject);
        commonData.EnableUI("Sensor", false);

        seismic = GameObject.Find("Seismic").GetComponent<Seismic>();
    }

    void Start()
    {
        // responseChart.axis = responseAxis;
        // responseChart.series = responseSeries;
        // responseChart.markLine = responseMarkLine;
        responseChart.axis = GameObject.Find("ResponseAxis").GetComponent<RawImage>();
        responseChart.series = GameObject.Find("ResponseSeries").GetComponent<Image>();
        responseChart.markLine = GameObject.Find("ResponseMarkLine").GetComponent<RawImage>();
        var responseRect = responseChart.axis.GetComponent<RectTransform>().rect;
        responseChart.width = Mathf.RoundToInt(responseRect.width);
        responseChart.height = Mathf.RoundToInt(responseRect.height);
        responseChart.markLine.color = markLineColor;
        responseChart.markLine.enabled = false;
        responseChart.InitTrigger(seismic);

        // inputAccChart.axis = inputAccAxis;
        // inputAccChart.series = inputAccSeries;
        // inputAccChart.markLine = inputAccMarkLine;
        inputAccChart.axis = GameObject.Find("InputAccAxis").GetComponent<RawImage>();
        inputAccChart.series = GameObject.Find("InputAccSeries").GetComponent<Image>();
        inputAccChart.markLine = GameObject.Find("InputAccMarkLine").GetComponent<RawImage>();
        var inputAccRect = inputAccChart.axis.GetComponent<RectTransform>().rect;
        inputAccChart.width = Mathf.RoundToInt(inputAccRect.width);
        inputAccChart.height = Mathf.RoundToInt(inputAccRect.height);
        inputAccChart.markLine.color = markLineColor;
        inputAccChart.markLine.enabled = false;
        inputAccChart.InitTrigger(seismic);
    }

    public void SetInputAcceleration(float[] north, float[] east, float dt)
    {
        this.dt = dt;
        building = null;

        PlotData data = new PlotData("Input Acceleration", 2, dt, inputAccChart.width, inputAccChart.height);

        data.name[0] = "North";
        data.yBottom[0] = 0.5f;
        data.yTop[0] = 1f;
        data.values[0] = north;

        data.name[1] = "East";
        data.yBottom[1] = 0f;
        data.yTop[1] = 0.5f;
        data.values[1] = east;

        // Texture2D texture = DataToTexture(data, out inputAccChart.seriesRect);
        // inputAccSeries.texture = texture;

        // responseSeries.texture = null;

        Texture2D axisTexture, seriesTexture;
        DataToTexture(data, out axisTexture, out seriesTexture, out inputAccChart.plotRect);

        inputAccChart.SetTexture(axisTexture, seriesTexture);
        responseChart.ClearTexture();
        SetTime(0f);
    }

    private void UpdateResponse()
    {
        if (building == null) return;
        if (building.northConfig == null) return;
        const int numSensors = 4;

        string title;
        switch (sensorType)
        {
            case SensorType.NorthDisplacement:
                title = "North Displacement";
                break;
            case SensorType.EastDisplacement:
                title = "East Displacement";
                break;
            case SensorType.NorthVelocity:
                title = "North Velocity";
                break;
            case SensorType.EastVelocity:
                title = "East Velocity";
                break;
            case SensorType.NorthAcceleration:
                title = "North Acceleration";
                break;
            case SensorType.EastAcceleration:
                title = "East Acceleration";
                break;
            default:
                Debug.LogError("Unknown sensor type: " + sensorType);
                title = "Unknown";
                break;
        }

        PlotData data = new PlotData(title, numSensors, dt, responseChart.width, responseChart.height);

        for (int i = 0; i < numSensors; i++)
        {
            float height = building.totalHeight * (i + 1) / numSensors;
            data.name[i] = $"S{i + 1}({height:F1}m)";
            data.yBottom[i] = (float)i / numSensors;
            data.yTop[i] = (float)(i + 1) / numSensors;

            float[] response;
            switch (sensorType)
            {
                case SensorType.NorthDisplacement:
                    response = building.northSolver.GetAllDisplacementResponseInterpolated(height);
                    break;
                case SensorType.EastDisplacement:
                    response = building.eastSolver.GetAllDisplacementResponseInterpolated(height);
                    break;
                case SensorType.NorthVelocity:
                    response = building.northSolver.GetAllVelocityResponseInterpolated(height);
                    break;
                case SensorType.EastVelocity:
                    response = building.eastSolver.GetAllVelocityResponseInterpolated(height);
                    break;
                case SensorType.NorthAcceleration:
                    response = building.northSolver.GetAllAccelerationResponseInterpolated(height);
                    break;
                case SensorType.EastAcceleration:
                    response = building.eastSolver.GetAllAccelerationResponseInterpolated(height);
                    break;
                default:
                    Debug.LogError("Unknown sensor type: " + sensorType);
                    response = new float[0];
                    break;
            }

            data.values[i] = response;
        }

        Texture2D axisTexture, seriesTexture;
        DataToTexture(data, out axisTexture, out seriesTexture, out responseChart.plotRect);
        responseChart.SetTexture(axisTexture, seriesTexture);
    }

    public void SetBuilding(MeshDeformer building)
    {
        if (building.northConfig == null) return;
        this.building = building;
        UpdateResponse();
    }

    public void SetTime(float time)
    {
        responseChart.SetProgress(time / seismic.duration);
        inputAccChart.SetProgress(time / seismic.duration);
    }

    public void EnableMarkLine(bool enable)
    {
        // enableMarkLine = enable;
        responseChart.EnableMarkLine(enable);
        inputAccChart.EnableMarkLine(enable);
    }

    public void EnableWaveFadeIn(bool enable)
    {
        // enableWaveFadeIn = enable;
        responseChart.EnableWaveFadeIn(enable);
        inputAccChart.EnableWaveFadeIn(enable);
    }
}
