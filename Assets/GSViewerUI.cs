using System.Collections.Generic;
using System.Text;
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
    public float cameraSpeedBoostMultiplier = 3f;
    [Range(1f, 89f)] public float cameraPitchLimit = 85f;

    [Header("Pick Marker")]
    public bool showPickMarker = true;
    public float pickMarkerScale = 0.02f;
    public Color pickMarkerColor = Color.yellow;

    [Header("Pick Debug Visualization")]
    public bool showPickDebug = true;
    public float debugRayLength = 100f;
    public float debugRayWidth = 0.025f;
    public float debugSurfaceLineWidth = 0.045f;
    public float debugMarkerScale = 0.08f;
    public Color debugRayColor = Color.cyan;
    public Color debugCandidateColor = new Color(1f, 0.55f, 0f, 1f);
    public Color debugSelectedColor = Color.magenta;
    public Color debugSurfaceSegmentColor = Color.yellow;
    private static readonly Color[] debugLabelColors =
    {
        new Color(1f, 0.55f, 0f, 1f),
        new Color(0f, 0.9f, 1f, 1f),
        new Color(0.2f, 1f, 0.25f, 1f),
        new Color(1f, 0.95f, 0f, 1f),
        new Color(0.75f, 0.45f, 1f, 1f),
        new Color(1f, 0.35f, 0.35f, 1f),
        new Color(0.45f, 0.75f, 1f, 1f),
        new Color(0.75f, 1f, 0.45f, 1f)
    };

    // UI状态
    public bool IsPickMode => isPickMode;
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
    private Rect controlWindowRect = new Rect(10, 10, 220, 660);
    private Rect infoWindowRect = new Rect(Screen.width - 430, 10, 420, 520);

    // 相机控制
    private Vector3 cameraResetPosition = new Vector3(0, 0, -5);
    private Quaternion cameraResetRotation = Quaternion.identity;
    private float cameraYaw;
    private float cameraPitch;
    private bool isMouseLooking;

    private GameObject pickMarker;
    private Vector2? lastPickScreenPixel;
    private Vector3 lastPickWorldPosition;
    private GsPixelPicker.PickResult? lastPickResult;
    private Ray lastPickRay;
    private LineRenderer debugRayLine;
    private LineRenderer debugSurfaceLine;
    private bool pickPending;
    private Vector2 pendingPickMousePos;
    private readonly List<GameObject> debugLabelAnchors = new List<GameObject>();
    private Material debugCandidateMaterial;
    private Material debugSelectedMaterial;
    private Material debugRayMaterial;
    private Material debugSurfaceLineMaterial;

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
            SyncCameraAnglesFromTransform();
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
        HandleCameraRoamInput();
        HandleMouseInput();
    }

    void OnGUI()
    {
        DrawPickScreenMarker();
        DrawPickDebugLabels();

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

        showPickDebug = GUILayout.Toggle(showPickDebug, "显示拾取调试: 射线/候选高斯/数值");

        if (pixelPicker != null)
        {
            GUILayout.Space(5);
            GUILayout.Label($"Surface Alpha: {pixelPicker.surfaceAlpha:F2}");
            pixelPicker.surfaceAlpha = GUILayout.HorizontalSlider(pixelPicker.surfaceAlpha, 0f, 1f, GUILayout.Height(20));
        }

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

        GUILayout.Label("右键拖动旋转，WASD移动，Q/E升降", GUI.skin.label);

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
        GUILayout.Label("拾取结果 / 选择理由:", GUILayout.Height(20));

        // 使用文本框显示多行结果
        GUIStyle textStyle = new GUIStyle(GUI.skin.textArea);
        textStyle.wordWrap = true;
        pickResultText = GUILayout.TextArea(pickResultText, textStyle, GUILayout.Height(260));

        GUILayout.Space(10);

        // 使用说明
        GUILayout.Label("操作说明:", GUI.skin.box);
        GUILayout.Label("右键拖动: 旋转视角");
        GUILayout.Label("W/A/S/D: 前后左右移动");
        GUILayout.Label("Q/E: 下降/上升");
        GUILayout.Label("Left Shift: 加速移动");
        GUILayout.Label("点选测距: 左键点击场景");
        GUILayout.Label("调试: 彩球在射线上；点云本体高亮为 Gaussian center");
        GUILayout.Label("重置视角: 回到初始位置");

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

    void HandleCameraRoamInput()
    {
        if (mainCamera == null)
            return;

        Vector3 move = Vector3.zero;

        if (Input.GetKey(KeyCode.W))
            move += mainCamera.transform.forward;
        if (Input.GetKey(KeyCode.S))
            move -= mainCamera.transform.forward;
        if (Input.GetKey(KeyCode.A))
            move -= mainCamera.transform.right;
        if (Input.GetKey(KeyCode.D))
            move += mainCamera.transform.right;
        if (Input.GetKey(KeyCode.Q))
            move -= Vector3.up;
        if (Input.GetKey(KeyCode.E))
            move += Vector3.up;

        if (move.sqrMagnitude > 0f)
        {
            float speed = cameraMoveSpeed;
            if (Input.GetKey(KeyCode.LeftShift))
                speed *= Mathf.Max(1f, cameraSpeedBoostMultiplier);

            mainCamera.transform.position += move.normalized * speed * Time.deltaTime;
        }

        bool mouseOverUi = IsMouseOverUiWindow();
        if (Input.GetMouseButtonDown(1))
            isMouseLooking = !mouseOverUi;
        if (Input.GetMouseButtonUp(1))
            isMouseLooking = false;

        if (!Input.GetMouseButton(1) || !isMouseLooking || mouseOverUi)
            return;

        cameraYaw += Input.GetAxis("Mouse X") * cameraRotateSpeed * Time.deltaTime;
        cameraPitch -= Input.GetAxis("Mouse Y") * cameraRotateSpeed * Time.deltaTime;
        cameraPitch = Mathf.Clamp(cameraPitch, -cameraPitchLimit, cameraPitchLimit);
        mainCamera.transform.rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
    }

    bool IsMouseOverUiWindow()
    {
        Vector2 guiMousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        return controlWindowRect.Contains(guiMousePos) || infoWindowRect.Contains(guiMousePos);
    }

    void SyncCameraAnglesFromTransform()
    {
        if (mainCamera == null)
            return;

        Vector3 euler = mainCamera.transform.rotation.eulerAngles;
        cameraYaw = NormalizeAngle(euler.y);
        cameraPitch = NormalizeAngle(euler.x);
        cameraPitch = Mathf.Clamp(cameraPitch, -cameraPitchLimit, cameraPitchLimit);
        isMouseLooking = false;
    }

    float NormalizeAngle(float angle)
    {
        while (angle > 180f)
            angle -= 360f;
        while (angle < -180f)
            angle += 360f;
        return angle;
    }

    void HandleMouseInput()
    {
        // 点选模式下的点击处理
        if (isPickMode && Input.GetMouseButtonDown(0))
        {
            // 检查是否点击在UI窗口上
            if (IsMouseOverUiWindow())
                return;

            QueuePickPoint(Input.mousePosition);
        }
    }

    void QueuePickPoint(Vector2 mousePos)
    {
        pendingPickMousePos = mousePos;
        if (pickPending)
            return;

        pickPending = true;
        StartCoroutine(PerformQueuedPickAfterFrame());
    }

    System.Collections.IEnumerator PerformQueuedPickAfterFrame()
    {
        yield return new WaitForEndOfFrame();
        pickPending = false;
        TryPickPoint(pendingPickMousePos);
    }

    void TryPickPoint(Vector2 mousePos)
    {
        if (pixelPicker == null)
        {
            pickResultText = "拾取器未配置\n请确保场景中有GsPixelPicker组件";
            return;
        }

        lastPickRay = mainCamera != null ? mainCamera.ScreenPointToRay(mousePos) : default;
        if (pixelPicker.TryPickDetailed(mousePos, out GsPixelPicker.PickResult pick))
        {
            Vector3 worldPos = pick.worldPosition;
            lastPickResult = pick;
            pickResultText = BuildPickResultText(pick, mousePos);
            ShowPickMarker(worldPos, mousePos);
            ShowDebugCandidates(pick);
            statusText = "拾取成功";
            Debug.Log($"拾取到点: {worldPos} | {pick.diagnostic}\n{pixelPicker.BuildCandidatesText(pick)}");
        }
        else
        {
            lastPickResult = null;
            ClearDebugCandidateMarkers();
            pickResultText = $"未拾取到点\n\n屏幕坐标:\n({mousePos.x:F0}, {mousePos.y:F0})\n\n{pick.diagnostic}";
            statusText = "未拾取到点";
        }
    }

    string BuildPickResultText(GsPixelPicker.PickResult pick, Vector2 mousePos)
    {
        var sb = new StringBuilder();
        sb.AppendLine("拾取成功!");
        sb.AppendLine();
        sb.AppendLine($"世界坐标: X={pick.worldPosition.x:F3}, Y={pick.worldPosition.y:F3}, Z={pick.worldPosition.z:F3}");
        sb.AppendLine($"Splat: {pick.splatIndex}  排序: {pick.sortedIndex}");
        sb.AppendLine($"Alpha: {pick.alpha:F3}  屏幕贡献: {pick.contribution:F3}  置信: {pick.confidence:F3}");
        sb.AppendLine($"Depth: {pick.depth:F3}");
        sb.AppendLine($"屏幕: ({mousePos.x:F0}, {mousePos.y:F0})  Pick: ({pick.pickPixel.x:F0}, {pick.pickPixel.y:F0})");
        sb.AppendLine("选择方式: 使用当前帧渲染数据，按点击像素的可见贡献选择候选，并用 surfaceAlpha 加权深度投回点击射线。");
        sb.AppendLine("可视化对应: 青线=整条射线，黄线=到最终测距点；彩球/标签 #N 在射线上对应候选深度；点云本体洋红高亮为对应 Gaussian center。");
        sb.AppendLine();
        sb.AppendLine(pick.selectionReason);
        sb.AppendLine();
        sb.Append(BuildCandidatesTextForUi(pick));
        return sb.ToString();
    }

    string BuildCandidatesTextForUi(GsPixelPicker.PickResult pick)
    {
        if (pick.candidates == null || pick.candidates.Length == 0)
            return "候选高斯: 无";

        var sb = new StringBuilder();
        sb.AppendLine($"候选高斯: 显示 {pick.candidates.Length}/{pick.totalCandidateCount}");
        foreach (var candidate in pick.candidates)
        {
            string marker = candidate.selected ? "最终" : "候选";
            sb.Append(candidate.selected ? "* " : "  ");
            sb.Append('#').Append(candidate.ordinal)
              .Append('[').Append(marker).Append(']')
              .Append(" splat=").Append(candidate.splatIndex)
              .Append(" sort=").Append(candidate.sortedIndex)
              .Append(" depth=").Append(candidate.linearDepth.ToString("F3"))
              .Append(" accum=").Append(candidate.accumulatedAlphaAfter.ToString("F3"))
              .Append(" used=").Append(candidate.usedContribution.ToString("F3"))
              .Append(" alpha=").Append(candidate.alpha.ToString("F3"))
              .Append(" screenContrib=").Append(candidate.contribution.ToString("F3"))
              .AppendLine();
        }
        return sb.ToString();
    }

    void ShowPickMarker(Vector3 worldPos, Vector2 clickPixel)
    {
        lastPickWorldPosition = worldPos;
        lastPickScreenPixel = clickPixel;

        if (!showPickMarker)
            return;

        if (pickMarker == null)
        {
            pickMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pickMarker.name = "3DGS Pick Marker";

            Collider col = pickMarker.GetComponent<Collider>();
            if (col != null)
                Destroy(col);

            Renderer mr = pickMarker.GetComponent<Renderer>();
            if (mr != null)
            {
                mr.material = CreateColorMaterial(pickMarkerColor);
            }
        }

        pickMarker.transform.position = worldPos;
        pickMarker.transform.localScale = Vector3.one * pickMarkerScale;
        pickMarker.SetActive(true);

        Debug.DrawLine(worldPos - Vector3.up * pickMarkerScale * 2f, worldPos + Vector3.up * pickMarkerScale * 2f, pickMarkerColor, 5f);
        Debug.DrawLine(worldPos - Vector3.right * pickMarkerScale * 2f, worldPos + Vector3.right * pickMarkerScale * 2f, pickMarkerColor, 5f);
        Debug.DrawLine(worldPos - Vector3.forward * pickMarkerScale * 2f, worldPos + Vector3.forward * pickMarkerScale * 2f, pickMarkerColor, 5f);
    }

    void ShowDebugCandidates(GsPixelPicker.PickResult pick)
    {
        ClearDebugCandidateMarkers();
        if (!showPickDebug || pick.candidates == null)
            return;

        EnsureDebugMaterials();
        UpdateDebugRayLines(pick);
        HighlightCandidateSplats(pick);

        foreach (var candidate in pick.candidates)
        {
            GameObject anchor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            anchor.name = candidate.selected ? $"Label Selected GS #{candidate.ordinal}" : $"Label Candidate GS #{candidate.ordinal}";

            Collider col = anchor.GetComponent<Collider>();
            if (col != null)
                Destroy(col);

            Renderer mr = anchor.GetComponent<Renderer>();
            if (mr != null)
            {
                mr.material = candidate.selected ? debugSelectedMaterial : debugCandidateMaterial;
                mr.material.color = candidate.selected ? debugSelectedColor : DebugLabelColor(candidate.ordinal);
            }

            anchor.transform.position = ProjectCandidateDepthOntoPickRay(candidate.linearDepth);
            float scale = candidate.selected ? debugMarkerScale * 1.6f : debugMarkerScale;
            anchor.transform.localScale = Vector3.one * scale;
            debugLabelAnchors.Add(anchor);
        }
    }

    void EnsureDebugMaterials()
    {
        if (debugCandidateMaterial == null)
            debugCandidateMaterial = CreateColorMaterial(debugCandidateColor);

        if (debugSelectedMaterial == null)
            debugSelectedMaterial = CreateColorMaterial(debugSelectedColor);

        if (debugRayMaterial == null)
            debugRayMaterial = CreateColorMaterial(debugRayColor);

        if (debugSurfaceLineMaterial == null)
            debugSurfaceLineMaterial = CreateColorMaterial(debugSurfaceSegmentColor);
    }

    Material CreateColorMaterial(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("UI/Default");
        if (shader == null)
            shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = shader != null ? new Material(shader) : new Material(debugCandidateMaterial);
        material.color = color;
        return material;
    }

    void UpdateDebugRayLines(GsPixelPicker.PickResult pick)
    {
        if (mainCamera == null)
            return;

        if (debugRayLine == null)
            debugRayLine = CreateDebugLine("Pick Debug Ray", debugRayMaterial, debugRayWidth);
        if (debugSurfaceLine == null)
            debugSurfaceLine = CreateDebugLine("Pick Surface Segment", debugSurfaceLineMaterial, debugSurfaceLineWidth);

        Vector3 rayStart = lastPickRay.origin;
        Vector3 rayEnd = lastPickRay.origin + lastPickRay.direction * debugRayLength;
        debugRayLine.positionCount = 2;
        debugRayLine.SetPosition(0, rayStart);
        debugRayLine.SetPosition(1, rayEnd);
        debugRayLine.enabled = true;

        debugSurfaceLine.positionCount = 2;
        debugSurfaceLine.SetPosition(0, rayStart);
        debugSurfaceLine.SetPosition(1, pick.worldPosition);
        debugSurfaceLine.enabled = true;
    }

    Vector3 ProjectCandidateDepthOntoPickRay(float linearDepth)
    {
        if (mainCamera == null)
            return lastPickRay.origin + lastPickRay.direction * Mathf.Max(linearDepth, 0f);

        Plane depthPlane = new Plane(mainCamera.transform.forward, mainCamera.transform.position + mainCamera.transform.forward * linearDepth);
        if (depthPlane.Raycast(lastPickRay, out float enter))
            return lastPickRay.GetPoint(enter);

        return lastPickRay.origin + lastPickRay.direction * Mathf.Max(linearDepth, 0f);
    }

    LineRenderer CreateDebugLine(string lineName, Material lineMaterial, float width)
    {
        GameObject lineObject = new GameObject(lineName);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 0;
        line.material = lineMaterial;
        line.startColor = lineMaterial.color;
        line.endColor = lineMaterial.color;
        line.startWidth = width;
        line.endWidth = width;
        line.numCapVertices = 12;
        line.numCornerVertices = 8;
        line.enabled = false;
        return line;
    }

    void HighlightCandidateSplats(GsPixelPicker.PickResult pick)
    {
        if (splatRenderer == null || pick.candidates == null)
            return;

        var indices = new List<int>(pick.candidates.Length);
        foreach (var candidate in pick.candidates)
            indices.Add(candidate.splatIndex);

        splatRenderer.DebugHighlightSplats(indices);
    }

    void ClearPickDebugState()
    {
        lastPickResult = null;
        lastPickScreenPixel = null;
        if (pickMarker != null)
            pickMarker.SetActive(false);
        ClearDebugCandidateMarkers();
    }

    void ClearDebugCandidateMarkers()
    {
        foreach (GameObject anchor in debugLabelAnchors)
        {
            if (anchor != null)
                Destroy(anchor);
        }
        debugLabelAnchors.Clear();

        if (debugRayLine != null)
            debugRayLine.enabled = false;
        if (debugSurfaceLine != null)
            debugSurfaceLine.enabled = false;
        if (splatRenderer != null)
            splatRenderer.DebugClearHighlightedSplats();
    }

    void DrawPickScreenMarker()
    {
        if (!lastPickScreenPixel.HasValue || mainCamera == null)
            return;

        Vector2 screen = lastPickScreenPixel.Value;

        float x = screen.x;
        float y = Screen.height - screen.y;
        const float size = 32f;
        const float thickness = 6f;

        Color prev = GUI.color;
        GUI.color = pickMarkerColor;
        GUI.DrawTexture(new Rect(x - size, y - thickness * 0.5f, size * 2f, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - thickness * 0.5f, y - size, thickness, size * 2f), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    void DrawPickDebugLabels()
    {
        if (!showPickDebug || !lastPickResult.HasValue || mainCamera == null)
            return;

        var pick = lastPickResult.Value;
        if (debugRayLine != null)
            debugRayLine.enabled = true;
        if (debugSurfaceLine != null)
            debugSurfaceLine.enabled = true;

        if (Event.current.type != EventType.Repaint || pick.candidates == null)
            return;

        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 12,
            alignment = TextAnchor.UpperLeft
        };

        foreach (var candidate in pick.candidates)
        {
            Vector3 screen = mainCamera.WorldToScreenPoint(ProjectCandidateDepthOntoPickRay(candidate.linearDepth));
            if (screen.z <= 0f)
                continue;

            float x = screen.x + 8f;
            float y = Screen.height - screen.y;
            Color prevColor = GUI.color;
            GUI.color = candidate.selected ? debugSelectedColor : DebugLabelColor(candidate.ordinal);
            string label =
                $"#{candidate.ordinal} {(candidate.selected ? "最终" : "候选")}\n" +
                $"splat={candidate.splatIndex}\n" +
                $"depth={candidate.linearDepth:F2} used={candidate.usedContribution:F3}\n" +
                $"acc={candidate.accumulatedAlphaAfter:F3}\n" +
                $"a={candidate.alpha:F3} c={candidate.contribution:F3}";
            GUI.Box(new Rect(x, y, 190f, 92f), label, style);
            GUI.color = prevColor;
        }
    }

    Color DebugLabelColor(int ordinal)
    {
        if (ordinal < 0)
            return debugCandidateColor;
        return debugLabelColors[ordinal % debugLabelColors.Length];
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
            SyncCameraAnglesFromTransform();
            statusText = "视角已重置";
            Debug.Log("视角已重置");
        }
        else
        {
            statusText = "相机未配置";
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
