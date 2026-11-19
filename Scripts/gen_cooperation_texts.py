#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""把协作JSON中的维护词条同步到标准中英文语言表，其他词条保留原样。"""
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path
from xml.sax.saxutils import quoteattr

ROOT = Path(__file__).resolve().parents[1]

def main():
    content = json.loads((ROOT/'ModuleData/DesignData/Cooperation/cooperation.json').read_text(encoding='utf-8-sig'))
    for lang, relative in [('en','ModuleData/Languages/std_LivingWorldNpcs_strings.xml'),
                           ('zh','ModuleData/Languages/CNs/std_LivingWorldNpcs_strings.xml')]:
        path = ROOT/relative
        text = path.read_text(encoding='utf-8')
        for key, translations in content.get('Texts', {}).items():
            value = translations[lang]
            if any(ord(c)>0xffff for c in value): raise ValueError('unsupported localization character: '+key)
            line = '    <string id='+quoteattr(key)+' text='+quoteattr(value)+' />'
            pattern = r'(?m)^[ \t]*<string id="'+re.escape(key)+r'" text=(?:"[^"]*"|\x27[^\x27]*\x27)\s*/>'
            if re.search(pattern,text): text = re.sub(pattern, lambda _:line, text)
            else:
                text, count = re.subn(r'(?m)^[ \t]*</strings>', lambda match:line+'\n'+match.group(0), text, count=1)
                if count != 1: raise ValueError('missing localization strings container: '+str(path))
        # 写盘前核实际XML节点，避免无声漏词条或同键重复被字符串替换掩盖。
        nodes = ET.fromstring(text).findall('.//string')
        for key, translations in content.get('Texts', {}).items():
            matches = [node for node in nodes if node.get('id') == key]
            if len(matches) != 1 or matches[0].get('text') != translations[lang]:
                raise ValueError('localization output mismatch: '+key)
        path.write_text(text, encoding='utf-8', newline='\n')
    print('PASS: cooperation texts generated')

if __name__ == '__main__': main()
