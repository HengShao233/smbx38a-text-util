#### 字体图集快捷生成工具 v2.2.1

该工具可用于快速建立字体图集：

- `./Release/FontAtlasGenerator.exe` 为工具主体
- `.cfg.json` 为工具的配置文件样例，使用时配置好 `.cfg.json`，运行同文件夹下的工具程序 `*.exe`，可以得到该配置所指示的运行效果。也可以将其他 `*.json` 拖放到 `*.exe` 上，拖放的新配置会被覆盖到 `.cfg.json` 中。
- 除了拖放配置，**还可以拖放 `*.smt` 脚本文件到该工具程序上以运行**，工具程序会将脚本文件中以类似 `$"字符串"` 格式定义的字符串替换成重新编码后的字符串 `TXT(D("#K*B=[,Fe "))`。
- 拖放文件是可以多选的。
- 如果没有拖放任何文件直接打开工具程序，且 `scan-folder-script` 参数为 `true` 该程序会扫描同级文件夹内的所有 `*.smt` 文件并执行替换。


配置文件格式如下：

```json
{
    // 字体图集生成器配置
    // 字体文件
    "font-path": "../../../px12.ttf",
    // 图集输出文件
    "output-path": "./",
    // 字体的 em size
    "em-size": 9,
    // 字体图块大小
    "grid-size": 12,
    // 字体图块偏移
    "grid-offset": [-2, -4],
    // 画布大小
    "canvas-size": 2048,
    // 字符集
    "char-set": "",
    // 是否指认字符集为补充字符
    "addition-char-set": true,
    // 需要替换字串的脚本
    "script": [],
    // 是否生成 util 类
    "output-util": true,
    // 特殊偏移 (为某个特定的字符指定偏移量)
    "spc-grid-offset": [
        // ["g", 0, -5],
        // ["穷", -1, 0]
    ],
    // 字符 y 轴偏移 (部分字符需要 y 轴超框, 这里提供一个接口处理)
    "char-offset-y": [
      ["g", 3],
      ["q", 6],
      ["p", 6],
      ["j", 4]
    ],
    // 字符大小 (只能调整宽度)
    "char-size": [
      ["j", 6],
      ["t", 9]
    ],
    // 是否扫描文件夹下的脚本并替换字符串
    "scan-folder-script":false,
    // 字符渲染类型
    "font-render-type":1
}
```



关于 font-render-type 参数（来自 [TextRenderingHint Enum (System.Drawing.Text) | Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/System.Drawing.Text.TextRenderingHint?view=net-8.0)）：

| AntiAlias                | 4    | Each character is drawn using its antialiased glyph bitmap without hinting. Better quality due to antialiasing. Stem width differences may be noticeable because hinting is turned off. |
| ------------------------ | ---- | ------------------------------------------------------------ |
| AntiAliasGridFit         | 3    | Each character is drawn using its antialiased glyph bitmap with hinting. Much better quality due to antialiasing, but at a higher performance cost. |
| ClearTypeGridFit         | 5    | Each character is drawn using its glyph ClearType bitmap with hinting. The highest quality setting. Used to take advantage of ClearType font features. |
| SingleBitPerPixel        | 2    | Each character is drawn using its glyph bitmap. Hinting is not used. |
| SingleBitPerPixelGridFit | 1    | Each character is drawn using its glyph bitmap. Hinting is used to improve character appearance on stems and curvature. |
| SystemDefault            | 0    | Each character is drawn using its glyph bitmap, with the system default rendering hint. The text will be drawn using whatever font-smoothing settings the user has selected for the system. |

> 具体用例见 ../Text/test.lvl



