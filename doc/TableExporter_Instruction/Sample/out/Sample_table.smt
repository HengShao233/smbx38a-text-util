' ============================================================
' 整合配置表脚本 (由 TableExporter 批处理生成, 禁止手动修改)
' 目标脚本名: MyTables
' 包含表数量: 1
' ============================================================

' ------------------------------------------------------------
' 表(脚本): Sample    所属关卡: Sample
' 源文件: [Sample]-[Sample].xlsx
' 函数前缀: Sample_    变量前缀: Sample__
' ------------------------------------------------------------

' 该脚本内容由配置表导出, 禁止修改
' -------- 接口列表
' 真实定义放在最底下
' Export Script Sample_SetId(id As String) 通过 id 获取行数据
' Export Script Sample_SetIndex(i As Integer) 通过行号获取行数据
' Export Script Sample_Index(Return Integer) 获取当前活跃行的行号（由 SetId/SetIndex 决定）
' Export Script Sample_RowCount(Return Integer) 行数 6
' Export Script Sample_GetId(Return String)
' Export Script Sample_GetMessage(Return String)
' Export Script Sample_GetMsgboxpos(i As Long, Return Long)
' Export Script Sample_GetAvatar(i As Long, Return Long)
' Export Script Sample_GetAvapos(i As Long, Return Long)
' Export Script Sample_GetNext(Return String)
' Export Script Sample_GetSound(i As Long, Return String)
' Export Script Sample_GetMsgboxstyle(i As Long, Return Long)
' Export Script Sample_TryError(Return Long)
' 依赖(由调用方自行 import, 本脚本不内联/不 include):
'   CUMath_Decode(str, start, length, 92)  <- cumath_utils.smt
'   TXT(D(payload))                        <- TxtDecoder.smt
' -------- 静态数据块
' -------- 动态数据块
' -------- 字段接口实现




Dim Sample__row_map As String = "P! !Q! dR!!FS!#*T!#lU!$N"
Dim Sample__data_map_00 As String = "    !  %    &  P    U  A   !   I   !S  A   !}  %   ##  1   #G      !}  %   #G 'M    U  A   )u  I   !S  A   *I      ##  1   #G      *I  %   *M '3    U  A   0a  I   !S  A   15  %   ##  1   #G      15  %   19 'H    U  A   )u  I   !S  A   7b  %   ##  1   #G      7b  %   7f #k    U  A   0a  I   !S  A   *I      ##  1   #G      :P  %   :T  k    U  A   )u  I   !S  A   *I      ##  1   #G   "
Dim Sample__data_chunk_0000 As String = "ppl1``Q# !E# !M# !Q# !U#hgT}T}`/#000000+>-2+s+8+~+4    w  #    y  #    {  #    }  ##1$M-)     !I  #   !K  #   !M  #   !O  #   !Q  #'c#k  !E!E   !u  #   !w  #   !y  #   !{  #0^$M!D!Eppl2   #3  $   #6  2104Popollensound.wav`` & !E# !M#M,K3q3'+y/k#U[($P} !Q# !S# !M#B=6>JG-)^+   !U# !Y# ![#>(f#y/ !e#!! !Y# !i#Y<#&,4W) !e#!! !Y# !q#O-_Eo:01 !e#!! !Y# !y#R6)'^E}5 !e#!! !Y# !#$7&n.u..- !e#!! !Y# !+$z)]'j#51 !e#!! !Y# !3$51_){Am# !e#!! !Y# !;$m#|,^$R) !e#!! !Y# !C$R).AR)65 !e#!! !Y# !K$65C$d4Y) !e#!! !Y#   !E#Z(b} !e#!! !Y#Y*b} !e#!! !Y#:+ !M#  P}Q} !Q# !S# !S$ !M#$0B%O} !U$!! !M#t+*$K3'+b'1* !Y#. . .  !Q# !M#'+b'1*^+y/ !Y#`......`` !U$!! !M#$7}8 !Y#`...... `` !Q# !Y# !Y$ !^$'+b'1*^+$7}8y/((P}Q}`/#000000+>-2)?)p+~+4)>/#FF0000+>-8/#FF8800/#FFE000/#00FF00/#00FFFF/#4444FF/#8800FF/#FF00FF/#FF0088/#AA00BB)~+>-f+!+2+s+8   *?  #   *A  #   *C  #   *E  #   *G  #'c!E  !E!Eppl3``j% !E# !M# !Q#d3}@&&K3 !U#Y<#&01 !E#y/$7}8 !_#`...... `` !b# !d# !M# !U#C$O'O} !f#!! !M#1/1/O} !f#!! !M#55f+o#X$K&f+E#1+O} !f#!! !M#$Ao&^+   !j# !_# !n#>(f#y/ !v#!! !_# !z#Y<#&,4W) !v#!! !_# !$$O-_Eo:01 !v#!! !_# !,$R6)'^E}5 !v#!! !_# !4$7&n.u..- !v#!! !_# !<$z)]'j#51 !v#!! !_# !D$51_){Am# !v#!! !_# !L$m#|,^$R) !v#!! !_# !T$R).AR)65 !v#!! !_# !]$65C$d4Y) !v#!! !_#   !f$Z(b} !v#!! !_#Y*b} !v#!! !_#:+ !M# !n$  y/_,*&A&'+N6t+s#';q&Q}`/#AAAAAA+>-2+s-40#0000000)>)?)s+>-f+~+4/#FF0000+>-8/#FF8800/#FFE000/#00FF00/#00FFFF/#4444FF/#8800FF/#FF00FF/#FF0088/#AA00BB/#000000)~   1+  #   1-  #   1/  #   11  #   13  #'c%1  !E!Eppl4``{% !E# !M#o&t+A&%B^+ !Q#`...... `` !S# !M#k$v#A-O#k$v#Y<#&k$v#C'v:k$v#n=e= !Q#`...... `` !S# !U#^({6K3O} !Y#!! !Q# !^#Y*B,Q%y5Q} !S# !c# !e# !M# !g#<7K3<4:1t+U#O} !Y#!! !M#d:(?x+X'^+   !i# !Q# !m#>(f#y/ !u#!! !Q# !y#Y<#&,4W) !u#!! !Q# !#$O-_Eo:01 !u#!! !Q# !+$R6)'^E}5 !u#!! !Q# !3$7&n.u..- !u#!! !Q# !;$z)]'j#51 !u#!! !Q# !C$51_){Am# !u#!! !Q# !K$m#|,^$R) !u#!! !Q# !S$R).AR)65 !u#!! !Q# ![$65C$d4Y) !u#!! !Q#   !E#Z(b} !u#!! !Q#Y*b} !u#!! !Q#:+ !M# !E# !e$  5+N}`/#000000+>-2)>)?+s+8+>-v+!+4)p)s)!+~+4/#FF0000+>-8/#FF8800/#FFE000/#00FF00/#00FFFF/#4444FF/#8800FF/#FF00FF/#FF0088/#AA00BB)~ppl5``_$ !E# !M#38O} !Q#!! !M#u(v#>(f#y/Y<#&01l.t?N} !U#!! !M# !Y#h#M, !U#   !M#a?WLy/Y<#&01k#=2 ![# !Y#    M, !U#   !M#;965y/Y<#&01Y*:+ ![# !Y#    ^+ !U#   !M#.284y/Y<#&01$7}8 ![# !Y# !M#$+Z' !M#`......`` ![# !^#$+Z'1-{4 !a#`....../#000000+>-2+>-f+>-v)n)?)p)>ppl6``_# !E# !M#1-{4Y<#&n$y/t+Q} !Q# !S# !U#R-(0'+Q}Q}`!/#000000+s+8)?)p+~+4"

Dim Sample__curr_data_map_id As Integer = 0
Dim Sample__curr_data_map_offset As Integer = 0
Dim Sample__field_index As Long = 0
Dim Sample__field_array_index As Integer = 0
Dim Sample__curr_data_map_offset_real As Long = 0
Dim Sample__field_index_real As Long = 0
Dim Sample__data_offset As Long = 0
Dim Sample__data_length As Long = 0
Dim Sample__data_chunk As Integer = 0
Dim Sample__target_data As String = ""
Dim Sample__temp_str As String = ""
Dim Sample__last_error As Long = 0
Dim Sample__curr_row_index As Integer = 0
Dim Sample__orig_data_map_id As Integer = 0
Dim Sample__orig_data_map_offset As Integer = 0
Dim Sample__tmp_long As Long = 0
Dim Sample__tmp_long2 As Long = 0
Dim Sample__tmp_int As Integer = 0
Dim Sample__tmp_str As String = ""

Script __TableExport_Sample_GetDataMapLen(idx As Integer, Return Long)
    If idx = 1 Then Return 384
    Return -1
End Script

Script __TableExport_Sample_LoadDataMap(idx As Integer, offset As Integer, Return Integer)
    Sample__temp_str = ""
    If idx = 1 Then Sample__temp_str = Mid(Sample__data_map_00, offset, 8)
    If "" = Sample__temp_str Then Return -1
    Sample__data_chunk = CUMath_Decode(Sample__temp_str, 1, 2, 92)
    Sample__data_offset = CUMath_Decode(Sample__temp_str, 3, 3, 92)
    Sample__data_length = CUMath_Decode(Sample__temp_str, 6, 3, 92)
    Return 0
End Script

Script __TableExport_Sample_LoadChunkFragment(checkArray As Integer, Return Integer)
    Sample__target_data = ""
    If Sample__data_chunk = 0 Then Sample__target_data = Mid(Sample__data_chunk_0000, Sample__data_offset, Sample__data_length)
    If "" = Sample__target_data Then Return -1
    If checkArray <> 0 Then
        If Sample__field_array_index >= 0 Then
            Sample__tmp_int = 1 + (Sample__field_array_index - 1) * 8
            If Sample__tmp_int + 7 > Len(Sample__target_data) Then Return -1
            Sample__data_chunk = CUMath_Decode(Sample__target_data, Sample__tmp_int, 2, 92)
            Sample__data_offset = CUMath_Decode(Sample__target_data, Sample__tmp_int + 2, 3, 92)
            Sample__data_length = CUMath_Decode(Sample__target_data, Sample__tmp_int + 5, 3, 92)
            Return __TableExport_Sample_LoadChunkFragment(0)
        End If
    End If
    Return 0
End Script

Script __TableExport_Sample_FindRow(uuid As Long, Return Integer)
    If uuid > 50 Then
        If uuid = 51 Then Return 5
        If uuid = 52 Then Return 6
    Else
        If uuid > 49 Then
            If uuid = 50 Then Return 4
        Else
            If uuid = 47 Then Return 1
            If uuid = 48 Then Return 2
            If uuid = 49 Then Return 3
        End If
    End If
    Return 0
End Script

Export Script Sample_RowCount(Return Integer)
    Return 6
End Script

Script __TableExport_Sample_SeekDataMap(Return Integer)
    Sample__tmp_long = 0
    For Sample__tmp_int = 1 To Len(Sample__tmp_str) Step 1
        Sample__tmp_long = (Sample__tmp_long * 31 + Asc(Mid(Sample__tmp_str, Sample__tmp_int, 1))) Mod 97
    Next
    Sample__tmp_int = __TableExport_Sample_FindRow(Sample__tmp_long)
    If Sample__tmp_int <= 0 Then Return -1
    Sample__tmp_long = (Sample__tmp_int - 1) * 4 + 1
    Sample__curr_data_map_id = CUMath_Decode(Sample__row_map, Sample__tmp_long + 1, 1, 92)
    Sample__curr_data_map_offset = CUMath_Decode(Sample__row_map, Sample__tmp_long + 2, 2, 92)
    Return 0
End Script

Script __TableExport_Sample_SeekDataMapByIndex(index As Integer, Return Integer)
    Sample__tmp_int = index
    If Sample__tmp_int <= 0 Or Sample__tmp_int > 6 Then Return -1
    Sample__tmp_long = (Sample__tmp_int - 1) * 4 + 1
    Sample__curr_data_map_id = CUMath_Decode(Sample__row_map, Sample__tmp_long + 1, 1, 92)
    Sample__curr_data_map_offset = CUMath_Decode(Sample__row_map, Sample__tmp_long + 2, 2, 92)
    Return 0
End Script

Script __TableExport_Sample_SeekChunk(Return Integer)
    Sample__curr_data_map_offset_real = Sample__curr_data_map_offset
    Sample__field_index_real = Sample__field_index
    Sample__tmp_long = __TableExport_Sample_GetDataMapLen(Sample__curr_data_map_id)
    If Sample__tmp_long < 0 Then Return -1
    If Sample__curr_data_map_offset_real + Sample__field_index_real * 8 + 7 > Sample__tmp_long Then
        Sample__tmp_long2 = (Sample__tmp_long - Sample__curr_data_map_offset_real + 1) \ 8
        If Sample__tmp_long2 > Sample__field_index_real Then Sample__tmp_long2 = Sample__field_index_real
        Sample__field_index_real = Sample__field_index_real - Sample__tmp_long2
        Sample__curr_data_map_id = Sample__curr_data_map_id + 1
        Sample__curr_data_map_offset_real = 1
        Sample__tmp_long = __TableExport_Sample_GetDataMapLen(Sample__curr_data_map_id)
        If Sample__tmp_long < 0 Then Return -1
    End If
    Sample__tmp_long = Sample__curr_data_map_offset_real + Sample__field_index_real * 8
    Return __TableExport_Sample_LoadDataMap(Sample__curr_data_map_id, Sample__tmp_long)
End Script

Export Script Sample_SetId(id As String)
    Sample__tmp_str = id
    Sample__last_error = 0
    If __TableExport_Sample_SeekDataMap() <> 0 Then
        Sample__curr_data_map_id = -1
        Sample__last_error = -1
        Sample__curr_row_index = -1
    Else
        Sample__curr_row_index = Sample__tmp_int
    End If
    Sample__orig_data_map_id = Sample__curr_data_map_id
    Sample__orig_data_map_offset = Sample__curr_data_map_offset
End Script

Export Script Sample_SetIndex(id As Integer)
    Sample__tmp_str = ""
    Sample__last_error = 0
    If __TableExport_Sample_SeekDataMapByIndex(id) <> 0 Then
        Sample__curr_data_map_id = -1
        Sample__last_error = -1
        Sample__curr_row_index = -1
    Else
        Sample__curr_row_index = id
    End If
    Sample__orig_data_map_id = Sample__curr_data_map_id
    Sample__orig_data_map_offset = Sample__curr_data_map_offset
End Script

Export Script Sample_Index(Return Integer)
    Return Sample__curr_row_index
End Script

Export Script Sample_GetId(Return String)
    Sample__curr_data_map_id = Sample__orig_data_map_id
    Sample__curr_data_map_offset = Sample__orig_data_map_offset
    Sample__field_index = 0
    Sample__field_array_index = -1
    If __TableExport_Sample_SeekChunk() <> 0 Then
        Sample__last_error = -2
        Return ""
    End If
    If __TableExport_Sample_LoadChunkFragment(0) <> 0 Then
        Sample__last_error = -3
        Return ""
    End If
    Return Sample__target_data & ""
End Script

Export Script Sample_GetMessage(Return String)
    Sample__curr_data_map_id = Sample__orig_data_map_id
    Sample__curr_data_map_offset = Sample__orig_data_map_offset
    Sample__field_index = 1
    Sample__field_array_index = -1
    If __TableExport_Sample_SeekChunk() <> 0 Then
        Sample__last_error = -2
        Return ""
    End If
    If __TableExport_Sample_LoadChunkFragment(0) <> 0 Then
        Sample__last_error = -3
        Return ""
    End If
    Return Sample__target_data & ""
End Script

Export Script Sample_GetMsgboxpos(i As Long, Return Long)
    Sample__curr_data_map_id = Sample__orig_data_map_id
    Sample__curr_data_map_offset = Sample__orig_data_map_offset
    Sample__field_index = 2
    Sample__field_array_index = i
    If __TableExport_Sample_SeekChunk() <> 0 Then
        Sample__last_error = -2
        Return 0
    End If
    If __TableExport_Sample_LoadChunkFragment(1) <> 0 Then
        Sample__last_error = -3
        Return 0
    End If
    Sample__tmp_long = CUMath_Decode(Sample__target_data, 1, Len(Sample__target_data), 92)
    Sample__tmp_long2 = Sample__tmp_long And 1
    Sample__tmp_long = Sample__tmp_long \ 2
    If Sample__tmp_long2 = 1 Then Sample__tmp_long = -(Sample__tmp_long + 1)
    Return Sample__tmp_long
End Script

Export Script Sample_GetMsgboxposLen(Return Long)
    Sample__curr_data_map_id = Sample__orig_data_map_id
    Sample__curr_data_map_offset = Sample__orig_data_map_offset
    Sample__field_index = 2
    Sample__field_array_index = -1
    If __TableExport_Sample_SeekChunk() <> 0 Then Return 0
    If __TableExport_Sample_LoadChunkFragment(0) <> 0 Then Return 0
    Return Len(Sample__target_data) \ 8
End Script

Export Script Sample_GetAvatar(i As Long, Return Long)
    Sample__curr_data_map_id = Sample__orig_data_map_id
    Sample__curr_data_map_offset = Sample__orig_data_map_offset
    Sample__field_index = 3
    Sample__field_array_index = i
    If __TableExport_Sample_SeekChunk() <> 0 Then
        Sample__last_error = -2
        Return 0
    End If
    If __TableExport_Sample_LoadChunkFragment(1) <> 0 Then
        Sample__last_error = -3
        Return 0
    End If
    Sample__tmp_long = CUMath_Decode(Sample__target_data, 1, Len(Sample__target_data), 92)
    Sample__tmp_long2 = Sample__tmp_long And 1
    Sample__tmp_long = Sample__tmp_long \ 2
    If Sample__tmp_long2 = 1 Then Sample__tmp_long = -(Sample__tmp_long + 1)
    Return Sample__tmp_long
End Script

Export Script Sample_GetAvatarLen(Return Long)
    Sample__curr_data_map_id = Sample__orig_data_map_id
    Sample__curr_data_map_offset = Sample__orig_data_map_offset
    Sample__field_index = 3
    Sample__field_array_index = -1
    If __TableExport_Sample_SeekChunk() <> 0 Then Return 0
    If __TableExport_Sample_LoadChunkFragment(0) <> 0 Then Return 0
    Return Len(Sample__target_data) \ 8
End Script

Export Script Sample_GetAvapos(i As Long, Return Long)
    Sample__curr_data_map_id = Sample__orig_data_map_id
    Sample__curr_data_map_offset = Sample__orig_data_map_offset
    Sample__field_index = 4
    Sample__field_array_index = i
    If __TableExport_Sample_SeekChunk() <> 0 Then
        Sample__last_error = -2
        Return 0
    End If
    If __TableExport_Sample_LoadChunkFragment(1) <> 0 Then
        Sample__last_error = -3
        Return 0
    End If
    Sample__tmp_long = CUMath_Decode(Sample__target_data, 1, Len(Sample__target_data), 92)
    Sample__tmp_long2 = Sample__tmp_long And 1
    Sample__tmp_long = Sample__tmp_long \ 2
    If Sample__tmp_long2 = 1 Then Sample__tmp_long = -(Sample__tmp_long + 1)
    Return Sample__tmp_long
End Script

Export Script Sample_GetAvaposLen(Return Long)
    Sample__curr_data_map_id = Sample__orig_data_map_id
    Sample__curr_data_map_offset = Sample__orig_data_map_offset
    Sample__field_index = 4
    Sample__field_array_index = -1
    If __TableExport_Sample_SeekChunk() <> 0 Then Return 0
    If __TableExport_Sample_LoadChunkFragment(0) <> 0 Then Return 0
    Return Len(Sample__target_data) \ 8
End Script

Export Script Sample_GetNext(Return String)
    Sample__curr_data_map_id = Sample__orig_data_map_id
    Sample__curr_data_map_offset = Sample__orig_data_map_offset
    Sample__field_index = 5
    Sample__field_array_index = -1
    If __TableExport_Sample_SeekChunk() <> 0 Then
        Sample__last_error = -2
        Return ""
    End If
    If __TableExport_Sample_LoadChunkFragment(0) <> 0 Then
        Sample__last_error = -3
        Return ""
    End If
    Return Sample__target_data & ""
End Script

Export Script Sample_GetSound(i As Long, Return String)
    Sample__curr_data_map_id = Sample__orig_data_map_id
    Sample__curr_data_map_offset = Sample__orig_data_map_offset
    Sample__field_index = 6
    Sample__field_array_index = i
    If __TableExport_Sample_SeekChunk() <> 0 Then
        Sample__last_error = -2
        Return ""
    End If
    If __TableExport_Sample_LoadChunkFragment(1) <> 0 Then
        Sample__last_error = -3
        Return ""
    End If
    Return Sample__target_data & ""
End Script

Export Script Sample_GetSoundLen(Return Long)
    Sample__curr_data_map_id = Sample__orig_data_map_id
    Sample__curr_data_map_offset = Sample__orig_data_map_offset
    Sample__field_index = 6
    Sample__field_array_index = -1
    If __TableExport_Sample_SeekChunk() <> 0 Then Return 0
    If __TableExport_Sample_LoadChunkFragment(0) <> 0 Then Return 0
    Return Len(Sample__target_data) \ 8
End Script

Export Script Sample_GetMsgboxstyle(i As Long, Return Long)
    Sample__curr_data_map_id = Sample__orig_data_map_id
    Sample__curr_data_map_offset = Sample__orig_data_map_offset
    Sample__field_index = 7
    Sample__field_array_index = i
    If __TableExport_Sample_SeekChunk() <> 0 Then
        Sample__last_error = -2
        Return 0
    End If
    If __TableExport_Sample_LoadChunkFragment(1) <> 0 Then
        Sample__last_error = -3
        Return 0
    End If
    Sample__tmp_long = CUMath_Decode(Sample__target_data, 1, Len(Sample__target_data), 92)
    Sample__tmp_long2 = Sample__tmp_long And 1
    Sample__tmp_long = Sample__tmp_long \ 2
    If Sample__tmp_long2 = 1 Then Sample__tmp_long = -(Sample__tmp_long + 1)
    Return Sample__tmp_long
End Script

Export Script Sample_GetMsgboxstyleLen(Return Long)
    Sample__curr_data_map_id = Sample__orig_data_map_id
    Sample__curr_data_map_offset = Sample__orig_data_map_offset
    Sample__field_index = 7
    Sample__field_array_index = -1
    If __TableExport_Sample_SeekChunk() <> 0 Then Return 0
    If __TableExport_Sample_LoadChunkFragment(0) <> 0 Then Return 0
    Return Len(Sample__target_data) \ 8
End Script

Export Script Sample_TryError(Return Long)
    Return Sample__last_error
End Script


