# GuassionSplatUnity

一个基于 Unity 的 3D Gaussian Splatting 示例项目，用于加载、查看、测量 3DGS 点云，并实验 Unity 与 Python 训练后端的联动流程。

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

## 已实现功能

- 3D Gaussian Splatting 模型显示
- IMGUI 查看器界面
- 点云显示/隐藏切换
- 相机视角重置与预设视角控制
- GPU Compute Shader 像素级点选
- 两点测距工具
- Unity 与 Python 训练服务端的 TCP Socket 通信
- 训练进度、Loss、迭代次数实时显示
- 训练完成后自动导入 PLY 并刷新显示

## 主要文件

```text
Assets/
├── GSTestScene.unity              # 示例场景
├── GSViewerUI.cs                  # 查看器与训练控制 UI
├── TrainingClient.cs              # Unity 端 Socket 客户端
├── TrainingAssetImporter.cs       # PLY 转 GaussianSplat asset 的导入工具
├── GsPixelPicker.cs               # 像素级点选逻辑
├── GsPixelPicker.compute          # 点选用 Compute Shader
├── ApproxGsMeasureTool.cs         # 两点测距工具
├── TrainingSystem_README.md       # 训练系统说明
└── GSViewerUI_README.md           # 查看器 UI 使用说明
```

## 快速开始

1. 使用 Unity Hub 打开本项目。
2. 确认 Unity 版本为 `2022.3.62f3` 或兼容版本。
3. 确认 `Packages/manifest.json` 中的 `org.nesnausk.gaussian-splatting` 本地路径有效。
4. 打开场景：

   ```text
   Assets/GSTestScene.unity
   ```

5. 点击 Play 运行场景。

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

## OpenSpec 开发流程

本项目已经初始化 OpenSpec，配置位于：

```text
openspec/config.yaml
```

推荐后续开发流程：

```text
/opsx:explore   # 讨论和分析方案
/opsx:propose   # 为新功能创建 proposal/design/tasks
/opsx:apply     # 按 tasks.md 实现功能
/opsx:archive   # 功能完成后归档 change
```

适合后续拆分的功能：

- Runtime PLY 加载
- 训练参数扩展
- 多次训练结果对比
- 更完整的 Build 发布流程

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
