# ═══════════════════════════════════════════════════════════════
# 实时回应延迟实测（BC-006 v2）——验证"单句回应 2s 预算"可行性
#
# 模拟 C# ReactiveAgent.BuildRespondPrompt + LLMService.ChatOnceAsync 的
# 真实请求形态（同模型 / reasoning_effort=none / max_tokens=80 / 无 response_format）：
#   1. 读 MCM 配置（同 test_llm_plan.py load_config）
#   2. 从 CNs XML 读 LWN_plan_respond_* 骨架拼回应 prompt
#   3. 发 1 次 warmup + N 次样本，统计延迟分布，判定 2s 预算达标率
#
# 用法: python Scripts/test_respond_latency.py [--rounds 5]
# ═══════════════════════════════════════════════════════════════
import json
import os
import sys
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET

MCM_PATH = os.path.expandvars(
    r"%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModSettings\Global\LivingWorldNpcs\LivingWorldNpcsSettings_v1.json")
PROMPTS_XML = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                           "ModuleData", "Languages", "CNs", "std_LivingWorldNpcs_prompts.xml")
BUDGET_S = 2.0


def load_config():
    if not os.path.exists(MCM_PATH):
        print(f"错误: MCM 配置文件不存在: {MCM_PATH}")
        sys.exit(2)
    cfg = json.load(open(MCM_PATH, encoding="utf-8-sig"))
    base = (cfg.get("LLMBaseUrl") or "").rstrip("/")
    key = cfg.get("LLMApiKey") or ""
    model = cfg.get("LLMModel") or ""
    if not (base and key and model):
        print("错误: MCM 未配置 LLM 三字段（LLMBaseUrl/LLMApiKey/LLMModel）")
        sys.exit(2)
    return base, key, model


def load_prompt(key, fallback=""):
    try:
        root = ET.parse(PROMPTS_XML).getroot()
        for el in root.iter("string"):
            if el.get("id") == key:
                return el.get("text") or ""
    except Exception as e:
        print(f"警告: prompt XML 解析失败: {e}")
    return fallback


def build_respond_prompt(topic, companion_line):
    """模拟 C# ReactiveAgent.BuildRespondPrompt（酒馆老板身份，respond 权重 0.85 → hot 档态度）"""
    occ_name = load_prompt("LWN_prompt_trait_occupation_tavernkeeper", "酒馆老板")
    trait = load_prompt("LWN_trait_social_high", "八面玲珑")
    identity = load_prompt("LWN_plan_respond_identity_template", "你是{0}。{1}。").format(occ_name, trait)
    attitude = load_prompt("LWN_plan_respond_attitude_hot",
                           "对方主动搭话，你愿意聊下去（意愿度高）——回应热情些，顺着话题说。")
    world = "骑马与砍杀2 卡拉迪亚中世纪世界"
    history = f"随从：{companion_line}"
    return "\n".join([
        load_prompt("LWN_plan_section_world", "【世界观】") + world,
        load_prompt("LWN_plan_respond_section_identity", "【你的身份】") + identity,
        load_prompt("LWN_plan_respond_section_attitude", "【你此刻的态度】") + attitude,
        load_prompt("LWN_plan_respond_section_topic", "【对话主题】") + topic,
        load_prompt("LWN_plan_respond_section_other", "【对方】") + "“铁匠”沃泰尔（对方是主动来和你搭话的人）",
        load_prompt("LWN_plan_respond_section_history", "【对话历史】") + history,
        load_prompt("LWN_plan_respond_section_last", "【对方刚说】") + history,
        load_prompt("LWN_plan_respond_rule",
                    "【要求】用一句话口语化回应对方（10-40 字），符合身份与性格，直接说台词本身——不要引号、不要解释、不要动作描写。"),
    ])


def call_once(base, key, model, prompt, timeout_s=BUDGET_S):
    body = {
        "model": model,
        "messages": [{"role": "system", "content": prompt}],
        "temperature": 0.7,
        "max_tokens": 80,
        "reasoning_effort": "none",  # 与 ChatOnceAsync 一致：关思考
    }
    req = urllib.request.Request(
        base + "/chat/completions",
        data=json.dumps(body).encode("utf-8"),
        headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"})
    t0 = time.time()
    try:
        with urllib.request.urlopen(req, timeout=timeout_s + 1) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            content = data["choices"][0]["message"]["content"].strip()
            elapsed = time.time() - t0
            return content, elapsed, None
    except Exception as e:
        return None, time.time() - t0, f"{type(e).__name__}: {e}"


def main():
    rounds = 5
    if "--rounds" in sys.argv:
        rounds = int(sys.argv[sys.argv.index("--rounds") + 1])
    base, key, model = load_config()
    topic = "我去和酒馆老板聊聊天。"
    line = "老板，今天生意可好？我家主人想和你聊聊。"
    prompt = build_respond_prompt(topic, line)
    print(f"模型: {model} | 预算: {BUDGET_S}s | 样本: {rounds} 轮（+1 warmup）")
    print(f"prompt 预览: {prompt[:120]}...\n")

    # warmup（长超时：首次连接初始化慢不计入统计；样本才按 2s 预算）
    _, _, err = call_once(base, key, model, prompt, timeout_s=30)
    if err:
        print(f"[FAIL] warmup 失败: {err}")
        sys.exit(2)

    samples = []
    for i in range(rounds):
        content, elapsed, err = call_once(base, key, model, prompt)
        if err:
            print(f"[{i + 1}] 失败({err}) elapsed={elapsed:.2f}s")
            time.sleep(3)  # 429 限流时给网关恢复时间
            continue
        mark = "OK" if elapsed <= BUDGET_S else "超预算"
        print(f"[{i + 1}] {elapsed:.2f}s ({mark}) → {content[:40]}")
        samples.append(elapsed)
        time.sleep(2)  # 请求间隔（防网关 429 限流）

    if not samples:
        print("无有效样本")
        sys.exit(2)
    samples.sort()
    avg = sum(samples) / len(samples)
    p95 = samples[min(len(samples) - 1, int(len(samples) * 0.95))]
    within = sum(1 for s in samples if s <= BUDGET_S)
    print(f"\n结果: min={samples[0]:.2f}s avg={avg:.2f}s p95={p95:.2f}s max={samples[-1]:.2f}s")
    print(f"预算内: {within}/{len(samples)} ({within * 100 // len(samples)}%)")
    ok = within == len(samples)
    print("结论:", "✅ 达标——2s 预算可行" if ok else "⚠️ 未全达——需调预算(降级阈值)或考虑流式")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
