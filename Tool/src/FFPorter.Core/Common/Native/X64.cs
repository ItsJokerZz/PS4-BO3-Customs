namespace FFPorter.Core.Common.Native;

internal sealed class X64
{
    public enum Reg { Rax, Rcx, Rdx, Rbx, Rsp, Rbp, Rsi, Rdi, R8, R9, R10, R11, R12, R13, R14, R15 }

    public sealed class Label
    {
        internal int Target = -1;
        internal readonly List<int> Fixups = [];
    }

    private readonly List<byte> _code = [];
    private int _unresolved;

    public int Length => _code.Count;

    public byte[] ToArray()
    {
        if (_unresolved != 0)
            throw new InvalidOperationException("A branch label was never bound");
        return [.. _code];
    }

    public X64 Raw(params ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
            _code.Add(value);
        return this;
    }

    public X64 Imm32(uint value)
    {
        for (int i = 0; i < 4; i++)
            _code.Add((byte)(value >> (8 * i)));
        return this;
    }

    public X64 Imm64(ulong value)
    {
        for (int i = 0; i < 8; i++)
            _code.Add((byte)(value >> (8 * i)));
        return this;
    }

    private static byte Rex(bool wide, int reg, int rm) => (byte)(0x40 | (wide ? 8 : 0) | ((reg >> 3) << 2) | (rm >> 3));

    private static byte ModRm(int mod, int reg, int rm) => (byte)((mod << 6) | ((reg & 7) << 3) | (rm & 7));

    private X64 Op(byte opcode, int reg, int rm) => Raw(Rex(true, reg, rm), opcode, ModRm(3, reg, rm));

    private X64 RspOperand(int reg, int displacement)
    {
        Raw(ModRm(2, reg, 4), 0x24);
        return Imm32((uint)displacement);
    }

    public X64 MovImm(Reg dst, ulong value)
    {
        Raw(Rex(true, 0, (int)dst), (byte)(0xB8 | ((int)dst & 7)));
        return Imm64(value);
    }

    public X64 MovImm32(Reg dst, int value)
    {
        Raw(Rex(true, 0, (int)dst), 0xC7, ModRm(3, 0, (int)dst));
        return Imm32((uint)value);
    }

    public X64 Mov(Reg dst, Reg src) => Op(0x89, (int)src, (int)dst);

    public X64 Add(Reg dst, Reg src) => Op(0x01, (int)src, (int)dst);

    public X64 AddImm32(Reg dst, int value)
    {
        Raw(Rex(true, 0, (int)dst), 0x81, ModRm(3, 0, (int)dst));
        return Imm32((uint)value);
    }

    public X64 Cmp(Reg left, Reg right) => Op(0x39, (int)right, (int)left);

    public X64 Lea(Reg dst, Reg @base, sbyte displacement)
    {
        if (((int)@base & 7) == 4)
            throw new ArgumentException("rsp/r12 bases need a SIB byte", nameof(@base));
        return Raw(Rex(true, (int)dst, (int)@base), 0x8D, ModRm(1, (int)dst, (int)@base), (byte)displacement);
    }

    public X64 StoreRsp(int displacement, Reg src)
    {
        Raw(Rex(true, (int)src, 4), 0x89);
        return RspOperand((int)src, displacement);
    }

    public X64 LoadRsp(Reg dst, int displacement)
    {
        Raw(Rex(true, (int)dst, 4), 0x8B);
        return RspOperand((int)dst, displacement);
    }

    public X64 SubRsp(int value)
    {
        Raw(0x48, 0x81, 0xEC);
        return Imm32((uint)value);
    }

    public X64 AddRsp(int value)
    {
        Raw(0x48, 0x81, 0xC4);
        return Imm32((uint)value);
    }

    public X64 Push(Reg reg)
    {
        if ((int)reg >= 8)
            Raw(0x41);
        return Raw((byte)(0x50 | ((int)reg & 7)));
    }

    public X64 Pop(Reg reg)
    {
        if ((int)reg >= 8)
            Raw(0x41);
        return Raw((byte)(0x58 | ((int)reg & 7)));
    }

    public X64 Call(Reg target)
    {
        if ((int)target >= 8)
            Raw(0x41);
        return Raw(0xFF, ModRm(3, 2, (int)target));
    }

    public X64 Ret() => Raw(0xC3);

    public X64 StoreXmm(int displacement, int xmm)
    {
        Raw(0xF3);
        if (xmm >= 8)
            Raw(0x44);
        Raw(0x0F, 0x7F);
        return RspOperand(xmm, displacement);
    }

    public X64 LoadXmm(int xmm, int displacement)
    {
        Raw(0xF3);
        if (xmm >= 8)
            Raw(0x44);
        Raw(0x0F, 0x6F);
        return RspOperand(xmm, displacement);
    }

    public X64 MovqToXmm(int xmm, Reg src) => Raw(0x66, Rex(true, xmm, (int)src), 0x0F, 0x6E, ModRm(3, xmm, (int)src));

    public X64 MovqFromXmm(Reg dst, int xmm) => Raw(0x66, Rex(true, xmm, (int)dst), 0x0F, 0x7E, ModRm(3, xmm, (int)dst));

    public X64 Pxor(int xmm)
    {
        Raw(0x66);
        if (xmm >= 8)
            Raw(0x45);
        return Raw(0x0F, 0xEF, ModRm(3, xmm, xmm));
    }

    public X64 Jmp(Label label) => Branch(label, 0xE9);

    public X64 Je(Label label) => Branch(label, 0x0F, 0x84);

    public X64 Jne(Label label) => Branch(label, 0x0F, 0x85);

    private X64 Branch(Label label, params ReadOnlySpan<byte> opcode)
    {
        Raw(opcode);
        int field = _code.Count;
        Imm32(0);
        if (label.Target >= 0)
        {
            Patch(field, label.Target);
        }
        else
        {
            label.Fixups.Add(field);
            _unresolved++;
        }
        return this;
    }

    public X64 Bind(Label label)
    {
        if (label.Target >= 0)
            throw new InvalidOperationException("Label bound twice");
        label.Target = _code.Count;
        foreach (int field in label.Fixups)
            Patch(field, label.Target);
        _unresolved -= label.Fixups.Count;
        label.Fixups.Clear();
        return this;
    }

    private void Patch(int field, int target)
    {
        int relative = target - (field + 4);
        for (int i = 0; i < 4; i++)
            _code[field + i] = (byte)(relative >> (8 * i));
    }
}
