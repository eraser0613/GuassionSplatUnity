using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using GaussianSplatting.Runtime;

public class GsPixelPicker : MonoBehaviour
{
    public Camera targetCamera;
    public GaussianSplatRenderer splatRenderer;
    public ComputeShader pickCompute;

    [Header("Picking")]
    [Tooltip("Search this many pixels around the click when the exact pixel has no confident contribution.")]
    [Range(0, 4)] public int neighborhoodRadius = 1;
    [Tooltip("Minimum per-splat alpha considered visible by the picker. Rendering discards below 1/255.")]
    public float alphaCutoff = 1.0f / 255.0f;
    [Tooltip("Accumulated alpha treated as the front visible surface. Lower values pick shallower/front layers; higher values pick deeper blended layers.")]
    [Range(0f, 1f)] public float surfaceAlpha = 0.55f;
    [Tooltip("Normally keep this off. Manual view refresh outside the render pass can use stale/wrong Unity camera matrices and pick offscreen splats.")]
    public bool refreshViewDataOnPick = false;
    [Tooltip("Match RenderGaussianSplats.shader backbuffer Y flip. Keep enabled for the current built-in pipeline viewer.")]
    public bool flipYForBackbuffer = true;
    [Tooltip("Maximum number of visible splats at the picked pixel to return for measurement diagnosis.")]
    [Range(1, 32)] public int maxDebugCandidates = 12;
    [Tooltip("How many Gaussian sigmas the true 3D ray intersection is allowed to pass from a splat center.")]
    [Range(0.5f, 4f)] public float rayCandidateSigma = 3f;
    [Tooltip("Safety cap: only test this many visible pixel contributors against the true 3D ray to avoid long GPU stalls.")]
    [Range(32, 4096)] public int maxRayTestSplats = 512;
    public bool logDebugInfo;

    public struct DebugCandidate
    {
        public int ordinal;
        public int splatIndex;
        public int sortedIndex;
        public Vector3 splatCenterWorld;
        public float linearDepth;
        public float alpha;
        public float contribution;
        public float accumulatedAlphaBefore;
        public float accumulatedAlphaAfter;
        public float usedContribution;
        public float confidence;
        public Vector2 gaussianLocal;
        public float power;
        public float rayT;
        public float centerRayDistance;
        public float ellipsoidDistance;
        public float closestLinearDepth;
        public bool selected;
    }

    public struct PickResult
    {
        public bool hit;
        public Vector2 inputPixel;
        public Vector2 pickPixel;
        public Vector3 worldPosition;
        public Vector3 splatCenterWorld;
        public int splatIndex;
        public int sortedIndex;
        public float depth;
        public float alpha;
        public float contribution;
        public float accumulatedAlpha;
        public float confidence;
        public int totalCandidateCount;
        public float candidateContributionSum;
        public int selectedCandidateOrdinal;
        public DebugCandidate[] candidates;
        public string selectionReason;
        public string diagnostic;
    }

    private GraphicsBuffer pickResultBuffer;
    private GraphicsBuffer pickCandidatesBuffer;
    private int kernelPick;
    private int candidateBufferCapacity;

    void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (pickCompute != null)
            kernelPick = pickCompute.FindKernel("CSPickPixel");

        pickResultBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 5, sizeof(float) * 4);
        EnsureCandidateBuffer();
    }

    void OnDestroy()
    {
        pickResultBuffer?.Dispose();
        pickCandidatesBuffer?.Dispose();
    }

    public bool TryPick(Vector2 pixel, out Vector3 worldPos)
    {
        bool hit = TryPickDetailed(pixel, out PickResult result);
        worldPos = result.worldPosition;
        return hit;
    }

    public bool TryPickDetailed(Vector2 pixel, out PickResult result)
    {
        result = new PickResult
        {
            hit = false,
            inputPixel = pixel,
            pickPixel = pixel,
            splatIndex = -1,
            sortedIndex = -1,
            selectedCandidateOrdinal = -1,
            candidates = System.Array.Empty<DebugCandidate>(),
            selectionReason = "Not picked",
            diagnostic = "Not picked"
        };

        if (pickCompute == null || splatRenderer == null || splatRenderer.asset == null || targetCamera == null)
        {
            result.diagnostic = "GsPixelPicker: 必需的引用未配置";
            Debug.LogError(result.diagnostic);
            return false;
        }

        if (splatRenderer.GpuPosData == null || splatRenderer.GpuOtherData == null || splatRenderer.GpuView == null ||
            splatRenderer.GpuSortKeys == null || splatRenderer.GpuChunks == null)
        {
            result.diagnostic = "GsPixelPicker: SplatRenderer 的 GPU buffers 未初始化";
            Debug.LogError(result.diagnostic);
            return false;
        }

        if (!TryConvertMousePixel(pixel, out Vector2 pickPixel, out string coordDiagnostic))
        {
            result.diagnostic = coordDiagnostic;
            return false;
        }

        result.pickPixel = pickPixel;

        if (refreshViewDataOnPick)
            RefreshRendererData();

        EnsureCandidateBuffer();

        pickCompute.SetBuffer(kernelPick, "_SplatPos", splatRenderer.GpuPosData);
        pickCompute.SetBuffer(kernelPick, "_SplatViewData", splatRenderer.GpuView);
        pickCompute.SetBuffer(kernelPick, "_OrderBuffer", splatRenderer.GpuSortKeys);
        pickCompute.SetBuffer(kernelPick, "_SplatChunks", splatRenderer.GpuChunks);
        pickCompute.SetBuffer(kernelPick, "_PickResult", pickResultBuffer);
        pickCompute.SetBuffer(kernelPick, "_PickCandidates", pickCandidatesBuffer);

        uint format = (uint)splatRenderer.asset.posFormat
                    | ((uint)splatRenderer.asset.scaleFormat << 8)
                    | ((uint)splatRenderer.asset.shFormat << 16);

        pickCompute.SetInt("_SplatFormat", (int)format);
        pickCompute.SetInt("_SplatChunkCount", splatRenderer.GpuChunksValid ? splatRenderer.GpuChunks.count : 0);
        pickCompute.SetInt("_SplatCount", splatRenderer.splatCount);
        pickCompute.SetInt("_NeighborhoodRadius", Mathf.Clamp(neighborhoodRadius, 0, 4));
        pickCompute.SetInt("_MaxDebugCandidates", candidateBufferCapacity);
        pickCompute.SetVector("_PickPixel", new Vector4(pickPixel.x, pickPixel.y, 0, 0));
        pickCompute.SetFloat("_PickAlphaCutoff", Mathf.Max(alphaCutoff, 0.0f));
        pickCompute.SetFloat("_PickSurfaceAlpha", Mathf.Clamp(surfaceAlpha, alphaCutoff, 0.95f));
        pickCompute.SetInt("_PickFlipY", flipYForBackbuffer ? 1 : 0);
        pickCompute.SetVector("_VecScreenParams", new Vector4(targetCamera.pixelWidth, targetCamera.pixelHeight, 0, 0));
        pickCompute.SetVector("_CameraWorldPos", targetCamera.transform.position);
        pickCompute.SetVector("_CameraForward", targetCamera.transform.forward);
        pickCompute.SetMatrix("_MatrixObjectToWorld", splatRenderer.transform.localToWorldMatrix);

        pickCompute.Dispatch(kernelPick, 1, 1, 1);

        var data = new Vector4[5];
        pickResultBuffer.GetData(data);

        if (data[0].w <= 0.5f)
        {
            result.diagnostic = $"Miss. input={Format(pixel)}, pick={Format(pickPixel)}. {coordDiagnostic}";
            if (logDebugInfo)
                Debug.Log($"[GsPixelPicker] {result.diagnostic}");
            return false;
        }

        result.hit = true;
        result.worldPosition = new Vector3(data[0].x, data[0].y, data[0].z);
        result.splatIndex = Mathf.RoundToInt(data[1].x);
        result.sortedIndex = Mathf.RoundToInt(data[1].y);
        result.pickPixel = new Vector2(data[1].z, data[1].w);
        result.splatCenterWorld = new Vector3(data[4].x, data[4].y, data[4].z);
        result.alpha = data[2].x;
        result.contribution = data[2].y;
        result.accumulatedAlpha = data[2].z;
        result.confidence = data[2].w;
        result.depth = data[3].x;
        result.candidateContributionSum = data[3].z;
        result.worldPosition = ProjectLinearDepthOntoPickRay(result.pickPixel + targetCamera.pixelRect.position, result.depth);
        ReadDebugCandidates(ref result);
        result.selectionReason = BuildSelectionReason(result);
        result.diagnostic =
            $"Hit splat={result.splatIndex}, sorted={result.sortedIndex}, input={Format(pixel)}, pick={Format(result.pickPixel)}, " +
            $"center={result.splatCenterWorld}, surfacePoint={result.worldPosition}, surfaceAlpha={surfaceAlpha:F2}, " +
            $"alpha={result.alpha:F4}, contribution={result.contribution:F4}, accum={result.accumulatedAlpha:F4}, confidence={result.confidence:F3}, depth={result.depth:F4}. " +
            $"candidates={result.totalCandidateCount}, shown={result.candidates.Length}. {result.selectionReason}. " +
            coordDiagnostic;

        if (logDebugInfo)
            Debug.Log($"[GsPixelPicker] {result.diagnostic}\n{BuildCandidatesText(result)}");

        return true;
    }

    void EnsureCandidateBuffer()
    {
        int capacity = Mathf.Clamp(maxDebugCandidates, 1, 32);
        int requiredCount = 1 + capacity * 4;
        if (pickCandidatesBuffer != null && candidateBufferCapacity == capacity && pickCandidatesBuffer.count == requiredCount)
            return;

        pickCandidatesBuffer?.Dispose();
        pickCandidatesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, requiredCount, sizeof(float) * 4);
        candidateBufferCapacity = capacity;
    }

    void ReadDebugCandidates(ref PickResult result)
    {
        var candidateData = new Vector4[1 + candidateBufferCapacity * 4];
        pickCandidatesBuffer.GetData(candidateData);

        result.totalCandidateCount = Mathf.Max(0, Mathf.RoundToInt(candidateData[0].x));
        result.selectedCandidateOrdinal = Mathf.RoundToInt(candidateData[0].y);
        int shownCount = Mathf.Min(result.totalCandidateCount, candidateBufferCapacity);
        var candidates = new List<DebugCandidate>(shownCount);

        for (int i = 0; i < shownCount; i++)
        {
            int baseIndex = 1 + i * 4;
            Vector4 center = candidateData[baseIndex + 0];
            Vector4 metrics = candidateData[baseIndex + 1];
            Vector4 blend = candidateData[baseIndex + 2];
            Vector4 shape = candidateData[baseIndex + 3];

            var candidate = new DebugCandidate
            {
                ordinal = i,
                splatIndex = Mathf.RoundToInt(center.w),
                sortedIndex = Mathf.RoundToInt(metrics.x),
                splatCenterWorld = new Vector3(center.x, center.y, center.z),
                linearDepth = metrics.y,
                alpha = metrics.z,
                contribution = metrics.w,
                rayT = metrics.y,
                centerRayDistance = 0f,
                ellipsoidDistance = 0f,
                closestLinearDepth = metrics.y,
                accumulatedAlphaBefore = blend.x,
                accumulatedAlphaAfter = blend.y,
                usedContribution = blend.z,
                confidence = blend.w,
                gaussianLocal = new Vector2(shape.x, shape.y),
                power = shape.z,
                selected = i == result.selectedCandidateOrdinal
            };
            candidates.Add(candidate);
        }

        result.candidates = candidates.ToArray();
    }

    string BuildSelectionReason(PickResult result)
    {
        if (result.candidates == null || result.candidates.Length == 0)
            return "没有记录到候选列表";

        DebugCandidate selected = default;
        bool foundSelected = false;
        foreach (var candidate in result.candidates)
        {
            if (!candidate.selected)
                continue;

            selected = candidate;
            foundSelected = true;
            break;
        }

        if (!foundSelected)
        {
            return $"最终选择 splat {result.splatIndex}，因为它在达到 surfaceAlpha={surfaceAlpha:F2} 前的可见贡献最大；该候选排在已显示列表之外";
        }

        return
            $"最终选择 #{selected.ordinal} / splat {selected.splatIndex}，理由：在从前到后混合时，它是累计 alpha 达到 surfaceAlpha={surfaceAlpha:F2} 之前" +
            $"可见贡献 contribution={selected.contribution:F4} 最大的候选；列表里 surfaceAlpha 后面的高斯仅用于解释遮挡/混合，不参与最终选择。" +
            $"测距点深度不是直接取该中心，而是用 surfaceAlpha 之前的候选 usedContribution 加权得到 visible surface depth={result.depth:F4}";
    }

    public string BuildCandidatesText(PickResult result)
    {
        if (result.candidates == null || result.candidates.Length == 0)
            return "候选高斯: 无";

        var sb = new StringBuilder();
        sb.AppendLine($"候选高斯: 显示 {result.candidates.Length}/{result.totalCandidateCount}，surfaceAlpha={surfaceAlpha:F2}");
        foreach (var candidate in result.candidates)
        {
            sb.Append(candidate.selected ? "* " : "  ");
            sb.Append('#').Append(candidate.ordinal)
              .Append(" splat=").Append(candidate.splatIndex)
              .Append(" sort=").Append(candidate.sortedIndex)
              .Append(" rayT=").Append(candidate.rayT.ToString("F3"))
              .Append(" depth=").Append(candidate.linearDepth.ToString("F3"))
              .Append(" centerDist=").Append(candidate.centerRayDistance.ToString("F3"))
              .Append(" ellipsoid=").Append(candidate.ellipsoidDistance.ToString("F3"))
              .Append(" alpha=").Append(candidate.alpha.ToString("F3"))
              .Append(" contrib=").Append(candidate.contribution.ToString("F3"))
              .AppendLine();
        }
        sb.Append(result.selectionReason);
        return sb.ToString();
    }

    void RefreshRendererData()
    {
        using (var cmd = new CommandBuffer { name = "AccurateGsPickRefresh" })
        {
            splatRenderer.PublicCalcViewData(cmd, targetCamera);
            Graphics.ExecuteCommandBuffer(cmd);
        }
    }

    Vector3 ProjectLinearDepthOntoPickRay(Vector2 screenPixel, float linearDepth)
    {
        Ray ray = targetCamera.ScreenPointToRay(screenPixel);
        Plane depthPlane = new Plane(targetCamera.transform.forward, targetCamera.transform.position + targetCamera.transform.forward * linearDepth);
        if (depthPlane.Raycast(ray, out float enter))
            return ray.GetPoint(enter);

        return ray.origin + ray.direction * Mathf.Max(linearDepth, 0f);
    }

    bool TryConvertMousePixel(Vector2 mousePixel, out Vector2 pickPixel, out string diagnostic)
    {
        Rect rect = targetCamera.pixelRect;
        pickPixel = mousePixel - rect.position;

        if (pickPixel.x < 0 || pickPixel.y < 0 || pickPixel.x >= rect.width || pickPixel.y >= rect.height)
        {
            diagnostic = $"Mouse pixel {Format(mousePixel)} outside camera pixel rect {rect}";
            return false;
        }

        diagnostic = $"CameraRect={rect}, localPixel={Format(pickPixel)}";
        return true;
    }

    static string Format(Vector2 v)
    {
        return $"({v.x:F0}, {v.y:F0})";
    }
}
