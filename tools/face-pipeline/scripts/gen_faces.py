# -*- coding: utf-8 -*-
# gen_faces.py — 脸池新母版生成（女池 5 张不同脸补齐: x20/x21/x25 用）
# 风格基准 = s2002/s2003: 战国姬武将正脸 CG、直视、严格对称、柔和、无刘海遮挡
# 用法: python gen_faces.py <outdir>
# 输出: oda_bigface_s2004/05/06.jpg（3 张不同长相）
import base64
import json
import os
import sys
import urllib.request

CFG = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\ShokuhoTaikouExpansionPack\ArtSource\api_config.json"
BASE_PROMPT = (
    "Digital painting portrait of a Japanese Sengoku era beautiful noble lady, "
    "half-body shot, front-facing, looking straight at camera, perfectly symmetrical face, "
    "soft CG style, clear delicate facial features, clean smooth skin, "
    "dark brown eyes, straight eyebrows, gentle pink lips, "
    "hairstyle: hair completely pulled back from forehead, middle part hair combed back, "
    "full bald forehead fully visible, no bangs, no hair in front of face, crown of head visible, "
    "wearing dark purple and teal patterned kimono, plain dark warm brown background, "
    "soft frontal lighting, high quality dramatic but soft, high detail face."
)
VARIANTS = [
    "Subtle variation: rounder cheeks, gentle almond eyes, warm mature look.",
    "Subtle variation: slightly sharper chin, confident narrow eyes, elegant cool look.",
    "Subtle variation: soft sweet youthful look, slightly larger eyes, tender smile.",
]

def gen(api, prompt, size="1024x1536"):
    body = json.dumps({"model": api["model"], "prompt": prompt, "n": 1, "size": size}).encode()
    req = urllib.request.Request(api["base_url"] + "/images/generations", data=body,
                                 headers={"Authorization": "Bearer " + api["api_key"],
                                          "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=300) as resp:
        # 有的返回 data[0].b64_json; 有的 data[0].url
        doc = json.loads(resp.read().decode("utf-8"))
    item = doc["data"][0]
    if "b64_json" in item:
        return base64.b64decode(item["b64_json"])
    if "url" in item:
        with urllib.request.urlopen(item["url"], timeout=300) as ur:
            return ur.read()
    raise RuntimeError("no image in response: " + json.dumps(doc)[:300])

def main():
    outdir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(outdir, exist_ok=True)
    api = json.load(open(CFG, encoding="utf-8"))
    for i, var in enumerate(VARIANTS):
        name = f"oda_bigface_s{2010 + i}"
        data = gen(api, BASE_PROMPT + " " + var)
        path = os.path.join(outdir, name + ".jpg")
        with open(path, "wb") as f:
            f.write(data)
        print("saved", path, len(data), "bytes")

if __name__ == "__main__":
    main()
