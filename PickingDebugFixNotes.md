# 3DGS 测距拾取与坐标修正说明

本文档记录本次对 Unity Gaussian Splatting 示例工程中测距拾取功能的关键修改、原因和当前算法语义。

## 1. 问题现象

在测距调试时，点击射线和最终选点基本能落在用户点击方向上，但候选高斯经常系统性低于真实画面位置。表现为：

- 候选高斯高亮位置整体偏低；
- 将射线在屏幕上向下平移一点，反而更接近候选；
- 相机运动后立即点击时，错位更明显；
- 真实画面中看到的高斯位置与 picker 认为的屏幕位置不一致。

这说明问题不像是单个 splat 的选择误差，而更像是坐标变换链路中的系统性偏移。

## 2. 当前测距算法语义

当前稳定版使用的是 **渲染一致的屏幕像素贡献拾取**，不是完整的 3D 椭球射线求交。

流程如下：

1. Unity 鼠标坐标转换为当前相机 pixel rect 内的 pick pixel。
2. Picker 读取 `GaussianSplatRenderer` 已计算好的 GPU 数据：
   - `_SplatViewData`
   - `_OrderBuffer`
   - `_SplatPos`
   - `_SplatChunks`
   - 当前相机位置与 forward
   - renderer transform
3. Compute shader 按渲染排序顺序遍历 splat。
4. 对每个 splat，判断点击像素是否落在该 splat 的屏幕投影椭圆内。
5. 计算该 splat 在该像素上的 alpha 和透明混合贡献：

   ```hlsl
   contribution = (1.0 - accumA) * alpha;
   ```

6. 在累计 alpha 达到 `surfaceAlpha` 之前，记录候选 splat。
7. 最终 splat 选择规则是：

   > 在达到 `surfaceAlpha` 前，可见贡献 `contribution` 最大的 splat。

8. 最终测距点不是直接使用 splat center，而是使用候选的 `usedContribution` 对线性深度做加权平均：

   ```hlsl
   weightedLinearDepthSum += linearDepth * usedContribution;
   contributionSum += usedContribution;
   finalLinearDepth = weightedLinearDepthSum / contributionSum;
   ```

9. C# 端将这个线性深度投回点击射线，得到最终世界坐标。

因此：

- 最终测距点位于点击射线上；
- 候选 splat 的 center 不一定在点击射线上；
- 当前算法更接近“渲染可见表面深度”，而不是严格几何交点。

## 3. `surfaceAlpha` 修改

默认 `surfaceAlpha` 已从：

```csharp
0.35f
```

调整为：

```csharp
0.55f
```

含义：

- 较小的 `surfaceAlpha` 更偏向前层、浅层、半透明最前面的贡献；
- 较大的 `surfaceAlpha` 会纳入更多前景贡献，测距深度更偏向累计可见表面内部一点；
- 对密集重叠区域，`0.55` 通常比 `0.35` 不那么容易被极浅的弱贡献拉到前面。

如果后续需要，可以继续暴露成 UI 滑条，便于现场调参。

## 4. 相机运动后的帧同步修正

原先左键点击是在 `Update()` 中立即执行拾取。相机运动也在 `Update()` 中发生，而 3DGS renderer 的 view/sort GPU 数据通常在相机渲染流程中更新。

因此原流程可能出现：

```text
当前帧 Update: 相机已经移动
当前帧 Update: picker 立即读取 GPU 数据
当前帧 Render: renderer 才更新新的 view/sort 数据
```

这会导致：

```text
点击射线 = 当前相机
picker 使用的 _SplatViewData/_OrderBuffer = 上一帧相机
```

修正方式：

- `Update()` 中只记录点击请求；
- 使用 `WaitForEndOfFrame()` 延迟到当前帧渲染结束后再执行 `TryPick`；
- 这样 picker 更可能读取到与当前相机一致的 view/sort 数据。

修改点：

- `GSViewerUI.QueuePickPoint(...)`
- `GSViewerUI.PerformQueuedPickAfterFrame()`

## 5. Y 翻转坐标修正

这是本次最关键的修正。

实际渲染 shader 中，splat 顶点在进入屏幕前会调用：

```hlsl
FlipProjectionIfBackbuffer(o.vertex);
```

位于：

```text
package/Shaders/RenderGaussianSplats.shader
package/Shaders/GaussianSplatting.hlsl
```

其逻辑是：

```hlsl
if (_CameraTargetTexture_TexelSize.z == 1.0)
    vpos.y = -vpos.y;
```

也就是当直接渲染到 backbuffer 时，真实画面中的 splat 会发生 clip-space Y 翻转。

之前 picker 的 compute shader 使用的是：

```hlsl
float2 centerNdc = view.pos.xy / view.pos.w;
float2 centerPx = (centerNdc * 0.5 + 0.5) * _VecScreenParams.xy;
```

它没有执行和渲染 shader 相同的 Y 翻转，导致：

```text
渲染看到的 splat 位置 != picker 认为的 splat 位置
```

最终表现为候选高斯系统性偏低或偏高。

修正后，picker 中加入：

```hlsl
if (_PickFlipY != 0)
{
    centerNdc.y = -centerNdc.y;
    axis1.y = -axis1.y;
    axis2.y = -axis2.y;
}
```

注意不仅翻转中心 `centerNdc.y`，还要翻转屏幕椭圆的两个轴 `axis1.y` 和 `axis2.y`。否则中心位置对了，椭圆覆盖判断仍然可能错。

C# 侧默认开启：

```csharp
public bool flipYForBackbuffer = true;
```

## 6. 调试可视化含义

当前调试显示的语义如下：

- 青线：点击射线；
- 黄线：从相机到最终测距点；
- 彩球/屏幕标签 `#N`：候选 splat 的深度投影到点击射线上的位置；
- 点云本体洋红高亮：对应候选 splat 的 Gaussian center；
- 右侧文本 `#N`：与屏幕标签 `#N` 对应。

需要注意：

- 彩球在射线上，因为它表示该候选的深度位置；
- 洋红高亮的 Gaussian center 不一定在射线上，因为当前算法是屏幕投影贡献拾取；
- 如果洋红 center 和彩球有偏移，不一定是 bug，只有系统性偏移时才需要怀疑坐标链路。

## 7. 为什么没有保留完整 3D 椭球射线求交

曾尝试在 compute shader 中对大量 splat 做真实 3D Gaussian 椭球射线求交，但打包后容易出现卡死或闪退风险。

原因是：

- 数据量可达数百万 splat；
- 每次点击全量解码 position / rotation / scale 成本高；
- GPU 同步读回和复杂 compute 容易导致驱动超时或不稳定。

因此当前稳定版回到渲染一致的屏幕贡献拾取。后续如果要实现真实几何拾取，建议采用两阶段策略：

1. 先用当前屏幕贡献 picker 得到少量候选；
2. 再只对这些候选执行 CPU 或小规模 compute 的真实 3D 射线校正。

## 8. 当前结论

本次修复后，picker 的屏幕坐标计算与实际渲染 shader 的 backbuffer Y 翻转保持一致。用户验证显示，候选位置与真实画面对齐问题已明显改善。

当前默认参数：

```csharp
surfaceAlpha = 0.55f;
flipYForBackbuffer = true;
```

当前算法定位：

> 渲染一致的 3DGS 屏幕贡献测距，而非严格几何射线求交。
