import re, glob, collections

BS = chr(92)

def strip_code(line):
    out = []
    i = 0
    q = None
    n = len(line)
    while i < n:
        c = line[i]
        if q:
            if c == BS:
                out.append(' ')
                i += 2
                continue
            if line.startswith(q, i):
                out.append(' ' * len(q))
                i += len(q)
                q = None
                continue
            out.append(' ')
            i += 1
            continue
        if c == '#':
            break
        if c == '"':
            q = '"""' if line.startswith('"""', i) else '"'
            out.append(' ' * len(q))
            i += len(q)
            continue
        if c == "'":
            out.append(' ')
            i += 2 if i + 1 < n else 1
            continue
        out.append(c)
        i += 1
    return ''.join(out)

def depths(code):
    """每个字符位置的(圆括号深度)"""
    d = []
    cur = 0
    for c in code:
        d.append(cur)
        if c == '(':
            cur += 1
        elif c == ')':
            cur = max(0, cur - 1)
    return d

def top_lambdas(code):
    """返回在圆括号深度 0 处开始的 lambda 参数表: [(start, params_text)]"""
    res = []
    d = depths(code)
    for m in re.finditer(r'\(([^()]*)\)\s*=>', code):
        if d[m.start()] == 0:
            res.append((m.start(), m.group(1)))
    return res

def parse_params(text):
    toks = text.replace('\n', ' ').split()
    params = []
    i = 0
    while i < len(toks):
        t = toks[i]
        if t.endswith(':'):
            ty = toks[i + 1] if i + 1 < len(toks) else '?'
            params.append((t[:-1], ty))
            i += 2
        elif ':' in t:
            nm, ty = t.split(':', 1)
            params.append((nm, ty))
            i += 1
        else:
            params.append((t, 'object'))
            i += 1
    return params

def ptype(p):
    ty = p[1]
    ty = re.split(r'[|\s]', ty)[0]
    return ty

rows = []
files = (sorted(glob.glob('lib/*.rav')) + sorted(glob.glob('examples/*.rav'))
         + sorted(glob.glob('tests/**/*.rav', recursive=True)))
for f in files:
    lines = open(f, encoding='utf-8-sig').read().splitlines()
    for ln, line in enumerate(lines, 1):
        code = strip_code(line)
        bars = [m.start() for m in re.finditer(r'\|', code)
                if not code.startswith('|>', m.start())
                and not code.startswith('||', m.start())
                and (m.start() == 0 or code[m.start() - 1] not in '|')]
        if not bars:
            continue
        lams = top_lambdas(code)
        rows.append(dict(f=f, ln=ln, nb=len(bars), lams=lams,
                         txt=line.strip()[:150],
                         arrow=('=>' in code)))

alt = [r for r in rows if len(r['lams']) >= 2]
match = [r for r in rows if len(r['lams']) < 2 and r['arrow']]
other = [r for r in rows if len(r['lams']) < 2 and not r['arrow']]

print('含 | 的代码行:', len(rows))
print('  A 交替式(同一行 >=2 个顶层 lambda):', len(alt))
print('  B 分支表式(行内有 => 但不足两个顶层 lambda，多半是跨行 match):', len(match))
print('  C 其它(位或/布尔或/flag/记号):', len(other))

print()
print('=== A 类:每个分支的首参类型 / 元数 ===')
conflict = []
catchall = []
for r in alt:
    ps = [parse_params(t) for _, t in r['lams']]
    ts = [ptype(p[0]) if p else '(空)' for p in ps]
    ar = [len(p) for p in ps]
    tag = ''
    if len(set(ts)) < len(ts) and len(set(ar)) > 1:
        tag = '  <== 同首参类型、不同元数'
        conflict.append(r)
    if 'object' in ts[:-1]:
        tag += '  <== 有无注解分支且不在最后'
        catchall.append(r)
    print('  %-32s:%-4d %s arity=%s%s' % (r['f'], r['ln'], ts, ar, tag))
    if tag:
        print('        ', r['txt'])

print()
print('同首参类型且元数不同:', len(conflict))
print('有 object 兜底分支且不在最后:', len(catchall))
for r in catchall:
    print('   ', r['f'], r['ln'], '|', r['txt'])
