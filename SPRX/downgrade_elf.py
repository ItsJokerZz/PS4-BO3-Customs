#!/usr/bin/env python3
# (c) flatz

import sys, os, struct
import argparse
import shutil

from pprint import pprint

def align_up(x, alignment):
	return (x + (alignment - 1)) & ~(alignment - 1)

def align_down(x, alignment):
	return x & ~(alignment - 1)

def is_intervals_overlap(p1, p2):
	return p1[0] <= p2[1] and p1[1] <= p2[0]

def check_file_magic(f, expected_magic):
	old_offset = f.tell()
	try:
		magic = f.read(len(expected_magic))
	except:
		return False
	finally:
		f.seek(old_offset)
	return magic == expected_magic

def check_sdk_version(sdk_version):
	if len(sdk_version) != 10:
		return False
	parts = sdk_version.split('.', 2)
	if len(parts) != 3:
		return False
	try:
		lengths = [2, 3, 3]
		for i, n in enumerate(parts):
			if len(n) != lengths[i]:
				return False
			n = int(n, 10)
	except:
		return False
	return True

def parse_sdk_version(sdk_version):
	major, minor, patch = sdk_version >> 24, (sdk_version >> 12) & 0xFFF, sdk_version & 0xFFF
	return major, minor, patch

def stringify_sdk_version(major, minor, patch):
	return '{0:02x}.{1:03x}.{2:03x}'.format(major, minor, patch)

def unstringify_sdk_version(sdk_version):
	major, minor, patch = map(lambda x: int(x, 16), sdk_version.split('.', 2))
	return major, minor, patch

def build_sdk_version(major, minor, patch):
	sdk_version = ((major & 0xFF) << 24) | ((minor & 0xFFF) << 12) | (patch & 0xFFF)
	return sdk_version

class ElfProgramHeader(object):
	FMT = '<2I6Q'

	PT_NULL = 0x0
	PT_LOAD = 0x1
	PT_DYNAMIC = 0x2
	PT_INTERP = 0x3
	PT_TLS = 0x7
	PT_SCE_DYNLIBDATA = 0x61000000
	PT_SCE_PROCPARAM = 0x61000001
	PT_SCE_MODULE_PARAM = 0x61000002
	PT_SCE_RELRO = 0x61000010
	PT_SCE_COMMENT = 0x6FFFFF00
	PT_SCE_VERSION = 0x6FFFFF01
	PT_GNU_EH_FRAME = 0x6474E550

	PF_X = 0x1
	PF_W = 0x2
	PF_R = 0x4
	PF_RX = PF_R | PF_X
	PF_RW = PF_R | PF_W

	def __init__(self):
		self.type = None
		self.offset = None
		self.vaddr = None
		self.paddr = None
		self.file_size = None
		self.mem_size = None
		self.flags = None
		self.align = None

	def load(self, f):
		data = f.read(struct.calcsize(ElfProgramHeader.FMT))
		if len(data) != struct.calcsize(ElfProgramHeader.FMT):
			return False
		self.type, self.flags, self.offset, self.vaddr, self.paddr, self.file_size, self.mem_size, self.align = struct.unpack(ElfProgramHeader.FMT, data)
		return True

	def save(self, f):
		data = struct.pack(ElfProgramHeader.FMT, self.type, self.flags, self.offset, self.vaddr, self.paddr, self.file_size, self.mem_size, self.align)
		if len(data) != struct.calcsize(ElfProgramHeader.FMT):
			return False
		f.write(data)
		return True

class ElfSectionHeader(object):
	FMT = '<2I4Q2I2Q'

	def __init__(self, fmt=None):
		self.name = None
		self.type = None
		self.flags = None
		self.addr = None
		self.offset = None
		self.size = None
		self.link = None
		self.info = None
		self.align = None
		self.entry_size = None

	def load(self, f):
		data = f.read(struct.calcsize(ElfProgramHeader.FMT))
		if len(data) != struct.calcsize(ElfProgramHeader.FMT):
			return False
		self.name, self.type, self.flags, self.addr, self.offset, self.size, self.link, self.info, self.align, self.entry_size = struct.unpack(ElfProgramHeader.FMT, data)
		return True

	def save(self, f):
		data = struct.pack(ElfProgramHeader.FMT, self.name, self.type, self.flags, self.addr, self.offset, self.size, self.link, self.info, self.align, self.entry_size)
		if len(data) != struct.calcsize(ElfProgramHeader.FMT):
			return False
		f.write(data)
		return True

class ElfFile(object):
	MAGIC = b'\x7FELF'

	FMT = '<4s5B6xB2HI3QI6H'

	CLASS_NONE = 0
	CLASS_64 = 2

	DATA_NONE = 0
	DATA_LSB = 1

	VERSION_CURRENT = 1

	MACHINE_X86_64 = 0x3E

	TYPE_EXEC = 0x2
	TYPE_SCE_EXEC = 0xFE00
	TYPE_SCE_EXEC_ASLR = 0xFE10
	TYPE_SCE_DYNAMIC = 0xFE18

	def __init__(self):
		self.magic = None
		self.cls = None
		self.encoding = None
		self.version = None
		self.os_abi = None
		self.abi_version = None
		self.nident_size = None
		self.type = None
		self.machine = None
		self.version = None
		self.entry = None
		self.phdr_offset = None
		self.shdr_offset = None
		self.flags = None
		self.ehdr_size = None
		self.phdr_size = None
		self.phdr_count = None
		self.shdr_size = None
		self.shdr_count = None
		self.shdr_strtable_idx = None

		self.phdrs = None
		self.shdrs = None

	def check(self, f):
		old_offset = f.tell()
		try:
			result = check_file_magic(f, ElfFile.MAGIC)
			print('elf magic: {0}'.format('ok' if result else 'invalid'))
		except:
			return False
		finally:
			f.seek(old_offset)
		return result

	def load(self, f):
		data = f.read(struct.calcsize(ElfFile.FMT))
		if len(data) != struct.calcsize(ElfFile.FMT):
			print('error: unable to read header')
			return False

		self.magic, self.cls, self.encoding, self.legacy_version, self.os_abi, self.abi_version, self.nident_size, self.type, self.machine, self.version, self.entry, self.phdr_offset, self.shdr_offset, self.flags, self.ehdr_size, self.phdr_size, self.phdr_count, self.shdr_size, self.shdr_count, self.shdr_strtable_idx = struct.unpack(ElfFile.FMT, data)
		if self.magic != ElfFile.MAGIC:
			print('error: invalid magic: {0}'.format(self.magic))
			return False
		if self.encoding != ElfFile.DATA_LSB:
			print('error: unsupported encoding: 0x{0:02X}'.format(self.encoding))
			return False
		if self.legacy_version != ElfFile.VERSION_CURRENT:
			raise Exception('Unsupported version: 0x{0:x}'.format(self.version))
		if self.cls != ElfFile.CLASS_64:
			print('error: unsupported class: 0x{0:02X}'.format(self.cls))
			return False
		if self.type not in [ElfFile.TYPE_SCE_EXEC, ElfFile.TYPE_SCE_EXEC_ASLR, ElfFile.TYPE_SCE_DYNAMIC]:
			print('error: unsupported type: 0x{0:04X}'.format(self.type))
			return False
		if self.machine != ElfFile.MACHINE_X86_64:
			print('error: unexpected machine: 0x{0:X}'.format(self.machine))
			return False
		if self.ehdr_size != struct.calcsize(ElfFile.FMT):
			print('error: invalid elf header size: 0x{0:X}'.format(self.ehdr_size))
			return False
		if self.phdr_size > 0 and self.phdr_size != struct.calcsize(ElfProgramHeader.FMT):
			print('error: invalid program header size: 0x{0:X}'.format(self.phdr_size))
			return False
		if self.shdr_size > 0 and self.shdr_size != struct.calcsize(ElfSectionHeader.FMT):
			print('error: invalid section header size: 0x{0:X}'.format(self.shdr_size))
			return False

		self.phdrs = []
		for i in range(self.phdr_count):
			phdr = ElfProgramHeader()
			f.seek(self.phdr_offset + i * self.phdr_size)
			if not phdr.load(f):
				print('error: unable to load program header #{0}'.format(i))
				return False
			self.phdrs.append(phdr)

		self.shdrs = []

		return True

	def save_hdr(self, f):
		data = struct.pack(ElfFile.FMT, self.magic, self.cls, self.encoding, self.legacy_version, self.os_abi, self.abi_version, self.nident_size, self.type, self.machine, self.version, self.entry, self.phdr_offset, self.shdr_offset, self.flags, self.ehdr_size, self.phdr_size, self.phdr_count, self.shdr_size, self.shdr_count, self.shdr_strtable_idx)
		if len(data) != struct.calcsize(ElfFile.FMT):
			print('error: unable to save header')
			return False
		f.write(data)

		for i, phdr in enumerate(self.phdrs):
			f.seek(self.phdr_offset + i * self.phdr_size)
			if not phdr.save(f):
				print('error: unable to save program header #{0}'.format(i))
				return False

		for i, shdr in enumerate(self.shdrs):
			f.seek(self.shdr_offset + i * self.shdr_size)
			if not shdr.save(f):
				print('error: unable to save section header #{0}'.format(i))
				return False

		return True

	def get_phdr_by_type(self, type):
		for i, phdr in enumerate(elf.phdrs):
			if phdr.type == type:
				return phdr
		return None

class MyParser(argparse.ArgumentParser):
	def error(self, message):
		self.print_help()
		sys.stderr.write('\nerror: {0}\n'.format(message))
		sys.exit(2)

parser = MyParser(description='elf downgrader tool')
parser.add_argument('input', type=str, help='old file')
parser.add_argument('output', type=str, help='new file')
parser.add_argument('--verbose', action='store_true', default=False, help='show details')
parser.add_argument('--sdk-version', type=str, required=True, default='06.200.001', help='needed sdk version')

if len(sys.argv) == 1:
	parser.print_usage()
	sys.exit(1)

args = parser.parse_args()

input_file_path = args.input
if not os.path.isfile(input_file_path):
	parser.error('invalid input file: {0}'.format(input_file_path))

output_file_path = args.output
if os.path.exists(output_file_path) and not os.path.isfile(output_file_path):
	parser.error('invalid output file: {0}'.format(output_file_path))

if args.sdk_version and not check_sdk_version(args.sdk_version):
	parser.error('bad sdk version')

shutil.copyfile(input_file_path, output_file_path)

if args.verbose:
	print('processing elf file: {0}'.format(output_file_path))
with open(output_file_path, 'r+b') as f:
	elf = ElfFile()
	if not elf.check(f):
		print('error: invalid elf file format')
		sys.exit(1)
	if not elf.load(f):
		print('error: unable to load elf file')
		sys.exit(1)


	if elf.type in [ElfFile.TYPE_SCE_EXEC, ElfFile.TYPE_SCE_EXEC_ASLR]:
		needed_type = ElfProgramHeader.PT_SCE_PROCPARAM
		param_magic = b'ORBI'
		if args.verbose:
			print('executable file detected')
	elif elf.type == ElfFile.TYPE_SCE_DYNAMIC:
		needed_type = ElfProgramHeader.PT_SCE_MODULE_PARAM
		param_magic = b'\xBF\xF4\x13\x3C'
		if args.verbose:
			print('module file detected')
	else:
		print('error: unsupported elf type')
		sys.exit(1)

	major, minor, patch = unstringify_sdk_version(args.sdk_version)
	new_sdk_version = build_sdk_version(major, minor, patch)
	new_sdk_version_str = stringify_sdk_version(major, minor, patch)
	if args.verbose:
		print('wanted sdk version: {0}'.format(new_sdk_version_str))

	if args.verbose:
		print('searching for {0} param segment...'.format('proc' if needed_type == ElfProgramHeader.PT_SCE_PROCPARAM else 'module'))
	phdr = elf.get_phdr_by_type(needed_type)
	if phdr is not None:
		if args.verbose:
			print('parsing param structure...')
		f.seek(phdr.offset)
		data = f.read(phdr.file_size)
		if len(data) != phdr.file_size:
			print('error: insufficient data read')
			sys.exit(1)
		param_size, = struct.unpack('<I', data[0x0:0x4])
		if param_size < 0x14:
			print('error: param structure is too small')
			sys.exit(1)
		data = data[:param_size]
		if data[0x8:0xC] != param_magic:
			print('error: unexpected param structure format')
			sys.exit(1)

		old_sdk_version, = struct.unpack('<I', data[0x10:0x14])
		major, minor, patch = parse_sdk_version(old_sdk_version)
		old_sdk_version_str = stringify_sdk_version(major, minor, patch)
		if args.verbose:
			print('sdk version: {0}'.format(old_sdk_version_str))

		if old_sdk_version > new_sdk_version:
			if args.verbose:
				print('fixing param structure...')
			f.seek(phdr.offset + 0x10)
			f.write(struct.pack('<I', new_sdk_version))
	else:
		print('warning: param segment not found (elf from old sdk?)')


	if new_sdk_version < 0x06000000:
		segs = []
		for i, phdr in enumerate(elf.phdrs):
			if phdr.type not in [ElfProgramHeader.PT_LOAD, ElfProgramHeader.PT_SCE_RELRO]:
				continue
			if phdr.type == ElfProgramHeader.PT_LOAD and phdr.flags == ElfProgramHeader.PF_RX:
				continue
			segs.append(phdr)


		segs.sort(key=lambda x: (x.vaddr, -(x.vaddr + x.mem_size)))

		i, count = 0, len(segs)
		while i < count:
			if i > 0 and (segs[i].vaddr >= segs[i - 1].vaddr and (segs[i].vaddr + segs[i].mem_size <= segs[i - 1].vaddr + segs[i - 1].mem_size)):
				segs = segs[:i] + segs[i + 1:]
				count -= 1
				continue
			i += 1

		count = len(segs)
		has_changes = False
		for i in range(count):
			mem_size_aligned = align_up(segs[i].mem_size, 0x4000)
			if (i + 1) < count and (segs[i].vaddr + mem_size_aligned) < segs[i + 1].vaddr:
				segs[i].mem_size = segs[i + 1].vaddr - segs[i].vaddr
				has_changes = True



		if has_changes:
			if args.verbose:
				print('removing memory holes...')


	if args.verbose:
		print('searching for version segment...')
	phdr = elf.get_phdr_by_type(ElfProgramHeader.PT_SCE_VERSION)
	if phdr is not None:
		if args.verbose:
			print('parsing library list...')
		f.seek(phdr.offset)
		data = f.read(phdr.file_size)
		if len(data) != phdr.file_size:
			print('error: insufficient data read')
			sys.exit(1)

		has_changes = False
		if phdr.file_size > 0:
			offset = 0
			while offset < phdr.file_size:
				length = data[offset]
				offset += 1
				name = data[offset:offset + length]
				name, old_sdk_version = name.split(b':', 1)
				if len(old_sdk_version) != struct.calcsize('I'):
					print('error: unexpected library list entry format')
					sys.exit(1)

				old_sdk_version, = struct.unpack('>I', old_sdk_version)
				major, minor, patch = parse_sdk_version(old_sdk_version)
				old_sdk_version_str = stringify_sdk_version(major, minor, patch)
				if args.verbose:
					print('{0} (sdk version: {1})'.format(name.decode('ascii'), old_sdk_version_str))

				if old_sdk_version > new_sdk_version:
					data = data[:offset] + name + b':' + struct.pack('>I', new_sdk_version) + data[offset + length:]
					has_changes = True

				offset += length

			if has_changes:
				if args.verbose:
					print('fixing sdk versions in library list...')
				f.seek(phdr.offset)
				f.write(data)
	else:
		if args.verbose:
			print('version segment not found')


	if args.verbose:
		print('fixing elf header...')

	elf.shdr_offset = 0
	elf.shdr_count = 0

	f.seek(0)
	if not elf.save_hdr(f):
		print('error: unable to save elf file')
		sys.exit(1)

if args.verbose:
	print('done')
