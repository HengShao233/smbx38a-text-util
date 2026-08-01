# TableExporter

把 Excel / CSV 配置表导出成 SMBX 38A 可直接使用的 TeaScript（`.smt`）查表脚本。

## 快速开始

```powershell
# 导出单张表
TableExporter.exe npc.csv -o out

# 含 text 列（需要字模转码）时，指定字体图集配置
TableExporter.exe dialog.csv -o out --font-config .cfg.json

# 批量导出一个目录
TableExporter.exe tables\ -o out

# 内置自检
TableExporter.exe --self-test
```

## 表格式

第一行是表头，格式为 `字段名:类型`：

| 写法 | 含义 |
| --- | --- |
| `hp:int` | 32 位整数 |
| `speed:float` | 浮点数（定点保留 4 位小数） |
| `boss:bool` | 布尔，接受 `0/1`、`true/false`、`yes/no` |
| `name:string` | 普通字符串 |
| `line:text` | 富文本，交给 FontAtlasGenerator 做字模转码 |
| `partner:ref` | 引用另一行的 uuid，存为完美哈希键 |
| `drops:int[]` | 数组，默认以 `\|` 分隔 |
| `tags:string[;]` | 数组，自定义分隔符 `;` |

规则：

- 必须有一列名为 `id`，作为每行的唯一 uuid（主键）。
- 以 `#` 开头的列或 `id` 以 `#` 开头的行会被忽略（当作注释）。
- 空行自动跳过；`id` 为空或重复会报错并指出行号。

## 生成的接口

以表名 `npc` 为例（前缀由表名转成 PascalCase）：

```vb
NpcFind(uuid)          ' uuid -> 定位码；未命中返回 -1
NpcHas(uuid)           ' 是否存在
NpcCount()             ' 总行数
NpcLocate(key)         ' 哈希键 -> 定位码（用于 ref 跳转）
NpcHash(uuid)          ' 计算哈希键

NpcHp(loc)             ' 标量字段
NpcSpeed(loc)
NpcDropsCount(loc)     ' 数组长度
NpcDropsAt(loc, i)     ' 数组元素
```

典型用法：

```vb
Dim loc As Integer = NpcFind("bowser")
If loc >= 0 Then
    Debug(NpcName(loc) + " HP=" + CStr(NpcHp(loc)))
End If
```

`ref` 字段存的是目标行的哈希键，可直接跳转：

```vb
Dim partner As Integer = NpcLocate(NpcPartner(loc))
```

## 实现要点

### 展开式二分查找

查表不使用运行时循环，而是在导出期把整棵二分决策树展开成嵌套的 `If / ElseIf / Else`
（类似编译期循环展开）。区间收敛到 4 行以内时改为顺序比较，避免脚本行数膨胀。
对解释执行的 TeaScript 来说，这样省掉了循环控制与下标运算的开销。

### 完美哈希

uuid 经 `h = (h * P + code) mod M` 映射为 int32，`P` 与初始盐从候选表中搜索到本表内
无碰撞为止。`M` 取 `2^31 - 1`，且 `P ≤ 4093`，保证中间值 `h * P + code < 2^53`，
使 TeaScript 的浮点运算完全精确——这一点由自检用例强制校验。

### base92 与字面量安全

数据用 base92 编码，字母表是可打印 ASCII 去掉 `"`、`\`、`` ` ``，
因此能安全地写进 TeaScript 字符串字面量。运行时的「字符 -> 数位」转换利用字母表的
连续性，用 3 次比较代替 92 分支查表。

有两个容易踩的坑，已在实现中处理并有对应测试：

- **首尾空格**：base92 的第 0 个数位正是空格，载荷完全可能以空格开头/结尾。
  一旦某个环节对字面量做了 trim，数据就会静默错位。因此每个数据块两端都加了
  `~` 哨兵，运行时统一用 `Mid` 跳过。
- **text 列**：FontAtlasGenerator 输出的是 `TXT(D("<payload>"))` 这样的**表达式源码**。
  必须剥到最内层的 `<payload>` 再存，由访问器在运行时执行 `TXT(D(seg))`；
  否则存下来的只是一串永不求值的源码文本，而且其中的裸引号会破坏字面量。

### FontAtlasGenerator 集成

以子进程方式调用，不引用其工程（本工程是 self-contained 单文件发布，
直接 `ProjectReference` 会触发 NETSDK1150）。查找顺序：

1. `--font-atlas` 显式指定
2. 本 exe 同目录（支持 `FontAtlasGenerator v2.2.2.exe` 这类带版本号/空格的宽松匹配）
3. `tools/`、`build/`，以及开发期回溯到解决方案目录下的 `bin/`
4. 系统 `PATH`

两个必须注意的行为：

- 配置中的 `font-path` 是相对路径，按**工作目录**解析，所以子进程必须在配置所在目录下运行，
  且配置要传绝对路径（它用 `File.Exists(arg)` 判定参数）。
- 它结尾会调 `Console.ReadKey()`，stdin 被重定向时必定抛异常，
  进程以 `0xE0434352` 退出——**即便转码已经成功**。因此成败判定以「产物文件是否生成」为准，
  退出码只作为诊断信息。

## 测试

```powershell
# 一键跑完：构建 -> 自检 -> 导表 + lint -> 生成测试关卡
pwsh -File tests\run_tests.ps1
```

分三层：

1. **C# 自检**（`--self-test`，35 个用例）
   覆盖 base92 字母表/编解码、zigzag、变长数字、转义、完美哈希无碰撞与浮点安全性、
   整表打包解析往返、数组、分块边界、哨兵机制、text 列契约。
   其中 `RuntimeSimulator` 用 C# 复刻了一遍生成脚本的解析逻辑，
   任何与 `TeaScriptEmitter` 的偏差都会被捕获。

2. **静态 lint**：用 `teascript-helper` 的 linter 检查生成脚本，要求 0 error。
   （残留的 W102 是 linter 把数据字面量当代码扫出的误报。）

3. **游戏内回归**：`tests/table_test.smt` 提供 40+ 个运行时断言，
   由 `make_test_level.py` 打包成 `build/table_test/TableTest.lvl`，
   进关卡自动执行，结果输出到 Debug 面板。

### 命名约束

38A 的 script 名必须匹配 `^[A-Za-z][A-Za-z0-9]*$`，且**不允许数字和下划线**，
所以生成的名字是纯 PascalCase（表名里的数字会被写成英文单词）。
另外，38A 对「代码 + 行尾注释」有解析 bug（lint 的 E014），
因此生成器把注释一律单独成行。

## 构建

```powershell
# Debug：会顺带构建 FontAtlasGenerator 并拷到输出目录
dotnet build -c Debug

# 发布单文件 exe 到 build/
dotnet publish -c Release -o ..\build
```
