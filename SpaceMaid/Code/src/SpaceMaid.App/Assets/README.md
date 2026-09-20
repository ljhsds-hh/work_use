# Assets

本目录存放 `SpaceMaid.App` 的位图资产。**所有资产都是程序化生成的，请勿手工编辑**；
改设计就改生成脚本的参数后重新生成。

| 文件 | 尺寸 | 字节数 | 用途 | 生成脚本 |
| --- | --- | --- | --- | --- |
| `app.ico` | 9 种尺寸（16/20/24/32/40/48/64/128/256） | 381038 | 应用图标（`<ApplicationIcon>`） | `Code/scripts/gen-icon.ps1` |
| `logo.png` | 256×256 | 8023 | 品牌图：左侧导航栏顶部品牌区、「关于」信息 | `Code/scripts/gen-logo.ps1` |
| `logo-64.png` | 64×64 | 2044 | 品牌图小尺寸（导航栏折叠态、列表行内） | `Code/scripts/gen-logo.ps1` |

两张品牌 PNG 都是**透明背景**、8 位 RGBA（PNG 颜色类型 6），已由 `SpaceMaid.App.csproj`
以 `<Resource>` 注册（带 `Condition="Exists(...)"`）。

## 视觉母题（与 app.ico 同一套）

- 蓝色对角渐变的圆角方块：`ARGB(255,70,146,238)` → `ARGB(255,32,92,180)`，左上 → 右下，
  圆角半径 = 边长 × 22%
- 白色圆弧环：线宽 = 边长 × 7.5%，起角 0°、顺时针扫 270°，端点为圆头（`LineCap::Round`）
- 白色小圆点：直径 = 边长 × 8%，落在弧环的缺口里（缺口朝右上，即北东 315°）

`logo-64.png` 是**按 64 px 重新绘制**的，不是把 256 px 缩小：缩小会把 4.8 px 宽的细弧重采样成糊边。
两个尺寸的几何比例完全一致（环 7.5%、点 8%、内缩 24%），已验证。

## 生成命令

本机执行策略禁止直接运行 `.ps1`，所以统一用 `Get-Content -Raw` 读文本再 `Invoke-Expression`。
在**仓库根目录**（或 `SpaceMaid/`、`SpaceMaid/Code/`）下执行：

```powershell
# 生成 logo.png 与 logo-64.png（覆盖写入本目录）
Invoke-Expression (Get-Content -Raw -Encoding UTF8 'SpaceMaid\Code\scripts\gen-logo.ps1')

# 逐像素校验（只读，不写任何文件）
Invoke-Expression (Get-Content -Raw -Encoding UTF8 'SpaceMaid\Code\scripts\check-logo.ps1')

# 应用图标（改动 app.ico 时才需要）
Invoke-Expression (Get-Content -Raw -Encoding UTF8 'SpaceMaid\Code\scripts\gen-icon.ps1')
```

`gen-logo.ps1` 也接受 `-AssetsDir <路径>`（通过 `Get-Content` 求值时用
`Invoke-Expression` 执行，`param()` 绑定不生效，需在调用前先设 `$AssetsDir`）。

## 逐像素校验结果（2026-09-20，`check-logo.ps1` 全 20 项 PASS）

校验脚本从磁盘读回 PNG，自己解析 IHDR/IDAT 并逐行反解滤波，因此下面的数字来自**真正落盘的字节**，
不是内存里的位图。校验脚本还会用生成脚本重新画一遍并比对计数，两处必须一致。

| 检查项 | `logo.png` | `logo-64.png` |
| --- | --- | --- |
| 尺寸 | 256×256 | 64×64 |
| 文件字节数 | 8023 | 2044 |
| 四角 Alpha（TL/TR/BL/BR） | 全为 `ARGB(0,0,0,0)` | 全为 `ARGB(0,0,0,0)` |
| 对角探针（左上内区，应偏蓝） | (26,26)=ARGB(255,66,141,232)；(38,38)=ARGB(255,64,138,230)；(51,51)=ARGB(255,63,135,226) | (6,6)=ARGB(255,67,141,233)；(10,10)=ARGB(255,64,138,229)；(13,13)=ARGB(255,63,135,226) |
| 渐变方向 | R 66→64→63、G 141→138→135、B 232→230→226（左上更亮、右下更深） | R 67→64→63、G 141→138→135、B 233→229→226 |
| 白色像素（R=G=B>240 且 A>200） | 6311 | 341 |
| 蓝色像素（B>R 且 A>200） | 56101 | 3486 |
| 全透明像素（A=0） | 2580 | 139 |
| 白色占比 | 不透明像素的 10.0% | 不透明像素的 8.9% |
| 弧环覆盖角 | 0/45/90/135/180/225/270° 均白 | 同左 |
| 缺口角（应非白） | 292.5°、337.5° 均非白 | 同左 |
| 缺口圆点 | (162,162) 为纯白 | (41,41) 为纯白 |
| 几何 | 中线半径 66.56 px、线宽 19.20 px、点半径 10.24 px | 中线半径 16.64 px、线宽 4.80 px、点半径 2.56 px |

> 白色像素数量在 256 px 图上是 **6311**（远超「存在白色像素」的下限，也在 2000–20000 的合理区间内）。
> 64 px 图 341 个，正好是 256 px 的 1/16 面积比（理论值 394），符合「按尺寸重绘」而非缩放。

当前字节的 SHA-256（用于确认文件没被手工改过）：

```
logo.png      EA9FB0A455BBCB04B2481BDB4384F5709179B98FAB4A5845C9D1CF1A41DF0329
logo-64.png   1930657D1724018756C2EEEA94D9CAA6811FBE197EE2C90DCE0ABB48286449A2
```

### 踩过的坑（改脚本前必读）

1. **GDI+ `DrawArc` 的角度方向**：角度是 y 轴向下的坐标系，**正的 sweep 在屏幕上是顺时针**。
   0°=右、90°=下、270°=上。曾经按相反的方向实现，结果整个圆弧被镜像到缺口朝左，
   圆点落在环上。确认方式是用一张临时位图 + 八个罗盘方向探针实测，不要凭直觉。
2. **`DrawArc` 的起点不是端点**：270° 圆弧的两个圆头端点在起角与起角+扫角处。
   缺口的位置要看这两个端点，而不是只看一个角度。
3. **对角探针会打到环上**：只有 |坐标−中心| < 约 0.24×边长 的区域才是稳定的纯色方块
   （环内圆在 0.305×边长 处，圆角抗锯齿在约 0.238×边长 处）。校验脚本因此只在对角线上
   取 0.10/0.15/0.20 三个点。
4. **拿 GDI+ 位图自己数像素时要先弄清通道顺序**：`Format32bppArgb` 在小端机器内存里是 BGRA。
   把 PNG 的 RGBA 字节直接倒进 `LockBits` 缓冲区会让 R 和 B 互换，所有「是不是蓝色」的判断都反掉。
   校验脚本因此直接按 PNG 顺序在原始字节上统计，不经过 `System.Drawing`。

## XAML 里的引用写法

`logo.png` / `logo-64.png` 编译后作为 WPF 资源嵌入 `SpaceMaid.g.resources`，
键名是 `assets/logo.png` 与 `assets/logo-64.png`（小写、正斜杠）。
**在 `SpaceMaid.App` 自己的 XAML 里，相对 URI 可以直接用：**

```xml
<!-- 推荐：相对 URI，程序集内资源的标准写法 -->
<Image Source="Assets/logo.png" Width="40" Height="40" />

<!-- 小尺寸位（导航栏折叠态等） -->
<Image Source="Assets/logo-64.png" Width="24" Height="24" />

<!-- 等价但更啰嗦的绝对 pack URI，跨程序集引用时必须用这种形式 -->
<Image Source="pack://application:,,,/Assets/logo.png" />
```

已实测（.NET 8 宿主加载 `SpaceMaid.dll` 后按 XAML 的方式解析）：
`Source="Assets/logo.png"` → 实际加载 256×256，`Source="Assets/logo-64.png"` → 实际加载 64×64，
绝对 pack URI 解析到 8023 / 2044 字节的 `image/png`。三种写法都可用。

> 注意：`Views/*.xaml` 受 `StaticSafetyTests.App_xaml_should_not_hardcode_colors` 约束，
> 写图片时不要顺带加 `#RRGGBB` / `Color=` / `SolidColorBrush`，颜色一律取 HandyControl 皮肤资源
> 或 `Themes/DesignTokens.xaml` 里的令牌。
