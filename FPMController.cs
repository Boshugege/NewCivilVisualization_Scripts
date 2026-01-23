using System.Diagnostics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

[RequireComponent(typeof(CharacterController))]
public class FPMController : MonoBehaviour
{
    private static CommonData commonData = CommonData.Instance;

    private RawImage crosshair;
    private Sensor sensor;
    private Seismic seismic;
    private DasVisualizer dasVisualizer;

    private Button playButton;
    private Button pauseButton;
    private Button stopButton;
    private Button sensorButton;
    private Button configButton;

    public Camera playerCamera;
    private bool isMenuOpen = false;
    private CharacterController characterController;
    private Vector3 moveDirection = Vector3.zero;
    private float rotationX = 0;
    private bool isFlying = true;

    void Awake()
    {
        crosshair = GameObject.Find("Crosshair").GetComponent<RawImage>();
        Assert.IsNotNull(crosshair);
        crosshair.enabled = true;

        sensor = GameObject.Find("Sensor").GetComponent<Sensor>();
        Assert.IsNotNull(sensor);

        seismic = GameObject.Find("Seismic").GetComponent<Seismic>();
        Assert.IsNotNull(seismic);

        GameObject dasVisualizerObj = GameObject.Find("DasVisualizer");
        if (dasVisualizerObj != null)
        {
            dasVisualizer = dasVisualizerObj.GetComponent<DasVisualizer>();
            Debug.Log("找到 DasVisualizer");
        }
        else
        {
            Debug.LogWarning("找不到 DasVisualizer 对象");
        }

        playButton = GameObject.Find("PlayButton")?.GetComponent<Button>();
        playButton.onClick.AddListener(() => { 
            seismic.Play();
            dasVisualizer.Play();
        });


        pauseButton = GameObject.Find("PauseButton")?.GetComponent<Button>();
        pauseButton.onClick.AddListener(() => { 
            seismic.Pause();
            dasVisualizer.Pause();
        });

        stopButton = GameObject.Find("StopButton")?.GetComponent<Button>();
        stopButton.onClick.AddListener(() => { 
            seismic.Stop();
            dasVisualizer.SetTime(0f);
            dasVisualizer.Pause();
        });

        sensorButton = GameObject.Find("SensorButton")?.GetComponent<Button>();
        if (sensorButton != null)
        {
            sensorButton.onClick.AddListener(() => { commonData.ToggleUI("Sensor"); });
        }

        configButton = GameObject.Find("ConfigButton")?.GetComponent<Button>();
        if (configButton != null)
        {
            configButton.onClick.AddListener(() => { commonData.ToggleUI("ConfigMenu"); });
        }

        playerCamera = GetComponentInChildren<Camera>();
    }

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.LeftAlt)) SetCursorState(!Cursor.visible);
        if (Input.GetKeyDown(KeyCode.M)) HandleMenuToggle();

        if (Cursor.visible) return;

        HandleModeSwitch();
        HandleCameraRotation();

        if (isFlying) FlyMovement();
        else WalkMovement();

        if (Input.GetKeyDown(KeyCode.T)) HandleRaycast();

        // if (Input.GetKeyDown(KeyCode.C))
        // {
        //     Stopwatch stopwatch = new Stopwatch();
        //     stopwatch.Start();
        //     LayeredSeismicPropagator propagator = new LayeredSeismicPropagator();
        //     propagator.Run();
        //     stopwatch.Stop();
        //     Debug.Log("LayeredSeismicPropagator execution time: " + stopwatch.ElapsedMilliseconds + " ms");
        // }
    }

    private void HandleMenuToggle()
    {
        isMenuOpen = !isMenuOpen;
        crosshair.enabled = !isMenuOpen;
        // configMenu.gameObject.SetActive(isMenuOpen);
        commonData.EnableUI("ConfigMenu", isMenuOpen);
        SetCursorState(isMenuOpen);
    }

    void HandleModeSwitch()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            isFlying = !isFlying;
            moveDirection = Vector3.zero; // 重置移动方向
        }
    }

    void HandleCameraRotation()
    {
        rotationX += -Input.GetAxis("Mouse Y") * commonData.mouseSensitivity;
        rotationX = Mathf.Clamp(rotationX, -CommonData.lookXLimit, CommonData.lookXLimit);
        playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
        transform.rotation *= Quaternion.Euler(0, Input.GetAxis("Mouse X") * commonData.mouseSensitivity, 0);
    }

    void WalkMovement()
    {
        if (characterController.isGrounded)
        {
            Vector3 forward = transform.TransformDirection(Vector3.forward);
            Vector3 right = transform.TransformDirection(Vector3.right);

            float curSpeedX = Input.GetAxis("Vertical") * commonData.walkSpeed;
            float curSpeedY = Input.GetAxis("Horizontal") * commonData.walkSpeed;
            moveDirection = (forward * curSpeedX) + (right * curSpeedY);

            if (Input.GetKeyDown(KeyCode.Space))
                moveDirection.y = commonData.jumpForce;
        }

        moveDirection.y -= commonData.gravity * Time.deltaTime;
        characterController.Move(moveDirection * Time.deltaTime);
    }

    void FlyMovement()
    {
        Vector3 move = Vector3.zero;
        float verticalInput = Input.GetAxis("Vertical");
        float horizontalInput = Input.GetAxis("Horizontal");

        // 前后移动（W/S）
        move += transform.forward * verticalInput;
        // 左右移动（A/D）
        move += transform.right * horizontalInput;
        // 上升（Space）下降（LeftControl）
        if (Input.GetKey(KeyCode.Space)) move += Vector3.up;
        if (Input.GetKey(KeyCode.LeftShift)) move += Vector3.down;

        characterController.Move(move * commonData.flySpeed * Time.deltaTime);
    }

    private void HandleRaycast()
    {
        Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));
        RaycastHit hit;

        GameObject lineObj = new GameObject("RayVisualization");
        LineRenderer lineRenderer = lineObj.AddComponent<LineRenderer>();
        lineRenderer.startWidth = 0.01f;
        lineRenderer.endWidth = 0.01f;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = Color.red;
        lineRenderer.endColor = Color.red;
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, ray.origin);
        lineRenderer.SetPosition(1, ray.origin + ray.direction * 1500f);
        lineRenderer.startWidth = 0.5f;
        lineRenderer.endWidth = 0.5f;
        Destroy(lineObj, 3f);

        if (Physics.Raycast(ray, out hit, 1500f, LayerMask.GetMask("MeshDeformer")))
        {
            MeshDeformer deformer = hit.collider.GetComponent<MeshDeformer>();
            if (deformer != null)
            {
                Debug.Log("WaveInfo.cs - Raycast Hit " + deformer.gameObject.name);
                // Debug.Log("WaveInfo.cs - Max X Disp: " + deformer.xSolver.maxDisplacement);
                // Debug.Log("WaveInfo.cs - Max Z Disp: " + deformer.zSolver.maxDisplacement);
                // Debug.Log("WaveInfo.cs - Max X Acc: " + deformer.xSolver.maxAcceleration);
                // Debug.Log("WaveInfo.cs - Max Z Acc: " + deformer.zSolver.maxAcceleration);
                sensor.SetBuilding(deformer);
            }
        }
    }

    public void OnFOVChanged(float value)
    {
        playerCamera.fieldOfView = value;
    }

    public void SetCursorState(bool visible)
    {
        Cursor.visible = visible;
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
    }

    // public void Trace
}