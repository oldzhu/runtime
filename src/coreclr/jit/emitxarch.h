// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if defined(TARGET_XARCH)

/************************************************************************/
/*           Public inline informational methods                        */
/************************************************************************/

public:
inline static bool isGeneralRegister(regNumber reg)
{
    return (reg <= REG_INT_LAST);
}

inline static bool isFloatReg(regNumber reg)
{
    return (reg >= REG_FP_FIRST && reg <= REG_FP_LAST);
}

inline static bool isDoubleReg(regNumber reg)
{
    return isFloatReg(reg);
}

inline static bool isMaskReg(regNumber reg)
{
    return (reg >= REG_MASK_FIRST && reg <= REG_MASK_LAST);
}

inline static bool isHighSimdReg(regNumber reg)
{
#ifdef TARGET_AMD64
    return ((reg >= REG_XMM16) && (reg <= REG_XMM31));
#else
    // X86 JIT operates in 32-bit mode and hence extended regs are not available.
    return false;
#endif
}

inline static bool isHighGPReg(regNumber reg)
{
#ifdef TARGET_AMD64
    return ((reg >= REG_R16) && (reg <= REG_R31));
#else
    // X86 JIT operates in 32-bit mode and hence extended regs are not available.
    return false;
#endif
}

/************************************************************************/
/*         Routines that compute the size of / encode instructions      */
/************************************************************************/

// code_t is a type used to accumulate bits of opcode + prefixes. On amd64, it must be 64 bits
// to support the REX prefixes. On both x86 and amd64, it must be 64 bits to support AVX, with
// its 3-byte VEX prefix.
typedef uint64_t code_t;

struct CnsVal
{
    ssize_t cnsVal;
    bool    cnsReloc;
};

UNATIVE_OFFSET emitInsSize(instrDesc* id, code_t code, bool includeRexPrefixSize);
UNATIVE_OFFSET emitInsSizeSVCalcDisp(instrDesc* id, code_t code, int var, int dsp);
UNATIVE_OFFSET emitInsSizeSV(instrDesc* id, code_t code, int var, int dsp);
UNATIVE_OFFSET emitInsSizeSV(instrDesc* id, code_t code, int var, int dsp, int val);
UNATIVE_OFFSET emitInsSizeRR(instrDesc* id, code_t code);
UNATIVE_OFFSET emitInsSizeRR(instrDesc* id, code_t code, int val);
UNATIVE_OFFSET emitInsSizeRR(instrDesc* id);
UNATIVE_OFFSET emitInsSizeAM(instrDesc* id, code_t code);
UNATIVE_OFFSET emitInsSizeAM(instrDesc* id, code_t code, int val);
UNATIVE_OFFSET emitInsSizeCV(instrDesc* id, code_t code);
UNATIVE_OFFSET emitInsSizeCV(instrDesc* id, code_t code, int val);

BYTE* emitOutputData16(BYTE* dst);
BYTE* emitOutputNOP(BYTE* dst, size_t nBytes);
BYTE* emitOutputAlign(insGroup* ig, instrDesc* id, BYTE* dst);
BYTE* emitOutputAM(BYTE* dst, instrDesc* id, code_t code, CnsVal* addc = nullptr);
BYTE* emitOutputSV(BYTE* dst, instrDesc* id, code_t code, CnsVal* addc = nullptr);
BYTE* emitOutputCV(BYTE* dst, instrDesc* id, code_t code, CnsVal* addc = nullptr);

BYTE* emitOutputR(BYTE* dst, instrDesc* id);
BYTE* emitOutputRI(BYTE* dst, instrDesc* id);
BYTE* emitOutputRR(BYTE* dst, instrDesc* id);
BYTE* emitOutputIV(BYTE* dst, instrDesc* id);

BYTE* emitOutputRRR(BYTE* dst, instrDesc* id);

BYTE* emitOutputLJ(insGroup* ig, BYTE* dst, instrDesc* id);

unsigned emitOutputRexOrSimdPrefixIfNeeded(instruction ins, BYTE* dst, code_t& code);
unsigned emitGetRexPrefixSize(instruction ins);
unsigned emitGetRexPrefixSize(instrDesc* id, instruction ins);
unsigned emitGetVexPrefixSize(instrDesc* id) const;
unsigned emitGetEvexPrefixSize(instrDesc* id) const;
unsigned emitGetPrefixSize(instrDesc* id, code_t code, bool includeRexPrefixSize);
unsigned emitGetAdjustedSize(instrDesc* id, code_t code) const;

code_t emitExtractRex2Prefix(instruction ins, code_t& code) const;
code_t emitExtractVexPrefix(instruction ins, code_t& code) const;
code_t emitExtractEvexPrefix(instruction ins, code_t& code) const;

unsigned insEncodeReg012(const instrDesc* id, regNumber reg, emitAttr size, code_t* code);
unsigned insEncodeReg345(const instrDesc* id, regNumber reg, emitAttr size, code_t* code);
code_t   insEncodeReg3456(const instrDesc* id, regNumber reg, emitAttr size, code_t code);
unsigned insEncodeRegSIB(const instrDesc* id, regNumber reg, code_t* code);

code_t insEncodeMRreg(const instrDesc* id, code_t code);
code_t insEncodeRMreg(const instrDesc* id, code_t code);
code_t insEncodeMRreg(const instrDesc* id, regNumber reg, emitAttr size, code_t code);
code_t insEncodeRRIb(const instrDesc* id, regNumber reg, emitAttr size);
code_t insEncodeOpreg(const instrDesc* id, regNumber reg, emitAttr size);

unsigned insSSval(unsigned scale);

static bool IsSSEInstruction(instruction ins);
static bool IsSSEOrAVXInstruction(instruction ins);
static bool IsAVXOnlyInstruction(instruction ins);
static bool IsAvx512OnlyInstruction(instruction ins);
static bool IsKMOVInstruction(instruction ins);
static bool IsAVXVNNIFamilyInstruction(instruction ins);
static bool IsAVXVNNIINTInstruction(instruction ins);
static bool Is3OpRmwInstruction(instruction ins);
static bool IsBMIInstruction(instruction ins);
static bool IsKInstruction(instruction ins);
static bool IsKInstructionWithLBit(instruction ins);
static bool IsApxOnlyInstruction(instruction ins);

static regNumber getBmiRegNumber(instruction ins);
static regNumber getSseShiftRegNumber(instruction ins);
static bool      HasRex2Encoding(instruction ins);
static bool      IsApxNddCompatibleInstruction(instruction ins);
static bool      IsApxNfCompatibleInstruction(instruction ins);
static bool      IsApxZuCompatibleInstruction(instruction ins);
bool             IsVexEncodableInstruction(instruction ins) const;
bool             IsEvexEncodableInstruction(instruction ins) const;
bool             IsRex2EncodableInstruction(instruction ins) const;
bool             IsApxNddEncodableInstruction(instruction ins) const;
bool             IsApxNfEncodableInstruction(instruction ins) const;
bool             IsApxExtendedEvexInstruction(instruction ins) const;
bool             IsShiftInstruction(instruction ins) const;
bool             IsLegacyMap1(code_t code) const;
bool             IsVexOrEvexEncodableInstruction(instruction ins) const;
bool             DoJitUseApxNDD(instruction ins) const;

code_t insEncodeMIreg(const instrDesc* id, regNumber reg, emitAttr size, code_t code);

code_t AddRexWPrefix(const instrDesc* id, code_t code);
code_t AddRex2WPrefix(const instrDesc* id, code_t code);
code_t AddRexRPrefix(const instrDesc* id, code_t code);
code_t AddRexXPrefix(const instrDesc* id, code_t code);
code_t AddRexBPrefix(const instrDesc* id, code_t code);
code_t AddRexPrefix(instruction ins, code_t code);
code_t AddRex2Prefix(instruction ins, code_t code);

bool   EncodedBySSE38orSSE3A(instruction ins) const;
bool   Is4ByteSSEInstruction(instruction ins) const;
code_t AddEvexVPrimePrefix(code_t code);
code_t AddEvexRPrimePrefix(code_t code);

static bool IsMovInstruction(instruction ins);
bool        HasSideEffect(instruction ins, emitAttr size);
bool        IsRedundantMov(
           instruction ins, insFormat fmt, emitAttr size, regNumber dst, regNumber src, bool canIgnoreSideEffects);
bool EmitMovsxAsCwde(instruction ins, emitAttr size, regNumber dst, regNumber src);

bool IsRedundantStackMov(instruction ins, insFormat fmt, emitAttr size, regNumber ireg, int varx, int offs);

static bool IsJccInstruction(instruction ins);
static bool IsJmpInstruction(instruction ins);
static bool IsBitwiseInstruction(instruction ins);

#ifdef TARGET_64BIT
bool AreUpperBitsZero(regNumber reg, emitAttr size);
bool AreUpperBitsSignExtended(regNumber reg, emitAttr size);
#endif // TARGET_64BIT

bool IsRedundantCmp(emitAttr size, regNumber reg1, regNumber reg2);

bool AreFlagsSetToZeroCmp(regNumber reg, emitAttr opSize, GenCondition cond);
bool AreFlagsSetForSignJumpOpt(regNumber reg, emitAttr opSize, GenCondition cond);

insOpts GetEmbRoundingMode(uint8_t mode) const;

void emitHandleGCrefRegs(BYTE* dst, instrDesc* id);

bool hasRexPrefix(code_t code)
{
#ifdef TARGET_AMD64
    const code_t REX_PREFIX_MASK = 0xFF00000000LL;
    return (code & REX_PREFIX_MASK) != 0;
#else  // !TARGET_AMD64
    return false;
#endif // !TARGET_AMD64
}

// 3-byte VEX prefix starts with byte 0xC4
#define VEX_PREFIX_MASK_3BYTE 0xFF000000000000ULL
#define VEX_PREFIX_CODE_3BYTE 0xC4000000000000ULL

bool TakesVexPrefix(instruction ins) const;
bool TakesRexWPrefix(const instrDesc* id) const;

// Returns true if the instruction encoding already contains VEX prefix
bool hasVexPrefix(code_t code)
{
    return (code & VEX_PREFIX_MASK_3BYTE) == VEX_PREFIX_CODE_3BYTE;
}
code_t AddVexPrefix(instruction ins, code_t code, emitAttr attr);
code_t AddVexPrefixIfNeeded(instruction ins, code_t code, emitAttr size)
{
    if (TakesVexPrefix(ins))
    {
        code = AddVexPrefix(ins, code, size);
    }
    return code;
}
code_t AddVexPrefixIfNeededAndNotPresent(instruction ins, code_t code, emitAttr size)
{
    if (TakesVexPrefix(ins) && !hasVexPrefix(code))
    {
        code = AddVexPrefix(ins, code, size);
    }
    return code;
}

static insTupleType insTupleTypeInfo(instruction ins);
static unsigned     insKMaskBaseSize(instruction ins);

// 2-byte REX2 prefix starts with byte 0xD5
#define REX2_PREFIX_MASK_2BYTE 0xFF0000000000ULL
#define REX2_PREFIX_CODE_2BYTE 0xD50000000000ULL

bool TakesRex2Prefix(const instrDesc* id) const;
//------------------------------------------------------------------------
// hasEvexPrefix: Returns true if the instruction encoding already
// contains Evex prefix.
//
// Arguments:
//    code - opcode + prefixes bits at some stage of encoding.
//
// Returns:
//    `true` if code has an Evex prefix.
//
bool hasRex2Prefix(code_t code)
{
#ifdef TARGET_AMD64
    return (code & REX2_PREFIX_MASK_2BYTE) == REX2_PREFIX_CODE_2BYTE;
#else
    return false;
#endif
}

bool IsExtendedGPReg(regNumber reg) const;

//------------------------------------------------------------------------
// HasKMaskRegisterDest: Temporary check to identify instructions that can
// be Evex encoded but require Opmask(KMask) register support.
// These are cases where for comparison instructions, result is written
//  to KMask when Evex encoded.
// TODO-XArch-AVX512: Refactor once KMask is added.
//
// Arguments:
//    ins - The instruction to check.
//
// Returns:
//    `true` if Evex encoding requires KMask support.
//
bool HasKMaskRegisterDest(instruction ins) const
{
    assert(UseEvexEncoding() == true);

    switch (ins)
    {
        // Requires KMask.
        case INS_pcmpgtb:
        case INS_pcmpgtd:
        case INS_pcmpgtw:
        case INS_pcmpgtq:
        case INS_pcmpeqb:
        case INS_pcmpeqd:
        case INS_pcmpeqq:
        case INS_pcmpeqw:
        case INS_cmpps:
        case INS_cmpss:
        case INS_cmppd:
        case INS_cmpsd:
        case INS_vpgatherdd:
        case INS_vpgatherqd:
        case INS_vpgatherdq:
        case INS_vpgatherqq:
        case INS_vgatherdps:
        case INS_vgatherqps:
        case INS_vgatherdpd:
        case INS_vgatherqpd:
        // KMOV can be promoted to EVEX with APX.
        case INS_kmovb_msk:
        case INS_kmovw_msk:
        case INS_kmovd_msk:
        case INS_kmovq_msk:
        {
            return true;
        }
        default:
        {
            return false;
        }
    }
}

bool useVEXEncodings;
bool UseVEXEncoding() const
{
    return useVEXEncodings;
}
void SetUseVEXEncoding(bool value)
{
    useVEXEncodings = value;
}

// Is Evex encoding supported.
bool useEvexEncodings;
bool UseEvexEncoding() const
{
    return useEvexEncodings;
}
void SetUseEvexEncoding(bool value)
{
    // We expect UseVEXEncoding to be true if UseEvexEncoding is true
    assert(!value || UseVEXEncoding());
    useEvexEncodings = value;
}

// Is Rex2 encoding supported.
bool useRex2Encodings;
bool UseRex2Encoding() const
{
    return useRex2Encodings;
}

void SetUseRex2Encoding(bool value)
{
    useRex2Encodings = value;
}

// Is Promoted EVEX encoding supported.
bool usePromotedEVEXEncodings;
bool UsePromotedEVEXEncoding() const
{
    return usePromotedEVEXEncodings;
}

void SetUsePromotedEVEXEncoding(bool value)
{
    usePromotedEVEXEncodings = value;
}

//------------------------------------------------------------------------
// UseSimdEncoding: Returns true if either VEX or EVEX encoding is supported
// contains Evex prefix.
//
// Returns:
//    `true` if target supports either.
//
bool UseSimdEncoding() const
{
    return UseVEXEncoding() || UseEvexEncoding();
}

// 4-byte EVEX prefix starts with byte 0x62
#define EVEX_PREFIX_MASK 0xFF00000000000000ULL
#define EVEX_PREFIX_CODE 0x6200000000000000ULL

bool TakesEvexPrefix(const instrDesc* id) const;
bool TakesApxExtendedEvexPrefix(const instrDesc* id) const;

//------------------------------------------------------------------------
// hasEvexPrefix: Returns true if the instruction encoding already
// contains Evex prefix.
//
// Arguments:
//    code - opcode + prefixes bits at some stage of encoding.
//
// Returns:
//    `true` if code has an Evex prefix.
//
bool hasEvexPrefix(code_t code)
{
    return (code & EVEX_PREFIX_MASK) == EVEX_PREFIX_CODE;
}
code_t AddEvexPrefix(const instrDesc* id, code_t code, emitAttr attr);

//------------------------------------------------------------------------
// AddSimdPrefixIfNeeded: Add the correct SIMD prefix if required.
//
// Arguments:
//    ins - the instruction being encoded.
//    code - opcode + prefixes bits at some stage of encoding.
//    size - operand size
//
// Returns:
//    code with prefix added.
// TODO-XARCH-AVX512 come back and check whether we can id `id` directly (no need)
// to pass emitAttr size
code_t AddSimdPrefixIfNeeded(const instrDesc* id, code_t code, emitAttr size)
{
    if (TakesEvexPrefix(id))
    {
        return AddEvexPrefix(id, code, size);
    }

    instruction ins = id->idIns();

    if (TakesVexPrefix(ins))
    {
        return AddVexPrefix(ins, code, size);
    }

    return code;
}

//------------------------------------------------------------------------
// AddX86PrefixIfNeeded: Add the correct instruction prefix if required.
//
// Arguments:
//    ins - the instruction being encoded.
//    code - opcode + prefixes bits at some stage of encoding.
//    size - operand size
//
code_t AddX86PrefixIfNeeded(const instrDesc* id, code_t code, emitAttr size)
{
    if (TakesEvexPrefix(id) || TakesApxExtendedEvexPrefix(id))
    {
        return AddEvexPrefix(id, code, size);
    }

    instruction ins = id->idIns();

    if (TakesVexPrefix(ins))
    {
        return AddVexPrefix(ins, code, size);
    }

    // Based on how we labeled REX2 enabled instructions, we can confirm there will not be
    // overlapping part between REX2 and VEX/EVEX, so order of the checks does not matter.
    if (TakesRex2Prefix(id))
    {
        return AddRex2Prefix(ins, code);
    }

    return code;
}

//------------------------------------------------------------------------
// AddX86PrefixIfNeededAndNotPresent: Add the correct instruction prefix if required.
//
// Arguments:
//    ins - the instruction being encoded.
//    code - opcode + prefixes bits at some stage of encoding.
//    size - operand size
//
code_t AddX86PrefixIfNeededAndNotPresent(const instrDesc* id, code_t code, emitAttr size)
{
    // TODO-xarch-apx:
    // consider refactor this part with AddSimdPrefixIfNeeded as a lot of functionality
    // of these functions are overlapping.

    if (TakesEvexPrefix(id) || TakesApxExtendedEvexPrefix(id))
    {
        return !hasEvexPrefix(code) ? AddEvexPrefix(id, code, size) : code;
    }

    instruction ins = id->idIns();

    if (TakesVexPrefix(ins))
    {
        return !hasVexPrefix(code) ? AddVexPrefix(ins, code, size) : code;
    }

    // Based on how we labeled REX2 enabled instructions, we can confirm there will not be
    // overlapping part between REX2 and VEX/EVEX, so order of the checks does not matter.
    if (TakesRex2Prefix(id))
    {
        return !hasRex2Prefix(code) ? AddRex2Prefix(ins, code) : code;
    }

    return code;
}

//------------------------------------------------------------------------
// SetEvexBroadcastIfNeeded: set embedded broadcast if needed.
//
// Arguments:
//    id - instruction descriptor
//    instOptions - emit options
void SetEvexBroadcastIfNeeded(instrDesc* id, insOpts instOptions)
{
    assert(id->idHasMem());

    if ((instOptions & INS_OPTS_EVEX_eb) != INS_OPTS_NONE)
    {
        assert(UseEvexEncoding());
        id->idSetEvexBroadcastBit();
    }
}

//------------------------------------------------------------------------
// SetEvexCompressedDisplacement: set compressed displacement
//
// Arguments:
//    id - instruction descriptor
void SetEvexCompressedDisplacement(instrDesc* id)
{
    assert(id->idHasMem());

    assert(UseEvexEncoding());
    id->idSetEvexCompressedDisplacementBit();
}

//------------------------------------------------------------------------
// SetEvexEmbMaskIfNeeded: set embedded mask if needed.
//
// Arguments:
//    id          - instruction descriptor
//    instOptions - emit options
//
void SetEvexEmbMaskIfNeeded(instrDesc* id, insOpts instOptions)
{
    if ((instOptions & INS_OPTS_EVEX_aaa_MASK) != 0)
    {
        assert(UseEvexEncoding());
        id->idSetEvexAaaContext(instOptions);
    }

    if ((instOptions & INS_OPTS_EVEX_z_MASK) == INS_OPTS_EVEX_em_zero)
    {
        assert(UseEvexEncoding());
        id->idSetEvexZContext();
    }
}

//------------------------------------------------------------------------
// SetEvexEmbRoundIfNeeded: set embedded round if needed.
//
// Arguments:
//    id          - instruction descriptor
//    instOptions - emit options
//
void SetEvexEmbRoundIfNeeded(instrDesc* id, insOpts instOptions)
{
    assert(!id->idHasMem());

    if ((instOptions & INS_OPTS_EVEX_b_MASK) != INS_OPTS_NONE)
    {
        // if EVEX.b needs to be set in this path, then it should be embedded rounding.
        assert(UseEvexEncoding());
        id->idSetEvexbContext(instOptions);
    }
}

//------------------------------------------------------------------------
// SetEvexNdIfNeeded: set NDD form - new data destination if needed.
//
// Arguments:
//    id          - instruction descriptor
//    instOptions - emit options
//
void SetEvexNdIfNeeded(instrDesc* id, insOpts instOptions)
{
    if ((instOptions & INS_OPTS_EVEX_nd_MASK) != 0)
    {
        assert(UsePromotedEVEXEncoding());
        assert(IsApxNddEncodableInstruction(id->idIns()));
        id->idSetEvexNdContext();
    }
    else
    {
        assert((instOptions & INS_OPTS_EVEX_nd_MASK) == 0);
    }
}

//------------------------------------------------------------------------
// SetEvexNdIfNeeded: set Evex.nf on instrDesc
//
// Arguments:
//    id          - instruction descriptor
//    instOptions - emit options
//
void SetEvexNfIfNeeded(instrDesc* id, insOpts instOptions)
{
    if ((instOptions & INS_OPTS_EVEX_nf_MASK) != 0)
    {
        assert(UsePromotedEVEXEncoding());
        assert(IsApxNfEncodableInstruction(id->idIns()));
        id->idSetEvexNfContext();
    }
    else
    {
        assert((instOptions & INS_OPTS_EVEX_nf_MASK) == 0);
    }
}

//------------------------------------------------------------------------
// SetEvexZuIfNeeded: set Evex.zu on instrDesc
//
// Arguments:
//    id          - instruction descriptor
//    instOptions - emit options
//
void SetEvexZuIfNeeded(instrDesc* id, insOpts instOptions)
{
    if ((instOptions & INS_OPTS_EVEX_zu_MASK) != 0)
    {
        assert(UsePromotedEVEXEncoding());
        instruction ins = id->idIns();
#ifdef TARGET_AMD64
        assert(IsApxZuCompatibleInstruction(ins));
#else
        // This method is not expected to be used on 32-bit systems.
        unreached();
#endif
        id->idSetEvexZuContext();
    }
    else
    {
        assert((instOptions & INS_OPTS_EVEX_zu_MASK) == 0);
    }
}

//------------------------------------------------------------------------
// SetApxPpxIfNeeded: set APX.ppx on instrDesc
//
// Arguments:
//    id          - instruction descriptor
//    instOptions - emit options
//
void SetApxPpxIfNeeded(instrDesc* id, insOpts instOptions)
{
    if ((instOptions & INS_OPTS_APX_ppx_MASK) != 0)
    {
        assert(HasApxPpx(id->idIns()));
        id->idSetApxPpxContext();
    }
}

//------------------------------------------------------------------------
// SetEvexDFVIfNeeded: set default flag values on an instrDesc
//
// Arguments:
//    id          - instruction descriptor
//    instOptions - emit options
//
void SetEvexDFVIfNeeded(instrDesc* id, insOpts instOptions)
{
#if defined(TARGET_AMD64)
    if ((instOptions & INS_OPTS_EVEX_dfv_MASK) != 0)
    {
        assert(UsePromotedEVEXEncoding());
        assert(IsCCMP(id->idIns()));
        id->idSetEvexDFV(instOptions);
    }
#endif
}

//------------------------------------------------------------------------
// AddSimdPrefixIfNeeded: Add the correct SIMD prefix.
// Check if the prefix already exists befpre adding.
//
// Arguments:
//    ins - the instruction being encoded.
//    code - opcode + prefixes bits at some stage of encoding.
//    size - operand size
//
// Returns:
//    TRUE if code has an Evex prefix.
// TODO-XARCH-AVX512 come back and check whether we can id `id` directly (no need)
// to pass emitAttr size
code_t AddSimdPrefixIfNeededAndNotPresent(const instrDesc* id, code_t code, emitAttr size)
{
    if (TakesEvexPrefix(id))
    {
        return !hasEvexPrefix(code) ? AddEvexPrefix(id, code, size) : code;
    }

    instruction ins = id->idIns();

    if (TakesVexPrefix(ins))
    {
        return !hasVexPrefix(code) ? AddVexPrefix(ins, code, size) : code;
    }

    return code;
}

bool TakesSimdPrefix(const instrDesc* id) const;

//------------------------------------------------------------------------
// hasVexOrEvexPrefix: Returns true if the instruction encoding already
// contains a Vex or Evex prefix.
//
// Arguments:
//    code - opcode + prefixes bits at some stage of encoding.
//
// Returns:
//    `true` if code has a SIMD prefix.
//
bool hasVexOrEvexPrefix(code_t code)
{
    return (hasVexPrefix(code) || hasEvexPrefix(code));
}

bool TryEvexCompressDisp8Byte(instrDesc* id, ssize_t dsp, ssize_t* compressedDsp, bool* fitsInByte) const;

//------------------------------------------------------------------------
// codeEvexMigrationCheck: Temporary check to use when adding EVEX codepaths
// TODO-XArch-AVX512: Remove implementation and uses once all Evex paths are
// completed.
//
// Arguments:
//    code - opcode + prefixes bits at some stage of encoding.
//
// Returns:
//    `true` if code has an Evex prefix.
//
bool codeEvexMigrationCheck(code_t code)
{
    return hasEvexPrefix(code);
}

ssize_t GetInputSizeInBytes(const instrDesc* id) const;

bool containsAVXInstruction = false;
bool ContainsAVX()
{
    return containsAVXInstruction;
}
void SetContainsAVX(bool value)
{
    containsAVXInstruction = value;
}

bool contains256bitOrMoreAVXInstruction = false;
bool Contains256bitOrMoreAVX() const
{
    return contains256bitOrMoreAVXInstruction;
}
void SetContains256bitOrMoreAVX(bool value)
{
    contains256bitOrMoreAVXInstruction = value;
}

bool containsCallNeedingVzeroupper = false;
bool ContainsCallNeedingVzeroupper() const
{
    return containsCallNeedingVzeroupper;
}
void SetContainsCallNeedingVzeroupper(bool value)
{
    containsCallNeedingVzeroupper = value;
}

bool        IsDstDstSrcAVXInstruction(instruction ins) const;
bool        IsDstSrcSrcAVXInstruction(instruction ins) const;
bool        IsThreeOperandAVXInstruction(instruction ins) const;
static bool HasRegularWideForm(instruction ins);
static bool HasRegularWideImmediateForm(instruction ins);
static bool DoesWriteZeroFlag(instruction ins);
static bool DoesWriteParityFlag(instruction ins);
static bool DoesWriteSignFlag(instruction ins);
static bool DoesResetOverflowAndCarryFlags(instruction ins);
bool        IsFlagsAlwaysModified(instrDesc* id);
static bool IsRexW0Instruction(instruction ins);
static bool IsRexW1Instruction(instruction ins);
static bool IsRexWXInstruction(instruction ins);
static bool IsRexW1EvexInstruction(instruction ins);

static bool  IsCCMP(instruction ins);
static insCC GetCCFromCCMP(instruction ins);

bool isAvx512Blendv(instruction ins)
{
    return ins == INS_vblendmps || ins == INS_vblendmpd || ins == INS_vpblendmb || ins == INS_vpblendmd ||
           ins == INS_vpblendmq || ins == INS_vpblendmw;
}
bool isAvxBlendv(instruction ins)
{
    return ins == INS_vblendvps || ins == INS_vblendvpd || ins == INS_vpblendvb;
}
bool isSse41Blendv(instruction ins)
{
    return ins == INS_blendvps || ins == INS_blendvpd || ins == INS_pblendvb;
}
bool isPrefetch(instruction ins)
{
    return (ins == INS_prefetcht0) || (ins == INS_prefetcht1) || (ins == INS_prefetcht2) || (ins == INS_prefetchnta);
}

/************************************************************************/
/*             Debug-only routines to display instructions              */
/************************************************************************/

void emitDispMask(const instrDesc* id, regNumber reg) const;
void emitDispReloc(ssize_t value) const;
void emitDispAddrMode(instrDesc* id, bool noDetail = false) const;
void emitDispShift(instruction ins, int cnt = 0) const;

const char* emitXMMregName(unsigned reg) const;
const char* emitYMMregName(unsigned reg) const;
const char* emitZMMregName(unsigned reg) const;

/************************************************************************/
/*  Private members that deal with target-dependent instr. descriptors  */
/************************************************************************/

private:
void       emitSetAmdDisp(instrDescAmd* id, ssize_t dsp);
instrDesc* emitNewInstrAmd(emitAttr attr, ssize_t dsp);
instrDesc* emitNewInstrAmdCns(emitAttr attr, ssize_t dsp, int cns);

instrDesc* emitNewInstrCallDir(int              argCnt,
                               VARSET_VALARG_TP GCvars,
                               regMaskTP        gcrefRegs,
                               regMaskTP        byrefRegs,
                               emitAttr retSize MULTIREG_HAS_SECOND_GC_RET_ONLY_ARG(emitAttr secondRetSize),
                               bool             hasAsyncRet);

instrDesc* emitNewInstrCallInd(int              argCnt,
                               ssize_t          disp,
                               VARSET_VALARG_TP GCvars,
                               regMaskTP        gcrefRegs,
                               regMaskTP        byrefRegs,
                               emitAttr retSize MULTIREG_HAS_SECOND_GC_RET_ONLY_ARG(emitAttr secondRetSize),
                               bool             hasAsyncRet);

void    emitGetInsCns(const instrDesc* id, CnsVal* cv) const;
ssize_t emitGetInsAmdCns(const instrDesc* id, CnsVal* cv) const;
void    emitGetInsDcmCns(const instrDesc* id, CnsVal* cv) const;
ssize_t emitGetInsAmdAny(const instrDesc* id) const;

/************************************************************************/
/*               Private helpers for instruction output                 */
/************************************************************************/

private:
insFormat emitInsModeFormat(instruction ins, insFormat base, insFormat FPld, insFormat FPst);

bool emitVerifyEncodable(instruction ins, emitAttr size, regNumber reg1, regNumber reg2 = REG_NA);

bool emitInsCanOnlyWriteSSE2OrAVXReg(instrDesc* id);

#if FEATURE_FIXED_OUT_ARGS
void emitAdjustStackDepthPushPop(instruction ins)
{
}
void emitAdjustStackDepth(instruction ins, ssize_t val)
{
}
#else  // !FEATURE_FIXED_OUT_ARGS
void emitAdjustStackDepthPushPop(instruction ins);
void emitAdjustStackDepth(instruction ins, ssize_t val);
#endif // !FEATURE_FIXED_OUT_ARGS

size_t emitSizeOfInsDsc_AMD(instrDesc* id) const;
size_t emitSizeOfInsDsc_CNS(instrDesc* id) const;
size_t emitSizeOfInsDsc_DSP(instrDesc* id) const;
size_t emitSizeOfInsDsc_NONE(instrDesc* id) const;
size_t emitSizeOfInsDsc_SPEC(instrDesc* id) const;

/*****************************************************************************
 *
 *  Convert between an index scale in bytes to a smaller encoding used for
 *  storage in instruction descriptors.
 */

inline emitter::opSize emitEncodeScale(size_t scale)
{
    assert(scale == 1 || scale == 2 || scale == 4 || scale == 8);
    return static_cast<emitter::opSize>(genLog2(static_cast<unsigned>(scale)));
}

inline emitAttr emitDecodeScale(unsigned ensz) const
{
    assert(ensz < 4);
    return emitter::emitSizeDecode[ensz];
}

/************************************************************************/
/*                   Output target-independent instructions             */
/************************************************************************/

void emitIns_J(instruction ins, BasicBlock* dst, int instrCount = 0, bool isRemovableJmpCandidate = false);

/************************************************************************/
/*           The public entry points to output instructions             */
/************************************************************************/

public:
void emitIns(instruction ins);

void emitIns(instruction ins, emitAttr attr);

void emitInsRMW(instruction inst, emitAttr attr, GenTreeStoreInd* storeInd, GenTree* src);

void emitInsRMW(instruction inst, emitAttr attr, GenTreeStoreInd* storeInd);

void emitIns_Nop(unsigned size);

void emitIns_Data16();

void emitIns_I(instruction ins, emitAttr attr, cnsval_ssize_t val);

void emitIns_R(instruction ins, emitAttr attr, regNumber reg, insOpts instOptions = INS_OPTS_NONE);

void emitIns_C(instruction ins, emitAttr attr, CORINFO_FIELD_HANDLE fdlHnd, int offs);

void emitIns_A(instruction ins, emitAttr attr, GenTreeIndir* indir);

void emitIns_R_I(instruction ins,
                 emitAttr    attr,
                 regNumber   reg,
                 ssize_t     val,
                 insOpts instOptions = INS_OPTS_NONE DEBUGARG(size_t targetHandle = 0)
                     DEBUGARG(GenTreeFlags gtFlags = GTF_EMPTY));

bool emitIns_Mov(
    instruction ins, emitAttr attr, regNumber dstReg, regNumber srgReg, bool canSkip, bool useApxNdd = false);

void emitIns_R_R(instruction ins, emitAttr attr, regNumber reg1, regNumber reg2, insOpts instOptions = INS_OPTS_NONE);

void emitIns_R_R_I(
    instruction ins, emitAttr attr, regNumber reg1, regNumber reg2, int ival, insOpts instOptions = INS_OPTS_NONE);

void emitIns_AR(instruction ins, emitAttr attr, regNumber base, int offs, insOpts instOptions = INS_OPTS_NONE);

void emitIns_AR_R_R(instruction ins,
                    emitAttr    attr,
                    regNumber   op2Reg,
                    regNumber   op3Reg,
                    regNumber   base,
                    int         offs,
                    insOpts     instOptions = INS_OPTS_NONE);

void emitIns_R_A(
    instruction ins, emitAttr attr, regNumber reg1, GenTreeIndir* indir, insOpts instOptions = INS_OPTS_NONE);

void emitIns_R_A_I(
    instruction ins, emitAttr attr, regNumber reg1, GenTreeIndir* indir, int ival, insOpts instOptions = INS_OPTS_NONE);

void emitIns_R_C_I(instruction          ins,
                   emitAttr             attr,
                   regNumber            reg1,
                   CORINFO_FIELD_HANDLE fldHnd,
                   int                  offs,
                   int                  ival,
                   insOpts              instOptions = INS_OPTS_NONE);

void emitIns_R_S_I(
    instruction ins, emitAttr attr, regNumber reg1, int varx, int offs, int ival, insOpts instOptions = INS_OPTS_NONE);

void emitIns_R_R_A(instruction   ins,
                   emitAttr      attr,
                   regNumber     reg1,
                   regNumber     reg2,
                   GenTreeIndir* indir,
                   insOpts       instOptions = INS_OPTS_NONE);

void emitIns_R_R_AR(instruction ins, emitAttr attr, regNumber reg1, regNumber reg2, regNumber base, int offs);

void emitIns_R_AR_R(instruction ins,
                    emitAttr    attr,
                    regNumber   reg1,
                    regNumber   reg2,
                    regNumber   base,
                    regNumber   index,
                    int         scale,
                    int         offs);

void emitIns_R_R_C(instruction          ins,
                   emitAttr             attr,
                   regNumber            reg1,
                   regNumber            reg2,
                   CORINFO_FIELD_HANDLE fldHnd,
                   int                  offs,
                   insOpts              instOptions = INS_OPTS_NONE);

void emitIns_R_R_S(instruction ins,
                   emitAttr    attr,
                   regNumber   reg1,
                   regNumber   reg2,
                   int         varx,
                   int         offs,
                   insOpts     instOptions = INS_OPTS_NONE);

void emitIns_R_R_R(instruction ins,
                   emitAttr    attr,
                   regNumber   reg1,
                   regNumber   reg2,
                   regNumber   reg3,
                   insOpts     instOptions = INS_OPTS_NONE);

void emitIns_R_R_A_I(instruction   ins,
                     emitAttr      attr,
                     regNumber     reg1,
                     regNumber     reg2,
                     GenTreeIndir* indir,
                     int           ival,
                     insFormat     fmt,
                     insOpts       instOptions = INS_OPTS_NONE);
void emitIns_R_R_AR_I(
    instruction ins, emitAttr attr, regNumber reg1, regNumber reg2, regNumber base, int offs, int ival);

void emitIns_C_R_I(instruction ins, emitAttr attr, CORINFO_FIELD_HANDLE fldHnd, int offs, regNumber reg, int ival);
void emitIns_S_R_I(instruction ins, emitAttr attr, int varNum, int offs, regNumber reg, int ival);
void emitIns_A_R_I(instruction ins, emitAttr attr, GenTreeIndir* indir, regNumber reg, int imm);

void emitIns_R_R_C_I(instruction          ins,
                     emitAttr             attr,
                     regNumber            reg1,
                     regNumber            reg2,
                     CORINFO_FIELD_HANDLE fldHnd,
                     int                  offs,
                     int                  ival,
                     insOpts              instOptions = INS_OPTS_NONE);

void emitIns_R_R_R_I(instruction ins,
                     emitAttr    attr,
                     regNumber   reg1,
                     regNumber   reg2,
                     regNumber   reg3,
                     int         ival,
                     insOpts     instOptions = INS_OPTS_NONE);

void emitIns_R_R_S_I(instruction ins,
                     emitAttr    attr,
                     regNumber   reg1,
                     regNumber   reg2,
                     int         varx,
                     int         offs,
                     int         ival,
                     insOpts     instOptions = INS_OPTS_NONE);

void emitIns_R_R_A_R(instruction   ins,
                     emitAttr      attr,
                     regNumber     targetReg,
                     regNumber     op1Reg,
                     regNumber     op3Reg,
                     GenTreeIndir* indir,
                     insOpts       instOptions);

void emitIns_R_R_C_R(instruction          ins,
                     emitAttr             attr,
                     regNumber            targetReg,
                     regNumber            op1Reg,
                     regNumber            op3Reg,
                     CORINFO_FIELD_HANDLE fldHnd,
                     int                  offs,
                     insOpts              instOptions = INS_OPTS_NONE);

void emitIns_R_R_S_R(instruction ins,
                     emitAttr    attr,
                     regNumber   targetReg,
                     regNumber   op1Reg,
                     regNumber   op3Reg,
                     int         varx,
                     int         offs,
                     insOpts     instOptions = INS_OPTS_NONE);

void emitIns_R_R_R_R(instruction ins,
                     emitAttr    attr,
                     regNumber   reg1,
                     regNumber   reg2,
                     regNumber   reg3,
                     regNumber   reg4,
                     insOpts     instOptions = INS_OPTS_NONE);

void emitIns_S(instruction ins, emitAttr attr, int varx, int offs);

void emitIns_S_R(
    instruction ins, emitAttr attr, regNumber ireg, int varx, int offs, insOpts instOptions = INS_OPTS_NONE);

void emitIns_R_S(
    instruction ins, emitAttr attr, regNumber ireg, int varx, int offs, insOpts instOptions = INS_OPTS_NONE);

void emitIns_S_I(instruction ins, emitAttr attr, int varx, int offs, int val);

void emitIns_R_C(instruction          ins,
                 emitAttr             attr,
                 regNumber            reg,
                 CORINFO_FIELD_HANDLE fldHnd,
                 int                  offs,
                 insOpts              instOptions = INS_OPTS_NONE);

void emitIns_C_R(instruction ins, emitAttr attr, CORINFO_FIELD_HANDLE fldHnd, regNumber reg, int offs);

void emitIns_C_I(instruction ins, emitAttr attr, CORINFO_FIELD_HANDLE fdlHnd, int offs, int val);

void emitIns_IJ(emitAttr attr, regNumber reg, unsigned base);

void emitIns_J_S(instruction ins, emitAttr attr, BasicBlock* dst, int varx, int offs);

void emitIns_R_L(instruction ins, emitAttr attr, BasicBlock* dst, regNumber reg);

void emitIns_R_D(instruction ins, emitAttr attr, unsigned offs, regNumber reg);

void emitIns_I_AR(
    instruction ins, emitAttr attr, int val, regNumber reg, int offs, insOpts instOptions = INS_OPTS_NONE);

void emitIns_I_AI(instruction ins, emitAttr attr, int val, ssize_t disp);

void emitIns_R_AR(instruction ins, emitAttr attr, regNumber reg, regNumber base, int disp);

void emitIns_R_AI(instruction  ins,
                  emitAttr     attr,
                  regNumber    ireg,
                  ssize_t disp DEBUGARG(size_t targetHandle = 0) DEBUGARG(GenTreeFlags gtFlags = GTF_EMPTY));

void emitIns_AR_R(instruction    ins,
                  emitAttr       attr,
                  regNumber      reg,
                  regNumber      base,
                  cnsval_ssize_t disp,
                  insOpts        instOptions = INS_OPTS_NONE);

void emitIns_AI_R(instruction ins, emitAttr attr, regNumber ireg, ssize_t disp);

void emitIns_I_ARR(instruction ins, emitAttr attr, int val, regNumber reg, regNumber rg2, int disp);

void emitIns_R_ARR(instruction ins, emitAttr attr, regNumber reg, regNumber base, regNumber index, int disp);

void emitIns_ARR_R(instruction ins, emitAttr attr, regNumber reg, regNumber base, regNumber index, int disp);

void emitIns_I_ARX(instruction ins, emitAttr attr, int val, regNumber reg, regNumber rg2, unsigned mul, int disp);

void emitIns_R_ARX(
    instruction ins, emitAttr attr, regNumber reg, regNumber base, regNumber index, unsigned scale, int disp);

void emitIns_ARX_R(instruction    ins,
                   emitAttr       attr,
                   regNumber      reg,
                   regNumber      base,
                   regNumber      index,
                   unsigned       scale,
                   cnsval_ssize_t disp,
                   insOpts        instOptions = INS_OPTS_NONE);

void emitIns_I_AX(instruction ins, emitAttr attr, int val, regNumber reg, unsigned mul, int disp);

void emitIns_R_AX(instruction ins, emitAttr attr, regNumber ireg, regNumber reg, unsigned mul, int disp);

void emitIns_AX_R(instruction ins, emitAttr attr, regNumber ireg, regNumber reg, unsigned mul, int disp);

void emitIns_SIMD_R_R_I(
    instruction ins, emitAttr attr, regNumber targetReg, regNumber op1Reg, int ival, insOpts instOptions);

void emitIns_SIMD_R_R_A(
    instruction ins, emitAttr attr, regNumber targetReg, regNumber op1Reg, GenTreeIndir* indir, insOpts instOptions);
void emitIns_SIMD_R_R_C(instruction          ins,
                        emitAttr             attr,
                        regNumber            targetReg,
                        regNumber            op1Reg,
                        CORINFO_FIELD_HANDLE fldHnd,
                        int                  offs,
                        insOpts              instOptions);
void emitIns_SIMD_R_R_R(
    instruction ins, emitAttr attr, regNumber targetReg, regNumber op1Reg, regNumber op2Reg, insOpts instOptions);
void emitIns_SIMD_R_R_S(
    instruction ins, emitAttr attr, regNumber targetReg, regNumber op1Reg, int varx, int offs, insOpts instOptions);

void emitIns_SIMD_R_R_A_I(instruction   ins,
                          emitAttr      attr,
                          regNumber     targetReg,
                          regNumber     op1Reg,
                          GenTreeIndir* indir,
                          int           ival,
                          insOpts       instOptions);
void emitIns_SIMD_R_R_C_I(instruction          ins,
                          emitAttr             attr,
                          regNumber            targetReg,
                          regNumber            op1Reg,
                          CORINFO_FIELD_HANDLE fldHnd,
                          int                  offs,
                          int                  ival,
                          insOpts              instOptions);
void emitIns_SIMD_R_R_R_I(instruction ins,
                          emitAttr    attr,
                          regNumber   targetReg,
                          regNumber   op1Reg,
                          regNumber   op2Reg,
                          int         ival,
                          insOpts     instOptions);
void emitIns_SIMD_R_R_S_I(instruction ins,
                          emitAttr    attr,
                          regNumber   targetReg,
                          regNumber   op1Reg,
                          int         varx,
                          int         offs,
                          int         ival,
                          insOpts     instOptions);

#ifdef FEATURE_HW_INTRINSICS
void emitIns_SIMD_R_R_R_A(instruction   ins,
                          emitAttr      attr,
                          regNumber     targetReg,
                          regNumber     op1Reg,
                          regNumber     op2Reg,
                          GenTreeIndir* indir,
                          insOpts       instOptions);
void emitIns_SIMD_R_R_R_C(instruction          ins,
                          emitAttr             attr,
                          regNumber            targetReg,
                          regNumber            op1Reg,
                          regNumber            op2Reg,
                          CORINFO_FIELD_HANDLE fldHnd,
                          int                  offs,
                          insOpts              instOptions);
void emitIns_SIMD_R_R_R_R(instruction ins,
                          emitAttr    attr,
                          regNumber   targetReg,
                          regNumber   op1Reg,
                          regNumber   op2Reg,
                          regNumber   op3Reg,
                          insOpts     instOptions);
void emitIns_SIMD_R_R_R_S(instruction ins,
                          emitAttr    attr,
                          regNumber   targetReg,
                          regNumber   op1Reg,
                          regNumber   op2Reg,
                          int         varx,
                          int         offs,
                          insOpts     instOptions);

void emitIns_SIMD_R_R_A_R(instruction   ins,
                          emitAttr      attr,
                          regNumber     targetReg,
                          regNumber     op1Reg,
                          regNumber     op2Reg,
                          GenTreeIndir* indir,
                          insOpts       instOptions);
void emitIns_SIMD_R_R_C_R(instruction          ins,
                          emitAttr             attr,
                          regNumber            targetReg,
                          regNumber            op1Reg,
                          regNumber            op2Reg,
                          CORINFO_FIELD_HANDLE fldHnd,
                          int                  offs,
                          insOpts              instOptions);
void emitIns_SIMD_R_R_S_R(instruction ins,
                          emitAttr    attr,
                          regNumber   targetReg,
                          regNumber   op1Reg,
                          regNumber   op2Reg,
                          int         varx,
                          int         offs,
                          insOpts     instOptions);

void emitIns_SIMD_R_R_R_A_I(instruction   ins,
                            emitAttr      attr,
                            regNumber     targetReg,
                            regNumber     op1Reg,
                            regNumber     op2Reg,
                            GenTreeIndir* indir,
                            int           ival,
                            insOpts       instOptions);
void emitIns_SIMD_R_R_R_C_I(instruction          ins,
                            emitAttr             attr,
                            regNumber            targetReg,
                            regNumber            op1Reg,
                            regNumber            op2Reg,
                            CORINFO_FIELD_HANDLE fldHnd,
                            int                  offs,
                            int                  ival,
                            insOpts              instOptions);
void emitIns_SIMD_R_R_R_R_I(instruction ins,
                            emitAttr    attr,
                            regNumber   targetReg,
                            regNumber   op1Reg,
                            regNumber   op2Reg,
                            regNumber   op3Reg,
                            int         ival,
                            insOpts     instOptions);
void emitIns_SIMD_R_R_R_S_I(instruction ins,
                            emitAttr    attr,
                            regNumber   targetReg,
                            regNumber   op1Reg,
                            regNumber   op2Reg,
                            int         varx,
                            int         offs,
                            int         ival,
                            insOpts     instOptions);
#endif // FEATURE_HW_INTRINSICS

void emitIns_BASE_R_R(instruction ins, emitAttr attr, regNumber op1Reg, regNumber op2Reg);

void emitIns_BASE_R_R_I(instruction ins, emitAttr attr, regNumber op1Reg, regNumber op2Reg, int ival);

regNumber emitIns_BASE_R_R_RM(
    instruction ins, emitAttr attr, regNumber targetReg, GenTree* treeNode, GenTree* regOp, GenTree* rmOp);

#ifdef TARGET_AMD64
// Is the last instruction emitted a call instruction?
bool emitIsLastInsCall();

// Insert a NOP at the end of the current instruction group if the last emitted instruction was a 'call',
// because the next instruction group will be an epilog.
void emitOutputPreEpilogNOP();
#endif // TARGET_AMD64

/*****************************************************************************
 *
 *  Given a jump, return true if it's a conditional jump.
 */

inline bool emitIsCondJump(instrDesc* jmp)
{
    instruction ins = jmp->idIns();

    assert(jmp->idInsFmt() == IF_LABEL);

    return (ins != INS_call && ins != INS_jmp);
}

/*****************************************************************************
 *
 *  Given a jump, return true if it's an unconditional jump.
 */

inline bool emitIsUncondJump(instrDesc* jmp)
{
    instruction ins = jmp->idIns();

    assert(jmp->idInsFmt() == IF_LABEL);

    return (ins == INS_jmp);
}

//------------------------------------------------------------------------
// HasEmbeddedBroadcast: Do we consider embedded broadcast while encoding.
//
// Arguments:
//    id - Instruction descriptor.
//
// Returns:
//    `true` if the instruction does embedded broadcast.
//
inline bool HasEmbeddedBroadcast(const instrDesc* id) const
{
    assert(id->idHasMem());
    return (id->idGetEvexbContext() & INS_OPTS_EVEX_eb) != 0;
}

//------------------------------------------------------------------------
// HasEmbeddedMask: Do we consider embedded masking while encoding.
//
// Arguments:
//    id - Instruction descriptor.
//
// Returns:
//    `true` if the instruction does embedded masking.
//
inline bool HasEmbeddedMask(const instrDesc* id) const
{
    return id->idIsEvexAaaContextSet() || id->idIsEvexZContextSet();
}

//------------------------------------------------------------------------
// HasCompressedDisplacement: Do we consider compressed displacement while encoding.
//
// Arguments:
//    id - Instruction descriptor.
//
// Returns:
//    `true` if the instruction does compressed displacement.
//
inline bool HasCompressedDisplacement(const instrDesc* id) const
{
    assert(id->idHasMem());
    return (id->idGetEvexbContext() & INS_OPTS_EVEX_cd) != 0;
}

inline bool HasHighSIMDReg(const instrDesc* id) const;
inline bool HasExtendedGPReg(const instrDesc* id) const;

inline bool HasMaskReg(const instrDesc* id) const;

#ifdef TARGET_AMD64
// true if this 'imm' can be encoded as a input operand to a ccmp instruction
static bool emitIns_valid_imm_for_ccmp(INT64 imm);
#endif // TARGET_AMD64

#endif // TARGET_XARCH
