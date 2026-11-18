"""Apply the maintained bilingual 511 dialogue revision; XML is generated output."""
from pathlib import Path
import argparse
import html
import json
import re

ROOT = Path(__file__).resolve().parents[3]
SOURCE = Path(__file__).with_name('story_feedback_texts.json')


def render(source, index, body):
    for key, pair in source.items():
        if not re.fullmatch(r'LWN_[A-Za-z0-9_]+', key) or len(pair) != 2 or not all(pair):
            raise ValueError('Invalid bilingual entry: ' + key)
        if any(ord(c) > 0xffff for text in pair for c in text):
            raise ValueError('Non-BMP character: ' + key)
        value = html.escape(pair[index], quote=True).replace('\n', '&#10;')
        line = '    <string id="%s" text="%s" />' % (key, value)
        pattern = r'^[ \t]*<string id="%s"[^\n]*/>\r?$' % re.escape(key)
        matches = re.findall(pattern, body, flags=re.M)
        if len(matches) > 1:
            raise ValueError('Duplicate localization key: ' + key)
        if matches:
            body = re.sub(pattern, lambda _: line, body, flags=re.M)
        else:
            if '</strings>' not in body:
                raise ValueError('Missing strings element')
            body = body.replace('</strings>', line + '\n</strings>')
    return body


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    source = json.loads(SOURCE.read_text(encoding='utf-8'))
    changed = []
    for directory, index in (('', 1), ('CNs', 0)):
        path = ROOT / 'ModuleData/Languages' / directory / 'std_LivingWorldNpcs_strings.xml'
        before = path.read_text(encoding='utf-8')
        after = render(source, index, before)
        if after != before:
            changed.append(str(path))
            if not args.check:
                path.write_text(after, encoding='utf-8', newline='\n')
    if args.check and changed:
        raise SystemExit('Bilingual source drift: ' + ', '.join(changed))
    print('511 bilingual revision: %d entries, %d outputs changed' % (len(source), len(changed)))


if __name__ == '__main__':
    main()
