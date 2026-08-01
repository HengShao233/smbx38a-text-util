#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 TableExporter 的快速验证关卡。

流程:
  1. 调用 TableExporter 把 tests/data/npc.csv 导成 npc_table.smt
  2. 把生成的表脚本 + table_test.smt 一起嵌进一个最小关卡
  3. 绑定 "Level - Start" 事件, 进关卡即自动跑全部用例, 结果打到 Debug 面板

用法:
    python make_test_level.py [--exe <TableExporter.exe>] [--out <dir>]
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.dirname(HERE)                       # TableExporter/
CSUTIL = os.path.dirname(PROJ)                     # CSUtil/
SKILL_SCRIPTS = os.path.join(
    CSUTIL, '.codebuddy', 'skills', 'smbx-38a', 'scripts')

sys.path.insert(0, SKILL_SCRIPTS)

try:
    from lvl_builder import LVLBuilder  # noqa: E402
except ImportError as exc:  # pragma: no cover
    print(f'[x] 无法导入 lvl_builder: {exc}')
    print(f'    请确认 skill 脚本存在于 {SKILL_SCRIPTS}')
    sys.exit(1)


def find_exporter(explicit: str | None) -> str:
    """定位 TableExporter.exe，优先使用显式路径。"""
    if explicit:
        if os.path.isfile(explicit):
            return explicit
        sys.exit(f'[x] 指定的 exe 不存在: {explicit}')

    candidates = [
        os.path.join(CSUTIL, 'build', 'TableExporter.exe'),
        os.path.join(PROJ, 'bin', 'Debug', 'net9.0', 'win-x64', 'TableExporter.exe'),
        os.path.join(PROJ, 'bin', 'Release', 'net9.0', 'win-x64', 'TableExporter.exe'),
    ]
    for c in candidates:
        if os.path.isfile(c):
            return c

    sys.exit('[x] 找不到 TableExporter.exe，请先 dotnet build 或 dotnet publish')


def export_table(exe: str, out_dir: str) -> str:
    """跑一次导表，返回生成的 smt 路径。"""
    csv_path = os.path.join(HERE, 'data', 'npc.csv')
    os.makedirs(out_dir, exist_ok=True)

    print(f'[>] 导表: {csv_path}')
    proc = subprocess.run(
        [exe, csv_path, '-o', out_dir],
        capture_output=True, text=True, encoding='utf-8', errors='replace')

    if proc.returncode != 0:
        print(proc.stdout)
        print(proc.stderr, file=sys.stderr)
        sys.exit(f'[x] 导表失败, 退出码 {proc.returncode}')

    print(proc.stdout.strip())

    smt = os.path.join(out_dir, 'npc_table.smt')
    if not os.path.isfile(smt):
        sys.exit(f'[x] 未生成预期的表脚本: {smt}')
    return smt


def build_level(table_smt: str, out_lvl: str) -> None:
    """把表脚本与测试脚本打包成关卡。"""
    with open(table_smt, encoding='utf-8') as f:
        table_body = f.read()
    with open(os.path.join(HERE, 'table_test.smt'), encoding='utf-8') as f:
        test_body = f.read()

    b = LVLBuilder(title='TableExporter Test')
    b.add_section_grid()

    # 一块地面，避免玩家直接掉出关卡（add_block 支持自定义宽度，铺一整条即可）。
    b.add_block(blk_id=457, x=-200000, y=-200032, w=768, h=32)

    b.add_script('NpcTable', table_body)
    b.add_script('TableTest', test_body)

    # 进入关卡即自动执行全部用例。
    b.add_script('Main', 'Call TableTest_RunAll()\n')
    b.add_event('Level - Start', autostart=1, script_name='Main')

    b.save(out_lvl)
    print(f'[√] 关卡已生成: {out_lvl}')


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument('--exe', help='TableExporter.exe 路径')
    ap.add_argument('--out', default=os.path.join(CSUTIL, 'build', 'table_test'),
                    help='输出目录')
    args = ap.parse_args()

    exe = find_exporter(args.exe)
    print(f'[i] 使用 exe: {exe}')

    out_dir = os.path.abspath(args.out)
    os.makedirs(out_dir, exist_ok=True)

    table_smt = export_table(exe, out_dir)

    # 把测试脚本一并拷到输出目录，方便手工查看/改动后重跑。
    shutil.copy2(os.path.join(HERE, 'table_test.smt'), out_dir)
    shutil.copy2(os.path.join(HERE, 'data', 'npc.csv'), out_dir)

    build_level(table_smt, os.path.join(out_dir, 'TableTest.lvl'))

    print()
    print('用 SMBX 38A 打开 TableTest.lvl 并进入游戏，')
    print('测试会自动运行，结果输出在 Debug 面板。')


if __name__ == '__main__':
    main()
