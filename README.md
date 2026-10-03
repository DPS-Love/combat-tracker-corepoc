# corepoc：受限 C# → GDScript 3 原型

核心只写一份 C#（`core/`），C# 目标直接编译；GDScript 3 由 `transpiler/`（Roslyn）生成。
两边用同一份测试向量跑，结果逐项比对。

```
core/            受限 C# 子集写的核心：转义、数字文本、界面数字格式、日志容器读写
core/Rt.cs       运行库 C# 版（不转译）
runtime/gd3/     运行库 GDScript 3 版（手写，和 Rt.cs 一一对应）
transpiler/      子集检查 + GDScript 3 生成
tests/           向量生成、C# 测试台、Godot 3 测试台、比对脚本
run.sh           一键：生成向量 → 转译 → 两边各跑一遍 → 比对
```

依赖：.NET 8 SDK、Godot 3.5 headless、Python 3。

```bash
GODOT=/path/to/Godot_v3.5.3-stable_linux_headless.64 \
TBH_REPO=../tbh-combat-tracker BROTATO_REPO=../brotato-combat-tracker ./run.sh
```

两个仓库默认在本仓库的同级目录。上次运行的结果见 `docs/report.txt`。
`run.sh` 只读地引用 TBH 和 Brotato 仓库里的旧实现做对照，不修改它们。
