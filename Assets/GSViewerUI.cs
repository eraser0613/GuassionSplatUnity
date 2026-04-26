using UnityEngine;
using GaussianSplatting.Runtime;

/// <summary>
/// 3DGS查看器前端界面控制器（使用IMGUI，无需UI包）
/// </summary>
public class GSViewerUI : MonoBehaviour
{
    [Header("3DGS References")]
    public GaussianSplatRenderer splatRenderer;
    public Camera mainCamera;
    public GsPixelPicker pixelPicker;

    [Header("Training")]
    public TrainingClient trainingClient;

    [Header("Camera Settings")]
    public float cameraMoveSpeed = 2f;
    public float cameraRotateSpeed = 90f;

    // UI状态
    private bool isPickMode = false;
    private int frameCount = 0;
    private float fps = 0f;
    private float fpsUpdateInterval = 0.5f;
    private float fpsAccumulator = 0f;
    private string pickResultText = "等待点击...";
    private string statusText = "就绪";

    // Training UI state
    private string dataPathInput = "";
    private string outputDirInput = "";
    private bool mockTraining = true;
    private int iterationsInput = 7000;

    // UI窗口位置
    private Rect controlWindowRect = new Rect(10, 10, 220, 620);
    private Rect infoWindowRect = new Rect(Screen.width - 310, 10, 300, 320);

    // 相机控制
    private Vector3 cameraResetPosition = new Vector3(0, 0, -5);
    private Quaternion cameraResetRotation = Quaternion.identity;

    // 视角预设
    private enum ViewPreset
    {
        Front,
        Back,
        Left,
        Right,
        Top,
        Bottom,
        Reset
    }

    void Start()
    {
        InitializeUI();
    }

    void InitializeUI()
    {
        // 自动查找引用
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (splatRenderer == null)
            splatRenderer = FindObjectOfType<GaussianSplatRenderer>();

        if (pixelPicker == null)
            pixelPicker = FindObjectOfType<GsPixelPicker>();

        if (trainingClient == null)
            trainingClient = FindObjectOfType<TrainingClient>();

        // Register training complete callback
        if (trainingClient != null)
        {
            trainingClient.OnTrainingComplete += OnTrainingComplete;
        }

        // 记录相机初始位置
        if (mainCamera != null)
        {
            cameraResetPosition = mainCamera.transform.position;
            cameraResetRotation = mainCamera.transform.rotation;
        }

        Debug.Log("3DGS查看器UI已初始化");
    }

    void OnTrainingComplete(string plyPath)
    {
        statusText = "Training done, importing...";
#if UNITY_EDITOR
        TrainingAssetImporter.ImportAndAssign(plyPath, splatRenderer);
        statusText = "Model loaded!";
#else
        statusText = "PLY ready: " + System.IO.Path.GetFileName(plyPath);
#endif
    }

    void Update()
    {
        UpdateFPS();
        HandleMouseInput();
    }

    void OnGUI()
    {
        // 绘制控制面板
        controlWindowRect = GUI.Window(0, controlWindowRect, DrawControlWindow, "3DGS 查看器");

        // 绘制信息面板
        infoWindowRect = GUI.Window(1, infoWindowRect, DrawInfoWindow, "信息面板");
    }

    void DrawControlWindow(int windowID)
    {
        GUILayout.BeginVertical();
        GUILayout.Space(10);

        // === 点选测距功能 ===
        GUILayout.Label("测量工具", GUI.skin.box);
        GUILayout.Space(5);

        GUI.color = isPickMode ? Color.green : Color.white;
        if (GUILayout.Button(isPickMode ? "● 点选中 (点击场景)" : "点选测距", GUILayout.Height(35)))
        {
            TogglePickMode();
        }
        GUI.color = Color.white;

        if (isPickMode)
        {
            GUILayout.Label("提示: 在3D场景中点击进行测距", GUI.skin.label);
        }

        GUILayout.Space(10);

        // === 视角控制 ===
        GUILayout.Label("视角控制", GUI.skin.box);
        GUILayout.Space(5);

        // 重置视角按钮
        if (GUILayout.Button("重置视角", GUILayout.Height(30)))
        {
            ResetCameraView();
        }

        GUILayout.Space(5);

        // 视角方向按钮（2x3网格）
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("前视图", GUILayout.Height(30)))
            SetCameraView(ViewPreset.Front);
        if (GUILayout.Button("后视图", GUILayout.Height(30)))
            SetCameraView(ViewPreset.Back);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("左视图", GUILayout.Height(30)))
            SetCameraView(ViewPreset.Left);
        if (GUILayout.Button("右视图", GUILayout.Height(30)))
            SetCameraView(ViewPreset.Right);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("俯视图", GUILayout.Height(30)))
            SetCameraView(ViewPreset.Top);
        if (GUILayout.Button("底视图", GUILayout.Height(30)))
            SetCameraView(ViewPreset.Bottom);
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        // === 显示控制 ===
        GUILayout.Label("显示控制", GUI.skin.box);
        GUILayout.Space(5);

        string toggleText = splatRenderer != null && splatRenderer.enabled ? "隐藏点云" : "显示点云";
        if (GUILayout.Button(toggleText, GUILayout.Height(30)))
        {
            TogglePointCloud();
        }

        GUILayout.Space(10);

        // === 训练控制 ===
        GUILayout.Label("3DGS 训练", GUI.skin.box);
        GUILayout.Space(5);

        // Data path input
        GUILayout.Label("数据路径:", GUILayout.Height(18));
        dataPathInput = GUILayout.TextField(dataPathInput, GUILayout.Height(20));

        // Iterations input
        GUILayout.BeginHorizontal();
        GUILayout.Label("迭代次数:", GUILayout.Width(65));
        string itersStr = GUILayout.TextField(iterationsInput.ToString(), GUILayout.Height(20));
        if (int.TryParse(itersStr, out int parsedIters) && parsedIters > 0)
            iterationsInput = parsedIters;
        GUILayout.EndHorizontal();

        // Mock toggle
        mockTraining = GUILayout.Toggle(mockTraining, "Mock模式(测试)");

        GUILayout.Space(3);

        // Connection + training buttons
        if (trainingClient != null)
        {
            bool isConnected = trainingClient.State != TrainingClient.TrainingState.Idle
                            && trainingClient.State != TrainingClient.TrainingState.Error;
            bool isTraining = trainingClient.State == TrainingClient.TrainingState.Training;

            if (!isConnected)
            {
                if (GUILayout.Button("连接服务器", GUILayout.Height(28)))
                {
                    trainingClient.Connect();
                    statusText = trainingClient.StatusMessage;
                }
            }
            else if (isTraining)
            {
                // 训练中：显示停止按钮
                GUI.color = Color.red;
                if (GUILayout.Button("停止训练", GUILayout.Height(28)))
                {
                    trainingClient.StopTraining();
                    statusText = "正在停止...";
                }
                GUI.color = Color.white;
            }
            else
            {
                // 已连接未训练：显示开始按钮
                GUI.color = Color.green;
                if (GUILayout.Button("开始训练", GUILayout.Height(28)))
                {
                    trainingClient.StartTraining(dataPathInput, outputDirInput, mockTraining, iterationsInput);
                }
                GUI.color = Color.white;
            }

            // Show training progress
            if (isTraining)
            {
                GUILayout.Space(5);

                // Progress bar
                float progress = trainingClient.Progress / 100f;
                Rect progressRect = GUILayoutUtility.GetRect(18, 22, GUILayout.ExpandWidth(true));
                DrawProgressBar(progressRect, progress, trainingClient.Progress);

                // Iteration count
                if (trainingClient.TotalIterations > 0)
                {
                    GUILayout.Label(
                        $"迭代: {trainingClient.CurrentIteration} / {trainingClient.TotalIterations}",
                        GUILayout.Height(16));
                }

                // Loss display
                if (trainingClient.CurrentLoss > 0f)
                {
                    GUILayout.Label($"Loss: {trainingClient.CurrentLoss:F5}", GUILayout.Height(16));
                }

                // Extra info
                if (!string.IsNullOrEmpty(trainingClient.ProgressInfo))
                {
                    GUILayout.Label(trainingClient.ProgressInfo, GUILayout.Height(16));
                }
            }
            else if (trainingClient.State != TrainingClient.TrainingState.Idle)
            {
                GUILayout.Label(trainingClient.StatusMessage, GUILayout.Height(18));
            }
        }
        else
        {
            GUILayout.Label("TrainingClient 未配置");
        }

        GUILayout.Space(10);

        // === 状态显示 ===
        GUILayout.Label($"状态: {statusText}");

        GUILayout.EndVertical();

        // 允许窗口拖动
        GUI.DragWindow();
    }

    void DrawInfoWindow(int windowID)
    {
        GUILayout.BeginVertical();
        GUILayout.Space(10);

        // 高斯点数
        int splatCount = splatRenderer != null ? splatRenderer.splatCount : 0;
        GUILayout.Label($"高斯点数: {splatCount:N0}");

        GUILayout.Space(5);

        // FPS
        GUILayout.Label($"FPS: {fps:F1}");

        GUILayout.Space(5);

        // 频标位置
        Vector2 mousePos = Input.mousePosition;
        GUILayout.Label($"鼠标: ({mousePos.x:F0}, {mousePos.y:F0})");

        GUILayout.Space(10);

        // 拾取结果
        GUILayout.Label("拾取结果:", GUILayout.Height(20));

        // 使用文本框显示多行结果
        GUIStyle textStyle = new GUIStyle(GUI.skin.textArea);
        textStyle.wordWrap = true;
        pickResultText = GUILayout.TextArea(pickResultText, textStyle, GUILayout.Height(120));

        GUILayout.Space(10);

        // 使用说明
        GUILayout.Label("操作说明:", GUI.skin.box);
        GUILayout.Label("1. 点击'点选测距'按钮");
        GUILayout.Label("2. 在3D场景中点击");
        GUILayout.Label("3. 查看拾取的3D坐标");
        GUILayout.Label("4. 使用视角按钮调整视角");

        GUILayout.EndVertical();

        // 允许窗口拖动
        GUI.DragWindow();
    }

    void UpdateFPS()
    {
        frameCount++;
        fpsAccumulator += Time.deltaTime;

        if (fpsAccumulator >= fpsUpdateInterval)
        {
            fps = frameCount / fpsAccumulator;
            frameCount = 0;
            fpsAccumulator = 0f;
        }
    }

    void HandleMouseInput()
    {
        // 点选模式下的点击处理
        if (isPickMode && Input.GetMouseButtonDown(0))
        {
            // 检查是否点击在UI窗口上
            Vector2 guiMousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (controlWindowRect.Contains(guiMousePos) || infoWindowRect.Contains(guiMousePos))
                return;

            TryPickPoint();
        }
    }

    void TryPickPoint()
    {
        if (pixelPicker == null)
        {
            pickResultText = "拾取器未配置\n请确保场景中有GsPixelPicker组件";
            return;
        }

        Vector2 mousePos = Input.mousePosition;
        if (pixelPicker.TryPick(mousePos, out Vector3 worldPos))
        {
            pickResultText = $"拾取成功!\n\n世界坐标:\nX: {worldPos.x:F3}\nY: {worldPos.y:F3}\nZ: {worldPos.z:F3}\n\n屏幕坐标:\n({mousePos.x:F0}, {mousePos.y:F0})";
            statusText = "拾取成功";
            Debug.Log($"拾取到点: {worldPos}");
        }
        else
        {
            pickResultText = $"未拾取到点\n\n屏幕坐标:\n({mousePos.x:F0}, {mousePos.y:F0})\n\n请点击3DGS区域";
            statusText = "未拾取到点";
        }
    }

    #region 挌钮事件处理

    void TogglePickMode()
    {
        isPickMode = !isPickMode;
        statusText = isPickMode ? "点选模式:点击场景" : "普通模式";
        pickResultText = "等待点击...";
    }

    void ResetCameraView()
    {
        if (mainCamera != null)
        {
            mainCamera.transform.position = cameraResetPosition;
            mainCamera.transform.rotation = cameraResetRotation;
            statusText = "视角已重置";
            Debug.Log("视角已重置");
        }
        else
        {
            statusText = "相机未配置";
        }
    }

    void SetCameraView(ViewPreset preset)
    {
        if (mainCamera == null || splatRenderer == null || splatRenderer.asset == null)
            return;

        // 获取目标中心
        Vector3 center = splatRenderer.transform.position;
        float distance = 5f;

        // 根据预设设置相机位置
        switch (preset)
        {
            case ViewPreset.Front:
                mainCamera.transform.position = center + new Vector3(0, 0, -distance);
                mainCamera.transform.rotation = Quaternion.identity;
                break;
            case ViewPreset.Back:
                mainCamera.transform.position = center + new Vector3(0, 0, distance);
                mainCamera.transform.rotation = Quaternion.Euler(0, 180, 0);
                break;
            case ViewPreset.Left:
                mainCamera.transform.position = center + new Vector3(-distance, 0, 0);
                mainCamera.transform.rotation = Quaternion.Euler(0, -90, 0);
                break;
            case ViewPreset.Right:
                mainCamera.transform.position = center + new Vector3(distance, 0, 0);
                mainCamera.transform.rotation = Quaternion.Euler(0, 90, 0);
                break;
            case ViewPreset.Top:
                mainCamera.transform.position = center + new Vector3(0, 0, -distance);
                mainCamera.transform.rotation = Quaternion.Euler(90, 0, 0);
                break;
            case ViewPreset.Bottom:
                mainCamera.transform.position = center + new Vector3(0, 0, distance);
                mainCamera.transform.rotation = Quaternion.Euler(-90, 0, 0);
                break;
            case ViewPreset.Reset:
                mainCamera.transform.position = cameraResetPosition;
                mainCamera.transform.rotation = cameraResetRotation;
                break;
        }

    }

    void DrawProgressBar(Rect rect, float progress, float displayPercent)
    {
        // Background
        GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        // Fill
        Rect fillRect = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(progress), rect.height);
        GUI.color = Color.Lerp(new Color(0.2f, 0.6f, 1f), new Color(0.1f, 0.9f, 0.3f), progress);
        GUI.DrawTexture(fillRect, Texture2D.whiteTexture);

        // Text overlay
        GUI.color = Color.white;
        GUIStyle centeredStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        GUI.Label(rect, $"{displayPercent:F1}%", centeredStyle);

        GUI.color = Color.white;
    }

    void TogglePointCloud()
    {
        if (splatRenderer != null)
        {
            splatRenderer.enabled = !splatRenderer.enabled;
            statusText = splatRenderer.enabled ? "已显示点云" : "已隐藏点云";
            Debug.Log($"点云显示: {splatRenderer.enabled}");
        }
        else
        {
            statusText = "未找到渲染器";
        }
    }

    #endregion
}
