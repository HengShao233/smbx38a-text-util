' 该脚本内容由配置表导出, 禁止修改
' -------- 接口列表
' 真实定义放在最底下
' Export Script Npc_SetId(id As String)
' Export Script Npc_GetId(Return String)
' Export Script Npc_GetName(Return String)
' Export Script Npc_GetHp(Return Long)
' Export Script Npc_GetSpeed(Return Long)
' Export Script Npc_GetBoss(Return Long)
' Export Script Npc_GetDrops(i As Long, Return Long)
' Export Script Npc_GetTags(i As Long, Return String)
' Export Script Npc_GetPartner(Return String)
' Export Script Npc_GetDesc(Return String)
' Export Script Npc_TryError(Return Long)

' 依赖(由调用方自行 import, 本脚本不内联/不 include):
'   CUMath_Decode(str, start, length, 92)  <- cumath_utils.smt
'   TXT(D(payload))                        <- TxtDecoder.smt


' -------- 静态数据块
Dim __row_map As String = " #! ! +! l -!!V 0!#B a!$. g!$x!$!%d!1!&N!T!':"
Dim __data_map_00 As String = "    !  '    (  '    .  !    /  #    1  !    2  9    M  9    t  &    y  1   !+  (   !2  .   !?  !   !@  !   !A  !   !B      !B  1   !c      !c  0   !r  &   !w  &   !|  !   !}  #   #   !   #!  )   #+  1   #F  '   #L  1   #]  '   #d  '   #j  !   #k  #   #m  !   #n  1   $!  1   $<  &   $A  2   $R  &   $W  -   $e  !   $f  #   $h  !   $i  9   %&  1   %A  &   %F  2   %W  *   %b  +   %l  !   %m  #   %o  !   %p  1   &$  1   &A  &   &F  4   &Y  '   &a  '   &g  !   &h  #   &j  !   &k  )   &t  1   '0  &   '5  1   'E  '   'K  '   'Q  !   'R  #   'T  !   'U      'U  1   's      's  1   (%  $   ((  $   (+  !   (,  #   (.  !   (/      (/  1   (G      (G  /"
Dim __data_chunk_0000 As String = "bowserBowserI('#    J  !    K  !    L  !5I^    g  %    k  %    o  &bossfireheavykoopa``Q#`Turtle bosspiranhaPiranha Plant'     !R  &   !W  +plantstationary``P#`Bite plantspinySpiny#%A    #*  !%   #;  '   #A  &groundspikylakitu``Q#`Spike shellgoombaGoomba#&I    #~  !   $   !#%   $2  '   $8  %groundweakkoopa``R#`Goomba enemykoopaKoopa Troopa%$m    %#  !   %$  !   %%  !')+   %6  '   %<  &groundshellgoopa``R#`Shell kickerhammerbroHammer Bro'%u    &!  !   &#  !+-   &4  '   &:  (groundthrowerkoopa``T#`Hammer throwerlakituLakitu%*k    &s  !/   '&  $   ')  (airthrowerspiny``Q#`Cloud riderthwompThwomp!!)    'g  &   'l  (stonecrusher``Q#`Crush blockbooBoo!#e    (?  &   (D  $ghostshy``O#`Shy ghost"

' -------- 动态数据块
Dim __curr_id As String = ""
Dim __curr_data_map_id As Integer = 0
Dim __curr_data_map_offset As Integer = 0
Dim __field_index As Long = 0
Dim __field_array_index As Integer = 0
Dim __curr_data_map_offset_real As Long = 0
Dim __field_index_real As Long = 0
Dim __data_offset As Long = 0
Dim __data_length As Long = 0
Dim __data_chunk As Integer = 0
Dim __target_data As String = ""
Dim __temp_str As String = ""
Dim __last_error As Long = 0
Dim __orig_data_map_id As Integer = 0
Dim __orig_data_map_offset As Integer = 0
Dim __tmp_long As Long = 0
Dim __tmp_long2 As Long = 0
Dim __tmp_int As Integer = 0
Dim __tmp_str As String = ""

Script __TableExport_Npc_GetDataMapLen(idx As Integer, Return Long)
    If idx = 1 Then Return 648
    Return -1
End Script

Script __TableExport_Npc_LoadDataMap(idx As Integer, offset As Integer, Return Integer)
    __temp_str = ""
    If idx = 1 Then __temp_str = Mid(__data_map_00, offset, 8)
    If "" = __temp_str Then Return -1
    __data_chunk = CUMath_Decode(__temp_str, 1, 2, 92)
    __data_offset = CUMath_Decode(__temp_str, 3, 3, 92)
    __data_length = CUMath_Decode(__temp_str, 6, 3, 92)
    Return 0
End Script

Script __TableExport_Npc_LoadChunkFragment(checkArray As Integer, Return Integer)
    __target_data = ""
    If __data_chunk = 0 Then __target_data = Mid(__data_chunk_0000, __data_offset, __data_length)
    If "" = __target_data Then Return -1
    If checkArray <> 0 Then
        If __field_array_index >= 0 Then
            __tmp_int = 1 + (__field_array_index - 1) * 8
            If __tmp_int + 7 > Len(__target_data) Then Return -1
            __data_chunk = CUMath_Decode(__target_data, __tmp_int, 2, 92)
            __data_offset = CUMath_Decode(__target_data, __tmp_int + 2, 3, 92)
            __data_length = CUMath_Decode(__target_data, __tmp_int + 5, 3, 92)
            Return __TableExport_Npc_LoadChunkFragment(0)
        End If
    End If
    Return 0
End Script

Script __TableExport_Npc_FindRow(uuid As Long, Return Integer)
    If uuid > 62 Then
        If uuid > 108 Then
            If uuid = 143 Then Return 9
        Else
            If uuid = 68 Then Return 6
            If uuid = 95 Then Return 7
            If uuid = 108 Then Return 8
        End If
    Else
        If uuid > 12 Then
            If uuid = 15 Then Return 4
            If uuid = 62 Then Return 5
        Else
            If uuid = 2 Then Return 1
            If uuid = 10 Then Return 2
            If uuid = 12 Then Return 3
        End If
    End If
    Return 0
End Script

Script __TableExport_Npc_SeekDataMap(Return Integer)
    __tmp_long = 2
    For __tmp_int = 1 To Len(__curr_id) Step 1
        __tmp_long = (__tmp_long * 33 + Asc(Mid(__curr_id, __tmp_int, 1))) Mod 149
    Next
    __tmp_int = __TableExport_Npc_FindRow(__tmp_long)
    If __tmp_int <= 0 Then Return -1
    __tmp_long = (__tmp_int - 1) * 5 + 1
    __curr_data_map_id = CUMath_Decode(__row_map, __tmp_long + 2, 1, 92)
    __curr_data_map_offset = CUMath_Decode(__row_map, __tmp_long + 3, 2, 92)
    Return 0
End Script

Script __TableExport_Npc_SeekChunk(Return Integer)
    __curr_data_map_offset_real = __curr_data_map_offset
    __field_index_real = __field_index
    __tmp_long = __TableExport_Npc_GetDataMapLen(__curr_data_map_id)
    If __tmp_long < 0 Then Return -1
    If __curr_data_map_offset_real + __field_index_real * 8 + 7 > __tmp_long Then
        __tmp_long2 = (__tmp_long - __curr_data_map_offset_real + 1) \ 8
        If __tmp_long2 > __field_index_real Then __tmp_long2 = __field_index_real
        __field_index_real = __field_index_real - __tmp_long2
        __curr_data_map_id = __curr_data_map_id + 1
        __curr_data_map_offset_real = 1
        __tmp_long = __TableExport_Npc_GetDataMapLen(__curr_data_map_id)
        If __tmp_long < 0 Then Return -1
    End If
    __tmp_long = __curr_data_map_offset_real + __field_index_real * 8
    Return __TableExport_Npc_LoadDataMap(__curr_data_map_id, __tmp_long)
End Script

' -------- 字段接口实现
Export Script Npc_SetId(id As String)
    __curr_id = id
    __last_error = 0
    If __TableExport_Npc_SeekDataMap() <> 0 Then
        __curr_data_map_id = -1
        __last_error = -1
    End If
    __orig_data_map_id = __curr_data_map_id
    __orig_data_map_offset = __curr_data_map_offset
End Script

Export Script Npc_GetId(Return String)
    __curr_data_map_id = __orig_data_map_id
    __curr_data_map_offset = __orig_data_map_offset
    __field_index = 0
    __field_array_index = -1
    If __TableExport_Npc_SeekChunk() <> 0 Then
        __last_error = -2
        Return ""
    End If
    If __TableExport_Npc_LoadChunkFragment(0) <> 0 Then
        __last_error = -3
        Return ""
    End If
    Return __target_data & ""
End Script

Export Script Npc_GetName(Return String)
    __curr_data_map_id = __orig_data_map_id
    __curr_data_map_offset = __orig_data_map_offset
    __field_index = 1
    __field_array_index = -1
    If __TableExport_Npc_SeekChunk() <> 0 Then
        __last_error = -2
        Return ""
    End If
    If __TableExport_Npc_LoadChunkFragment(0) <> 0 Then
        __last_error = -3
        Return ""
    End If
    Return __target_data & ""
End Script

Export Script Npc_GetHp(Return Long)
    __curr_data_map_id = __orig_data_map_id
    __curr_data_map_offset = __orig_data_map_offset
    __field_index = 2
    __field_array_index = -1
    If __TableExport_Npc_SeekChunk() <> 0 Then
        __last_error = -2
        Return 0
    End If
    If __TableExport_Npc_LoadChunkFragment(0) <> 0 Then
        __last_error = -3
        Return 0
    End If
    __tmp_long = CUMath_Decode(__target_data, 1, Len(__target_data), 92)
    __tmp_long2 = __tmp_long And 1
    __tmp_long = __tmp_long \ 2
    If __tmp_long2 = 1 Then __tmp_long = -(__tmp_long + 1)
    Return __tmp_long
End Script

Export Script Npc_GetSpeed(Return Long)
    __curr_data_map_id = __orig_data_map_id
    __curr_data_map_offset = __orig_data_map_offset
    __field_index = 3
    __field_array_index = -1
    If __TableExport_Npc_SeekChunk() <> 0 Then
        __last_error = -2
        Return 0
    End If
    If __TableExport_Npc_LoadChunkFragment(0) <> 0 Then
        __last_error = -3
        Return 0
    End If
    __tmp_long = CUMath_Decode(__target_data, 1, Len(__target_data), 92)
    __tmp_long2 = __tmp_long And 1
    __tmp_long = __tmp_long \ 2
    If __tmp_long2 = 1 Then __tmp_long = -(__tmp_long + 1)
    Return __tmp_long
End Script

Export Script Npc_GetBoss(Return Long)
    __curr_data_map_id = __orig_data_map_id
    __curr_data_map_offset = __orig_data_map_offset
    __field_index = 4
    __field_array_index = -1
    If __TableExport_Npc_SeekChunk() <> 0 Then
        __last_error = -2
        Return 0
    End If
    If __TableExport_Npc_LoadChunkFragment(0) <> 0 Then
        __last_error = -3
        Return 0
    End If
    __tmp_long = CUMath_Decode(__target_data, 1, Len(__target_data), 92)
    __tmp_long2 = __tmp_long And 1
    __tmp_long = __tmp_long \ 2
    If __tmp_long2 = 1 Then __tmp_long = -(__tmp_long + 1)
    Return __tmp_long
End Script

Export Script Npc_GetDrops(i As Long, Return Long)
    __curr_data_map_id = __orig_data_map_id
    __curr_data_map_offset = __orig_data_map_offset
    __field_index = 5
    __field_array_index = i
    If __TableExport_Npc_SeekChunk() <> 0 Then
        __last_error = -2
        Return 0
    End If
    If __TableExport_Npc_LoadChunkFragment(1) <> 0 Then
        __last_error = -3
        Return 0
    End If
    __tmp_long = CUMath_Decode(__target_data, 1, Len(__target_data), 92)
    __tmp_long2 = __tmp_long And 1
    __tmp_long = __tmp_long \ 2
    If __tmp_long2 = 1 Then __tmp_long = -(__tmp_long + 1)
    Return __tmp_long
End Script

Export Script Npc_GetDropsLen(Return Long)
    __curr_data_map_id = __orig_data_map_id
    __curr_data_map_offset = __orig_data_map_offset
    __field_index = 5
    __field_array_index = -1
    If __TableExport_Npc_SeekChunk() <> 0 Then Return 0
    If __TableExport_Npc_LoadChunkFragment(0) <> 0 Then Return 0
    Return Len(__target_data) \ 8
End Script

Export Script Npc_GetTags(i As Long, Return String)
    __curr_data_map_id = __orig_data_map_id
    __curr_data_map_offset = __orig_data_map_offset
    __field_index = 6
    __field_array_index = i
    If __TableExport_Npc_SeekChunk() <> 0 Then
        __last_error = -2
        Return ""
    End If
    If __TableExport_Npc_LoadChunkFragment(1) <> 0 Then
        __last_error = -3
        Return ""
    End If
    Return __target_data & ""
End Script

Export Script Npc_GetTagsLen(Return Long)
    __curr_data_map_id = __orig_data_map_id
    __curr_data_map_offset = __orig_data_map_offset
    __field_index = 6
    __field_array_index = -1
    If __TableExport_Npc_SeekChunk() <> 0 Then Return 0
    If __TableExport_Npc_LoadChunkFragment(0) <> 0 Then Return 0
    Return Len(__target_data) \ 8
End Script

Export Script Npc_GetPartner(Return String)
    __curr_data_map_id = __orig_data_map_id
    __curr_data_map_offset = __orig_data_map_offset
    __field_index = 7
    __field_array_index = -1
    If __TableExport_Npc_SeekChunk() <> 0 Then
        __last_error = -2
        Return ""
    End If
    If __TableExport_Npc_LoadChunkFragment(0) <> 0 Then
        __last_error = -3
        Return ""
    End If
    Return __target_data & ""
End Script

Export Script Npc_GetDesc(Return String)
    __curr_data_map_id = __orig_data_map_id
    __curr_data_map_offset = __orig_data_map_offset
    __field_index = 8
    __field_array_index = -1
    If __TableExport_Npc_SeekChunk() <> 0 Then
        __last_error = -2
        Return ""
    End If
    If __TableExport_Npc_LoadChunkFragment(0) <> 0 Then
        __last_error = -3
        Return ""
    End If
    Return __target_data & ""
End Script

Export Script Npc_TryError(Return Long)
    Return __last_error
End Script
