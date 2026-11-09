import re, glob

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

LAM = re.compile(r'\(([^()]*)\)\s*=>')

rows = []
files = (sorted(glob.glob('lib/*.rav')) + sorted(glob.glob('examples/*.rav'))
         + sorted(glob.glob('tests/**/*.rav', recursive=True)))
for f in files:
    for ln, line in enumerate(open(f, encoding='utf-8-sig').read().splitlines(), 1):
        code = strip_code(line)
        bars = [m.start() for m in re.finditer(r'\|', code)
                if not code.startswith('|>', m.start())
                and not code.startswith('||', m.start())
                and (m.start() == 0 or code[m.start() - 1] not in '|')]
        if not bars:
            continue
        rows.append((f, ln, len(bars), LAM.findall(code), line.strip()[:160]))

print("含 | 的代码行:", len(rows))
multi = [r for r in rows if len(r[3]) >= 2]
print("同一行 >=2 个 lambda:", len(multi))

def firsttype(p):
    return p.split(':', 1)[1].strip().split()[0] if ':' in p else '(无注解)'

def nparams(p):
    return len(re.findall(r'[A-Za-z_][\w]*\s*:', p))

hits = []
for f, ln, nb, lams, txt in multi:
    ts = [firsttype(l) for l in lams]
    pars = [nparams(l) for l in lams]
    if len(set(ts)) < len(ts) and len(set(pars)) > 1:
        hits.append((f, ln, ts, pars, txt))
print("第一参类型相同且元数不同:", len(hits))
for h in hits:
    print("   ", h[0], h[1], h[2], h[3], "|", h[4])

print()
print("=== 没有 lambda 的 | 行（看是不是别的用法）===")
for f, ln, nb, lams, txt in rows:
    if not lams:
        print("   ", f, ln, "|", txt[:140])
