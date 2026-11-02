"""Minimal ECMA-335 metadata reader + IL disassembler (no dependencies)."""
import struct, sys

# ---------------------------------------------------------------- PE / CLI
class PE:
    def __init__(self, data):
        self.d = data
        e = struct.unpack_from('<I', data, 0x3c)[0]
        nsec = struct.unpack_from('<H', data, e + 6)[0]
        optsz = struct.unpack_from('<H', data, e + 20)[0]
        opt = e + 24
        magic = struct.unpack_from('<H', data, opt)[0]
        dd = opt + (96 if magic == 0x10b else 112)
        self.dirs = [struct.unpack_from('<II', data, dd + 8 * i) for i in range(16)]
        self.secs = []
        s = opt + optsz
        for i in range(nsec):
            name, vsz, va, rsz, raw = struct.unpack_from('<8sIIII', data, s + 40 * i)
            self.secs.append((va, max(vsz, rsz), raw))
    def off(self, rva):
        for va, sz, raw in self.secs:
            if va <= rva < va + sz: return rva - va + raw
        raise ValueError(hex(rva))

# Table schemas. Column kinds: 2/4 = fixed ints, 's','g','b' heaps,
# ('t', n) simple index, ('c', name) coded index.
CODED = {
 'TypeDefOrRef': (2, [2, 1, 27]), 'HasConstant': (2, [4, 8, 23]),
 'HasCustomAttribute': (5, [6, 4, 1, 2, 8, 9, 10, 0, 14, 23, 20, 17, 26, 27, 32, 35, 38, 39, 40, 42, 44, 43]),
 'HasFieldMarshal': (1, [4, 8]), 'HasDeclSecurity': (2, [2, 6, 32]),
 'MemberRefParent': (3, [2, 1, 26, 6, 27]), 'HasSemantics': (1, [20, 23]),
 'MethodDefOrRef': (1, [6, 10]), 'MemberForwarded': (1, [4, 6]),
 'Implementation': (2, [38, 35, 39]), 'CustomAttributeType': (3, [None, None, 6, 10, None]),
 'ResolutionScope': (2, [0, 26, 35, 1]), 'TypeOrMethodDef': (1, [2, 6]),
}
T = lambda n: ('t', n)
C = lambda n: ('c', n)
SCHEMA = {
 0x00: ('Module', [('Generation', 2), ('Name', 's'), ('Mvid', 'g'), ('EncId', 'g'), ('EncBaseId', 'g')]),
 0x01: ('TypeRef', [('Scope', C('ResolutionScope')), ('Name', 's'), ('Namespace', 's')]),
 0x02: ('TypeDef', [('Flags', 4), ('Name', 's'), ('Namespace', 's'), ('Extends', C('TypeDefOrRef')), ('FieldList', T(4)), ('MethodList', T(6))]),
 0x03: ('FieldPtr', [('Field', T(4))]),
 0x04: ('Field', [('Flags', 2), ('Name', 's'), ('Sig', 'b')]),
 0x05: ('MethodPtr', [('Method', T(6))]),
 0x06: ('MethodDef', [('RVA', 4), ('ImplFlags', 2), ('Flags', 2), ('Name', 's'), ('Sig', 'b'), ('ParamList', T(8))]),
 0x07: ('ParamPtr', [('Param', T(8))]),
 0x08: ('Param', [('Flags', 2), ('Seq', 2), ('Name', 's')]),
 0x09: ('InterfaceImpl', [('Class', T(2)), ('Interface', C('TypeDefOrRef'))]),
 0x0a: ('MemberRef', [('Class', C('MemberRefParent')), ('Name', 's'), ('Sig', 'b')]),
 0x0b: ('Constant', [('Type', 2), ('Parent', C('HasConstant')), ('Value', 'b')]),
 0x0c: ('CustomAttribute', [('Parent', C('HasCustomAttribute')), ('Type', C('CustomAttributeType')), ('Value', 'b')]),
 0x0d: ('FieldMarshal', [('Parent', C('HasFieldMarshal')), ('Native', 'b')]),
 0x0e: ('DeclSecurity', [('Action', 2), ('Parent', C('HasDeclSecurity')), ('Perm', 'b')]),
 0x0f: ('ClassLayout', [('Pack', 2), ('Size', 4), ('Parent', T(2))]),
 0x10: ('FieldLayout', [('Offset', 4), ('Field', T(4))]),
 0x11: ('StandAloneSig', [('Sig', 'b')]),
 0x12: ('EventMap', [('Parent', T(2)), ('EventList', T(20))]),
 0x13: ('EventPtr', [('Event', T(20))]),
 0x14: ('Event', [('Flags', 2), ('Name', 's'), ('Type', C('TypeDefOrRef'))]),
 0x15: ('PropertyMap', [('Parent', T(2)), ('PropertyList', T(23))]),
 0x16: ('PropertyPtr', [('Property', T(23))]),
 0x17: ('Property', [('Flags', 2), ('Name', 's'), ('Type', 'b')]),
 0x18: ('MethodSemantics', [('Semantics', 2), ('Method', T(6)), ('Association', C('HasSemantics'))]),
 0x19: ('MethodImpl', [('Class', T(2)), ('Body', C('MethodDefOrRef')), ('Decl', C('MethodDefOrRef'))]),
 0x1a: ('ModuleRef', [('Name', 's')]),
 0x1b: ('TypeSpec', [('Sig', 'b')]),
 0x1c: ('ImplMap', [('Flags', 2), ('Member', C('MemberForwarded')), ('Name', 's'), ('Scope', T(26))]),
 0x1d: ('FieldRVA', [('RVA', 4), ('Field', T(4))]),
 0x1e: ('EncLog', [('Token', 4), ('Func', 4)]),
 0x1f: ('EncMap', [('Token', 4)]),
 0x20: ('Assembly', [('Hash', 4), ('Maj', 2), ('Min', 2), ('Bld', 2), ('Rev', 2), ('Flags', 4), ('Key', 'b'), ('Name', 's'), ('Culture', 's')]),
 0x21: ('AssemblyProcessor', [('P', 4)]),
 0x22: ('AssemblyOS', [('A', 4), ('B', 4), ('C', 4)]),
 0x23: ('AssemblyRef', [('Maj', 2), ('Min', 2), ('Bld', 2), ('Rev', 2), ('Flags', 4), ('Key', 'b'), ('Name', 's'), ('Culture', 's'), ('Hash', 'b')]),
 0x24: ('AssemblyRefProcessor', [('P', 4), ('Ref', T(35))]),
 0x25: ('AssemblyRefOS', [('A', 4), ('B', 4), ('C', 4), ('Ref', T(35))]),
 0x26: ('File', [('Flags', 4), ('Name', 's'), ('Hash', 'b')]),
 0x27: ('ExportedType', [('Flags', 4), ('TypeDefId', 4), ('Name', 's'), ('Namespace', 's'), ('Impl', C('Implementation'))]),
 0x28: ('ManifestResource', [('Offset', 4), ('Flags', 4), ('Name', 's'), ('Impl', C('Implementation'))]),
 0x29: ('NestedClass', [('Nested', T(2)), ('Enclosing', T(2))]),
 0x2a: ('GenericParam', [('Number', 2), ('Flags', 2), ('Owner', C('TypeOrMethodDef')), ('Name', 's')]),
 0x2b: ('MethodSpec', [('Method', C('MethodDefOrRef')), ('Inst', 'b')]),
 0x2c: ('GenericParamConstraint', [('Owner', T(42)), ('Constraint', C('TypeDefOrRef'))]),
}

class Meta:
    def __init__(self, data):
        self.pe = pe = PE(data)
        cli = pe.off(pe.dirs[14][0])
        mrva, msz = struct.unpack_from('<II', data, cli + 8)
        m = pe.off(mrva)
        vlen = struct.unpack_from('<I', data, m + 12)[0]
        p = m + 16 + vlen
        nstreams = struct.unpack_from('<H', data, p + 2)[0]; p += 4
        self.heaps = {}
        for _ in range(nstreams):
            o, sz = struct.unpack_from('<II', data, p); p += 8
            e = data.index(b'\0', p); name = data[p:e].decode()
            p = (e + 4) & ~3
            self.heaps[name] = data[m + o:m + o + sz]
        tb = self.heaps.get('#~') or self.heaps['#-']
        hs = tb[6]
        self.ws, self.wg, self.wb = (4 if hs & 1 else 2), (4 if hs & 2 else 2), (4 if hs & 4 else 2)
        valid = struct.unpack_from('<Q', tb, 8)[0]
        p = 24
        self.rows = {}
        for i in range(64):
            if valid >> i & 1:
                self.rows[i] = struct.unpack_from('<I', tb, p)[0]; p += 4
        if hs & 0x20: p += 4  # extra data
        self.tables = {}
        for i in sorted(self.rows):
            name, cols = SCHEMA[i]
            sizes = [self._size(k) for _, k in cols]
            rs = sum(sizes); n = self.rows[i]
            rows = []
            for r in range(n):
                q = p + r * rs; row = {}
                for (cn, k), sz in zip(cols, sizes):
                    v = struct.unpack_from('<H' if sz == 2 else '<I', tb, q)[0]; q += sz
                    if k == 's': v = self.string(v)
                    elif k == 'b': v = self.blob(v)
                    elif isinstance(k, tuple) and k[0] == 'c':
                        bits, tags = CODED[k[1]]
                        v = (tags[v & ((1 << bits) - 1)], v >> bits)
                    row[cn] = v
                rows.append(row)
            self.tables[name] = rows
            p += rs * n
        self._index()

    def _size(self, k):
        if k in (2, 4): return k
        if k == 's': return self.ws
        if k == 'g': return self.wg
        if k == 'b': return self.wb
        if k[0] == 't': return 4 if self.rows.get(k[1], 0) > 0xffff else 2
        bits, tags = CODED[k[1]]
        mx = max(self.rows.get(t, 0) for t in tags if t is not None)
        return 2 if mx < (1 << (16 - bits)) else 4

    def string(self, o):
        h = self.heaps['#Strings']; e = h.index(b'\0', o)
        return h[o:e].decode('utf-8', 'replace')
    def blob(self, o):
        h = self.heaps['#Blob']; n, l = cuint(h, o)
        return h[o + l:o + l + n]
    def ustring(self, o):
        h = self.heaps['#US']; n, l = cuint(h, o)
        return h[o + l:o + l + n - 1].decode('utf-16-le', 'replace') if n else ''

    def g(self, t, i): return self.tables.get(t, [None] * (i))[i - 1] if i else None

    def _index(self):
        td = self.tables.get('TypeDef', []); md = self.tables.get('MethodDef', []); fd = self.tables.get('Field', [])
        self.nested = {r['Nested']: r['Enclosing'] for r in self.tables.get('NestedClass', [])}
        self.method_owner = {}; self.field_owner = {}
        for i, t in enumerate(td, 1):
            ms = t['MethodList']; me = td[i]['MethodList'] if i < len(td) else len(md) + 1
            fs = t['FieldList']; fe = td[i]['FieldList'] if i < len(td) else len(fd) + 1
            t['methods'] = list(range(ms, me)); t['fields'] = list(range(fs, fe))
            for m in t['methods']: self.method_owner[m] = i
            for f in t['fields']: self.field_owner[f] = i

    def typedef_name(self, i):
        t = self.tables['TypeDef'][i - 1]
        n = t['Name']
        if i in self.nested: return self.typedef_name(self.nested[i]) + '/' + n
        return (t['Namespace'] + '.' if t['Namespace'] else '') + n
    def typeref_name(self, i):
        t = self.tables['TypeRef'][i - 1]
        n = (t['Namespace'] + '.' if t['Namespace'] else '') + t['Name']
        if t['Scope'][0] == 1: return self.typeref_name(t['Scope'][1]) + '/' + t['Name']
        return n
    def type_name(self, tag, i):
        if tag == 2: return self.typedef_name(i)
        if tag == 1: return self.typeref_name(i)
        if tag == 27: return self.sig_type(self.tables['TypeSpec'][i - 1]['Sig'], [0])
        return '?'

    # ---- signatures
    def sig_type(self, b, p):
        et = b[p[0]]; p[0] += 1
        simple = {1: 'void', 2: 'bool', 3: 'char', 4: 'sbyte', 5: 'byte', 6: 'short', 7: 'ushort', 8: 'int', 9: 'uint',
                  10: 'long', 11: 'ulong', 12: 'float', 13: 'double', 14: 'string', 0x16: 'TypedRef', 0x18: 'IntPtr', 0x19: 'UIntPtr', 0x1c: 'object'}
        if et in simple: return simple[et]
        if et in (0x11, 0x12):
            v, l = cuint(b, p[0]); p[0] += l
            return self.type_name(*[(2, 1, 27)[v & 3], v >> 2][:2])
        if et == 0x0f: return self.sig_type(b, p) + '*'
        if et == 0x10: return 'ref ' + self.sig_type(b, p)
        if et == 0x1d: return self.sig_type(b, p) + '[]'
        if et in (0x13, 0x1e):
            v, l = cuint(b, p[0]); p[0] += l; return ('!' if et == 0x13 else '!!') + str(v)
        if et == 0x15:
            g = self.sig_type(b, p); n, l = cuint(b, p[0]); p[0] += l
            return g + '<' + ','.join(self.sig_type(b, p) for _ in range(n)) + '>'
        if et == 0x14:
            t = self.sig_type(b, p); rank, l = cuint(b, p[0]); p[0] += l
            ns, l = cuint(b, p[0]); p[0] += l
            for _ in range(ns): _, l = cuint(b, p[0]); p[0] += l
            nl, l = cuint(b, p[0]); p[0] += l
            for _ in range(nl): _, l = cuint(b, p[0]); p[0] += l
            return t + '[' + ',' * (rank - 1) + ']'
        if et in (0x1f, 0x20):
            v, l = cuint(b, p[0]); p[0] += l; return self.sig_type(b, p)
        if et == 0x45: return self.sig_type(b, p)
        if et == 0x41: return '...'
        if et == 0x1b: return 'fnptr'
        return '?%x' % et
    def method_sig(self, b):
        p = [0]; cc = b[0]; p[0] = 1
        if cc & 0x10: _, l = cuint(b, 1); p[0] += l
        n, l = cuint(b, p[0]); p[0] += l
        ret = self.sig_type(b, p)
        args = [self.sig_type(b, p) for _ in range(n)]
        return ret, args, bool(cc & 0x20)
    def field_sig(self, b):
        return self.sig_type(b, [1])

    def token_name(self, tok):
        t, i = tok >> 24, tok & 0xffffff
        try:
            if t == 0x06:
                m = self.tables['MethodDef'][i - 1]
                return self.typedef_name(self.method_owner[i]) + '::' + m['Name'] + '(' + ','.join(self.method_sig(m['Sig'])[1]) + ')'
            if t == 0x04:
                return self.typedef_name(self.field_owner[i]) + '::' + self.tables['Field'][i - 1]['Name']
            if t == 0x0a:
                m = self.tables['MemberRef'][i - 1]; tag, ci = m['Class']
                owner = self.type_name(tag, ci) if tag in (1, 2, 27) else '?'
                if m['Sig'][0] == 6: return owner + '::' + m['Name']
                return owner + '::' + m['Name'] + '(' + ','.join(self.method_sig(m['Sig'])[1]) + ')'
            if t == 0x02: return self.typedef_name(i)
            if t == 0x01: return self.typeref_name(i)
            if t == 0x1b: return self.sig_type(self.tables['TypeSpec'][i - 1]['Sig'], [0])
            if t == 0x2b:
                ms = self.tables['MethodSpec'][i - 1]; tag, mi = ms['Method']
                return self.token_name(((0x06 if tag == 6 else 0x0a) << 24) | mi) + '<inst>'
            if t == 0x70: return repr(self.ustring(i))
            if t == 0x11: return 'sig'
        except Exception as e:
            return 'tok%08x(%s)' % (tok, e)
        return 'tok%08x' % tok

    # ---- IL
    def method_body(self, idx):
        m = self.tables['MethodDef'][idx - 1]
        if not m['RVA']: return None
        d = self.pe.d; o = self.pe.off(m['RVA'])
        h = d[o]
        if h & 3 == 2: return d[o + 1:o + 1 + (h >> 2)]
        flags, = struct.unpack_from('<H', d, o); size = (flags >> 12) * 4
        cs, = struct.unpack_from('<I', d, o + 4)
        return d[o + size:o + size + cs]

def cuint(b, o):
    x = b[o]
    if x & 0x80 == 0: return x, 1
    if x & 0xc0 == 0x80: return ((x & 0x3f) << 8) | b[o + 1], 2
    return ((x & 0x1f) << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3], 4

# opcode table: name, operand kind
OPS = {}
def _op(code, name, kind=''): OPS[code] = (name, kind)
for c, n in [(0, 'nop'), (1, 'break'), (2, 'ldarg.0'), (3, 'ldarg.1'), (4, 'ldarg.2'), (5, 'ldarg.3'), (6, 'ldloc.0'), (7, 'ldloc.1'), (8, 'ldloc.2'), (9, 'ldloc.3'),
             (0xa, 'stloc.0'), (0xb, 'stloc.1'), (0xc, 'stloc.2'), (0xd, 'stloc.3'), (0x14, 'ldnull'), (0x15, 'ldc.i4.m1')] + [(0x16 + i, 'ldc.i4.%d' % i) for i in range(9)] + \
            [(0x25, 'dup'), (0x26, 'pop'), (0x2a, 'ret'), (0x46, 'ldind.i1'), (0x47, 'ldind.u1'), (0x48, 'ldind.i2'), (0x49, 'ldind.u2'), (0x4a, 'ldind.i4'), (0x4b, 'ldind.u4'),
             (0x4c, 'ldind.i8'), (0x4d, 'ldind.i'), (0x4e, 'ldind.r4'), (0x4f, 'ldind.r8'), (0x50, 'ldind.ref'), (0x51, 'stind.ref'), (0x52, 'stind.i1'), (0x53, 'stind.i2'), (0x54, 'stind.i4'),
             (0x55, 'stind.i8'), (0x56, 'stind.r4'), (0x57, 'stind.r8'), (0x58, 'add'), (0x59, 'sub'), (0x5a, 'mul'), (0x5b, 'div'), (0x5c, 'div.un'), (0x5d, 'rem'), (0x5e, 'rem.un'),
             (0x5f, 'and'), (0x60, 'or'), (0x61, 'xor'), (0x62, 'shl'), (0x63, 'shr'), (0x64, 'shr.un'), (0x65, 'neg'), (0x66, 'not'), (0x67, 'conv.i1'), (0x68, 'conv.i2'), (0x69, 'conv.i4'),
             (0x6a, 'conv.i8'), (0x6b, 'conv.r4'), (0x6c, 'conv.r8'), (0x6d, 'conv.u4'), (0x6e, 'conv.u8'), (0x76, 'conv.r.un'), (0x7a, 'throw'), (0x8e, 'ldlen'),
             (0x90, 'ldelem.i1'), (0x91, 'ldelem.u1'), (0x92, 'ldelem.i2'), (0x93, 'ldelem.u2'), (0x94, 'ldelem.i4'), (0x95, 'ldelem.u4'), (0x96, 'ldelem.i8'), (0x97, 'ldelem.i'),
             (0x98, 'ldelem.r4'), (0x99, 'ldelem.r8'), (0x9a, 'ldelem.ref'), (0x9b, 'stelem.i'), (0x9c, 'stelem.i1'), (0x9d, 'stelem.i2'), (0x9e, 'stelem.i4'), (0x9f, 'stelem.i8'),
             (0xa0, 'stelem.r4'), (0xa1, 'stelem.r8'), (0xa2, 'stelem.ref'), (0xb3, 'conv.ovf.i1'), (0xb4, 'conv.ovf.u1'), (0xb5, 'conv.ovf.i2'), (0xb6, 'conv.ovf.u2'), (0xb7, 'conv.ovf.i4'),
             (0xb8, 'conv.ovf.u4'), (0xb9, 'conv.ovf.i8'), (0xba, 'conv.ovf.u8'), (0xc3, 'ckfinite'), (0xd1, 'conv.u2'), (0xd2, 'conv.u1'), (0xd3, 'conv.i'), (0xd4, 'conv.ovf.i'),
             (0xd5, 'conv.ovf.u'), (0xd6, 'add.ovf'), (0xd7, 'add.ovf.un'), (0xd8, 'mul.ovf'), (0xd9, 'mul.ovf.un'), (0xda, 'sub.ovf'), (0xdb, 'sub.ovf.un'), (0xdc, 'endfinally'),
             (0xdf, 'stind.i'), (0xe0, 'conv.u')]:
    _op(c, n)
for c, n in [(0xe, 'ldarg.s'), (0xf, 'ldarga.s'), (0x10, 'starg.s'), (0x11, 'ldloc.s'), (0x12, 'ldloca.s'), (0x13, 'stloc.s')]: _op(c, n, 'u8')
_op(0x1f, 'ldc.i4.s', 'i8'); _op(0x20, 'ldc.i4', 'i32'); _op(0x21, 'ldc.i8', 'i64'); _op(0x22, 'ldc.r4', 'r4'); _op(0x23, 'ldc.r8', 'r8')
for c, n in [(0x27, 'jmp'), (0x28, 'call'), (0x6f, 'callvirt'), (0x70, 'cpobj'), (0x71, 'ldobj'), (0x72, 'ldstr'), (0x73, 'newobj'), (0x74, 'castclass'), (0x75, 'isinst'), (0x79, 'unbox'),
             (0x7b, 'ldfld'), (0x7c, 'ldflda'), (0x7d, 'stfld'), (0x7e, 'ldsfld'), (0x7f, 'ldsflda'), (0x80, 'stsfld'), (0x81, 'stobj'), (0x8c, 'box'), (0x8d, 'newarr'), (0x8f, 'ldelema'),
             (0xa3, 'ldelem'), (0xa4, 'stelem'), (0xa5, 'unbox.any'), (0xc2, 'refanyval'), (0xc6, 'mkrefany'), (0xd0, 'ldtoken'), (0x29, 'calli')]: _op(c, n, 'tok')
for c, n in [(0x2b, 'br.s'), (0x2c, 'brfalse.s'), (0x2d, 'brtrue.s'), (0x2e, 'beq.s'), (0x2f, 'bge.s'), (0x30, 'bgt.s'), (0x31, 'ble.s'), (0x32, 'blt.s'), (0x33, 'bne.un.s'),
             (0x34, 'bge.un.s'), (0x35, 'bgt.un.s'), (0x36, 'ble.un.s'), (0x37, 'blt.un.s'), (0xde, 'leave.s')]: _op(c, n, 'br8')
for c, n in [(0x38, 'br'), (0x39, 'brfalse'), (0x3a, 'brtrue'), (0x3b, 'beq'), (0x3c, 'bge'), (0x3d, 'bgt'), (0x3e, 'ble'), (0x3f, 'blt'), (0x40, 'bne.un'), (0x41, 'bge.un'),
             (0x42, 'bgt.un'), (0x43, 'ble.un'), (0x44, 'blt.un'), (0xdd, 'leave')]: _op(c, n, 'br32')
_op(0x45, 'switch', 'sw')
OPS2 = {0x00: ('arglist', ''), 0x01: ('ceq', ''), 0x02: ('cgt', ''), 0x03: ('cgt.un', ''), 0x04: ('clt', ''), 0x05: ('clt.un', ''), 0x06: ('ldftn', 'tok'), 0x07: ('ldvirtftn', 'tok'),
        0x09: ('ldarg', 'u16'), 0x0a: ('ldarga', 'u16'), 0x0b: ('starg', 'u16'), 0x0c: ('ldloc', 'u16'), 0x0d: ('ldloca', 'u16'), 0x0e: ('stloc', 'u16'), 0x0f: ('localloc', ''),
        0x11: ('endfilter', ''), 0x12: ('unaligned.', 'u8'), 0x13: ('volatile.', ''), 0x14: ('tail.', ''), 0x15: ('initobj', 'tok'), 0x16: ('constrained.', 'tok'), 0x17: ('cpblk', ''),
        0x18: ('initblk', ''), 0x1a: ('rethrow', ''), 0x1c: ('sizeof', 'tok'), 0x1d: ('refanytype', ''), 0x1e: ('readonly.', '')}

def disasm(md, code):
    out = []; p = 0
    while p < len(code):
        start = p; b = code[p]; p += 1
        if b == 0xfe:
            name, kind = OPS2.get(code[p], ('?fe%02x' % code[p], '')); p += 1
        else:
            name, kind = OPS.get(b, ('?%02x' % b, ''))
        arg = ''
        if kind == 'u8': arg = str(code[p]); p += 1
        elif kind == 'i8': arg = str(struct.unpack_from('<b', code, p)[0]); p += 1
        elif kind == 'u16': arg = str(struct.unpack_from('<H', code, p)[0]); p += 2
        elif kind == 'i32': arg = str(struct.unpack_from('<i', code, p)[0]); p += 4
        elif kind == 'i64': arg = str(struct.unpack_from('<q', code, p)[0]); p += 8
        elif kind == 'r4': arg = repr(struct.unpack_from('<f', code, p)[0]); p += 4
        elif kind == 'r8': arg = repr(struct.unpack_from('<d', code, p)[0]); p += 8
        elif kind == 'tok': t = struct.unpack_from('<I', code, p)[0]; p += 4; arg = md.token_name(t)
        elif kind == 'br8': d = struct.unpack_from('<b', code, p)[0]; p += 1; arg = 'IL_%04x' % (p + d)
        elif kind == 'br32': d = struct.unpack_from('<i', code, p)[0]; p += 4; arg = 'IL_%04x' % (p + d)
        elif kind == 'sw':
            n = struct.unpack_from('<I', code, p)[0]; p += 4
            ts = [struct.unpack_from('<i', code, p + 4 * i)[0] for i in range(n)]; p += 4 * n
            arg = ','.join('IL_%04x' % (p + t) for t in ts)
        out.append('  IL_%04x: %s %s' % (start, name, arg))
    return out

def dump(path, out, with_il=True):
    md = Meta(open(path, 'rb').read())
    fl = open(out, 'w')
    for i, t in enumerate(md.tables.get('TypeDef', []), 1):
        ext = t['Extends']
        base = md.type_name(*ext) if ext[1] else ''
        fl.write('\nTYPE %s : %s\n' % (md.typedef_name(i), base))
        for f in t['fields']:
            fr = md.tables['Field'][f - 1]
            st = 'static ' if fr['Flags'] & 0x10 else ''
            fl.write('  F %s%s %s\n' % (st, md.field_sig(fr['Sig']), fr['Name']))
        for m in t['methods']:
            mr = md.tables['MethodDef'][m - 1]
            ret, args, inst = md.method_sig(mr['Sig'])
            st = '' if inst else 'static '
            fl.write('  M %s%s %s(%s)\n' % (st, ret, mr['Name'], ', '.join(args)))
            if with_il:
                body = md.method_body(m)
                if body is not None:
                    try: fl.write('\n'.join(disasm(md, body)) + '\n')
                    except Exception as e: fl.write('  <disasm error %s>\n' % e)
    fl.close()
    return md

if __name__ == '__main__':
    dump(sys.argv[1], sys.argv[2], with_il=len(sys.argv) < 4)
