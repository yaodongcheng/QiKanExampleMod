BannerlordTalk v1.0.3 正式包

本包精确目标：Mount & Blade II: Bannerlord 1.4.8
要求 MCM：Bannerlord.MBOptionScreen / MCM v5.12.2
可选 Living Commanders 接口边界：AFBattleCommanderBridge v1.6.5（可选）

安装：
1. 确认游戏版本与上面的精确目标一致。
2. 把本压缩包中的 BannerlordTalk 文件夹复制到游戏 Modules 目录。
3. 在启动器中勾选 BannerlordTalk，并确保 MCM 与原生模块在它之前加载。
4. 不要同时安装另一份 BannerlordTalk 版本包；两个包的 DLL 和 SubModule.xml 不同。

本包不包含 MCM、TaleWorlds、Newtonsoft.Json、Harmony、Living Commanders 或 Audit Probe 的 DLL，也不包含 API Key、MCM 配置、提示词配置和存档。KnowledgeLibraries 目录中的三份 TXT 是可选的游戏外常识库，导入方法见该目录说明。

压缩包根目录另附“通用主聊天提示词.txt”“安装与使用教程.txt”和“模组功能说明.txt”。通用提示词可按纯文本导入，不含指定角色设定；常识库应按当前世界三选一，在游戏外编辑后整库粘贴。安装或升级本包不会覆盖现有 MCM、API、Fish TTS、提示词或存档配置。

长文本安全：公共常识主页不加载整库正文，只显示固定统计摘要；分辨率和 UI 缩放未变时也不会重复刷新布局。导入弹窗只显示有界统计、诊断和短预览，完整剪贴板文本仍用于校验与确认；战役保存和“复制当前整库”仍使用全文。

Gemini 3.7 Flash：在 MCM 填写 Google 官方 OpenAI 兼容端点和精确模型名 `gemini-3.7-flash`，再选择低 / 中 / 高思考等级。此专用配置不会改动其他 OpenAI-compatible 模型的请求格式。

兼容性证据：本包已经针对上述精确参考程序集完成 Release 编译、离线验证和 ZIP 逐文件哈希核对。这不等于真实游戏加载、联网模型、TTS、存读档或长档稳定证明；尤其 1.3.15 包仍需在 1.3.15 实机单独验收。
