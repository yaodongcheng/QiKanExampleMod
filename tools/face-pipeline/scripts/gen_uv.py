# -*- coding: utf-8 -*-
# gen_uv.py — gpt-image-2 直接生成"人脸 UV 展开贴图"(用户方案: UV 平面展开, 免 3DMM 链)
# 用法: python gen_uv.py <outdir> <outname> <prompt文件或prompt字符串>
import base64, json, os, sys, time, urllib.request

CFG = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\ShokuhoTaikouExpansionPack\ArtSource\api_config.json"
DEFAULT_PROMPT = """[核心主题]：专业 3D 女性面部角色头部 UV 纹理贴图、漫反射贴图、高细节皮肤的漫射贴图。
[布局与格式]：平面纹理投影，头部展开布局，对称面部对齐，高像素密度，8K 分辨率，中性棕肤色背景，紧闭的眼睛，紧闭的嘴巴，没有头发，没有化妆。宽高比1:1
[光照与 PBR]：无阴影，无高光，愉悦感，均匀照明，PBR 颜色标准，哑光效果，纯漫反射色。
[皮肤细节]：逼真的微孔，皮肤瑕疵，细微的雀斑，细纹，自然的肤色变化，次表面散射基础色，高保真纹理，扫描级细节，超写实。"""

def gen(api, prompt, size="1024x1024"):
    body = json.dumps({"model": api["model"], "prompt": prompt, "n": 1, "size": size}).encode()
    for attempt in range(8):
        try:
            req = urllib.request.Request(api["base_url"] + "/images/generations", data=body,
                                         headers={"Authorization": "Bearer " + api["api_key"],
                                                  "Content-Type": "application/json"})
            with urllib.request.urlopen(req, timeout=600) as resp:
                doc = json.loads(resp.read().decode("utf-8"))
            item = doc["data"][0]
            if "b64_json" in item:
                return base64.b64decode(item["b64_json"])
            if "url" in item:
                with urllib.request.urlopen(item["url"], timeout=600) as ur:
                    return ur.read()
            raise RuntimeError("no image: " + json.dumps(doc)[:200])
        except urllib.error.HTTPError as e:
            print("HTTP", e.code, "attempt", attempt, flush=True)
            time.sleep(35)   # 429 限流退避
        except Exception as e:
            print("err", str(e)[:80], "attempt", attempt, flush=True)
            time.sleep(20)
    raise RuntimeError("gen failed after retries")

def main():
    outdir, outname = sys.argv[1], sys.argv[2]
    prompt = sys.argv[3] if len(sys.argv) > 3 else DEFAULT_PROMPT
    os.makedirs(outdir, exist_ok=True)
    api = json.load(open(CFG, encoding="utf-8"))
    data = gen(api, prompt)
    path = os.path.join(outdir, outname + ".png")
    with open(path, "wb") as f:
        f.write(data)
    print("saved", path, len(data), "bytes")

if __name__ == "__main__":
    main()
