# RobotSimulation 文档

> English version: [`README.md`](README.md) / 英文版见 [`README.md`](README.md)。

文档按模块 + 语言拆分：每个模块提供英文（`en.md`）与中文（`zh-CN.md`）两个独立文件，本索引也遵循同一条规则——本页是中文那份，英文那份见 [`README.md`](README.md)。

| 模块 | 中文 / 简体中文 | English |
|---|---|---|
| **架构设计** | [`architecture/zh-CN.md`](architecture/zh-CN.md) | [`architecture/en.md`](architecture/en.md) |
| **`RobotSimulation.Core`**（核心） | [`core/zh-CN.md`](core/zh-CN.md) | [`core/en.md`](core/en.md) |
| **`RobotSimulation.Robot`**（机器人） | [`robot/zh-CN.md`](robot/zh-CN.md) | [`robot/en.md`](robot/en.md) |
| **`RobotSimulation.OpenGL`**（渲染后端） | [`opengl/zh-CN.md`](opengl/zh-CN.md) | [`opengl/en.md`](opengl/en.md) |
| **测试宿主与数据** | [`testing/zh-CN.md`](testing/zh-CN.md) | [`testing/en.md`](testing/en.md) |

---

## 阅读顺序

1. [`architecture/zh-CN.md`](architecture/zh-CN.md) — 先看整体包化架构、边界、运行时/渲染线程模型与背后的关键设计决策（ADR）。
2. [`core/zh-CN.md`](core/zh-CN.md) — 引擎内核的公共 API（场景、几何、渲染抽象、工具）。
3. [`robot/zh-CN.md`](robot/zh-CN.md) — 机器人领域模型（URDF → `RobotModel` / `RobotState`）。
4. [`opengl/zh-CN.md`](opengl/zh-CN.md) — 唯一渲染后端 + 宿主组装 + **自定义着色器**用法。
5. [`testing/zh-CN.md`](testing/zh-CN.md) — 两个最小宿主（裸窗口 / Avalonia）与它们的测试数据：加载什么、文件从哪来、如何换模型、如何重新生成样本；另有 `src/RobotSimulation.Tests` 的单元级路径解析检查。

> 仓库根入口见 [`../README.zh-CN.md`](../README.zh-CN.md)（中文）与 [`../README.md`](../README.md)（English）。
