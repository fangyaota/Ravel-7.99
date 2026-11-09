import re, glob

BS = chr(92)

def strip_code(line):
    out, i, q, n = [], 0, None, len(line)
    while i < n:
        c = line[i]
        if q:
            if c == BS:
                out.append(' '); i += 2; continue
            if line.startswith(q, i):
                out.append(' ' * len(q)); i += len(q); q = None; continue
            out.append(' '); i += 1; continue
        if c == '#': break
        if c == '"':
            q = '"""' if line.startswith('"""', i) else '"'
            out.append(' ' * len(q)); i += len(q); continue
        if c == "'":
            out.append(' '); i += 2 if i + 1 < n else 1; continue
        out.append(c); i += 1
    return ''.join(out)

def depth_at(code):
    d, cur = [], 0
    for c in code:
        d.append(cur)
        if c == '(': cur += 1
        elif c == ')': cur = max(0, cur - 1)
    return d

def top_lambdas(code):
    d = depth_at(code)
    return [(m.start(), m.group(1)) for m in re.finditer(r'\(([^()]*)\)\s*=>', code) if d[m.start()] == 0]

cats = {'交替(同行)': [], '分支表(跨行/行内 =>)': [], '其它': []}
for f in sorted(glob.glob('lib/*.rav')):
    for ln, line in enumerate(open(f, encoding='utf-8-sig').read().splitlines(), 1):
        code = strip_code(line)
        bars = [m.start() for m in re.finditer(r'\|', code)
                if not code.startswith('|>', m.start()) and not code.startswith('||', m.start())
                and (m.start() == 0 or code[m.start()-1] not in '|')]
        if not bars: continue
        lams = top_lambdas(code)
        if len(lams) >= 2: cats['交替(同行)'].append((f, ln, lams, line.strip()))
        elif '=>' in code or code.lstrip().startswith('|'): cats['分支表(跨行/行内 =>)'].append((f, ln, lams, line.strip()))
        else: cats['其它'].append((f, ln, lams, line.strip()))

for k, v in cats.items():
    print(k, len(v))
print()
for f, ln, lams, txt in cats['交替(同行)']:
    print('  交替', f, ln, [t for _, t in lams], '|', txt[:120])
print()
for f, ln, lams, txt in cats['其它']:
    print('  其它', f, ln, '|', txt[:120])
