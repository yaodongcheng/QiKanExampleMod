"""Resolve the authored player guide against the current preview's formal data."""
from __future__ import annotations

import copy
import re


def build_walkthrough(authored, payload):
    guide = copy.deepcopy(authored)

    def value_at(path):
        value = payload
        for part in path:
            if isinstance(value, list):
                matches = [x for x in value if isinstance(x, dict)
                           and (x.get('Id') == part or x.get('DialogueId') == part)]
                if len(matches) != 1:
                    raise ValueError('Ambiguous/missing guide source: ' + '/'.join(path))
                value = matches[0]
            else:
                value = value[part]
        if not isinstance(value, (str, int, float)) or isinstance(value, bool):
            raise ValueError('Guide fact must be scalar: ' + '/'.join(path))
        return str(value)

    facts = {key: value_at(path) for key, path in guide.pop('Facts').items()}

    def substitute(value):
        if isinstance(value, str):
            def replace(match):
                key = match.group(1)
                if key not in facts:
                    raise ValueError('Unknown guide fact: ' + key)
                return facts[key]
            return re.sub(r'\{\{([a-z_]+)\}\}', replace, value)
        if isinstance(value, list):
            return [substitute(x) for x in value]
        if isinstance(value, dict):
            return {k: substitute(v) for k, v in value.items()}
        return value

    guide = substitute(guide)
    ids = set()
    for route in guide['Routes']:
        if route['Id'] in ids:
            raise ValueError('Duplicate guide route: ' + route['Id'])
        ids.add(route['Id'])
        route['GrantsPass'] = route['Approach'] in payload['tuning']['Gate']['PassApproaches']
        if route['Ability'] not in ('money', 'force', 'guile', 'charm'):
            raise ValueError('Unknown guide ability: ' + route['Ability'])
        if not all(route.get(field) for field in ('Conditions', 'Steps', 'Recovery', 'Result')):
            raise ValueError('Incomplete guide route: ' + route['Id'])

    def resolve_links(value):
        if isinstance(value, dict):
            if 'Links' in value:
                for link in value['Links']:
                    actor = link['Actor']
                    definition = next((d for d in payload['dialogues'] if actor in d['EntryActors']), None)
                    if definition is None:
                        raise ValueError('Missing guide actor: ' + actor)
                    node = next((n for n in definition['Nodes'] if n['Id'] == link['Node']), None)
                    if node is None:
                        raise ValueError('Missing guide node: ' + link['Node'])
                    link['Line'] = payload['language'][node['TextKey']]
                    if 'Option' in link:
                        option = next((o for o in node.get('Options', []) if o['Id'] == link['Option']), None)
                        if option is None:
                            raise ValueError('Missing guide option: ' + link['Option'])
                        link['PlayerLine'] = payload['language'][option['PlayerKey']]
            for item in value.values():
                resolve_links(item)
        elif isinstance(value, list):
            for item in value:
                resolve_links(item)

    resolve_links(guide)
    return guide
