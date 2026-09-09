# RobotSimulation 文档 / Documentation Index

> 文档按模块 + 语言拆分为多个子文件夹；每个模块提供中文（`zh-CN.md`）与英文（`en.md`）两个独立文件。
> Docs are split by module **and** language: each module ships a Chinese (`zh-CN.md`) and an English (`en.md`) file.

| 模块 / Module | 中文 / 简体中文 | English |
|---|---|---|
| **架构设计 / Architecture** | [`architecture/zh-CN.md`](architecture/zh-CN.md) | [`architecture/en.md`](architecture/en.md) |
| **`RobotSimulation.Core`**（核心 / Core） | [`core/zh-CN.md`](core/zh-CN.md) | [`core/en.md`](core/en.md) |
| **`RobotSimulation.Robot`**（机器人 / Robot） | [`robot/zh-CN.md`](robot/zh-CN.md) | [`robot/en.md`](robot/en.md) |
| **`RobotSimulation.OpenGL`**（渲染后端 / OpenGL） | [`opengl/zh-CN.md`](opengl/zh-CN.md) | [`opengl/en.md`](opengl/en.md) |

---

## 阅读顺序 / Suggested reading order

1. [`architecture/zh-CN.md`](architecture/zh-CN.md) — 先看整体包化架构、边界、线程模型与设计决策（ADR）。
2. [`core/zh-CN.md`](core/zh-CN.md) — 引擎内核的公共 API（示例、几何、渲染抽象、工具）。
3. [`robot/zh-CN.md`](robot/zh-CN.md) — 机器人领域模型（URDF → `RobotModel` / `RobotState`）。
4. [`opengl/zh-CN.md`](opengl/zh-CN.md) — 唯一渲染后端 + 宿主组装 + **自定义着色器**用法。

> 仓库根入口见 [`../README.md`](../README.md)（English）与 [`../README.zh-CN.md`](../README.zh-CN.md)（中文）。
