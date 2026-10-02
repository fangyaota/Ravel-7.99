#!/usr/bin/env bash
# Ravel 的构建脚本 —— 把 CONTEXT.md「编译运行」那几步收成一条命令。
#
#     bash build.sh              发布到 out/,再跑全量测试
#     bash build.sh --warn       顺带再跑一遍 `--warn test`(逮"少给一块"那种静默失败)
#     bash build.sh --rebuild    顺带做一次 -t:Rebuild,有 C# 警告就当场红
#     bash build.sh --no-test    只发布
#     bash build.sh --release    用 Release 配置(默认 Debug)
#
# 两条讲究都在 CONTEXT.md 里写着,别改:
#   * **先删 out/**:增量 publish 有时不更新它,会跑到陈旧产物、得出假的结论;
#   * **`DOTNET_GCHeapHardLimit`**:压住 GC 堆,和平时跑法一致(不然行为会飘)。
#
# 找不到 dotnet?脚本自己翻(`$DOTNET` → PATH → 几个常见装法),翻不到就把怎么修说全;
# 换个终端(WSL、没继承系统 PATH 的 git-bash)时值在这 —— 别指望 `dotnet` 一定在 PATH 上。
#
# 发布产物是**自洽**的:插件 dll(`plugins/`)和标准库(`lib/`)都拷到 dll 旁边
# (见 Ravel.csproj 的 `CopyPlugins` / `CopyLib`),搜索路径里有"程序集目录"那两格
# (见 Runtime/ModuleSearchPath.cs)—— 所以 `out/` 那一份**换哪个工作目录都跑得起来**。
# 最后一步的冒烟就是从别处跑它。
set -euo pipefail

# `pwd -W` 是 git-bash 的:给的是 `D:/...` 这种 **Windows 认的**绝对路径。
# 少了它,`/d/...` 这种 MSYS 路径交给 dotnet.exe 是找不到文件的(冒烟那一步要绝对路径)。
ROOT=$(cd "$(dirname "$0")" && { pwd -W 2>/dev/null || pwd; })
cd "$ROOT"

# 找 dotnet:`$DOTNET` → PATH → 几个常见装法。**别假设它在 PATH 上** —— 换个终端
# (WSL、没继承系统 PATH 的 git-bash)就会是 `dotnet: command not found`,那是 shell 的话,
# 不是 Ravel 的错;脚本半路停下还看不出该修哪儿,所以这儿自己找、找不到就把话说全。
find_dotnet() {
    if [ -n "${DOTNET:-}" ]; then
        if [ -x "$DOTNET" ] || command -v "$DOTNET" >/dev/null 2>&1; then
            printf '%s' "$DOTNET"; return 0
        fi
        echo "DOTNET='$DOTNET' 指的那个东西用不了" >&2
        return 1
    fi
    command -v dotnet >/dev/null 2>&1 && { command -v dotnet; return 0; }
    for p in \
        "$HOME/.dotnet/dotnet" "$HOME/.dotnet/dotnet.exe" \
        "/c/Program Files/dotnet/dotnet.exe" \
        "/mnt/c/Program Files/dotnet/dotnet.exe" \
        "/usr/share/dotnet/dotnet" "/usr/local/share/dotnet/dotnet"
    do
        [ -x "$p" ] && { printf '%s' "$p"; return 0; }
    done
    cat >&2 <<'MSG'
找不到 dotnet。三选一:
  * 装一个 SDK:https://dotnet.microsoft.com/download
  * 把它加进 PATH —— git-bash 里:  export PATH="$PATH:/c/Program Files/dotnet"
  * 或者直接指给脚本:  DOTNET="/c/Program Files/dotnet/dotnet.exe" bash build.sh
MSG
    return 1
}
DOTNET=$(find_dotnet)

# 调的是 Windows 那个 `dotnet.exe` 时,**绝对路径得是 Windows 形式** ——
# git-bash 那边 `pwd -W` 已经给了(`D:/...`);WSL 那边给的是 `/mnt/d/...`,而 dotnet.exe
# 不认那个,得用 `wslpath -w` 翻一道。原生 Linux 的 dotnet 认 `/mnt/...`,所以只对 `.exe` 翻。
win_path() {
    case "$DOTNET" in
        *.exe)
            if command -v wslpath >/dev/null 2>&1; then wslpath -w "$1"
            else printf '%s' "$1"; fi ;;
        *) printf '%s' "$1" ;;
    esac
}

CONF=Debug
GC=0x10000000
run_warn=0
run_rebuild=0
run_test=1

for arg in "$@"; do
    case "$arg" in
        --warn)    run_warn=1 ;;
        --rebuild) run_rebuild=1 ;;
        --no-test) run_test=0 ;;
        --release) CONF=Release ;;
        -h|--help) sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "不认识的参数 '$arg'(试试 --help)" >&2; exit 2 ;;
    esac
done

echo "== 发布($CONF)→ out/ =="
rm -rf out
DOTNET_GCHeapHardLimit=$GC "$DOTNET" publish Ravel.csproj -c "$CONF" -o out

if [ "$run_rebuild" = 1 ]; then
    echo
    echo "== C# 编译(-t:Rebuild) =="
    # 末尾那个 `|| { … }` **不能省**:`set -e` 之下 `log=$(失败的命令)` 会让脚本**当场退出**,
    # 而日志是进了变量的 —— 于是**编译报错时屏幕上什么都没有**(不是"没输出",是这一段压根
    # 没走到)。截住它、把编译器原话倒出来再退。
    log=$(DOTNET_GCHeapHardLimit=$GC "$DOTNET" build Ravel.csproj -c "$CONF" -t:Rebuild -v q --nologo 2>&1) || {
        printf '%s\n' "$log" >&2
        echo "^^^ C# 编译没过 —— 上面是编译器原话" >&2
        exit 1
    }
    if printf '%s\n' "$log" | grep -E ": (warning|error) "; then
        echo "^^^ 上面这些得清掉(记忆里那条:C# 警告要清 obj 重编才算数)" >&2
        exit 1
    fi
    echo "0 警告 0 错误"
fi

if [ "$run_test" = 1 ]; then
    echo
    echo "== 全量测试 =="
    DOTNET_GCHeapHardLimit=$GC "$DOTNET" out/ravel.dll test

    if [ "$run_warn" = 1 ]; then
        echo
        echo '== --warn test(逮「少给一块」那种静默失败) =='
        DOTNET_GCHeapHardLimit=$GC "$DOTNET" out/ravel.dll --warn test
    fi
fi

# 发布产物自洽:在**别的目录**里跑,只认 out/ 那一份(那里没有 lib/)。
echo
echo "== 冒烟:换个工作目录跑 out/ =="
DLL=$(win_path "$ROOT/out/ravel.dll")
tmp=$(mktemp -d)
printf 'print (1 + 1)\n' > "$tmp/smoke.rav"
# 取**最后一行非空**的:输出末尾本来就跟一个空行(`tail -1` 会捞到它)。
# 末尾 `|| true` 同理不能省 —— **恰恰是跑不起来的时候**这条管道自身是失败的,少了它
# `set -e` 会让脚本当场退出,底下那句"多半是 out/ 里少了 lib/ 或 plugins/"就永远打不出来,
# 而那正是这一格存在的理由。
#
# 一处**挡不住的小瑕疵**:解释器那份报错是写在 **stdout** 上的(不是 stderr),于是会被
# 这一抓顺走;而 stdout 一旦是管道,.NET 就按 OEM 码页(CP936)写字节 —— 那串中文再吐回
# UTF-8 终端就是乱码。要留下"最后一行"就得抓,所以躲不开;好在底下那句提示是 ASCII 的,
# 该说清的事它说了。(stderr 就别再并进来了:它不抓,直接落终端,反而一切正常。)
got=$(cd "$tmp" && DOTNET_GCHeapHardLimit=$GC "$DOTNET" "$DLL" smoke.rav | grep -v '^$' | tail -1) || true
rm -rf "$tmp"
if [ "$got" = "2" ]; then
    echo "ok"
else
    echo "冒烟没过(期望 2,得到 '$got')—— 多半是 out/ 里少了 lib/ 或 plugins/" >&2
    exit 1
fi
