using System;
using System.Runtime.InteropServices;
using UnityEngine;
using GaussianSplatting.Runtime;

public class ApproxGsMeasureTool : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    public GaussianSplatRenderer splatRenderer;
    public LineRenderer lineRenderer;
    public GsPixelPicker picker;
    public GSViewerUI viewerUI;

    [Header("Fallback Picking")]
    public bool preferChunkBounds = true;
    public bool fallbackToBoundsIfPickerFails = true;

    [Header("Markers")]
    public bool drawMarkers = true;
    public float markerScale = 0.03f;

    private Vector3? pointA;
    private Vector3? pointB;
    private GameObject markerA;
    private GameObject markerB;

    private Bounds[] chunkBoundsLocal;
    private bool chunkBoundsReady;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ChunkInfoRaw
    {
        public uint colR, colG, colB, colA;

        public float posXMin, posXMax;
        public float posYMin, posYMax;
        public float posZMin, posZMax;

        public uint sclX, sclY, sclZ;
        public uint shR, shG, shB;
    }

    void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (viewerUI == null)
            viewerUI = FindObjectOfType<GSViewerUI>();

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
            lineRenderer.useWorldSpace = true;
        }

        BuildChunkBounds();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && viewerUI != null && viewerUI.IsPickMode)
            TryPick();

        if (Input.GetKeyDown(KeyCode.R))
            ResetMeasurement();
    }

    void BuildChunkBounds()
    {
        chunkBoundsReady = false;
        chunkBoundsLocal = null;

        if (splatRenderer == null || splatRenderer.asset == null)
            return;

        var asset = splatRenderer.asset;

        if (!preferChunkBounds || asset.chunkData == null)
        {
            Debug.Log("未使用 chunk picking，或该资产没有 chunkData，将退回整体 bounds picking。");
            return;
        }

        byte[] bytes = asset.chunkData.bytes;
        int stride = Marshal.SizeOf<ChunkInfoRaw>();

        if (bytes == null || bytes.Length < stride)
        {
            Debug.Log("chunkData 无效，将退回整体 bounds picking。");
            return;
        }

        int count = bytes.Length / stride;
        chunkBoundsLocal = new Bounds[count];

        IntPtr ptr = Marshal.AllocHGlobal(stride);

        try
        {
            for (int i = 0; i < count; i++)
            {
                Marshal.Copy(bytes, i * stride, ptr, stride);
                ChunkInfoRaw raw = Marshal.PtrToStructure<ChunkInfoRaw>(ptr);

                Vector3 bmin = new Vector3(raw.posXMin, raw.posYMin, raw.posZMin);
                Vector3 bmax = new Vector3(raw.posXMax, raw.posYMax, raw.posZMax);

                Bounds b = new Bounds();
                b.SetMinMax(bmin, bmax);
                chunkBoundsLocal[i] = b;
            }

            chunkBoundsReady = true;
            Debug.Log($"已加载 {count} 个 chunk bounds，用于 fallback picking。");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    void TryPick()
    {
        if (targetCamera == null || splatRenderer == null || splatRenderer.asset == null)
        {
            Debug.LogWarning("请先把 Camera 和 GaussianSplatRenderer 拖到脚本上。");
            return;
        }

        bool ok = false;
        Vector3 hitWorld = Vector3.zero;

        // 1) 优先使用与渲染贡献一致的像素级 picker
        if (picker != null)
        {
            ok = picker.TryPickDetailed(Input.mousePosition, out GsPixelPicker.PickResult pick);
            hitWorld = pick.worldPosition;
            if (ok)
                Debug.Log($"Accurate pick: {pick.diagnostic}");
        }

        // 2) 如果 picker 失败，按需退回旧的 bounds/chunk picking
        if (!ok && fallbackToBoundsIfPickerFails)
        {
            Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);

            ok = TryPickFromChunks(ray, out hitWorld);
            if (!ok)
                ok = TryPickFromWholeBounds(ray, out hitWorld);
        }

        if (!ok)
        {
            Debug.Log("没有点到 3DGS 区域。");
            return;
        }

        if (pointA == null)
        {
            pointA = hitWorld;
            PlaceMarker(ref markerA, hitWorld, Color.green);
            Debug.Log($"Point A: {hitWorld}");
        }
        else if (pointB == null)
        {
            pointB = hitWorld;
            PlaceMarker(ref markerB, hitWorld, Color.red);
            Debug.Log($"Point B: {hitWorld}");
            UpdateMeasurement();
        }
        else
        {
            ResetMeasurement();
            pointA = hitWorld;
            PlaceMarker(ref markerA, hitWorld, Color.green);
            Debug.Log($"Point A: {hitWorld}");
        }
    }

    bool TryPickFromWholeBounds(Ray worldRay, out Vector3 hitWorld)
    {
        hitWorld = Vector3.zero;

        var asset = splatRenderer.asset;
        Transform tr = splatRenderer.transform;

        Matrix4x4 worldToLocal = tr.worldToLocalMatrix;
        Vector3 localOrigin = worldToLocal.MultiplyPoint(worldRay.origin);
        Vector3 localDir = worldToLocal.MultiplyVector(worldRay.direction).normalized;

        Bounds localBounds = new Bounds();
        localBounds.SetMinMax(asset.boundsMin, asset.boundsMax);

        if (!RayAabb(localOrigin, localDir, localBounds.min, localBounds.max, out float tMin, out float tMax))
            return false;

        float tHit = tMin >= 0 ? tMin : tMax;
        if (tHit < 0)
            return false;

        Vector3 localHit = localOrigin + localDir * tHit;
        hitWorld = tr.localToWorldMatrix.MultiplyPoint(localHit);
        return true;
    }

    bool TryPickFromChunks(Ray worldRay, out Vector3 hitWorld)
    {
        hitWorld = Vector3.zero;

        if (!chunkBoundsReady || chunkBoundsLocal == null || chunkBoundsLocal.Length == 0)
            return false;

        Transform tr = splatRenderer.transform;
        Matrix4x4 worldToLocal = tr.worldToLocalMatrix;

        Vector3 localOrigin = worldToLocal.MultiplyPoint(worldRay.origin);
        Vector3 localDir = worldToLocal.MultiplyVector(worldRay.direction).normalized;

        bool found = false;
        float bestT = float.PositiveInfinity;
        Vector3 bestLocalHit = Vector3.zero;

        for (int i = 0; i < chunkBoundsLocal.Length; i++)
        {
            Bounds b = chunkBoundsLocal[i];

            if (!RayAabb(localOrigin, localDir, b.min, b.max, out float tMin, out float tMax))
                continue;

            float tHit = tMin >= 0 ? tMin : tMax;
            if (tHit < 0)
                continue;

            if (tHit < bestT)
            {
                bestT = tHit;
                bestLocalHit = localOrigin + localDir * tHit;
                found = true;
            }
        }

        if (!found)
            return false;

        hitWorld = tr.localToWorldMatrix.MultiplyPoint(bestLocalHit);
        return true;
    }

    bool RayAabb(Vector3 ro, Vector3 rd, Vector3 bmin, Vector3 bmax, out float tmin, out float tmax)
    {
        tmin = float.NegativeInfinity;
        tmax = float.PositiveInfinity;

        for (int i = 0; i < 3; i++)
        {
            float origin = ro[i];
            float dir = rd[i];
            float min = bmin[i];
            float max = bmax[i];

            if (Mathf.Abs(dir) < 1e-6f)
            {
                if (origin < min || origin > max)
                    return false;
            }
            else
            {
                float t1 = (min - origin) / dir;
                float t2 = (max - origin) / dir;

                if (t1 > t2)
                {
                    float tmp = t1;
                    t1 = t2;
                    t2 = tmp;
                }

                tmin = Mathf.Max(tmin, t1);
                tmax = Mathf.Min(tmax, t2);

                if (tmin > tmax)
                    return false;
            }
        }

        return true;
    }

    void UpdateMeasurement()
    {
        if (pointA == null || pointB == null)
            return;

        float distance = Vector3.Distance(pointA.Value, pointB.Value);

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, pointA.Value);
            lineRenderer.SetPosition(1, pointB.Value);
        }

        Debug.Log($"Distance = {distance:F3} m");
        Debug.Log($"A = {pointA.Value}");
        Debug.Log($"B = {pointB.Value}");
    }

    void PlaceMarker(ref GameObject marker, Vector3 pos, Color color)
    {
        if (!drawMarkers)
            return;

        if (marker == null)
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.transform.localScale = Vector3.one * markerScale;

            Collider col = marker.GetComponent<Collider>();
            if (col != null)
                Destroy(col);

            Renderer mr = marker.GetComponent<Renderer>();
            if (mr != null)
            {
                Material material = CreateColorMaterial(color);
                if (material != null)
                    mr.material = material;
            }
        }

        marker.transform.position = pos;
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

        if (shader == null)
            return null;

        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    public void ResetMeasurement()
    {
        pointA = null;
        pointB = null;

        if (markerA != null) Destroy(markerA);
        if (markerB != null) Destroy(markerB);

        if (lineRenderer != null)
            lineRenderer.positionCount = 0;

        Debug.Log("Measurement reset.");
    }
}