# RAWMate Windows DPI 审计

> 1.5.3发布收尾：用户已授权将本地正式版升级并发布1.5.3；隔离正式候选通过本轮最小smoke。1.5.2仅为未发布准备记录；下方原审计和验收hash作为历史保留，当前发布见docs/RELEASE_NOTES_1.5.3.md。

> 发布准备补充：2026-11-09用户接受G01验收并保留G03为已知限制，允许准备1.5.2。其他DPI暂缓，不阻塞该局部修复；未测环境状态不变。当前候选与准备结果见 [1.5.2发布报告](RAWMate-1.5.2-Release-Report.md)，下文hash保留对应原验收产物。

> 2026-11-09 Phase 2：G01 已在真实窗口复现并以局部布局修复；RAWMate HWND DPI=192、System DPI-aware。仅 D02（物理4K/200%）完成本轮验收，保留P2限制。详见文末运行时补充；以下11-08记录作为历史保留。

日期：2026-11-08，Asia/Hong_Kong。Phase 1 只读审计；用户随后选择“暂不操作界面，只完成审计与离线验证”。没有启动 RAWMate 窗口、修改显示设置、Manifest 或生产源码。

## 1. 当前项目与产物

| 项目 | 实际值 |
| --- | --- |
| 唯一源码 / 仓库 | C:\rawmate\RAWMate.cs / C:\rawmate |
| 分支 / HEAD | main / fc507cc619584e1d0934bc0821009ced106fbb1b |
| 开始时工作树 | 干净 |
| 正式版本 | FileVersion 1.5.1.0 / ProductVersion 1.5.1 / UI v1.5.1 |
| 正式 EXE SHA256 | 68E28B80E615F7B3EFC8F2489FBC95E697D3647725739777D2DC908D2AF7A5DC |
| RAWMate.cs SHA256 | 107C091402087569ACF8AE9454595C24C1815A5DB099E08EAA8E66C5680719C7 |
| 本轮测试构建 | BUILD_RAWMate-Test.cmd；仅重建 RAWMate-Test.exe |
| 测试 EXE SHA256 | A45BFBB603AFEC63FB9EA5B3056B0BA1ACCBAD49BED7BC146EB17FB83C5D523B |

UI 是单文件 C# WPF，使用 Grid、StackPanel、Viewbox、ScrollViewer、Canvas、Image 和代码构建的 ControlTemplate。引用 System.Windows.Forms 不代表主窗口为 WinForms。构建使用系统 Framework64\v4.0.30319\csc.exe，没有 /win32manifest 参数、项目 app.manifest 或 exe.config。

实读 CLR image version v4.0.30319；EXE 未声明 TargetFrameworkAttribute。本机 Framework registry Release=534296、Version=4.8.10008，编译器版本 14.8.10001.0。不能由编译器目录名推断程序已选择现代 WPF DPI 兼容开关。

## 2. 实际 EXE Manifest 与 DPI 模式

对正式/Test EXE 使用 LoadLibraryEx 的数据/资源映射读取 RT_MANIFEST/ID1，不执行 Program.Main。两者均为编译器默认 manifest：

```xml
<assemblyIdentity version="1.0.0.0" name="MyApplication.app"/>
<requestedExecutionLevel level="asInvoker" uiAccess="false"/>
```

没有 dpiAware、dpiAwareness、PerMonitor、PerMonitorV2 或 GDI scaling 元素。原始文件保存在 `test-output/display-compatibility-20261108/RAWMate.exe.manifest.xml` 和对应 Test 文件。

生产源码没有 SetProcessDPIAware、SetProcessDpiAwareness、SetProcessDpiAwarenessContext、SetThreadDpiAwarenessContext、WM_DPICHANGED 或 DpiChanged 的显式配置/处理。

本机 HKCU/HKLM AppCompatFlags\Layers 对正式/Test 两个规范路径均未查询到覆盖项；旁置的 exe.config / exe.manifest 均不存在。原始查询保存在 dpi-overrides.json。这不能替代实际进程 awareness 核验。

微软文档说明传统 WPF 默认 System DPI-aware，使用 DIP 自动缩放，跨不同 DPI 屏幕时可能由 OS 缩放。结合本项目配置，目前没有证据可宣称已显式启用 Per-Monitor V1/V2。**本轮没有启动正式/Test 进程，实际进程/窗口 awareness 与动态 DPI 行为仍待运行时核验；不把框架默认行为当作该 EXE 的实测结论。** [Microsoft：WPF DPI](https://learn.microsoft.com/en-us/windows/win32/hidpi/declaring-managed-apps-dpi-aware)，[Manifest DPI 元素](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests)。

## 3. 当前显示环境与证据限制

只读原生探针发现一个活动显示器：DELL U2720QM，3840×2160 / 60 Hz，work area 3840×2064，非远程桌面。WMI 同时存在 Zako Display Adapter 与 NVIDIA RTX 4070 Ti；有虚拟适配器不等于本轮已使用虚拟显示测试。项目旧上下文写 U2725QM，本轮硬件事实以当前 EDID/WMI 为准，未修改旧文档。

Windows 注册表原始值：ProductName=Windows 10 Enterprise、DisplayVersion=26H2、Build=26300.9711；保留原始信息，不单凭 ProductName 推断产品名称。

缩放证据需要区分：

- GetScaleFactorForMonitor 返回 180。
- Shell_TrayWnd 是 Per-Monitor V2，GetDpiForWindow 返回 192 DPI，换算为 200%；这是 shell 窗口读数，不是 RAWMate 的实际 DPI。
- 初版辅助探针在 PMv2 线程调用了 GetDpiForMonitor，该调用上下文不符合微软建议，其 192 读数**不作为有效 DPI 验收证据**；v2 输出已明确标记，并加入上述 shell HWND 独立读数。原始 v1/v2 JSON 均保留。
- 未打开 Windows 显示设置核对，未更改缩放；本轮不把当前环境直接记为 D02 PASS。 [Microsoft：GetDpiForMonitor 限制](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor)。

驱动模式列表包含 1366×768、1920×1080、2560×1440、2560×1600、3840×2160。未列出 3440×1440。列出模式只表示驱动报告，不证明该模式可稳定显示或已完成测试。

## 4. 当前适配机制

| 区域 | 实现与证据 |
| --- | --- |
| 主窗口 | RAWMate.cs:1581–1651。默认 1380×860 DIP；首选最小 1000×640 DIP。初始化按 SystemParameters.WorkArea 减 8 DIP 限制 Width/Height 和 MinWidth/MinHeight。工作区小于首选下限时可放低最小值。 |
| 原生窗口 | CenterScreen、原生标题栏、默认可调整大小；没有固定 Left/Top 位置。UseLayoutRounding 与 SnapsToDevicePixels 开启。 |
| 左侧栏 | RAWMate.cs:1693–1738。固定 320 DIP 列；内部宽 280 DIP，Viewbox Uniform / DownOnly，垂直靠顶；版本和设置在独立 Auto 底行。低高度会缩小侧栏内容，并非新增滚动条。 |
| 右侧工作区 | Star 主区 + Auto 照片信息底区；Gallery 与单张是独立 Grid 布局。 |
| Grid 顶栏 | RAWMate.cs:2042–2063。标题与右侧 Auto 列；右侧所有摘要/筛选/排序/缩略图/视图按钮在同一横向 StackPanel，不能自动换行或优先压缩摘要。 |
| 网格卡片 | WrapPanel 排列；宽度按缩略图档位确定，预览高度由宽度计算；文件名单行 ellipsis。GPS 为 20 DIP 容器 / 14 DIP 单图标。 |
| 单张预览 | RAWMate.cs:2095–2170、2354–2440、4715–4765。Star 预览区、固定大小导航器叠层、缩放/胶片条靠底；实际可用区参与适应倍率和导航器 viewport 计算。 |
| 胶片条/导航器 | 胶片条约 450 DIP、MaxWidth 460；导航器外框 208×154 / 图像框 190×116 DIP。属于固定 DIP，不是固定物理像素。 |
| 底部 GPS | RAWMate.cs:2752–2818。Auto 当前 GPS + divider + Star 来源 + Auto 操作；来源文件名/坐标可省略，操作尽量保留；第一行 EXIF NoWrap/ellipsis。 |
| 字体/按钮 | Microsoft YaHei UI；多数按钮固定 DIP 高度与模板内边距，部分宽度固定。正常 WPF DPI 缩放可放大，但有限空间下仍可能裁切/挤压。 |
| 自定义弹窗 | RAWMate.cs:5467–5548。CenterOwner、SizeToContent.Height、固定宽 400/420/520/560 DIP；MaxHeight=max(300, 主工作区高度-60)。部分详情有内部 ScrollViewer，外层不一定可滚动。 |
| 标准弹窗 | 分类、配对/清理确认和部分错误使用 MessageBox/系统文件夹或文件选择器；未发现依赖固定屏幕 X/Y 的定位。 |

## 5. 风险与源码可证实差异

1. **窄 Grid 顶栏长摘要**：固定横排总宽度可能超过主区；离线实测见兼容性报告 G01。最小修复候选是摘要使用可压缩/ellipsis 的 Star 区、按钮单独 Auto 区，保留事件与业务逻辑。当前未实现。
2. **多屏/动态 DPI**：没有显式 per-monitor 配置或处理，WorkArea 取全局/主工作区；不能证明跨屏后字体、图片和弹窗仍正确。必须实机核验，不能直接换 PMv2 Manifest。
3. **低高度弹窗**：固定 StackPanel 内容可能高于 MaxHeight，外层缺少滚动能力，底部确认区存在裁切风险；尚无实际弹窗/DPI 复现，不能列为已确认 P1。
4. **窗口位置/尺寸恢复未实现**：构造时 CenterScreen；LoadSettings/SaveSettings（约 RAWMate.cs:2992–3059）只存目录、视图、当前照片、缩放、Grid 档位、排序和筛选，不存 Left/Top/Width/Height/WindowState/RestoreBounds。因此用户提出的窗口位置恢复验收不能由现有实现保证。属于已有行为差异，不能顺手扩展为新功能。
5. **窄工作区字体大小**：侧栏 Viewbox 可缩小文字/命中区，几何未裁切不等于实际清晰可读；需物理缩放与操作验收。

## 6. 审计结论

已有 DIP/Star/Auto/Viewbox/工作区约束，支持继续进行有边界的显示验收；没有证据支持“任意分辨率”或“已支持 PMv2”。本轮保持源码、Manifest、正式 EXE 和已发布基线不变，不实施修复。当前交付为只读审计与离线布局基线，真实分辨率/DPI/动态切换验证仍 BLOCKED。

离线方法、模型图、问题证据、回归和后续验收矩阵见 [DISPLAY-COMPATIBILITY-REPORT.md](C:/rawmate/DISPLAY-COMPATIBILITY-REPORT.md)。

## Phase 2 运行时补充（2026-11-09）

继续已有审计，没有重新执行Phase 1，没有改变Manifest、DPI配置或最小窗口尺寸。工作分支codex/g01-header-layout，HEAD仍为fc507cc619584e1d0934bc0821009ced106fbb1b，未提交。正式v1.5.1历史基线保持不变。

### 真实环境与自身HWND

- PHYSICAL：单台Dell U2720QM。Windows显示设置只读截图明确显示3840×2160、200%；当前显卡模式读取3840×2160 / 60 Hz，没有修改系统设置。
- 标准C:\rawmate\RAWMate-Test.exe实际PID=35324、HWND=1969864。GetDpiForWindow=192；GetProcessDpiAwareness=1、HRESULT=0；GetAwarenessFromDpiAwarenessContext=1。真实MainWindow测试宿主另外验证SystemAware=true，PerMonitorV1/V2=false；WPF CompositionTarget.TransformToDevice=2。
- 这是真实RAWMate窗口数据，不是Explorer/Shell DPI。没有PM动态切换、跨不同DPI显示器迁移的实测证据，不能宣称Per-Monitor支持。

| 状态 | 外框物理px | 客户区物理px | WPF外框DIP |
| --- | --- | --- | --- |
| 普通 | 2760×1720 | 2734×1649 | 1380×860 |
| 最小 | 2000×1280 | 1974×1209 | 1000×640；客户区987×604.5 |
| 最大化 | 3866×2090，x/y=-13含非客户边缘 | 3840×2019 | 以客户区为布局依据 |

证据根目录test-output/display-phase2-20261109。standard-runtime-normal.json、standard-runtime-minimum.json、standard-runtime-maximized-final.json直接记录标准产物。外部Read-WindowRuntime.ps1只在测量线程临时启用PMv2坐标上下文以避免坐标虚拟化，finally恢复，不改变目标进程/系统配置。首次未切换测量上下文的半尺寸读数保存在standard-runtime-normal-virtualized-diagnostic.json，仅作诊断，不作为物理尺寸。windows-display-readonly.png保存显示设置原值。

### G01：实机确认及局部修复

测试宿主G01LiveFixture.cs链接生产源码，调用真实MainWindow构造、HWND与Application.Run。F9只替换galleryCaption.Text，不改变文件数、tileFiles、决定或业务数据；F8记录真实边界和命中。宿主与最终标准Test构建分开，标准Test不含F8/F9入口。

同一最小客户区987 DIP、五位数摘要下：

| 项目 | 修复前 | 修复后 |
| --- | --- | --- |
| Grid按钮 | x=951–983，可命中 | x=903–935，可命中 |
| Single按钮 | x=991–1023，完全越界，centerHit=false | x=943–975，完整可命中 |
| 摘要 | 原宽327，无省略，Auto栈挤出窗口 | 独立Star列，NoWrap / CharacterEllipsis |
| 实际操作 | Single不可访问 | 点击Single进入照片、点击Grid返回成功 |

原始JSON：before-live/005-stress-toggle.json、after-live/004-stress-toggle.json。真实截图before-live/min-stress.png、after-live/min-stress.png。普通3/3摘要在最小窗口也挤占“图库”标题。G01正式确认为P1；标题挤占为同根因P2。测试候选已修复，正式已发布EXE尚未包含本轮修复。

唯一生产变更在BuildGridView：Auto标题 / Star受限摘要 / Auto原五按钮。只给摘要省略，按钮工厂、尺寸、顺序、文案、图标和事件不变；不扩大最小窗口、不改Manifest，不改GPS、分类或清理业务。

### 验证与限制

原21离线模型重跑，按钮越界/裁剪findings=0（历史2个压力失败保留）。新增366个宽度/摘要边界模型、7692条断言通过；UX/Grid78、Cull61通过。离屏渲染仍不作为物理DPI证据。

当前4K/200%标准Test实机检查了最大化/还原/最小化、最小窗口、Grid/Single、横竖图、缩放/平移/导航器、顶栏菜单、GPS来源及确认取消、分类/取消分类确认取消、配对报告、清理确认取消、设置。最小窗口第一行EXIF长文本末尾裁切记为G03/P2，未扩大本轮修改范围。G02位置恢复明确排除。

用户选择暂不调整显示设置，D01和D03–D10继续BLOCKED；可选模式、动态DPI及跨屏迁移NOT TESTED。仅D02完成当前物理设备上的本轮UI验收，保留G03限制。

### 产物与保护

- 新Test SHA256：023F844992F7EDE9AF18283B75DC27680A2AD8AF7F979D57D02657EB8AD9068E。
- 正式EXE SHA256仍为68E28B80E615F7B3EFC8F2489FBC95E697D3647725739777D2DC908D2AF7A5DC。
- 新源码SHA256：272F7397A47415BB3DD1CA5F03AE05A47B630EBB3F0270DB03614637C2C61DFB。
- 6/6隔离照片长度/SHA不变，24/24冻结release fixture一致，无新增temp/journal/backup/XMP。
- error.log全程SHA未变。正常退出更新共享派生日期缓存，已另存本轮副本并恢复原缓存；正式状态7/7、Test状态4/4恢复测试前SHA，没有恢复或掩盖异常日志。
- 测试窗口已关闭；无真实GPS写入、分类移动、回收删除；无commit/merge/push/tag/Release。
