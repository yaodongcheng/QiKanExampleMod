# -*- coding: utf-8 -*-
"""
Created on Thu Dec 11 10:16:58 2025
Updated on Thu Dec 11 11:30:00 2025
Modified on Thu Dec 12 2025 by ChatGPT to match example output format
"""

import re
import json
import os

class TR5ScriptParser:
    def __init__(self):
        # 1. 匹配对话/旁白 [[内容]]
        self.text_content_pattern = re.compile(r'\[\[(.*?)\]\]', re.DOTALL)
        
        # 2. 匹配参数 (参数)
        self.args_pattern = re.compile(r'\((.*?)\)')
        
        # 3. 匹配操作符
        self.operator_pattern = re.compile(r'(==|!=|>=|<=|>|<)')

    def parse_file(self, content):
        # 1. 移除注释 //...
        content = re.sub(r'//.*', '', content)

        
# 1.5 只处理 [[...]] 里的文本，把 {} 换成你想要的占位符
        def _brace_replacer(m):
            inner = m.group(1)
            # 在 [[...]] 里面，把 { 和 } 换成你定义的安全标记
            # 这里示例用 %%LEFT%% / %%RIGHT%%，你可以自行改
            inner = inner.replace('{', '%%LEFT%%').replace('}', '%%RIGHT%%')
            return f'[[{inner}]]'

        content = re.sub(r'\[\[(.*?)\]\]', _brace_replacer, content, flags=re.DOTALL)
        
        # 2. 词法分析
        events = []
        iterator = self._tokenize(content)
        
        try:
            while True:
                token_type, token_val = next(iterator)
                if token_type == 'KEY_BLOCK' and token_val.startswith('事件'):
                    parts = token_val.split(':', 1)
                    event_id = parts[1].strip() if len(parts) > 1 else "Unknown"
                    event_body = self._parse_block_content(iterator)
                    events.append(self._process_event(event_id, event_body))
        except StopIteration:
            pass
            
        return events

    def _tokenize(self, text):
        buffer = []
        i = 0
        length = len(text)
        
        while i < length:
            char = text[i]
            
            if char == '{':
                key = "".join(buffer).strip()
                if key:
                    yield ('KEY_BLOCK', key)
                buffer = []
                yield ('OPEN', '{')
            elif char == '}':
                line = "".join(buffer).strip()
                if line:
                    yield ('LINE', line)
                buffer = []
                yield ('CLOSE', '}')
            elif char == '\n' or char == ';':
                line = "".join(buffer).strip()
                if line:
                    yield ('LINE', line)
                buffer = []
            else:
                buffer.append(char)
            i += 1


    def _parse_block_content(self, iterator):
        block_data = []
        while True:
            token_type, token_val = next(iterator)
            if token_type == 'CLOSE':
                break
            elif token_type == 'KEY_BLOCK':
                key = token_val.strip()
                sub_content = self._parse_block_content(iterator)
                block_data.append({"type": "block", "key": key, "content": sub_content})
            elif token_type == 'LINE':
                block_data.append({"type": "line", "content": token_val})
        return block_data

    def _process_event(self, event_id, raw_data):
        # 事件级结构与示范严格对齐
        event = {
            "id": event_id.replace("事件", "").strip(),
            "type": "事件",
            "attributes": {},
            "trigger": {},
            "conditions": [],
            "script": []
        }

        for item in raw_data:
            if item['type'] == 'line':
                line = item['content']
                if line.startswith('屬性:'):
                    attr_val = line.split(':', 1)[1].strip()
                    event['attributes'] = {"value": attr_val}
                elif line.startswith('發生契機:'):
                    event['trigger'] = self._parse_trigger(line)
            
            elif item['type'] == 'block':
                key = item['key']
                if key.startswith('發生條件'):
                    event['conditions'] = self._parse_conditions(item['content'])
                elif key.startswith('執行'):
                    event['script'] = self._parse_execution(item['content'])

        return event

    def _parse_trigger(self, line):
        # 發生契機:遊戲開始時
        try:
            parts = line.split(':', 1)
            content = parts[1].strip()
            trigger_name = content.split('(')[0].strip()
            args = self.args_pattern.findall(content)
            if not args and '(' in content:
                args_match = re.search(r'\((.*?)\)', content)
                if args_match:
                    args = args_match.group(1).split(',')
            return {
                "type": trigger_name,
                "params": [a.strip() for a in args if a.strip()]
            }
        except:
            return {"type": "ErrorParsingTrigger", "params": [], "raw": line}
    def _parse_conditions1(self, content_list):
            conditions = []
            for item in content_list:
                if item['type'] == 'line':
                    line = item['content'].strip()
                    if not line:
                        continue
                    # 只处理以 調查: = self.text_content_pattern.search(line)
            text_match = line
            text_content = text_match.group(1) if text_match else None
            
            if text_content is not None:
                text_content = self._clean_text(text_content)
           
            # 去掉 [[文本]] 后再解析命令头
            clean_line = self.text_content_pattern.sub('', line).strip()
            
            # 調查 行（无论是否包含比较符）
            if clean_line.startswith('調查:'):
                expr = clean_line[len('調查:'):].strip()
                node = self._parse_comparison(expr)
                # 在執行脚本中同样为 type=調查
                node["type"] = "調查"
                return node
    def _parse_conditions(self, content_list):
        conditions = []
        for item in content_list:
            if item['type'] == 'line':
                line = item['content'].strip()
                if not line:
                    continue
                raw_cond = line.replace('調查:', '').strip()
                cond_node = self._parse_comparison(raw_cond)
                # 输出字段名与示范保持一致
                cond_node["type"] = "調查"
                conditions.append(cond_node)
            elif item['type'] == 'block':
                # 此示例未用到 AND/OR，先不扩展
                pass
        return conditions

    def _parse_execution(self, content_list):
        sequence = []
        i = 0
        while i < len(content_list):
            item = content_list[i]
            if item['type'] == 'line':
                cmd = self._parse_command_line(item['content'])
                if cmd:
                    sequence.append(cmd)
                i += 1
            elif item['type'] == 'block':
                block_key = item['key']
                # ＯＲ調查：只要子調查有一项满足
                if block_key.startswith('ＯＲ調查'):
                  or_node = {
                      "cmd": "ＯＲ調查",
                      "conditions": []
                  }
                  for sub in item['content']:
                      if sub['type'] == 'line':
                          line = sub['content'].strip()
                          if line.startswith('調查:'):
                              raw_cond = line[len('調查:'):].strip()
                              cond_node = self._parse_comparison(raw_cond)
                              or_node["conditions"].append(cond_node)
                  sequence.append(or_node)
                # 場合別
                if block_key.startswith('場合別'):
                    baai_node = self._parse_baai(block_key, item['content'])
                    sequence.append(baai_node)
                # 主人公別
                # 主人公別
                if block_key.startswith('主人公別'):
                    node = {
                        "cmd": "主人公別",
                        "children": self._parse_player_switch(item['content'])
                    }
                    sequence.append(node)
                # 發生條件之外的 調查/分歧 块（例如示例中的分支逻辑）
                elif block_key.startswith('分歧'):
                    val_list = self.args_pattern.findall(block_key)
                    val = val_list[0] if val_list else ""
                    node = {
                        "cmd": "分歧",
                        "value": val,
                        "children": self._parse_execution(item['content'])
                    }
                    sequence.append(node)
                elif block_key.startswith('主人公分歧'):
                    # 在主人公別内部处理，这里通常不会进入
                    sequence.append({"cmd": "RawBlock", "raw_key": block_key})
                else:
                    # 其它块递归处理为普通序列
                    sequence.extend(self._parse_execution(item['content']))
                i += 1
        return sequence

    def _parse_player_switch(self, content_list):
        """解析 主人公別 块内部结构，使之符合示范输出"""
        result = []
        for item in content_list:
            if item['type'] != 'block':
                continue
            key = item['key']
            if key.startswith('主人公分歧'):
                # 取 (xxx) 里的主人公名
                vals = self.args_pattern.findall(key)
                value = vals[0] if vals else ""
                case_node = {
                    "cmd": "主人公分歧",
                    "value": value,
                    "children": []
                }
                # 递归解析内部脚本，但按照示范需要特殊处理
                inner_seq = []
                # 这里复用 _parse_execution 但要处理 嵌套調查/分歧
                for sub in item['content']:
                    if sub['type'] == 'line':
                        # 普通命令或調查
                        line_cmd = self._parse_command_line(sub['content'])
                        if line_cmd:
                            # 如果是比较且来自調查:，在主人公別内部仍保持 type=調查
                            if line_cmd.get("type") == "Comparison" and "調查:" in sub['content']:
                                line_cmd["type"] = "調查"
                            inner_seq.append(line_cmd)
                    elif sub['type'] == 'block':
                        sub_key = sub['key']
                        if sub_key.startswith('分歧'):
                            vals2 = self.args_pattern.findall(sub_key)
                            v2 = vals2[0] if vals2 else ""
                            branch_node = {
                                "cmd": "分歧",
                                "value": v2,
                                "children": self._parse_execution(sub['content'])
                            }
                            inner_seq.append(branch_node)
                        else:
                            inner_seq.extend(self._parse_execution(sub['content']))
                case_node["children"] = inner_seq
                result.append(case_node)
        return result

    def _parse_comparison(self, text):
        """
        text 已经是 調查: 后面的部分，例如:
        (勢力::織田家.關係)>= (勢力::敵對)
        或 (大名家::織田信長.存在)
        """
        text = text.strip()
        op_match = self.operator_pattern.search(text)
        if op_match:
            operator = op_match.group(1)
            parts = text.split(operator, 1)
            left = parts[0].strip()
            right = parts[1].strip() if len(parts) > 1 else ""

            # 去掉左右最外层括号
            left = self._strip_outer_parens(left)
            right = self._strip_outer_parens(right)

            return {
                "type": "調查",
                "operator": operator,
                "left": left,
                "right": right
            }
        else:
            # 无比较符，整体作为表达式
            expr = self._strip_outer_parens(text)
            return {
                "type": "調查",
                "expression": expr
            }
    def _parse_top_level_parens(self, s: str):
           
            if not s:
                return []
    
            s = s.strip()
            res = []
            depth = 0
            start = None
    
            for i, ch in enumerate(s):
                if ch == '(':
                    if depth == 0:
                        # 记录内容起点（不含 '('）
                        start = i + 1
                    depth += 1
                elif ch == ')':
                    if depth > 0:
                        depth -= 1
                        if depth == 0 and start is not None:
                            # 完成一个顶层括号段
                            res.append(s[start:i].strip())
                            start = None

            return res
    def _parse_baai(self, block_key, content_list):

            # 解析 場合別:(...) 中的表達式
            cond_expr_list = self._parse_top_level_parens(block_key)
            cond_expr = cond_expr_list[0] if cond_expr_list else ""
            cond_node = self._parse_comparison(cond_expr)
    
            result = {
                "cmd": "場合別",
                "condition": cond_node,
                "cases": []
            }
    
            # 遍歷子 block，尋找 場合分歧
            for item in content_list:
                if item['type'] != 'block':
                    continue
                sub_key = item['key'].strip()
                if sub_key.startswith('場合分歧'):
                    val_list = self._parse_top_level_parens(sub_key)
                    case_val = val_list[0] if val_list else ""
                    case_node = {
                        "value": case_val,
                        "children": self._parse_execution(item['content'])
                    }
                    result["cases"].append(case_node)
    
            return result
    def _strip_outer_parens(self, s: str) -> str:
        s = s.strip()
        if s.startswith('(') and s.endswith(')'):
            return s[1:-1].strip()
        return s
    def _clean_text(self, text):
        """
        1. 把脚本中的换行 \n 换成空字符串
        2. 不要出现字面值 '\n'
        3. 保留占位符 {} 原样
        """
        if text is None:
            return None
        # 将真实换行替换为空
        text = text.replace('\n', '')
        # 避免出现转义形式的 \n
        text = text.replace('\\n', '')
        return text
    def _parse_text_command(self, cmd, raw_line):
        node = {"cmd": cmd, "params": []}
        match = self.text_content_pattern.search(raw_line)
        if match:
            text = match.group(1)
            text = text.replace('\n', '').replace('\\n', '')
            node["text"] = text
        return node
    def _extract_balanced_params(self, text):           
            params = []
            buffer = []
            balance = 0
            started = False
            
            for char in text:
                if char == '(':
                    if not started:
                        started = True
                        balance = 1
                        continue # 跳过最外层的 (
                    else:
                        balance += 1
                        buffer.append(char)
                elif char == ')':
                    if started:
                        balance -= 1
                        if balance == 0:
                            # 闭合了最外层
                            params.append("".join(buffer))
                            buffer = []
                            started = False
                        else:
                            # 内部嵌套的 )
                            buffer.append(char)
                else:
                    if started:
                        buffer.append(char)
            
            return params
    def _parse_command_line(self, line):
        # 提取 [[文本]]
        text_match = self.text_content_pattern.search(line)
        text_content = text_match.group(1) if text_match else None
        
        if text_content is not None:
            text_content = self._clean_text(text_content)
       
        # 去掉 [[文本]] 后再解析命令头
        clean_line = self.text_content_pattern.sub('', line).strip()
        
        
        # 調查比较语句
        if clean_line.startswith('調查:') and self.operator_pattern.search(clean_line):
            expr = clean_line.replace('調查:', '').strip()
            node = self._parse_comparison(expr)
            # 在執行脚本中作为 type=調查 的节点
            node["type"] = "調查"
            return node

        # 无冒号：可能是单纯文本（当前示例用不到）
        if ':' not in clean_line:
            if text_content is not None:
                return {"cmd": "TextOnly", "params": [], "text": text_content}
            return {"cmd": "Raw", "val": clean_line}

        parts = clean_line.split(':', 1)
        command = parts[0].strip()
        #params_raw = parts[1].strip()
        params_raw = parts[1].strip() if len(parts) > 1 else ""
        
        #params = self.args_pattern.findall(params_raw)
        params = self._extract_balanced_params(params_raw)
        if not params and params_raw:
            params = [params_raw]

        # 依据示范映射命令名，参数与结构
        node = {
            "cmd": command,
            "params": params
        }
        if text_content is not None:
            node["text"] = text_content
       
        # 旁白
        if command == "旁白":
            node["cmd"] = "旁白"
            node["params"] = []
        # 自語
        if command == "自語":
            node["cmd"] = "自語"
            node["params"] = []
            
        # 對話
        if command == "對話":
            node["cmd"] = "對話"
            if len(params) >= 2:
                # 与示范相同：params 里保留两人名字
                node["params"] = [params[0], params[1]]
            else:
                node["params"] = params
         # 對話選擇
        if command == "對話選擇":
            node["cmd"] = "對話選擇"
            if len(params) >= 2:
                # 与示范相同：params 里保留两人名字
                node["params"] = [params[0], params[1]]
            else:
                node["params"] = params         

        # 選擇命令当前示例未涉及，保留原逻辑但保证不含 \n
        if command == "選擇":
            options_text = re.findall(r'\[\[(.*?)\]\]', line, re.DOTALL)
            options_text = [self._clean_text(t) for t in options_text]
            options_val = re.findall(r'\((.*?)\)', line)
            node = {"cmd": "選擇", "options": []}
            for i in range(len(options_text)):
                opt = {"text": options_text[i]}
                if i < len(options_val):
                    opt["next_branch_val"] = options_val[i]
                node["options"].append(opt)
        if command in ("旁白", "自語", "對話", "對話選擇"):           
            text = text_content.replace('%%LEFT%%', '{').replace('%%RIGHT%%', '}')
            node["text"] = text
        
        return node

def main():
    # 设定输入文件夹路径，这里默认为当前目录下的 input_scripts 文件夹
    # 如果文件就在当前目录，可以改为 input_folder = '.'
    input_folder = '.' 
    output_file = 'mb2_shokuho_events.json'    
    
    # 用于存储合并后的所有事件
    all_events = []
    
    parser = TR5ScriptParser()
    
    # 检查输入目录是否存在
    if not os.path.exists(input_folder):
        print(f"错误：目录 '{input_folder}' 不存在。")
        return

    print(f"正在扫描目录 '{input_folder}' 下的 .txt 文件...")
    
    # 遍历目录下的所有文件
    for filename in os.listdir(input_folder):
        if filename.endswith(".txt"):
            file_path = os.path.join(input_folder, filename)
            print(f"正在处理文件: {filename}")
            
            try:
                with open(file_path, 'r', encoding='utf-8') as f:
                    content = f.read()
                
                # 解析单个文件内容
                file_events = parser.parse_file(content)
                newFileName = filename.replace(".evm.decompiled", "")                
                newFileName = newFileName.replace(".txt", "")
                newFileName = f"{newFileName}.json"
                
                try:
                    with open(newFileName, 'w', encoding='utf-8') as f:
                        json.dump(file_events, f, indent=2, ensure_ascii=False)
                    print(f"结果已保存至: {newFileName}")
                except Exception as e:
                    print(f"写入输出文件时发生错误: {e}")
                
                
            except Exception as e:
                print(f"处理文件 {filename} 时发生错误: {e}")

    # 将合并后的结果写入 JSON
    print(f"所有文件处理完毕。")
    

if __name__ == "__main__":
    main()