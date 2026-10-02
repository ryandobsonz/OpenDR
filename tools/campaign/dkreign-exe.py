"""Reads the original game's code: dkreign.exe is packed (its code is compressed into the file and unpacked
by a stub at start), so this runs the stub in an x86 emulator until it jumps into the unpacked code, and
saves the memory image. Then it finds strings, the code that uses an address, and disassembles.
Needs: pip install unicorn capstone pefile

    python tools/campaign/dkreign-exe.py unpack                  # DrData/dkreign.exe -> tools/campaign/out/dkreign.mem
    python tools/campaign/dkreign-exe.py strings "cube|punct"    # strings matching a regex, with their addresses
    python tools/campaign/dkreign-exe.py xrefs 0x60e9a8          # where the code uses that address
    python tools/campaign/dkreign-exe.py dis 0x586000 0x586420   # disassembly, strings it names shown

Addresses are virtual (the image is based at 0x400000)."""
import os, re, struct, sys

root = os.path.normpath(os.path.join(os.path.dirname(__file__), '..', '..'))
EXE = os.path.join(root, 'DrData', 'dkreign.exe')
MEM = os.path.join(root, 'tools', 'campaign', 'out', 'dkreign.mem')
BASE = 0x400000


def unpack():
    import pefile
    from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
    from unicorn.x86_const import UC_X86_REG_ESP, UC_X86_REG_EAX, UC_X86_REG_EIP

    pe = pefile.PE(EXE)
    size = pe.OPTIONAL_HEADER.SizeOfImage
    data = open(EXE, 'rb').read()
    mu = Uc(UC_ARCH_X86, UC_MODE_32)
    mu.mem_map(BASE, (size + 0xfff) & ~0xfff)
    mu.mem_write(BASE, pe.header)
    for s in pe.sections:
        mu.mem_write(BASE + s.VirtualAddress, s.get_data())

    # Every import, and every function the stub looks up, is a RET at its own address, answered below.
    stub, stubs = 0x10000000, {}
    mu.mem_map(stub, 0x100000)

    def add_stub(name):
        addr = stub + 16 * len(stubs)
        stubs[addr] = name
        mu.mem_write(addr, b'\xc3')
        return addr

    for entry in pe.DIRECTORY_ENTRY_IMPORT:
        for imp in entry.imports:
            mu.mem_write(imp.address, struct.pack('<I', add_stub(imp.name.decode() if imp.name else f'ord{imp.ordinal}')))

    mu.mem_map(0x80000, 0x100000)
    mu.reg_write(UC_X86_REG_ESP, 0x100000)
    heap = [0x20000000]
    mu.mem_map(heap[0], 0x4000000)
    state = {'pos': 0, 'entry': None}
    libs = {}

    def alloc(n):
        a = heap[0]
        heap[0] += (n + 0xffff) & ~0xfff
        return a

    def cstr(a):
        return bytes(mu.mem_read(a, 64)).split(bytes(1))[0].decode()

    # name: (arguments, result from the arguments)
    calls = {
        'GlobalAlloc': (2, lambda a: alloc(a[1])), 'VirtualAlloc': (4, lambda a: alloc(a[1])),
        'GlobalLock': (1, lambda a: a[0]), 'GlobalUnlock': (1, lambda a: 0), 'GlobalFree': (1, lambda a: 0),
        'VirtualFree': (3, lambda a: 0), 'VirtualProtect': (4, lambda a: 1), 'FlushInstructionCache': (3, lambda a: 1),
        'GetModuleHandleA': (1, lambda a: BASE), 'GetVersion': (0, lambda a: 0), 'GetLastError': (0, lambda a: 0),
        'LoadLibraryA': (1, lambda a: libs.setdefault(cstr(a[0]), 0x30000000 + 0x10000 * len(libs))),
        'GetProcAddress': (2, lambda a: add_stub(cstr(a[1]) if a[1] > 0xffff else f'ord{a[1]}')),
        'CreateFileA': (7, lambda a: 0x77), 'CloseHandle': (1, lambda a: 1), 'GetFileSize': (2, lambda a: len(data)),
        'CreateFileMappingA': (6, lambda a: 0x78), 'UnmapViewOfFile': (1, lambda a: 1),
    }

    def module_file_name(a):
        name = b'C:\\dark\\dkreign.exe' + bytes(1)
        mu.mem_write(a[1], name)
        return len(name) - 1

    def read_file(a):
        chunk = data[state['pos']:state['pos'] + a[2]]
        mu.mem_write(a[1], chunk)
        state['pos'] += len(chunk)
        if a[3]:
            mu.mem_write(a[3], struct.pack('<I', len(chunk)))
        return 1

    def seek(a):
        distance = struct.unpack('<i', struct.pack('<I', a[1]))[0]
        state['pos'] = [distance, state['pos'] + distance, len(data) + distance][a[3]]
        return state['pos']

    def map_view(a):
        view = alloc(len(data))
        mu.mem_write(view, data[a[3]:])
        return view

    calls.update({'GetModuleFileNameA': (3, module_file_name), 'ReadFile': (5, read_file),
                  'SetFilePointer': (4, seek), 'MapViewOfFile': (5, map_view)})
    stub_end = BASE + 0x1000 + pe.sections[0].SizeOfRawData
    code_end = BASE + pe.sections[0].VirtualAddress + pe.sections[0].Misc_VirtualSize

    def hook(uc, address, size, user):
        if address in stubs:
            name = stubs[address]
            esp = uc.reg_read(UC_X86_REG_ESP)
            ret = struct.unpack('<I', uc.mem_read(esp, 4))[0]
            if name not in calls:
                print(f'The stub calls {name}, which this does not answer')
                uc.emu_stop()
                return

            n, f = calls[name]
            uc.reg_write(UC_X86_REG_EAX, f(struct.unpack('<7I', uc.mem_read(esp + 4, 28))) & 0xffffffff)
            uc.reg_write(UC_X86_REG_ESP, esp + 4 + 4 * n)
            uc.reg_write(UC_X86_REG_EIP, ret)
        else:
            state['entry'] = address
            uc.emu_stop()

    mu.hook_add(UC_HOOK_CODE, hook, None, stub, stub + 0x100000)
    mu.hook_add(UC_HOOK_CODE, hook, None, stub_end, code_end)
    mu.emu_start(BASE + pe.OPTIONAL_HEADER.AddressOfEntryPoint, 0)
    if state['entry'] is None:
        sys.exit('The stub did not reach the unpacked code')

    os.makedirs(os.path.dirname(MEM), exist_ok=True)
    open(MEM, 'wb').write(bytes(mu.mem_read(BASE, size)))
    print(f'Unpacked; the game starts at {state["entry"]:#x}. Saved {MEM}')


def image():
    if not os.path.exists(MEM):
        sys.exit('Run unpack first')
    return open(MEM, 'rb').read()


def string_at(d, a):
    p = a - BASE
    if not 0 <= p < len(d):
        return None
    end = d.find(bytes(1), p)
    s = d[p:end]
    return s.decode('ascii') if 0 < len(s) < 80 and all(32 <= c < 127 for c in s) else None


def disassemble(d, start, end):
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    for i in Cs(CS_ARCH_X86, CS_MODE_32).disasm(d[start - BASE:end - BASE], start):
        names = [string_at(d, int(t, 16)) for t in re.findall(r'0x[0-9a-f]{6,8}', i.op_str)]
        named = ''.join(f'  "{n}"' for n in names if n)
        print(f'{i.address:x}: {i.mnemonic} {i.op_str}{named}')


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)

    command = sys.argv[1]
    if command == 'unpack':
        unpack()
    elif command == 'strings':
        d = image()
        for m in re.finditer(rb'[\x20-\x7e]{4,}', d):
            if re.search(sys.argv[2], m.group().decode(), re.I):
                print(f'{BASE + m.start():x}: {m.group().decode()}')
    elif command == 'xrefs':
        d = image()
        for m in re.finditer(re.escape(struct.pack('<I', int(sys.argv[2], 16))), d):
            print(f'{BASE + m.start():x}')
    elif command == 'dis':
        disassemble(image(), int(sys.argv[2], 16), int(sys.argv[3], 16))
    else:
        sys.exit(__doc__)


main()
