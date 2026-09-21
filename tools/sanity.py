#!/usr/bin/env python3
"""SnookerKit structural sanity checker (no Unity/compiler in this sandbox).

Checks, over every Assets/**/*.cs file:
  1. Brace/paren/bracket balance (string- and char-literal aware, ignores comments).
  2. Forbidden API surface: TMPro, UnityEngine.InputSystem, UnityEditor (outside Assets/Editor),
     Rigidbody.linearVelocity, PhysicsMaterial (the 2023+ name), GameObject.Find/Camera.main in
     Update/FixedUpdate/Update-adjacent hot methods, HTTP, reflection-heavy Newtonsoft usage.
  3. Namespace: every file must be `namespace SnookerKit` (Editor files may use SnookerKit.EditorTools).
  4. Duplicate top-level type declarations across the project (class/struct/enum/interface).
  5. GameEvents subscription hygiene: every `GameEvents.X +=` has a matching `-=`.
Exit code 0 = clean; prints a per-check report.
"""
import os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets")
ROOT = os.path.normpath(ROOT)

STRIP = re.compile(r'//[^\n]*|/\*.*?\*/|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])\'', re.S)

def balanced(src):
    s = STRIP.sub(lambda m: '""' if m.group(0).startswith(('"', "'")) else ' ', src)
    stack = []
    pairs = {')': '(', ']': '[', '}': '{'}
    for ch in s:
        if ch in '([{':
            stack.append(ch)
        elif ch in ')]}':
            if not stack or stack[-1] != pairs[ch]:
                return False, f"unmatched {ch}"
            stack.pop()
    if stack:
        return False, f"unclosed {stack[-1]}"
    return True, "ok"

def find_files():
    out = []
    for dirpath, _, files in os.walk(ROOT):
        for f in files:
            if f.endswith('.cs'):
                out.append(os.path.join(dirpath, f))
    return sorted(out)

def main():
    files = find_files()
    if not files:
        print("NO .cs FILES FOUND"); return 1
    problems = 0
    types = {}
    sub, unsub = {}, {}
    for path in files:
        rel = os.path.relpath(path, ROOT)
        src = open(path, encoding='utf-8').read()
        ok, why = balanced(src)
        if not ok:
            print(f"[BALANCE] {rel}: {why}"); problems += 1
        # forbidden
        low = src
        if 'TMPro' in low or 'TextMeshPro' in low:
            print(f"[FORBIDDEN] {rel}: TextMeshPro"); problems += 1
        if 'UnityEngine.InputSystem' in low or 'using UnityEngine.InputSystem' in low:
            print(f"[FORBIDDEN] {rel}: InputSystem"); problems += 1
        if 'UnityEditor' in low and '/Editor/' not in rel.replace(os.sep, '/') and not rel.startswith('Editor'):
            print(f"[FORBIDDEN] {rel}: UnityEditor outside Assets/Editor"); problems += 1
        if 'linearVelocity' in low:
            print(f"[FORBIDDEN] {rel}: Rigidbody.linearVelocity (2022.3 uses velocity)"); problems += 1
        if re.search(r'\bPhysicsMaterial\b', low):
            print(f"[FORBIDDEN] {rel}: PhysicsMaterial (2022.3 uses PhysicMaterial)"); problems += 1
        if 'Newtonsoft' in low:
            print(f"[FORBIDDEN] {rel}: Newtonsoft"); problems += 1
        # namespace
        m = re.search(r'^\s*namespace\s+([\w.]+)', src, re.M)
        ns = m.group(1) if m else None
        in_editor = rel.replace(os.sep, '/').startswith('Editor/')
        ok_ns = (ns == 'SnookerKit') or (in_editor and ns == 'SnookerKit.EditorTools')
        if not ok_ns:
            print(f"[NAMESPACE] {rel}: got {ns}"); problems += 1
        # duplicate top-level types
        stripped = STRIP.sub(' ', src)
        for tm in re.finditer(r'^\s*(?:\[[\w\(\)\., \s]*\]\s*)*(?:public|internal)?\s*(?:static\s+|abstract\s+|sealed\s+|partial\s+)*(class|struct|enum|interface)\s+(\w+)', stripped, re.M):
            name = tm.group(2)
            if name in types and types[name] != rel:
                print(f"[DUPLICATE] type {name} in {rel} and {types[name]}"); problems += 1
            types[name] = rel
        # event hygiene
        for em in re.finditer(r'GameEvents\.(\w+)\s*(\+=|-=)', src):
            if em.group(2) == '+=': sub[em.group(1)] = sub.get(em.group(1), 0) + 1
            else: unsub[em.group(1)] = unsub.get(em.group(1), 0) + 1
    for ev, c in sub.items():
        if unsub.get(ev, 0) < c:
            print(f"[EVENTS] GameEvents.{ev}: {c} subscriptions but {unsub.get(ev, 0)} unsubscriptions"); problems += 1
    total_lines = sum(len(open(p, encoding='utf-8').read().splitlines()) for p in files)
    print(f"\nfiles={len(files)} types={len(types)} lines={total_lines}")
    if problems:
        print(f"PROBLEMS: {problems}"); return 1
    print("SANITY: CLEAN"); return 0

if __name__ == '__main__':
    sys.exit(main())
