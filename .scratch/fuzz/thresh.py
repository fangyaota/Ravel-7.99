import os, subprocess, sys
REPO=os.getcwd(); DLL=os.path.join(REPO,"out","ravel.dll")
d=os.path.join(REPO,".scratch","fuzz","probe"); os.makedirs(d,exist_ok=True)
pats = {
 "bang":  lambda n: "x := " + "!"*n + "true\n",
 "neg":   lambda n: "x := " + "- "*n + "1\n",
 "pow":   lambda n: "x := " + "**".join(["1"]*n) + "\n",
 "nilq":  lambda n: "x := " + "a?."*n + "b\n",
 "coal":  lambda n: "x := " + " ?? ".join(["a"]*n) + "\n",
 "pipe":  lambda n: "x := " + " |> ".join(["a"]*n) + "\n",
 "tilde": lambda n: "x := " + "~"*n + "1\n",
 "dot":   lambda n: "x := a" + ".b"*n + "\n",
}
def run(src):
    p=os.path.join(d,"t.rav"); open(p,"w",encoding="utf-8").write(src)
    r=subprocess.run(["dotnet",DLL,"ast",p],cwd=REPO,capture_output=True,timeout=180)
    err=(r.stdout+r.stderr).decode("utf-8","replace")
    if "Stack overflow" in err or r.returncode not in (0,1): return "CRASH", err
    if "!!" in err: return "syntax", err
    return "ok", err
for name, f in pats.items():
    lo, hi = 2, 8192
    res={}
    for n in [50,100,200,400,800,1200,1600,2000,3000]:
        k,e = run(f(n))
        if k=="CRASH":
            print(f"{name}: 崩于 n={n}"); lo_ok=n; break
    else:
        print(f"{name}: 3000 内不崩")
