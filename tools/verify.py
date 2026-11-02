r"""Checks every Harmony target in src/Patches.cs against a game build.
Usage: python tools/verify.py "<game>\Platypus_Data\Managed\Platypus.dll"
"""
import sys, re, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); from dnparse import Meta
md = Meta(open(sys.argv[1],'rb').read()); src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'Patches.cs')).read()
types = {}
for i,t in enumerate(md.tables['TypeDef'],1): types[md.typedef_name(i)] = i
def methods(tn):
    t = md.tables['TypeDef'][types[tn]-1]; out=[]
    for m in t['methods']:
        r = md.tables['MethodDef'][m-1]; ret,args,inst = md.method_sig(r['Sig']); out.append((r['Name'], args))
    return out
csmap = {'typeof(bool)':'bool','typeof(Rewired.Player)':'Rewired.Player','typeof(string)':'string','typeof(float)':'float'}
ok=bad=0
# Patch("Type","Method", args, ...)
for m in re.finditer(r'Patch\("(\w+)",\s*"(\w+)",\s*(null|Type\.EmptyTypes|new Type\[\] \{([^}]*)\})', src):
    tn, mn, a, inner = m.groups()
    if tn not in types: print('MISSING TYPE', tn); bad+=1; continue
    cands = [x for x in methods(tn) if x[0]==mn]
    if a=='null': good = len(cands)==1
    elif a=='Type.EmptyTypes': good = any(len(x[1])==0 for x in cands)
    else:
        want=[csmap[s.strip()] for s in inner.split(',')]; good = any(x[1]==want for x in cands)
    print(('OK  ' if good else 'BAD ')+tn+'.'+mn, a if a!='new Type[] {%s}'%inner else want, [x[1] for x in cands]); ok+=good; bad+= not good
# simInit / fixedTypes arrays
arr = re.search(r'string\[\] fixedTypes = \{(.*?)\};', src, re.S).group(1)
for tn in re.findall(r'"(\w+)"', arr):
    good = tn in types and any(x[0]=='FixedUpdate' and not x[1] for x in methods(tn)); print(('OK  ' if good else 'BAD ')+tn+'.FixedUpdate'); ok+=good; bad+=not good
for tn, mn in re.findall(r'new string\[\] \{ "(\w+)", "(\w+)" \}', src):
    good = tn in types and any(x[0]==mn and not x[1] for x in methods(tn)); print(('OK  ' if good else 'BAD ')+tn+'.'+mn+' (sim init)'); ok+=good; bad+=not good
# fields
for kind, T, F, name in re.findall(r'(Getter|Setter)<global::(\w+), ([\w<>, :.]+)>\("(\w+)"\)', src):
    t = md.tables['TypeDef'][types[T]-1]; ft=None
    for f in t['fields']:
        r=md.tables['Field'][f-1]
        if r['Name']==name: ft=md.field_sig(r['Sig'])
    want = F.replace('global::','').replace('Vector2','UnityEngine.Vector2').replace('Vector3','UnityEngine.Vector3').replace('Dictionary<int, BaseEntity>','System.Collections.Generic.Dictionary`2<int,BaseEntity>')
    good = ft==want; print(('OK  ' if good else 'BAD ')+'%s %s.%s field type %s vs %s'%(kind,T,name,ft,want)); ok+=good; bad+=not good
print('ok=%d bad=%d'%(ok,bad))
