# -*- coding: utf-8 -*-
"""一次性脚本：修正 installer.iss 的词库/词典打包行 + 预配置用户 settings.json"""
import json
import re

ISS = r"E:\Workspace_AI\ZCODE\demo_0820\QuoteWidget\tools\release\installer.iss"
SETTINGS = None  # 后面单独处理

# ---- installer.iss ----
c = open(ISS, encoding="utf-8").read()
line_exe = 'Source: "..\\..\\bin\\Release\\net10.0-windows\\win-x64\\publish\\QuoteWidget.exe"; DestDir: "{app}"; Flags: ignoreversion'
addition = (
    line_exe + "\n"
    + 'Source: "..\\..\\bin\\Release\\net10.0-windows\\win-x64\\词库\\*"; DestDir: "{app}\\词库"; Flags: recursesubdirs createallsubdirs uninsneveruninstall\n'
    + 'Source: "..\\..\\bin\\Release\\net10.0-windows\\win-x64\\词典\\*"; DestDir: "{app}\\词典"; Flags: recursesubdirs createallsubdirs uninsneveruninstall'
)
if "DestDir: \"{app}\\词库\"" not in c:
    c = c.replace(line_exe, addition, 1)
c = c.replace("; 词库与词典不打包：首次运行自动生成内置词库，避免覆盖用户已编辑的内容",
              "; 词库与词典随包分发（uninsneveruninstall：卸载时保留用户编辑的内容）")
open(ISS, "w", encoding="utf-8").write(c)
print("iss ok:", "词库" in c and "词典" in c)

# ---- 用户 settings.json 预配置 ----
import os
settings_path = os.path.join(
    os.environ["APPDATA"], "QuoteWidget", "settings.json")
try:
    data = json.load(open(settings_path, encoding="utf-8"))
    data["TranslateEngine"] = "Zhipu"
    import os as _os
    _key_file = _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "zhipu.key")
    if _os.path.exists(_key_file):
        data["ZhipuApiKey"] = open(_key_file, encoding="utf-8").read().strip()
    json.dump(data, open(settings_path, "w", encoding="utf-8"),
              ensure_ascii=False, indent=2)
    print("settings.json configured")
except FileNotFoundError:
    print("settings.json 不存在（首次启动会写入默认值，默认值已内置 Key）")
