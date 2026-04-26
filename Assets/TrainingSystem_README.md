# Unity + Python 3DGS 训练系统

## 架构概览

```
Unity Editor (C# 前端)                    Python (后端)
┌─────────────────────────┐  TCP Socket   ┌─────────────────────────┐
│ GSViewerUI              │  127.0.0.1    │ train_server.py         │
│  ├─ 连接服务器 按钮      │   :9090       │  ├─ Socket Server       │
│  ├─ 开始训练 按钮        │──────────────>│  ├─ Mock 模式 (复制PLY) │
│  ├─ 停止训练 按钮        │               │  └─ Real 模式 (train.py)│
│  ├─ 训练进度条           │               │                         │
│  └─ 迭代/Loss 实时显示   │<──────────────│  返回 progress/ply_path │
│                         │  JSON 消息    │                         │
│ TrainingClient          │               └─────────────────────────┘
│  ├─ Socket 收发          │
│  ├─ 主线程回调队列       │
│  └─ 进度状态跟踪        │
│                         │
│ TrainingAssetImporter   │
│  ├─ 反射调用 AssetCreator│
│  ├─ PLY → .asset 转换   │
│  └─ 赋值给 Renderer     │
│                         │
│ GaussianSplatRenderer   │
│  └─ 检测 asset 变化     │
│     自动重建GPU资源刷新  │
└─────────────────────────┘
```

## 已实现功能

### 训练控制
- **开始训练** — 支持 Mock 模式（测试）和 Real 模式（真实 3DGS 训练）
- **停止训练** — 训练中可随时点击红色"停止训练"按钮，Python 端会杀掉 train.py 子进程
- **自定义迭代次数** — UI 中可输入训练迭代次数（默认 7000）
- **自定义数据路径** — 输入 COLMAP 处理后的数据目录路径

### 训练进度实时回传
- **进度条** — 蓝→绿渐变彩色进度条，居中显示百分比
- **迭代计数** — 显示 `迭代: 700 / 7000`
- **Loss 值** — 实时显示当前训练 Loss
- **事件信息** — 显示 Saving Gaussians、Evaluating 等关键事件

### 训练结果自动加载
- 训练完成后自动导入 PLY 文件
- 通过反射调用 GaussianSplatAssetCreator 转换为 .asset
- 自动赋值给 GaussianSplatRenderer，画面即时刷新

### 查看器功能
- **视角控制** — 前/后/左/右/俯/底 六方向预设视角 + 重置
- **点云显示切换** — 一键显示/隐藏 3DGS 点云
- **点选测距** — GPU Compute Shader 精确像素级点选，显示 3D 世界坐标
- **测量工具** — 两点测距，支持 Chunk AABB 近似拾取
- **实时信息** — 高斯点数、FPS、鼠标坐标

## 通信协议

**传输层**: TCP Socket，长度前缀 + JSON

```
[4 字节消息长度 (大端 big-endian)] + [JSON 正文 (UTF-8)]
```

**消息类型**:

| 方向 | type | 字段 | 说明 |
|------|------|------|------|
| Unity → Python | `start_training` | `data_path`, `output_dir`, `mock`, `iterations` | 开始训练 |
| Unity → Python | `stop_training` | — | 停止训练 |
| Python → Unity | `training_started` | `message` | 确认开始 |
| Python → Unity | `training_progress` | `iteration`, `total_iterations`, `progress`, `loss`, `info` | 训练进度 |
| Python → Unity | `training_complete` | `status`, `ply_path` 或 `message` | 训练完成/失败 |
| Python → Unity | `training_stopped` | `message` | 确认已停止 |
| Unity → Python | `ping` | — | 心跳 |
| Python → Unity | `pong` | — | 心跳回复 |

## 文件清单

### Python 后端
| 文件 | 路径 | 作用 |
|------|------|------|
| train_server.py | `E:\unityhub\gaussian-splatting\train_server.py` | Socket 服务器，接收训练/停止指令，解析 train.py 输出并转发进度 |
| train.py | `E:\unityhub\gaussian-splatting\train.py` | 3DGS 训练脚本（GRAPHDECO/Inria），使用 tqdm 输出进度 |

### Unity 前端
| 文件 | 路径 | 作用 |
|------|------|------|
| TrainingClient.cs | `Assets\TrainingClient.cs` | Runtime MonoBehaviour，Socket 客户端，线程安全消息收发，进度状态跟踪 |
| TrainingAssetImporter.cs | `Assets\TrainingAssetImporter.cs` | Editor-only，通过反射调用 GaussianSplatAssetCreator 将 PLY 转为 .asset |
| GSViewerUI.cs | `Assets\GSViewerUI.cs` | IMGUI 界面，训练控制（连接、开始/停止、进度条）+ 查看器功能 |
| GsPixelPicker.cs | `Assets\GsPixelPicker.cs` | GPU Compute Shader 像素级精确拾取 |
| ApproxGsMeasureTool.cs | `Assets\ApproxGsMeasureTool.cs` | 两点测距工具，支持 Chunk AABB 近似拾取 |

## 数据流

```
1. 用户在 Unity 点击 "开始训练"
       │
       ▼
2. TrainingClient 发送 JSON: {"type":"start_training", "mock":false, "iterations":7000, ...}
       │  TCP Socket
       ▼
3. Python train_server.py 收到请求，启动训练子线程
       │
       ├─ Mock 模式: 模拟 10 步进度更新，复制已有 PLY
       └─ Real 模式: 调用 train.py 子进程，解析 tqdm 输出
       │
       ├─ 每 1% 进度变化 → 发送 {"type":"training_progress", "progress":50, "loss":0.03, ...}
       │                        │  TCP Socket
       │                        ▼
       │                   TrainingClient 更新 Progress/Loss/Iteration 属性
       │                        │
       │                        ▼
       │                   GSViewerUI 实时绘制进度条、迭代数、Loss
       │
       ├─ [可选] 用户点击 "停止训练"
       │    → TrainingClient 发送 {"type":"stop_training"}
       │    → Python 设置 stop_event，kill 子进程
       │    → 发送 {"type":"training_stopped"}
       │    → Unity 状态回到 Connected
       │
       ▼
4. 训练完成，Python 发送: {"type":"training_complete", "status":"ok", "ply_path":"..."}
       │  TCP Socket
       ▼
5. TrainingClient 在主线程回调队列收到消息
       │
       ▼
6. GSViewerUI.OnTrainingComplete() 被触发
       │
       ▼
7. TrainingAssetImporter.ImportAndAssign()
       ├─ 反射调用 GaussianSplatAssetCreator.CreateAsset()
       ├─ PLY → GaussianSplatAsset (.asset + .bytes 文件)
       └─ renderer.m_Asset = newAsset
       │
       ▼
8. GaussianSplatRenderer.Update() 检测到 asset 变化
       │
       ▼
9. 自动 DisposeResourcesForAsset() + CreateResourcesForAsset()
       │
       ▼
10. 画面刷新，显示新的 3D 高斯模型
```

## 使用步骤

### 1. 场景配置（仅首次）

在 Unity 场景中：
- 新建空 GameObject，添加 `TrainingClient` 组件
- `GSViewerUI` 会自动查找 `TrainingClient`（也可手动拖拽赋值）
- 确保场景中有 `GaussianSplatRenderer`（已有的 GaussianSplats 对象）

### 2. 启动 Python 服务器

```bash
conda activate 3dgs
python E:\unityhub\gaussian-splatting\train_server.py
```

输出：
```
3DGS Training Server listening on 127.0.0.1:9090
Waiting for Unity client...
```

### 3. Unity 中操作

1. 进入 Play 模式
2. 左侧面板 "3DGS 训练" 区域
3. 填写参数：
   - **数据路径**: COLMAP 数据目录（如 `E:\unityhub\gaussian-splatting\data\barn_40`）
   - **迭代次数**: 默认 7000，可自定义
   - **Mock模式**: 勾选则为测试模式（不需要真实数据）
4. 点击 **"连接服务器"** → 状态变为 "Connected"
5. 点击 **"开始训练"** → 进度条实时更新
6. 训练中可点击红色 **"停止训练"** 按钮中止
7. 训练完成后自动导入 PLY 并刷新显示

### 4. 数据路径格式

数据目录需要是 COLMAP 处理后的标准结构：
```
data_path/
├── images/          ← 原始图片
└── sparse/
    └── 0/           ← COLMAP 稀疏重建结果
        ├── cameras.bin
        ├── images.bin
        └── points3D.bin
```

## 关键设计决策

### 为什么用 TCP Socket 而不是 HTTP/WebSocket？
- 本地通信延迟最低
- 不需要额外依赖库
- 长度前缀协议解决粘包问题，实现简单

### 为什么用反射调用 GaussianSplatAssetCreator？
- 复用已有的、经过验证的 PLY → Asset 转换逻辑（Morton 重排、数据量化、纹理压缩等）
- 避免从零重写 ~600 行的资产创建代码
- 代价是依赖内部 API，插件升级可能需要适配

### 线程安全
- Socket 收发在子线程
- Unity API 调用必须在主线程
- 使用 `ConcurrentQueue<Action>` 在 `Update()` 中分发回调

### 训练进度解析
- Python 端解析 train.py 的 tqdm 输出（正则匹配 `10%|...|  700/7000`）
- 同时解析 `[ITER N]` 格式的事件行（Saving、Evaluating 等）
- 每 1% 进度变化发送一次，避免消息洪泛

### 停止训练机制
- Python 端训练在子线程运行，主线程保持接收消息
- 收到 `stop_training` 后设置 `threading.Event`
- 训练线程检测到 Event 后调用 `proc.kill()` 杀掉 train.py 子进程
- 回复 `training_stopped`，Unity 端状态回到 Connected

### Asset 自动刷新
- `GaussianSplatRenderer.Update()` 每帧检查 `m_Asset` 引用和 `dataHash`
- 赋值新 asset 后自动销毁旧 GPU 资源并重建，无需手动刷新

## 待完成

- [x] 安装 `diff-gaussian-rasterization`（需要 VS 2022 的 cl.exe 在 PATH 中）
- [x] 安装 `simple-knn`、`lpips` 等训练依赖
- [x] 打通真实 train.py 训练流程
- [x] 添加训练进度百分比回传（Python 端解析 stdout 进度）
- [x] 添加停止训练功能
- [ ] Build 打包时的 Runtime PLY 加载（当前仅支持 Editor 模式）
- [ ] 训练参数扩展（learning rate、densify 参数等通过 UI 配置）
- [ ] 多次训练结果对比（保存历史训练记录）
