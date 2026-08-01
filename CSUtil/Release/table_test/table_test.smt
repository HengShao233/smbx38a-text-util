' ==========================================================
' TableExporter 运行时回归测试 (TeaScript 侧)
'
' 用法: 与生成的 npc_table.smt 一同放入关卡, 调用 TableTestRunAll()。
' 结果通过 Debug 输出; 失败项会逐条列出期望值与实际值。
'
' 依赖: npc_table.smt (由 TableExporter 从 npc.csv 导出)
'       cumath_utils.smt, TxtDecoder.smt (由用户自行 import)
'
' NPC 表字段: id(s), name(s), hp(i), speed(i), boss(i),
'             drops(i[|]), tags(s[;]), partner(s), desc(s)
' ==========================================================

Dim TableTestPassed As Long = 0
Dim TableTestFailed As Long = 0

' ---------- 入口 ----------

Export Script TableTestRunAll()
    TableTestPassed = 0
    TableTestFailed = 0

    Call Debug("========================================")
    Call Debug("TableExporter runtime test")
    Call Debug("========================================")

    Call TableTestScalars()
    Call TableTestArrays()
    Call TableTestMiss()

    Call Debug("----------------------------------------")
    Call Debug("passed " & TableTestPassed & ", failed " & TableTestFailed)

    If TableTestFailed = 0 Then
        Call Debug("*** ALL TESTS PASSED ***")
    Else
        Call Debug("*** SOME TESTS FAILED ***")
    End If
End Script

' ---------- 测试用例 ----------

Script TableTestScalars()
    Call Debug("- scalars")

    Call Npc_SetId("goomba")
    Call TableTestEqStr("goomba.name", "Goomba", Npc_GetName())
    Call TableTestEqNum("goomba.hp", 1, Npc_GetHp())
    Call TableTestEqNum("goomba.speed", 250, Npc_GetSpeed())
    Call TableTestEqNum("goomba.boss=0", 0, Npc_GetBoss())
    Call TableTestEqStr("goomba.partner", "koopa", Npc_GetPartner())
    ' desc 是 text 类型, GetDesc 返回 payload 原文, 需 TXT(D()) 解码
    ' 这里只验证非空
    Call TableTestTrue("goomba.desc non-empty", Len(Npc_GetDesc()) > 0)

    Call Npc_SetId("bowser")
    Call TableTestEqStr("bowser.name", "Bowser", Npc_GetName())
    Call TableTestEqNum("bowser.hp", 20, Npc_GetHp())
    Call TableTestEqNum("bowser.speed", 325, Npc_GetSpeed())
    Call TableTestEqNum("bowser.boss=1", 1, Npc_GetBoss())
    Call TableTestEqStr("bowser.partner", "koopa", Npc_GetPartner())

    Call Npc_SetId("boo")
    Call TableTestEqNum("boo.hp=-1", -1, Npc_GetHp())
    Call TableTestEqNum("boo.speed", 125, Npc_GetSpeed())
    Call TableTestEqNum("boo.boss=0", 0, Npc_GetBoss())

    Call Npc_SetId("piranha")
    Call TableTestEqNum("piranha.speed=0", 0, Npc_GetSpeed())
    Call TableTestEqStr("piranha.partner empty", "", Npc_GetPartner())

    Call Npc_SetId("thwomp")
    Call TableTestEqNum("thwomp.hp=-1", -1, Npc_GetHp())
    Call TableTestEqNum("thwomp.speed", 50, Npc_GetSpeed())
End Script

Script TableTestArrays()
    Call Debug("- arrays")

    Call Npc_SetId("koopa")
    Call TableTestEqNum("koopa.drops len", 3, Npc_GetDropsLen())
    Call TableTestEqNum("koopa.drops[1]", 3, Npc_GetDrops(1))
    Call TableTestEqNum("koopa.drops[2]", 4, Npc_GetDrops(2))
    Call TableTestEqNum("koopa.drops[3]", 5, Npc_GetDrops(3))
    Call TableTestEqNum("koopa.tags len", 2, Npc_GetTagsLen())
    Call TableTestEqStr("koopa.tags[1]", "ground", Npc_GetTags(1))
    Call TableTestEqStr("koopa.tags[2]", "shell", Npc_GetTags(2))

    Call Npc_SetId("bowser")
    Call TableTestEqNum("bowser.drops len", 3, Npc_GetDropsLen())
    Call TableTestEqNum("bowser.drops[3]", 30, Npc_GetDrops(3))
    Call TableTestEqNum("bowser.tags len", 3, Npc_GetTagsLen())
    Call TableTestEqStr("bowser.tags[3]", "heavy", Npc_GetTags(3))

    Call Npc_SetId("piranha")
    Call TableTestEqNum("piranha.drops empty", 0, Npc_GetDropsLen())
    Call TableTestEqNum("piranha.tags len", 2, Npc_GetTagsLen())

    Call TableTestEqNum("drops oob = 0", 0, Npc_GetDrops(99))
    Call TableTestEqStr("tags oob = empty", "", Npc_GetTags(99))
End Script

Script TableTestMiss()
    Call Debug("- miss")
    Call Npc_SetId("nonexistent_id")
    Call TableTestEqNum("miss err != 0", -1, Npc_TryError())
End Script

' ---------- 断言原语 ----------

Script TableTestOk(name As String)
    TableTestPassed = TableTestPassed + 1
    Call Debug("  [ok]   " & name)
End Script

Script TableTestFail(name As String, expected As String, actual As String)
    TableTestFailed = TableTestFailed + 1
    Call Debug("  [FAIL] " & name & " expected=" & expected & " actual=" & actual)
End Script

Script TableTestEqStr(name As String, expected As String, actual As String)
    If expected = actual Then
        Call TableTestOk(name)
    Else
        Call TableTestFail(name, expected, actual)
    End If
End Script

Script TableTestEqNum(name As String, expected As Long, actual As Long)
    If expected = actual Then
        Call TableTestOk(name)
    Else
        Call TableTestFail(name, "" & expected, "" & actual)
    End If
End Script
