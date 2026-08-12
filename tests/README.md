# WorldSim — 测试规范与 CI（单一真相：契约 + Unity EditMode）

> **可执行测试唯一位置**：`WorldSim/Assets/Scripts/Tests/`（asmdef `WorldSim.Tests`，EditMode headless）。
> 本目录只保留**契约、CI 脚本与说明**；不再存放可编译的 `.cs` 双源副本（P1 已清除，避免私有 `DeterminismMath` 漂移）。

---

## 1. 目录结构

```
tests/
├── README.md
├── contracts/
│   └── determinism-contract.md    # 确定性契约（单一真相源）
├── gate0/                         # 仅占位说明 → 见 Assets/.../Tests/Gate0/
├── unit/                          # 仅占位说明 → 见 Assets/.../Tests/Unit/
└── ci/
    ├── version-pins.json
    ├── assert-burst-pinned.ps1
    ├── assert-region-presets-synced.ps1
    ├── check-sim-asmdef.ps1
    ├── resolve-unity.ps1
    ├── run-gate0-local.ps1
    └── asmdef-boundary-check.md
```

---

## 2. 运行方式

### 2.1 本地一键
```powershell
powershell -File tests/ci/run-gate0-local.ps1
```

### 2.2 CI（`.github/workflows/gate0.yml`）
1. **pin-versions**（ubuntu）：`version-pins` ↔ env；`assert-burst-pinned`；`check-sim-asmdef`；`assert-region-presets-synced`
2. **gate0**（self-hosted Windows X64）：`resolve-unity.ps1` → 全量 `WorldSim.Tests` EditMode

---

## 3. 红线

- 禁止再向 `tests/unit` / `tests/gate0` 添加会与 Assets 分叉的 `.cs`。
- 指标入哈希前必须 `Quantize`；哈希算法为 **FNV-1a-64**（xxHash 延后）。
- `region-presets.json`：`design/gdd/data/` 与 `StreamingAssets/Data/` 必须字节一致。
