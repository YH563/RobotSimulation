# RobotSimulation documentation

> 简体中文版本见 [`README.zh-CN.md`](README.zh-CN.md) / For the Chinese version, see [`README.zh-CN.md`](README.zh-CN.md).

Docs are split by module **and** language: every module ships an English (`en.md`) and a Chinese (`zh-CN.md`)
file, and this index follows the same rule — this page is the English half, its companion is
[`README.zh-CN.md`](README.zh-CN.md).

| Module | English | 简体中文 |
|---|---|---|
| **Architecture** | [`architecture/en.md`](architecture/en.md) | [`architecture/zh-CN.md`](architecture/zh-CN.md) |
| **`RobotSimulation.Core`** (kernel) | [`core/en.md`](core/en.md) | [`core/zh-CN.md`](core/zh-CN.md) |
| **`RobotSimulation.Robot`** (robot) | [`robot/en.md`](robot/en.md) | [`robot/zh-CN.md`](robot/zh-CN.md) |
| **`RobotSimulation.OpenGL`** (render backend) | [`opengl/en.md`](opengl/en.md) | [`opengl/zh-CN.md`](opengl/zh-CN.md) |
| **Test hosts & data** | [`testing/en.md`](testing/en.md) | [`testing/zh-CN.md`](testing/zh-CN.md) |

---

## Suggested reading order

1. [`architecture/en.md`](architecture/en.md) — start here: package boundaries, dependency direction, the
   runtime/render thread model and the decisions (ADRs) behind them.
2. [`core/en.md`](core/en.md) — the kernel's public API (scene, geometry, render abstraction, utilities).
3. [`robot/en.md`](robot/en.md) — the robot domain model (URDF → `RobotModel` / `RobotState`).
4. [`opengl/en.md`](opengl/en.md) — the only render backend, host composition and **custom shaders**.
5. [`testing/en.md`](testing/en.md) — the two minimal host tests (bare window / Avalonia) and their test
   data: what they load, where the files come from, how to switch models or regenerate samples; plus the
   unit-level path-resolution checks in `src/RobotSimulation.Tests`.

> Repo entry points: [`../README.md`](../README.md) (English) and [`../README.zh-CN.md`](../README.zh-CN.md) (中文).

