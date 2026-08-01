<#
.SYNOPSIS
    TableExporter 一键测试。

.DESCRIPTION
    依次执行:
      1. dotnet build（Debug 会顺带构建并拷贝 FontAtlasGenerator）
      2. 内置自检 --self-test（编码 / 哈希 / 打包 / 布局契约）
      3. 导出示例表并用 teascript lint 检查生成脚本
      4. 生成可直接进游戏验证的测试关卡

.EXAMPLE
    pwsh -File run_tests.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipLevel
)

$ErrorActionPreference = 'Stop'
$env:PYTHONIOENCODING = 'utf-8'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Split-Path -Parent $here
$csutil = Split-Path -Parent $proj
$exe = Join-Path $proj 'bin\Debug\net9.0\win-x64\TableExporter.exe'
$lint = Join-Path $csutil '.codebuddy\skills\teascript-helper\scripts\teascript_lint.py'

$failed = 0

function Step($name) {
    Write-Host ''
    Write-Host "==== $name ====" -ForegroundColor Cyan
}

Step '1/4 构建'
dotnet build $proj -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host '构建失败' -ForegroundColor Red; exit 1 }
Write-Host '构建通过' -ForegroundColor Green

Step '2/4 内置自检'
& $exe --self-test
if ($LASTEXITCODE -ne 0) { $failed++; Write-Host '自检失败' -ForegroundColor Red }
else { Write-Host '自检通过' -ForegroundColor Green }

Step '3/4 导表 + lint'
$outDir = Join-Path $here 'out'
& $exe (Join-Path $here 'data\npc.csv') -o $outDir
if ($LASTEXITCODE -ne 0) { $failed++; Write-Host '导表失败' -ForegroundColor Red }

$targets = @((Join-Path $outDir 'npc_table.smt'), (Join-Path $here 'table_test.smt'))
foreach ($f in $targets) {
    $leaf = Split-Path -Leaf $f
    Write-Host ('-- lint ' + $leaf)

    # 只关心 error；W102 多为把数据字面量当代码扫出的误报。
    $out = (python $lint $f 2>&1 | Out-String)
    $summary = ''
    foreach ($ln in ($out -split "`n")) {
        if ($ln -match '\d+ error') { $summary = $ln.Trim() }
    }

    if ($summary -match '([1-9]\d*) error') {
        $failed++
        Write-Host ('   ' + $summary) -ForegroundColor Red
        foreach ($ln in ($out -split "`n")) {
            if ($ln -match 'ERROR') { Write-Host ('   ' + $ln.Trim()) -ForegroundColor Red }
        }
    } else {
        Write-Host ('   ' + $summary) -ForegroundColor Green
    }
}

if (-not $SkipLevel) {
    Step '4/4 生成测试关卡'
    python (Join-Path $here 'make_test_level.py')
    if ($LASTEXITCODE -ne 0) { $failed++; Write-Host '关卡生成失败' -ForegroundColor Red }
} else {
    Step '4/4 生成测试关卡 (已跳过)'
}

Write-Host ''
if ($failed -eq 0) {
    Write-Host '全部检查通过。' -ForegroundColor Green
    exit 0
} else {
    Write-Host "存在 $failed 项失败。" -ForegroundColor Red
    exit 1
}
