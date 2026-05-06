# GuassionSplatUnity

一个基于 Unity 的 3D Gaussian Splatting 查看与测距示例项目。当前版本重点支持 3DGS 模型浏览、运行时相机漫游、像素级点选测距、候选高斯调试可视化，以及 Unity 与 Python 训练后端的实验性联动。

## 项目环境

- Unity: `2022.3.62f3`
- 主要依赖: `org.nesnausk.gaussian-splatting`
- 示例场景: `Assets/GSTestScene.unity`

> 注意：当前 `Packages/manifest.json` 中的 Gaussian Splatting 包使用本地路径依赖：
>
> ```json
> "org.nesnausk.gaussian-splatting": "file:../../../package"
> ```
>
> 如果只克隆本仓库，需要确保本地存在对应的 `package` 目录，或根据实际情况修改该依赖路径。

## 当前稳定功能

### 3DGS 查看

- 加载并显示 Unity Gaussian Splatting asset。
- 支持点云显示/隐藏。
- 支持 FPS、鼠标位置、高斯数量等基础信息显示。

### 运行时相机漫游

运行 Play 或打包后的 exe 后，可以使用：

| 操作 | 功能 |
| --- | --- |
| 鼠标右键拖动 | 旋转视角 |
| `W/A/S/D` | 前后左右移动 |
| `Q/E` | 下降 / 上升 |
| `Left Shift` | 加速移动 |
| 重置视角按钮 | 回到初始相机位置和朝向 |

固定前/后/左/右/顶/底视角按钮已移除，当前以自由漫游为主。

### 点选测距

左侧控制面板中点击：

```text
点选测距
```

进入点选模式后，鼠标左键点击场景才会执行拾取和测距。普通主界面状态下左键不会触发测距。

测距相关参数：

- `Surface Alpha` 滑条：范围 `0 ~ 1`，默认 `0.55`。
- 越小越偏向前层浅表贡献。
- 越大越纳入更多透明累积，深度更偏向内部/后层。

当前最终黄色测距点 marker 较小，默认大小为：

```csharp
pickMarkerScale = 0.02f;
```

## 当前测距算法说明

当前稳定版使用的是 **渲染一致的屏幕像素贡献测距**，不是严格的 3D 几何射线求交。

简化流程：

1. 将鼠标点击转换为相机 pixel rect 内的 pick pixel。
2. 使用 `GaussianSplatRenderer` 当前 GPU view data 与 sort order。
3. 按渲染顺序遍历 splat。
4. 判断 splat 的屏幕投影椭圆是否覆盖点击像素。
5. 计算每个候选 splat 在该像素上的 alpha 和透明混合贡献：

   ```hlsl
   contribution = (1.0 - accumA) * alpha;
   ```

6. 在累计 alpha 达到 `Surface Alpha` 前，使用候选的 `usedContribution` 对线性深度做加权。
7. 将最终线性深度投回点击射线，得到世界空间测距点。

因此：

- 最终测距点会落在点击射线上；
- 候选高斯的 center 不一定在射线上；
- 当前算法更接近“渲染可见表面深度”，不是 mesh-style 几何交点。

## 拾取调试可视化

点选测距时可打开：

```text
显示拾取调试: 射线/候选高斯/数值
```

调试元素含义：

| 显示 | 含义 |
| --- | --- |
| 青色线 | 点击射线 |
| 黄色线 | 相机到最终测距点的线段 |
| 黄色小点 | 最终测距点 |
| 彩色球 / `#N` 标签 | 候选 splat 的深度投影到点击射线上的位置 |
| 点云本体洋红高亮 | 对应候选 splat 的 Gaussian center |
| 右侧文本 `#N` | 与屏幕标签 `#N` 对应的候选数值 |

常见字段：

- `alpha`：该 splat 在点击像素上的不透明度。
- `screenContrib` / `contribution`：该 splat 在透明混合下的可见贡献。
- `accum`：遍历到该 splat 后的累计 alpha。
- `used`：该 splat 对当前 Surface Alpha 深度计算实际使用的贡献。
- `depth`：候选对应的线性深度。

## 已修复的关键拾取问题

### 1. 相机移动后的帧同步

拾取不再在 `Update()` 中立即执行，而是先记录点击请求，并在 `WaitForEndOfFrame()` 后执行。

这样可以避免：

```text
点击射线 = 当前相机
GPU view/sort 数据 = 上一帧相机
```

导致的候选错位。

### 2. Backbuffer Y 翻转

实际 3DGS 渲染 shader 在 backbuffer 路径中会执行 Y 翻转：

```hlsl
FlipProjectionIfBackbuffer(o.vertex);
```

picker 已同步加入对应修正：

```hlsl
centerNdc.y = -centerNdc.y;
axis1.y = -axis1.y;
axis2.y = -axis2.y;
```

并在 C# 中默认开启：

```csharp
flipYForBackbuffer = true;
```

该修正确认解决了候选高斯系统性偏低的问题。

## 已知测距局限

3DGS 是半透明高斯混合表示，不是真实 mesh 表面。当前测距有以下已知局限：

- 前层透明高斯会先累积 alpha，稀释后层贡献；
- 在密集重叠区域，最终点可能比用户认知中的真实表面略靠前；
- 即使将 `Surface Alpha` 调到较大，也不能完全消除前层透明贡献影响；
- 相机靠近目标后通常更准确，因为单像素覆盖的世界范围更小，前后层混合减少。

相关调研报告：

```text
PickingDebugFixNotes.md
PickingDepthBiasInvestigationReport.md
```

## 主要文件

```text
Assets/
├── GSTestScene.unity              # 示例场景
├── GSViewerUI.cs                  # 查看器、漫游、测距和调试 UI
├── GsPixelPicker.cs               # 像素级点选与结果解析
├── GsPixelPicker.compute          # 点选用 Compute Shader
├── ApproxGsMeasureTool.cs         # 两点测距工具
├── TrainingClient.cs              # Unity 端 Socket 客户端
├── TrainingAssetImporter.cs       # Editor 下 PLY 转 GaussianSplatAsset 的导入工具
├── TrainingSystem_README.md       # 训练系统说明
└── GSViewerUI_README.md           # 查看器 UI 说明
```

文档：

```text
PickingDebugFixNotes.md
PickingDepthBiasInvestigationReport.md
openspec/
```

## 快速开始：Unity Editor

1. 使用 Unity Hub 打开本项目。
2. 确认 Unity 版本为 `2022.3.62f3` 或兼容版本。
3. 确认 `Packages/manifest.json` 中的 `org.nesnausk.gaussian-splatting` 本地路径有效。
4. 打开场景：

   ```text
   Assets/GSTestScene.unity
   ```

5. 点击 Play 运行场景。

## 快速开始：Windows exe

已验证可构建 Windows standalone：

```text
Builds/GaussianExample/GaussianExample.exe
```

运行时需要保留整个构建目录，不要只拷贝 exe：

```text
GaussianExample.exe
GaussianExample_Data/
UnityPlayer.dll
MonoBleedingEdge/
UnityCrashHandler64.exe
```

## Python 训练服务联动

如果需要使用 Unity 中的训练控制功能，需要先启动 Python 训练服务端。

示例：

```bash
conda activate 3dgs
python E:/unityhub/gaussian-splatting/train_server.py
```

Unity 端默认连接：

```text
127.0.0.1:9090
```

运行后可在 Unity 的 UI 中连接服务器、开始训练、停止训练，并查看训练进度。

注意：训练完成后自动导入 PLY 依赖 `UnityEditor` / `AssetDatabase`，主要适用于 Editor 内工作流；打包 exe 中不能直接使用 Editor 导入流程。

## 关于外部 PLY / Bundle 加载

曾实验过运行时 AssetBundle 加载方案，但当前稳定版本已回滚该功能。当前 exe 使用场景中已有的 GaussianSplatAsset，不自动扫描磁盘，也不提供运行时 bundle 选择入口。

如果后续需要支持外部模型，建议重新设计为独立、可控的加载流程，并避免启动时扫描磁盘。

## OpenSpec 开发流程

本项目已经初始化 OpenSpec，配置位于：

```text
openspec/config.yaml
```

已归档的变更包括：

- `accurate-3dgs-picking`
- `runtime-roaming-camera-controls`

推荐后续开发流程：

```text
/opsx:explore   # 讨论和分析方案
/opsx:propose   # 为新功能创建 proposal/design/tasks
/opsx:apply     # 按 tasks.md 实现功能
/opsx:archive   # 功能完成后归档 change
```

适合后续拆分的功能：

- 更稳健的二阶段几何拾取；
- Depth Bias / 拾取模式切换；
- 外部模型加载；
- 训练参数扩展；
- 多次训练结果对比；
- 更完整的 Build 发布流程。

## Git 注意事项

这是 Unity 项目，上传到 GitHub 前应使用 `.gitignore` 排除以下目录：

- `Library/`
- `Temp/`
- `Obj/`
- `Logs/`
- `Build/`
- `Builds/`
- `UserSettings/`

避免把 Unity 自动生成文件和大体积缓存提交到仓库。
