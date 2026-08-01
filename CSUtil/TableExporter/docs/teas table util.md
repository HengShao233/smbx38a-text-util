# 导表

本文档简述基于 tea script 的导表工具需求以及基础的实现方案.

## 配置表规范

1. 配置表为标准 xlsx 文档
2. 关于留用行:
   - 第 1 行留用为简介/备注行.
   - 第 2 行留用为字段名 (字段名必须符合 `字段名约束`).
   - 第 3 行留用为字段类型, 仅支持 `int(i/i32/integer)`/`str(s/string)`/`text(txt/t)` 导出后分别对应 tea script 中的 `Long`/`String(仅支持 ascii 字符, 若出现了非 ascii 字符, fallback 到 ' ' 空格字符.)`/`String(text util 工具链语境下的 A 串)`. 关于类型, 可以跟随后缀 `[]`, 用于书写数组类数据. 对于数组类数据, 可以使用 `,`/`:`/`;`/`|`/`/` 几个分隔符, 若想指定一个分隔符, 可以写成 `[:]`, 这样就只使用 `:` 作为分隔符. 对于字符串中含分隔符的情况, 可以使用转义字符 `\` 进行转义, 如: `aaa\:aaa:bbb` 会被解析为 {`aaa:aaa`,`bbb`}
   - 第 A 列留用为 id 列, 字段名 `id` 留用 (可不填), 禁止其它字段使用. `id` 字段的类型为 string (仅支持 ascii 字符串, 首尾无空格类字符 (首尾 trim)). 同 `chunk` 下 `id` 禁止重复. `id` 最长为 64 个字符, 最短为 1 字符.
3. 名约束: 名必须符合 C 风格的标识符命名约束(Identifier: `[a-zA-Z_][0-9a-zA-Z_]*`).
4. 表名与字段名:
   - 4.1. 表名约束:
     - 表名约束是 `名约束` 的收束版本, 首先要满足 `名约束`
     - **表名不区分大小写**.
   - 4.2. 字段名约束:
     - 字段名约束是 `名约束` 的收束版本, 首先要满足 `名约束`
     - 对于一次导表中被分配到相同 `chunk` 的配置, 禁止出现字段重名.
     - **字段名不区分大小写**.
5. 其它约束:
   - 字段名最多只包含 64 个.

以下是一个配置表案例:

文件名: `随便什么名字.xlsx`;
worksheet 名: `随便什么名字 [sheetName]`

> 这里表名可以填在 worksheet name 处, 在末尾使用 `[]` 括起来. 若在 worksheet name 处找不到相关定义, 到 A1 单元格找 (同样是末尾使用 `[]` 括起来的字符串, 若找不到, 那就是未定义的 sheet name 了).
> 多个同名表合并位一张脚本.

| `[sheetName]` | 名称   | 高度   | 对话                             |
| --------------- | ------ | ------ | -------------------------------- |
| id              | name   | height | talk                             |
| s               | text   | int    | t[;]                             |
| roleA           | 玩家 A | 32     | 大漠孤烟直，;长河落日圆。;完了。 |
| roleB           | 玩家 B | 64     | 举头望明月，;低头思故乡。;终了。 |

## 导出结果

> 概念解析:
>
> 1. chunk: 一个数据块, 由**紧密排列**的数据单元构成. 在 tea script 中体现为一个小于或等于 `16383` 字符的 ascii 字符串 (具体内容视数据而定).
> 2. 数据单元: 一个不可分的数据, 对于一个非数组类型的 field 而言, 一个 field 值为一个数据单元. 对于数组类型的 field 而言, 数组头为一个数据单元, 一个数组元素为一个数据单元. 同一个数据单元禁止横跨两个 chunk.
> 3. 映射数据: 用于作为给定 id 查找 chunk.

```tea
' 该脚本内容由配置表导出, 禁止修改
' -------- 接口列表
' ~这里罗列所有的字段获取接口, 使用注释形式, 真实的定义放在最底下~
' Export Script <SheetName>_SetId(id As String) ' 装填目标 id
' Export Script <SheetName>_Get<fieldName1>(Return Long) ' 字段名, 类型
' Export Script <SheetName>_Get<fieldName2>(i As Long, Return Long) ' 字段名, 类型 (对于数组类型, 需要填入 idx)
' Export Script <SheetName>_Get<fieldName3>(Return String) ' 字段名, 类型 (对于数组类型, 需要填入 idx)
' Export Script <SheetName>_Get<fieldName4>(i As Long, Return String) ' 字段名, 类型 (对于数组类型, 需要填入 idx)
' ......

' ----------------------------------------------- 以下内容非用户可读
' -----------------------------------------------------------------
' -----------------------------------------------------------------
' -----------------------------------------------------------------
' -------- 静态数据块
' __row_map 只有一个, 用于存 uuid->(__data_map:id 和 __data_map:offset) 的映射
' __row_map 的大概结构如是: [uuid 5位][__data_map:id 1位][__data_map:offset 2位]...
' **__row_map 中的 uuid 是有序排列的!**
' uuid 是 int32, 理论上会耗费 5 个 base92 位, __data_map:id 会占一个 base92 位, __data_map:offset 会占 2 个 base92 位.
' 因为对于一行数据, 导出后其字段已经是固定的, 所以不再需要额外记录 length (直接写死在脚本里)
' 因故, 一行数据在 __row_map 中会占 8 个 base92 位. 出于 16383 的限制, 一张表实质上就只能放 2048 项了.
' 不过 __row_map 长度名义上上不封顶, 超过 2048 仅在导表时抛出 warning 即可.
Dim __row_map As String = "..." ' ascii 字符串, 92-base, 最大长度不限 (但是实现上应该还是有限制的, 理论上是 16383 个字符).

' __data_map 最大数量为 92 个, 最大长度为 8463 (92^92 - 1, 两位 base-92 整数)
' 该组数据结构用于维护 row 的各个字段对应的 __data_chunk:id 和 __data_chunk:offset 和 length(数据块大小)
' __data_map 的大概结构如是 [__data_chunk:id 2位][__data_chunk:offset 3位][length 3位]... 即紧密排列的字段定位数据 (field), 一个字段定位数据长 8 个 base92 位.
' 值得注意的是, 上面并没有要求一个 row 的索引目标不能跨越两个 __data_map, 当 row 的某个字段对应的 offset 已经超过了当前 __data_map 的长度时, 应该从下一个 __data_map 开头继续查起.
' 虽然没有规定一个 row 的索引目标禁止跨两个 __data_map, 但是一组字段位 ([__data_chunk:id 2位][__data_chunk:offset 3位][length 3位]) 是禁止跨 data map 的, 否则会带来 seek 上的麻烦.
Dim __data_map_00 As String = "..." ' 保存每个数据对应的 chunk (用于根据 id/field name 查询到对应的 chunk 位置.)
Dim __data_map_01 As String = "..." ' 保存每个数据对应的 chunk (用于根据 id/field name 查询到对应的 chunk 位置.)

' chunk 最大数量为 8463 个 (92^92 - 1, 两位 base-92 整数)
' 每个 chunk 最大长度为 16383, 为 ascii 字符串, chunk 中的每个具体的数据单元都**紧密排列**. 因为数据单元禁止跨 chunk 存在, 为了不让 chunk 数量过多, 需要提前处理好数据单元, 将小的尽量堆在一起.
Dim __data_chunk_0000 As String = "..." ' 保存的具体数据
Dim __data_chunk_0001 As String = "..." ' 保存的具体数据
' ...
Dim __data_chunk_000F As String = "..." ' 保存的具体数据

' -------- 动态数据块
' tea script 的 Integer 是 int16, Long 是 int32 ...
Dim __curr_id As String = "" ' 用户输入的 id

Dim __curr_data_map_id As Integer = 0
Dim __curr_data_map_offset As Integer = 0

Dim __field_index As Long = 0 ' 字段下标
Dim __field_array_index As Integer = 0 ' 是否是数组 <0:不是数组; >=0:是数组(并指示要取的数组元素下标)

Dim __curr_data_map_offset_real As Long = 0
Dim __field_index_real As Long = 0 ' 字段下标 (真实的)

Dim __data_offset As Long = 0
Dim __data_length As Long = 0
Dim __data_chunk As Integer = 0

Dim __target_data As String = "" ' 最终读到的目标数据

Dim __temp_str As String = "" ' 临时变量, 用于在单个局部作用域内存储字符串数据

' -------- 数据截取
Script __TableExport_<SheetName>_GetDataMapLen(idx As Integer, Return Long)
    ' 通过 idx 获取对应的 DataMap 块长度:
    ' 根据 idx 二分查找 (当分支剩下的比较对象 <= 3 个时, 直接按顺序 If idx = xxx Then Return ...)
    If idx > 中间值 Then
        ' If ...
        ' 或 Return 写死的长度
    Else
        ' If ...
        ' 或 Return 写死的长度
    EndIf

    ' 否则返回 -1
    Return -1
End Script

Script __TableExport_<SheetName>_LoadDataMap(idx As Integer, offset As Integer, Return Integer)
    ' 通过 idx/offset 获取对应的 DataMap 并装填到 __data_offset&__data_length&__data_chunk:
    ' 根据 idx 二分查找 (这里默认 offset+8 不会超过对应 __data_map 的长度边界)
    ' 返回值: 0 为成功; <>0 为失败, 具体值指示失败原因

    ' 这里借 __temp_str 来存储找到的 __data_map 片段
    __temp_str = ""
    If idx > 中间值 Then
        ' If ...
        ' 或 __temp_str = Mid(__data_map_{}, offset, 8)
    Else
        ' If ...
        ' 或 __temp_str = Mid(__data_map_{}, offset, 8)
    EndIf

    If "" = __temp_str Then Return -1 ' 找不到片段
    __data_chunk = CUMath_Decode(__temp_str, offset, 2, 92)
    __data_offset = CUMath_Decode(__temp_str, offset+2, 3, 92)
    __data_length = CUMath_Decode(__temp_str, offset+5, 3, 92)
    Return 0
End Script

Script __TableExport_<SheetName>_LoadChunkFragment(checkArray As Integer, Return Integer)
    ' 根据 __data_offset&__data_length&__data_chunk 加载对应的 chunk 片段 (具体逻辑与 __TableExport_<SheetName>_LoadDataMap 相似)
    ' 1. 根据 __data_chunk 二分查找
    ' 将查到的 chunk 片段用 Mid 函数截取到 __target_data 中.
    ' 返回值: 0 为成功; <>0 为失败, 具体值指示失败原因

    ' ......
    ' 特殊地, 对于数组类数据, 且 checkArray <> 0 时, 需要使用数组头 (第一次截取到的 __target_data) 和 __field_array_index 重新装载 __data_offset&__data_length&__data_chunk 并重新截取 (递归调用 __TableExport_<SheetName>_LoadChunkFragment, 并将 checkArray 置为 0). 数组头的格式和 __data_map 类似, 同为紧密排列的 [__data_chunk:id 2位][__data_chunk:offset 3位][length 3位] 数据.
End Script

Script __TableExport_<SheetName>_SeekDataMap(Return Integer)
    ' 通过 __curr_id 定位到 dataMap (主要是填充 __curr_data_map_id & __curr_data_map_offset)
    ' 所有内容由导表工具生成, 大体逻辑:
    '     1. 根据动态构建的公式 __curr_id 计算的 uuid 值 (int32) (这里需要提出一种可以动态构建公式的算法, 能够将表中所有 id 通过的算式以 O(1) 的复杂度转换成不会重复的 uuid)
    '     2. 根据 uuid 和二分查找 __row_map (因为 __row_map 中的 uuid 是有序排列的) 定位对应的 dataMap. 并将计算好的 __curr_data_map_id & __curr_data_map_offset 写入上下文.
    '     - 返回值: 0 成功; <>0 失败, 返回值需要反应失败原因.

    ' ......
End Script

Script __TableExport_<SheetName>_SeekChunk(Return Integer)
    ' 查找对应的 __data_map 并填充 __curr_data_map_id & __curr_data_map_offset
    ' 所有内容由导表工具生成, 大体逻辑:
    '     1. 定位 field
    '        __curr_data_map_offset_real = __curr_data_map_offset
    '        __field_index_real = __field_index
    '        若 __curr_data_map_offset_real+__field_index_real*8 大于 __TableExport_<SheetName>_GetDataMapLen(__curr_data_map_offset_real) 的结果, __curr_data_map_offset_real += 1 且 __field_index_real -= (上一个 map 从 index 到结尾剩下的 field 数).
    '     2. 从 field 中读取 __data_offset/__data_length/__data_chunk (调用 __TableExport_<SheetName>_LoadDataMap)

    ' ......
End Script

' -------- 字段接口实现
Export Script <SheetName>_SetId(id As String)
    __curr_id = id
    If __TableExport_<SheetName>_SeekDataMap() <> 0 Then ' ... 先找好 data map
End Script

Export Script <SheetName>_Get<fieldName1>(Return Long)
    __field_index = xx ' 写死的字段下标 id
    __field_array_index = -1 ' 不是数组

    If __TableExport_<SheetName>_SeekChunk() <> 0 Then ' ...
    Call __TableExport_<SheetName>_LoadChunkFragment(0) ' ... 不是数组, 不用 check array ...
    Return CUMath_Decode(__target_data, 1, Len(__target_data), 92) ' 解析值
End Script

' ...... 一些其它字段

Export Script <SheetName>_Get<fieldName??>(Return String)
    ' ......
    Return __target_data & "" ' 特殊地, 对于 string 类型的返回值, 需要在结尾拼接一个空串. 在函数中直接返回 string 会导致程序崩溃, 这是 tea script 的 bug.
End Script
```

> 关于 text 类型的 string, 直接存 text util 语境下的 A 串字面量, 具体编码规则与导出逻辑可参考 smbx38a-text-util 工程. 不建议另外实现导出算法, 应该直接调用 FontAtlasGenerator 进行转码.
>
> 由于 tea script 的局限性, 我们只能采用在 tea 脚本中填入字面量字符串的方案实现导表. 为此, 我们需要在脚本层面竭尽所能寻找提高性能的可能. 这里不排斥使用 goto 等语法进行飞线 (上面的二分查找就可以用 goto 展平), 也不纠结非用户可读部分的生成代码的可读性, 一切应以性能优先 (也包含编译性能, 因为这些脚本都是在运行时动态解析的).
>
> 此外因为 tea script 的字符串字面量采用的编码是跟随系统编码的. 非 ascii 字符串会有严重问题, 切勿出现非 ascii 的字符串字面量.
>
> 关于解析和二进制位操作, 优先参考以及使用 cumath_util 提供的工具函数, 可以避免大部分不必要的坑.

> 本设计案引用的工程/脚本:
> https://github.com/HengShao233/smbx38a-text-util/tree/main (字符串导出工具)
> https://github.com/HengShao233/smbx38a-tescript-common-utils/blob/main/cumath_utils.smt (cumath_util 工具函数集)

## 使用

```tea
Dim name As String = "" ' 名称
Dim talkMsg2 As String = "" ' 对话消息 2

' 比方说我们要使用表 RoleInfo 的内容:
' 装载要加载的 id:
Call RoleInfo_SetId("toad a")
name = TXT(D(RoleInfo_GetName())) ' 获取了名称的 txt
talkMsg2 = TXT(D(RoleInfo_GetTalk(2))) ' 获取了对话 2 的 txt
```

> 所幸 tea script 姑且还存在可用的跨脚本调用方案 (export script), 否则就真的无计可施了......

## 后记

1. 虽然这里采取了字符串字面量的方案, 但一般情况下还是不太推荐在脚本中使用过长的字符串. 可以在实现时考虑是否有降低字符串长度的可能.
2. 关于 64 字符字符串转 int32 uuid 公式的生成, 是一个问题. 需要你提出较为优秀的解决方案. 公式不宜过于复杂 (会增加读表的复杂度), 但是又需要完全不碰撞 (因为我不想搞字符串比较, 如果这无法做到, 那只能在 _SeekDataMap 中补充比较 id 字符串的策略了 (只对可能碰撞的几个进行比较)). tea script 的数值运算, 小数是不可信的, 只能整数运算, 且尽量只做加减移位.
3. 我们不考虑合理 id 以外的 id 传入可能产生的意外 (比如传递了表中没有的 id, 我们的 uuid 生成机制是无法判别 id 是否在表中的, 我们尽量保证表内 id 不碰撞, 但是无法保证不与其它 id 碰撞). 这点需要用户进行自我约束.
4. 关于异常值 (数组越界/找不到的 id 等), 返回一个默认值, 提供 `<SheetName>_TryError()` 接口, 返回一个 error code (通常是在 get/setId 过程中产生的 error code, 每一次 get/setId 操作都会覆盖该方法的返回值). 以便 debug.
