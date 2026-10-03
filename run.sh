#!/usr/bin/env bash
# 一键：生成向量 → 转译核心 → C# 跑一遍 → Godot 3 跑一遍 → 比对
set -euo pipefail
cd "$(dirname "$0")"
# 环境变量：GODOT（Godot 3.5 headless 可执行文件）、TBH_REPO、BROTATO_REPO（两个仓库只读引用，做对照）
GODOT=${GODOT:-godot3}
TBH_REPO=$(cd "${TBH_REPO:-../tbh-combat-tracker}" && pwd)
BROTATO_REPO=${BROTATO_REPO:-../brotato-combat-tracker}
BRO="$BROTATO_REPO/mods-unpacked/DPSLove-CombatTracker/core"
mkdir -p out
dotnet build transpiler -c Release -v q --nologo
dotnet build tests/csrunner -c Release -v q --nologo -p:TbhRepo="$TBH_REPO"
python3 tests/gen_vectors.py out/vectors.json
dotnet transpiler/bin/Release/net8.0/Transpiler.dll core out/gd3
rm -rf tests/gd3/gen tests/gd3/legacy && mkdir -p tests/gd3/gen tests/gd3/legacy
cp out/gd3/*.gd runtime/gd3/rt.gd tests/gd3/gen/
cp "$BRO/log_format.gd" "$BRO/fmt.gd" tests/gd3/legacy/      # 只读拷贝，仓库不动
dotnet tests/csrunner/bin/Release/net8.0/csrunner.dll out/vectors.json out/cs.json
VEC="$PWD/out/vectors.json" OUT="$PWD/out/gd3.json" "$GODOT" --no-window --path tests/gd3 -s res://runner.gd 2>&1 | grep -v "^Godot Engine" || true
python3 tests/compare.py out/vectors.json out/cs.json out/gd3.json
