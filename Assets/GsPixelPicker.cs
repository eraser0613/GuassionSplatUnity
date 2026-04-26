using UnityEngine;
using UnityEngine.Rendering;
using GaussianSplatting.Runtime;

public class GsPixelPicker : MonoBehaviour
{
    public Camera targetCamera;
    public GaussianSplatRenderer splatRenderer;
    public ComputeShader pickCompute;

    private GraphicsBuffer pickResultBuffer;
    private int kernelPick;

    struct PickResult
    {
        public Vector4 value;
    }

    void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (pickCompute != null)
            kernelPick = pickCompute.FindKernel("CSPickPixel");

        pickResultBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(float) * 4);
    }

    void OnDestroy()
    {
        pickResultBuffer?.Dispose();
    }

    public bool TryPick(Vector2 pixel, out Vector3 worldPos)
    {
        worldPos = Vector3.zero;

        if (pickCompute == null || splatRenderer == null || splatRenderer.asset == null || targetCamera == null)
        {
            Debug.LogError("GsPixelPicker: 必需的引用未配置！");
            return false;
        }

        // 检查 splatRenderer 的 GPU buffers 是否已初始化
        if (splatRenderer.GpuPosData == null || splatRenderer.GpuView == null ||
            splatRenderer.GpuSortKeys == null || splatRenderer.GpuChunks == null || splatRenderer.asset == null)
        {
            Debug.LogError("GsPixelPicker: SplatRenderer 的 GPU buffers 或 asset 未初始化!");
            return false;
        }        using (var cmd = new CommandBuffer())
        {
            splatRenderer.PublicCalcViewData(cmd, targetCamera);
            Graphics.ExecuteCommandBuffer(cmd);
        }

        pickCompute.SetBuffer(kernelPick, "_SplatPos", splatRenderer.GpuPosData);
        pickCompute.SetBuffer(kernelPick, "_SplatViewData", splatRenderer.GpuView);
        pickCompute.SetBuffer(kernelPick, "_OrderBuffer", splatRenderer.GpuSortKeys);
        pickCompute.SetBuffer(kernelPick, "_SplatChunks", splatRenderer.GpuChunks);
        pickCompute.SetBuffer(kernelPick, "_PickResult", pickResultBuffer);

        uint format = (uint)splatRenderer.asset.posFormat
                    | ((uint)splatRenderer.asset.scaleFormat << 8)
                    | ((uint)splatRenderer.asset.shFormat << 16);

        pickCompute.SetInt("_SplatFormat", (int)format);
        pickCompute.SetInt("_SplatChunkCount", splatRenderer.GpuChunksValid ? splatRenderer.GpuChunks.count : 0);

        pickCompute.SetInt("_SplatCount", splatRenderer.splatCount);
        pickCompute.SetVector("_PickPixel", new Vector4(pixel.x, pixel.y, 0, 0));
        pickCompute.SetFloat("_PickAlphaThreshold", 0.5f);
        pickCompute.SetVector("_VecScreenParams", new Vector4(targetCamera.pixelWidth, targetCamera.pixelHeight, 0, 0));
        pickCompute.SetMatrix("_MatrixObjectToWorld", splatRenderer.transform.localToWorldMatrix);

        pickCompute.Dispatch(kernelPick, 1, 1, 1);

        var data = new PickResult[1];
        pickResultBuffer.GetData(data);

        if (data[0].value.w > 0.5f)
        {
            float px = data[0].value.x;
            float py = data[0].value.y;
            float ndcDepth = data[0].value.z;

            float x = (px / targetCamera.pixelWidth) * 2f - 1f;
            float y = (py / targetCamera.pixelHeight) * 2f - 1f;

            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(targetCamera.projectionMatrix, false);
            Matrix4x4 vp = gpuProj * targetCamera.worldToCameraMatrix;
            Matrix4x4 invVP = vp.inverse;

            Vector4 clip = new Vector4(x, y, ndcDepth, 1f);
            Vector4 world = invVP * clip;
            world /= world.w;

            worldPos = new Vector3(world.x, world.y, world.z);
            return true;
        }

        return false;
    }
}