# JPG GPS（RAWMate 1.5.1）

历史正式 1.4.1 不含 GPS 编辑。历史 1.5 限 root\\jpg；1.5.1 扩展为当前 Collection Root 内任意子目录的正式 JPG/JPEG。来源可跨子目录，应用目标不要求与来源同一天；只调整 ValidateJpg 的集合成员谓词和提示，CheckPath 的本地/链接/硬链接/ADS 禁止、完整性证明、工具、事务均保持。ARW / XMP / 地图 / 推断没有 GPS 写入计划，也不匹配 RAW。历史 Phase 3B / 3B.1 失败证据保留。

## 工具与五字段

用户从设置按钮显式选择本机完整包内的 ExifTool 13.59 EXE；需保留同目录 exiftool_files 依赖，其他版本/包会被拒绝。可选依赖不影响挑片。没有 PATH 搜索、下载、安装或自动更新。所选目录完整 510 文件 manifest 按相对路径、Bytes、SHA256 排序冻结：
`94C298C2C60460507FFA7F3098F26B69E56490A08269AFB2AFE639F84048E750`。
EXE：`68C079C32FDAE0D6C7130E9A5FB73F8AC9DABDF9AB8DA312DA4F6C549D6D3385`。
exiftool_files：`08729004609E803644A560CA546EAC6E95F3CB0D5480FFCA0EF766F3B7589353`。
工具选择、RAWMate 启动恢复或重新选择时完整验证一次 manifest；当前 adapter session 持续持有所有依赖的 FileShare.Read 锁。每次运行前后检查目录成员、Windows file identity、锁状态及注入环境；异常阻断，必须重新选择并完整验证，不能降级。session 关闭/重新选择释放锁和 baseline 缓存。不同版本/包 fail closed。

启动命令：`<显式 exe> -config "" -charset filename=UTF8 -@ -`。
UTF8 标准输入逐行参数，禁用户 config、换行/NUL 注入、shell。读取双管道，有进程树超时/输出容量限制。

```text
-overwrite_original
-EXIF:GPSLatitude=<绝对十进制度>
-EXIF:GPSLatitudeRef=N或S
-EXIF:GPSLongitude=<绝对十进制度>
-EXIF:GPSLongitudeRef=E或W
-EXIF:GPSStatus#=A
--
<同目录 .rawmate-gps-GUID.JPG>
```

不复制 GPSDateStamp/GPSTimeStamp、海拔或整个 GPS IFD。已有 GPSVersionID/GPSMapDatum/GPSDifferential 严格保留。无 GPS IFD 时，ExifTool 必须创建标准 GPSVersionID=2 3 0 0；仅在结构证明新增 GPS IFD 且原来不存在时接受，不覆盖已有 VersionID。

## 来源与 UI

WIC 只读显示六位十进制度；无有效坐标显示 GPS：—。单张信息区提供直接设为来源/应用来源；当前来源显示不可执行的“当前来源”。网格只显示单一 Location Filled / Location Off / Location Ripple Filled 图标（来源优先），Tooltip 分别为已有 GPS 信息 / 无 GPS 信息 / 当前 GPS 来源。两种视图都显示可清除的轻量来源行；没有来源则隐藏。来源只保存在内存，绑定 Windows file identity + SHA256；切换根目录立即清空，同目录刷新保留有效来源。文件监视器检测修改/删除/重命名，1 秒 identity 轮询兜底；异常清空。无 GPS 无法设为来源，来源自身永远跳过，不写 settings。

ExifTool 只在左下角“设置 → GPS / 元数据”选择/更换，显示状态、13.59 版本和完整路径。照片右键只有原四项挑片操作、分隔线与两个 GPS 次级入口。未配置时，应用显示“取消 / 前往设置”，不影响挑片。

每次完整读取所选目标并分类为缺失、已有、来源、不可写/无效，再显示一次汇总确认及当前/来源坐标。默认仅缺失；覆盖必须主动勾选并再次确认。Ctrl/Shift 既有选择与空白区域框选都可用。批量编排逐张执行原单 JPG transaction；一张失败继续其他目标，已成功的不回滚。统一进度显示索引/总数/文件名，“取消剩余”只停止未开始目标，当前事务先安全结束。结果只汇总一次成功/跳过/失败，详情含原因和恢复材料。处理期间禁止第二次 GPS 操作及应用退出，不禁止正常浏览/挑片；根目录或来源改变阻断未提交事务。

## 事务与完整性

`GpsEditPlan` 恰好包含一个 JPG。拒绝 UNC、reparse/link、硬链接和 ADS 路径。先同目录 GUID .JPG 副本，校验与原件 SHA256 相同，再 ExifTool 写副本。写后：
- 五字段回读准确，Status=A，误差 <=1e-7 度。
- JPG Validate 必须为 0 Error / 0 Warning；不使用 -m、-F、ignoreMinorErrors，不过滤 warning。
- ExifTool ImageDataHash 不变；JPEG entropy、DQT/DHT/SOF/SOS、非 EXIF 段、EOI tail 完整 payload 保持。
- WIC 全帧逐行 CopyPixels，尺寸/编码器不变；嵌入 JPEG payload 实际解码。
- 非目标 EXIF 语义与 typed IFD 数据不变；已有 preview/thumbnail/ICC 原样保持。
- MakerNotes 默认逐字节相同。在 ExistingGpsIfdPath，Sony HiddenDataOffset 只有独立扫描真实 HiddenData 起点、合法范围、相同 length/SHA256、实际 delta 精确匹配后才允许对应四字节指针变化；其余 MakerNotes 字节严格相同。不使用固定 +97。新增目录路径的更严格完整结构证明见下节。
- MPImageStart 按实际 MPF 基址证明有效引用、长度、payload 和定位变化，MPF 其余字节一致；不扩展 Sony/private 白名单。
- 公共 IFD 指针重定位只能引用同一已证明不变的目录或 preview/thumbnail payload。未知结构 fail closed。

### No-GPS-IFD controlled relocation（1.5）

用户批准新增严格的 `CreateGpsIfdPath`，并非取消 MakerNotes hash 门禁。写前已有 GPS 目录时继续走原 `ExistingGpsIfdPath`，其 MakerNotes 证明主体保持原始字节等价；两条路径共同保留全部图像、解码、语义、MPF 与事务门禁。

新建路径仅接受单 EXIF APP1、little-endian classic TIFF、相同 TIFF base、写前无 GPSInfo/目录、写后只新增标准六项 GPS 目录。精确验证 VersionID=2 3 0 0、五字段计划值/Ref/Status=A、type/count、inline padding 和 next=0。其他 EXIF directory schema/order、引用及公共范围 ownership 必须被完整证明；额外 tag、未知 gap/padding/overlap 均拒绝。

Sony out-of-line entry 由 `count × type-size > 4` 动态识别，不按型号或固定 Tag ID/数量/位移放行。实际起点独立由目录表末端和累计 typed length 推导，offset 必须精确指向它；每项 payload 原始 bytes/SHA256 相同、delta 匹配，紧密顺序池完整耗尽 envelope。0x2044 单独为 `NESTED_PROVEN_RELOCATION`：LONG[2] 的 length 不变，仅 offset LONG 可以变化；独立 trailer locator 唯一、真实 HiddenData length/hash 不变、引用/delta 正确。整个 MakerNotes byte ownership map 的 unexplained count 必须为零。

生产测试入口 `tests/Run-CreateGpsIfdTests.ps1` 的 readonly 模式对 retained pair 只读验证，并执行带预期门禁类别的内存 mutation；transactions 模式只写 test-output 下全新 working copy，不提交旧失败 temp。这些测试不能替代完整发布回归或实际 UI 冒烟。历史 FAIL 不改写。

验证完成后重查来源/原件 SHA256 及 root/session epoch；持久 journal + File.Replace 原子替换与原件备份。再次核验指纹，异常回滚；失败副本保留且提示路径。上次恢复文件存在时阻断，不自动覆盖或清理它们。成功只清理本事务 disposable backup/journal。

## 挑片状态与缓存

点击“刷新目录”先后台递归重新枚举，再强制重读当前集合全部 JPG 的 GPS 显示 metadata，即使路径、length、mtime 和文件 ID 都不变；复用既有轻量 WIC 读取，不调用 ExifTool、GPS transaction、JPEG full decode 或 private/relocation proof。单 worker 后台逐张读取、16 张一批回 UI；scan / refresh generation 阻止旧任务回填。仅当前集合 GPS 显示缓存无条件失效，图像/缩略图仅按身份变化逐路径淘汰，不清除其他集合缓存或挑片状态。

刷新同时核验用户选过的当前 root 来源文件，重新建立原 SHA256/file identity 绑定。有效 GPS 保留来源并更新坐标；删除、不属于 root、无 GPS 或不可读则清除。若监视器已先清掉过期坐标，只保留路径候选供刷新重新读取，不保留旧坐标；显式“清除”或切 root 不会自动恢复。该候选仅当前会话有效，不能作为未核验的写入来源。同目录文件列表未变时不重建视图；筛选/排序、选择、当前照片、模式和缩放/导航器保持。

递归分类/取消分类期间暂停 watcher 并使旧 metadata generation 作废；只有成功移动且状态已持久化的 old→new 映射才迁移来源候选。结束后清除旧身份/坐标，通过原刷新管线重新读取新路径，不把旧 Source 对象直接改名复用。移动失败保留旧路径并重新核验。分类不执行 GPS 写入或 JPG 完整性门禁。

写入回调不调用决定、导航、RebuildUi、RefreshCurrentFolder、完成汇总或清理。P/J/R/X、计数、模式、筛选、排序、当前照片、缩放/滚动保持。仅 RemoveImageCachesForPaths(target)；per-path epoch 阻断旧 full-image completion，thumbnail/metadata 文件 stamp 防止旧 cache 回填。当前照片仍是目标时立即 WIC 重读 GPS，主图内容不重新编码、不重载缩放。

## 验证入口与限制

先 `Run-GpsModelTests.ps1`，再 `Run-CullModelTests.ps1`。真实 JPG 集成使用 `Run-JpgGpsIntegrationTests.ps1 -ExifToolPath <exe>`：从显式 BaselineRoot 冻结副本复制到新 test-output，测试无 GPS、覆盖、取消、故障；ARW 仅复制/哈希保护，永不写入。

仅当前 Sony ILCE-6700 JPG 已证明布局支持。其他 JPEG、其他相机/MakerNotes、big-endian TIFF、不一致或未知布局可能被拒绝，不能放宽门禁。不保证电源故障自动恢复；恢复材料人工核查。物理 DPI、清理 UI 未实测时逐项 NOT TESTED。任何未通过项见本轮验收报告；不代表正式发布或 ARW 支持。

## 性能与完整性

一次 `-X -b -all` 合并 GPS/metadata/strict Validate/ImageDataHash 与五类二进制块提取；不用 `-e`（它会抑制 Thumbnail/Preview 提取），不使用 stay_open。XML base64 块按原始字节 SHA256 比较，重复块必须一致；其他语义及 JPEG/private 动态证明保持。额外回归把合并提取与独立 `-b` 对照。

每个 adapter 最多保存 16 个 per-file baseline（GPS、尺寸、完整结构/语义/块哈希/全帧解码证明）。key 为完整路径，绑定 Windows volume/file ID、创建/写入时间、length、attributes、links 与完整 SHA256。复用前重新锁定文件并计算 fingerprint；新 ID、内容变动（即使 size/time 恢复）、锁失败或 session 异常均不复用。读取全程保持 FileShare.Read，写后临时文件永不复用 baseline。File.Replace 前仍重查原件及已验证临时文件 identity/完整 SHA256。

写后 Capture 已完整 WIC CopyPixels，因此仅去掉对相同内容紧接着再次调用的内置 ValidateParsing；自定义/故障注入验证 callback 不跳过。全帧尺寸上限、stride 溢出和像素格式检查保留；缩略图/MPImage2 的实际解码也保留。

事务 stopwatch 输出阶段 ms 和调用/manifest/decode/cache 计数；阶段嵌套，不可相加当作总耗时，ExifTool 内部 Validate 与 metadata 提取合并。结果 Diagnostics 供测试记录；本机测试/正式状态目录写最后一批 `gps-performance.log`（不含照片路径、文件名、坐标或照片内容）。性能事实见 `test-output/GPS-Performance-Report.md`；UX 候选验收见 `test-output/GPS-UX-Grid-Candidate-Report.md`。
