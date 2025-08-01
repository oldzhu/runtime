// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//*****************************************************************************
// LoaderHeap.h
//

//
// Utility functions for managing memory allocations that typically do not
// need releasing.
//
//*****************************************************************************


#ifndef __LoaderHeap_h__
#define __LoaderHeap_h__

#include "utilcode.h"
#include "ex.h"
#include "executableallocator.h"

//==============================================================================
// Interface used to back out loader heap allocations.
//==============================================================================
class ILoaderHeapBackout
{
#ifdef _DEBUG
#define BackoutMem(pMem, dwSize)  RealBackoutMem( (pMem), (dwSize), __FILE__, __LINE__, "UNKNOWN", -1 )
#else
#define BackoutMem(pMem, dwSize)  RealBackoutMem( (pMem), (dwSize) )
#endif

public:
    virtual void RealBackoutMem(void *pMem
                        , size_t dwSize
#ifdef _DEBUG
                        , _In_ _In_z_ const char *szFile
                        , int lineNum
                        , _In_ _In_z_ const char *szAllocFile
                        , int allocLineNum
#endif
                        ) = 0;
};

//==============================================================================
// This structure packages up all the data needed to back out an AllocMem.
// It's mainly a short term parking place to get the data from the AllocMem
// to the AllocMemHolder while preserving the illusion that AllocMem() still
// returns just a pointer as it did in V1.
//==============================================================================
struct TaggedMemAllocPtr
{
    // Note: For AllocAlignedMem blocks, m_pMem and m_dwRequestedSize are the actual values to pass
    // to BackoutMem. Do not add "m_dwExtra"
    void        *m_pMem;                //Pointer to AllocMem'd block (needed to pass back to BackoutMem)
    size_t       m_dwRequestedSize;     //Requested allocation size (needed to pass back to BackoutMem)

    ILoaderHeapBackout  *m_pHeap;          //The heap that alloc'd the block (needed to know who to call BackoutMem on)

    //For AllocMem'd blocks, this is always 0.
    //For AllocAlignedMem blocks, you have to add m_dwExtra to m_pMem to arrive
    //   at the actual aligned pointer.
    size_t       m_dwExtra;

#ifdef _DEBUG
    const char  *m_szFile;              //File that called AllocMem
    int          m_lineNum;             //Line # of AllocMem callsite
#endif

//! Note: this structure is copied around using bitwise copy ("=").
//! Don't get too fancy putting stuff in here. It's really just a temporary
//! holding place to get stuff from RealAllocMem() to the MemAllocHolder.


  public:

    //
    // This makes "void *ptr = pLoaderHeap->AllocMem()" work as in V1.
    //
    operator void*() const
    {
        LIMITED_METHOD_CONTRACT;
        return (void*)(m_dwExtra + (BYTE*)m_pMem);
    }

    template < typename T >
    T cast() const
    {
        LIMITED_METHOD_CONTRACT;
        return reinterpret_cast< T >( operator void *() );
    }
};



// # bytes to leave between allocations in debug mode
// Set to a > 0 boundary to debug problems - I've made this zero, otherwise a 1 byte allocation becomes
// a (1 + LOADER_HEAP_DEBUG_BOUNDARY) allocation
#define LOADER_HEAP_DEBUG_BOUNDARY  0

#define VIRTUAL_ALLOC_RESERVE_GRANULARITY (64*1024)    // 0x10000  (64 KB)

typedef DPTR(struct LoaderHeapBlock) PTR_LoaderHeapBlock;

struct LoaderHeapBlock
{
    PTR_LoaderHeapBlock     pNext;
    PTR_VOID                pVirtualAddress;
    size_t                  dwVirtualSize;
    BOOL                    m_fReleaseMemory;

#ifndef DACCESS_COMPILE
    // pVirtualMemory  - the start address of the virtual memory
    // cbVirtualMemory - the length in bytes of the virtual memory
    // fReleaseMemory  - should LoaderHeap be responsible for releasing this memory
    void Init(void   *pVirtualMemory,
              size_t  cbVirtualMemory,
              BOOL    fReleaseMemory)
    {
        LIMITED_METHOD_CONTRACT;
        this->pNext = NULL;
        this->pVirtualAddress = pVirtualMemory;
        this->dwVirtualSize = cbVirtualMemory;
        this->m_fReleaseMemory = fReleaseMemory;
    }

    // Just calls LoaderHeapBlock::Init
    LoaderHeapBlock(void   *pVirtualMemory,
                    size_t  cbVirtualMemory,
                    BOOL    fReleaseMemory)
    {
        WRAPPER_NO_CONTRACT;
        Init(pVirtualMemory, cbVirtualMemory, fReleaseMemory);
    }

    LoaderHeapBlock()
    {
        WRAPPER_NO_CONTRACT;
        Init(NULL, 0, FALSE);
    }
#else
    // No ctors in DAC builds
    LoaderHeapBlock() {}
#endif
};


struct LoaderHeapFreeBlock;

// Collection of methods for helping in debugging heap corruptions
#ifdef _DEBUG
class  LoaderHeapSniffer;
struct LoaderHeapEvent;
#endif




// When an interleaved LoaderHeap is constructed, this is the interleaving size
inline UINT32 GetStubCodePageSize()
{
#if (defined(TARGET_ARM64) && defined(TARGET_UNIX)) || defined(TARGET_WASM)
    return max(16*1024u, GetOsPageSize());
#elif defined(TARGET_ARM)
    return 4096; // ARM is special as the 32bit instruction set does not easily permit a 16KB offset
#else
    return 16*1024;
#endif
}

enum class LoaderHeapImplementationKind
{
    Data,
    Executable,
    Interleaved
};

class UnlockedLoaderHeapBaseTraversable
{
protected:
#ifdef DACCESS_COMPILE
    UnlockedLoaderHeapBaseTraversable() {}
#else
    UnlockedLoaderHeapBaseTraversable() :
        m_pFirstBlock(NULL)
    {
        LIMITED_METHOD_CONTRACT;
    }
#endif

public:
#ifdef DACCESS_COMPILE
public:
    void EnumMemoryRegions(enum CLRDataEnumMemoryFlags flags);
    
typedef bool EnumPageRegionsCallback (PTR_VOID pvArgs, PTR_VOID pvAllocationBase, SIZE_T cbReserved);
    void EnumPageRegions (EnumPageRegionsCallback *pCallback, PTR_VOID pvArgs);
#endif
    
protected:
    // Linked list of ClrVirtualAlloc'd pages
    PTR_LoaderHeapBlock m_pFirstBlock;
};

//===============================================================================
// This is the base class for LoaderHeap and InterleavedLoaderHeap. It holds the
// common handling for LoaderHeap events, and the data structures used for bump
// pointer allocation (although not the actual allocation routines).
//===============================================================================
typedef DPTR(class UnlockedLoaderHeapBase) PTR_UnlockedLoaderHeapBase;
class UnlockedLoaderHeapBase : public UnlockedLoaderHeapBaseTraversable, public ILoaderHeapBackout
{
#ifdef _DEBUG
    friend class LoaderHeapSniffer;
#endif
#ifdef DACCESS_COMPILE
    friend class ClrDataAccess;
#endif

protected:
    size_t GetBytesAvailCommittedRegion();

#ifndef DACCESS_COMPILE
    const 
#endif
    LoaderHeapImplementationKind m_kind;

    size_t              m_dwTotalAlloc;

    // Allocation pointer in current block
    PTR_BYTE            m_pAllocPtr;

    // Points to the end of the committed region in the current block
    PTR_BYTE            m_pPtrToEndOfCommittedRegion;
    
public:
#ifdef DACCESS_COMPILE
    UnlockedLoaderHeapBase() {}
#else
    UnlockedLoaderHeapBase(LoaderHeapImplementationKind kind);
    virtual ~UnlockedLoaderHeapBase();
#endif // DACCESS_COMPILE

    BOOL IsExecutable() { return m_kind == LoaderHeapImplementationKind::Executable || m_kind == LoaderHeapImplementationKind::Interleaved; }
    BOOL IsInterleaved() { return m_kind == LoaderHeapImplementationKind::Interleaved; }

#ifdef _DEBUG
    size_t DebugGetWastedBytes()
    {
        WRAPPER_NO_CONTRACT;
        return m_dwDebugWastedBytes + GetBytesAvailCommittedRegion();
    }

    void DumpFreeList();

// Extra CallTracing support
    void UnlockedClearEvents();     //Discard saved events
    void UnlockedCompactEvents();   //Discard matching alloc/free events
    void UnlockedPrintEvents();     //Print event list
#endif

public:

#ifdef _DEBUG
    enum
    {
        kCallTracing    = 0x00000001,   // Keep a permanent log of all callers

        kEncounteredOOM = 0x80000000,   // One time flag to record that an OOM interrupted call tracing
    }
    LoaderHeapDebugFlags;

    DWORD               m_dwDebugFlags;

    LoaderHeapEvent    *m_pEventList;   // Linked list of events (in reverse time order)
#endif


#ifdef _DEBUG
    size_t              m_dwDebugWastedBytes;
    static DWORD        s_dwNumInstancesOfLoaderHeaps;
#endif
};

//===============================================================================
// This is the base class for LoaderHeap It's used as a simple
// allocator that's semantically (but not perfwise!) equivalent to a blackbox
// alloc/free heap. The ability to free is via a "backout" mechanism that is
// not considered to have good performance.
//
//===============================================================================
class UnlockedLoaderHeap : public UnlockedLoaderHeapBase
{
#ifdef _DEBUG
    friend class LoaderHeapSniffer;
#endif
#ifdef DACCESS_COMPILE
    friend class ClrDataAccess;
#endif
    friend struct LoaderHeapFreeBlock;

public:

private:
    // Points to the end of the reserved region for the current block
    PTR_BYTE            m_pEndReservedRegion;

    // When we need to ClrVirtualAlloc() MEM_RESERVE a new set of pages, number of bytes to reserve
    DWORD               m_dwReserveBlockSize;

    // When we need to commit pages from our reserved list, number of bytes to commit at a time
    DWORD               m_dwCommitBlockSize;

    // Range list to record memory ranges in
    RangeList *         m_pRangeList;

    // This is used to hold on to a block of reserved memory provided to the
    // constructor. We do this instead of adding it as the first block because
    // that requires comitting the first page of the reserved block, and for
    // startup working set reasons we want to delay that as long as possible.
    LoaderHeapBlock      m_reservedBlock;

    LoaderHeapFreeBlock *m_pFirstFreeBlock;

#ifndef DACCESS_COMPILE
protected:
    // Use this version if dwReservedRegionAddress already points to a
    // blob of reserved memory.  This will set up internal data structures,
    // using the provided, reserved memory.
    UnlockedLoaderHeap(DWORD dwReserveBlockSize,
                       DWORD dwCommitBlockSize,
                       const BYTE* dwReservedRegionAddress,
                       SIZE_T dwReservedRegionSize,
                       RangeList *pRangeList = NULL,
                       LoaderHeapImplementationKind kind = LoaderHeapImplementationKind::Data);

    virtual ~UnlockedLoaderHeap();
#endif

private:
    size_t GetBytesAvailReservedRegion();

protected:
    // number of bytes available in region
    size_t UnlockedGetReservedBytesFree()
    {
        LIMITED_METHOD_CONTRACT;
        return m_pEndReservedRegion - m_pAllocPtr;
    }

    PTR_BYTE UnlockedGetAllocPtr()
    {
        LIMITED_METHOD_CONTRACT;
        return m_pAllocPtr;
    }

private:
    // Get some more committed pages - either commit some more in the current reserved region, or, if it
    // has run out, reserve another set of pages
    BOOL GetMoreCommittedPages(size_t dwMinSize);

    // Commit memory pages starting at the specified adress
    BOOL CommitPages(void* pData, size_t dwSizeToCommitPart);

protected:
    // Reserve some pages at any address
    BOOL UnlockedReservePages(size_t dwCommitBlockSize);

protected:
    // In debug mode, allocate an extra LOADER_HEAP_DEBUG_BOUNDARY bytes and fill it with invalid data.  The reason we
    // do this is that when we're allocating vtables out of the heap, it is very easy for code to
    // get careless, and end up reading from memory that it doesn't own - but since it will be
    // reading some other allocation's vtable, no crash will occur.  By keeping a gap between
    // allocations, it is more likely that these errors will be encountered.
    void *UnlockedAllocMem(size_t dwSize
#ifdef _DEBUG
                          ,_In_ _In_z_ const char *szFile
                          ,int  lineNum
#endif
                          );
    void *UnlockedAllocMem_NoThrow(size_t dwSize
#ifdef _DEBUG
                                   ,_In_ _In_z_ const char *szFile
                                   ,int  lineNum
#endif
                                   );

protected:
    // Allocates memory aligned on power-of-2 boundary.
    //
    // The return value is a pointer that's guaranteed to be aligned.
    //
    // FREEING THIS BLOCK: Underneath, the actual block allocated may
    // be larger and start at an address prior to the one you got back.
    // It is this adjusted size and pointer that you pass to BackoutMem.
    // The required adjustment is passed back thru the pdwExtra pointer.
    //
    // Here is how to properly backout the memory:
    //
    //   size_t dwExtra;
    //   void *pMem = UnlockedAllocAlignedMem(dwRequestedSize, alignment, &dwExtra);
    //   _ASSERTE( 0 == (pMem & (alignment - 1)) );
    //   UnlockedBackoutMem( ((BYTE*)pMem) - dExtra, dwRequestedSize + dwExtra );
    //
    // If you use the AllocMemHolder or AllocMemTracker, all this is taken care of
    // behind the scenes.
    //
    //
    void *UnlockedAllocAlignedMem(size_t  dwRequestedSize
                                 ,size_t  dwAlignment
                                 ,size_t *pdwExtra
#ifdef _DEBUG
                                 ,_In_ _In_z_ const char *szFile
                                 ,int  lineNum
#endif
                                 );

    void *UnlockedAllocAlignedMem_NoThrow(size_t  dwRequestedSize
                                         ,size_t  dwAlignment
                                         ,size_t *pdwExtra
#ifdef _DEBUG
                                         ,_In_ _In_z_ const char *szFile
                                         ,int  lineNum
#endif
                                 );

protected:
    // This frees memory allocated by UnlockAllocMem. It's given this horrible name to emphasize
    // that it's purpose is for error path leak prevention purposes. You shouldn't
    // use LoaderHeap's as general-purpose alloc-free heaps.
    void UnlockedBackoutMem(void *pMem
                          , size_t dwSize
#ifdef _DEBUG
                          , _In_ _In_z_ const char *szFile
                          , int lineNum
                          , _In_ _In_z_ const char *szAllocFile
                          , int AllocLineNum
#endif
                          );

public:
    // Perf Counter reports the size of the heap
    size_t GetSize ()
    {
        LIMITED_METHOD_CONTRACT;
        return m_dwTotalAlloc;
    }

    size_t AllocMem_TotalSize(size_t dwRequestedSize);
public:
#ifdef _DEBUG
    void DumpFreeList();
#endif
private:
    static void ValidateFreeList(UnlockedLoaderHeap *pHeap);
    static void WeGotAFaultNowWhat(UnlockedLoaderHeap *pHeap);
};

struct InterleavedLoaderHeapConfig
{
    uint32_t StubSize;
    void* Template;
    void (*CodePageGenerator)(uint8_t* pageBase, uint8_t* pageBaseRX, size_t size);
    void (*DataPageGenerator)(uint8_t* pageBase, size_t size);
};

void InitializeLoaderHeapConfig(InterleavedLoaderHeapConfig *pConfig, size_t stubSize, void* templateInImage, void (*codePageGenerator)(uint8_t* pageBase, uint8_t* pageBaseRX, size_t size), void (*dataPageGenerator)(uint8_t* pageBase, size_t size));

//===============================================================================
// This is the base class for InterleavedLoaderHeap. It's used as a simple
// allocator for stubs in a scheme where each stub is a small fixed size, and is paired
// with memory which is GetStubCodePageSize() bytes away. In addition there is an
// ability to free is via a "backout" mechanism that is not considered to have good performance.
//
//===============================================================================
class UnlockedInterleavedLoaderHeap : public UnlockedLoaderHeapBase
{
#ifdef _DEBUG
    friend class LoaderHeapSniffer;
    friend struct LoaderHeapFreeBlock;
#endif

#ifdef DACCESS_COMPILE
    friend class ClrDataAccess;
#endif

public:

private:
    PTR_BYTE            m_pEndReservedRegion;

    // For interleaved heap (RX pages interleaved with RW ones), this specifies the allocation granularity,
    // which is the individual code block size
    DWORD               m_dwGranularity;

    // Range list to record memory ranges in
    RangeList *         m_pRangeList;

    struct InterleavedStubFreeListNode
    {
        InterleavedStubFreeListNode *m_pNext;
    };

    InterleavedStubFreeListNode  *m_pFreeListHead;

    const InterleavedLoaderHeapConfig *m_pConfig;

#ifndef DACCESS_COMPILE
protected:
    UnlockedInterleavedLoaderHeap(
        RangeList *pRangeList,
        const InterleavedLoaderHeapConfig *pConfig);

    virtual ~UnlockedInterleavedLoaderHeap();
#endif

private:
    size_t GetBytesAvailReservedRegion();

protected:
    // number of bytes available in region
    size_t UnlockedGetReservedBytesFree()
    {
        LIMITED_METHOD_CONTRACT;
        return m_pEndReservedRegion - m_pAllocPtr;
    }

    PTR_BYTE UnlockedGetAllocPtr()
    {
        LIMITED_METHOD_CONTRACT;
        return m_pAllocPtr;
    }

private:
    // Get some more committed pages - either commit some more in the current reserved region, or, if it
    // has run out, reserve another set of pages
    BOOL GetMoreCommittedPages(size_t dwMinSize);

    // Commit memory pages starting at the specified adress
    BOOL CommitPages(void* pData, size_t dwSizeToCommitPart);

protected:
    // Reserve some pages at any address
    BOOL UnlockedReservePages(size_t dwCommitBlockSize);

protected:
    // Allocates memory for a single stub which is a pair of memory addresses
    // The first address is the pointer at the stub code, and the second
    // address is the data for the stub. These are separated by GetStubCodePageSize()
    // bytes.
    //
    // The return value is a pointer that's guaranteed to be aligned.
    //
    // Here is how to properly backout the memory:
    //
    //   void *pMem = UnlockedAllocStub(d);
    //   UnlockedBackoutStub(pMem);
    //
    // If you use the AllocMemHolder or AllocMemTracker, all this is taken care of
    // behind the scenes.
    //
    //
    void *UnlockedAllocStub(
#ifdef _DEBUG
                                 _In_ _In_z_ const char *szFile
                                 ,int  lineNum
#endif
                                 );

    void *UnlockedAllocStub_NoThrow(
#ifdef _DEBUG
                                         _In_ _In_z_ const char *szFile
                                         ,int  lineNum
#endif
                                 );

protected:
    // This frees memory allocated by UnlockedAllocStub. It's given this horrible name to emphasize
    // that it's purpose is for error path leak prevention purposes. You shouldn't
    // use LoaderHeap's as general-purpose alloc-free heaps.
    void UnlockedBackoutStub(void *pMem
#ifdef _DEBUG
                          , _In_ _In_z_ const char *szFile
                          , int lineNum
                          , _In_ _In_z_ const char *szAllocFile
                          , int AllocLineNum
#endif
                          );
};

//===============================================================================
// This is the class used for CodeManager allocations. At one point it the logic
// was shared with UnlockedLoaderHeap, but that has been changed. This heap is designed
// to provide an api surface that can be used to control the memory regions where
// allocations occur, and provides an alloc only api surface.
//
// Caller is responsible for synchronization. ExplicitControlLoaderHeap is
// not multithread safe.
//===============================================================================
typedef DPTR(class ExplicitControlLoaderHeap) PTR_ExplicitControlLoaderHeap;
class ExplicitControlLoaderHeap : public UnlockedLoaderHeapBaseTraversable
{
#ifdef DACCESS_COMPILE
    friend class ClrDataAccess;
#endif

private:
    // Allocation pointer in current block
    PTR_BYTE            m_pAllocPtr;

    // Points to the end of the committed region in the current block
    PTR_BYTE            m_pPtrToEndOfCommittedRegion;
    PTR_BYTE            m_pEndReservedRegion;

    size_t              m_dwTotalAlloc;

    // When we need to commit pages from our reserved list, number of bytes to commit at a time
    DWORD               m_dwCommitBlockSize;

    // Is this an executable heap?
    bool                m_fExecutableHeap;

    // This is used to hold on to a block of reserved memory provided to the
    // constructor. We do this instead of adding it as the first block because
    // that requires comitting the first page of the reserved block, and for
    // startup working set reasons we want to delay that as long as possible.
    LoaderHeapBlock      m_reservedBlock;

public:

#ifdef _DEBUG
    size_t              m_dwDebugWastedBytes;
    static DWORD        s_dwNumInstancesOfLoaderHeaps;
#endif

#ifdef _DEBUG
    size_t DebugGetWastedBytes()
    {
        WRAPPER_NO_CONTRACT;
        return m_dwDebugWastedBytes + GetBytesAvailCommittedRegion();
    }
#endif

#ifndef DACCESS_COMPILE
public:
    ExplicitControlLoaderHeap(bool fMakeExecutable = false);

    ~ExplicitControlLoaderHeap();
#endif

private:
    size_t GetBytesAvailCommittedRegion();
    size_t GetBytesAvailReservedRegion();

public:
    // number of bytes available in region
    size_t GetReservedBytesFree()
    {
        LIMITED_METHOD_CONTRACT;
        return m_pEndReservedRegion - m_pAllocPtr;
    }

    PTR_BYTE GetAllocPtr()
    {
        LIMITED_METHOD_CONTRACT;
        return m_pAllocPtr;
    }

private:
    // Get some more committed pages - either commit some more in the current reserved region, or, if it
    // has run out, reserve another set of pages
    BOOL GetMoreCommittedPages(size_t dwMinSize);

    // Commit memory pages starting at the specified adress
    BOOL CommitPages(void* pData, size_t dwSizeToCommitPart);

public:
    // Reserve some pages at any address
    BOOL ReservePages(size_t dwCommitBlockSize);

    // Perf Counter reports the size of the heap
    size_t GetSize ()
    {
        LIMITED_METHOD_CONTRACT;
        return m_dwTotalAlloc;
    }

    size_t AllocMem_TotalSize(size_t dwRequestedSize);

public:

    void *AllocMemForCode_NoThrow(size_t dwHeaderSize, size_t dwCodeSize, DWORD dwCodeAlignment, size_t dwReserveForJumpStubs);

    void SetReservedRegion(BYTE* dwReservedRegionAddress, SIZE_T dwReservedRegionSize, BOOL fReleaseMemory);
};

//===============================================================================
// Create the LoaderHeap lock. It's the same lock for several different Heaps.
//===============================================================================
inline CRITSEC_COOKIE CreateLoaderHeapLock()
{
    return ClrCreateCriticalSection(CrstLoaderHeap,CrstFlags(CRST_UNSAFE_ANYMODE | CRST_DEBUGGER_THREAD));
}

//===============================================================================
// Thread-safe variant of UnlockedLoaderHeap.
//===============================================================================
typedef DPTR(class LoaderHeap) PTR_LoaderHeap;
class LoaderHeap : public UnlockedLoaderHeap
{
private:
    CRITSEC_COOKIE    m_CriticalSection;

#ifndef DACCESS_COMPILE
public:
    LoaderHeap(DWORD dwReserveBlockSize,
               DWORD dwCommitBlockSize,
               RangeList *pRangeList = NULL,
               LoaderHeapImplementationKind kind = LoaderHeapImplementationKind::Data,
               BOOL fUnlocked = FALSE
               )
      : UnlockedLoaderHeap(dwReserveBlockSize,
                           dwCommitBlockSize,
                           NULL, 0,
                           pRangeList,
                           kind),
        m_CriticalSection(fUnlocked ? NULL : CreateLoaderHeapLock())
    {
        WRAPPER_NO_CONTRACT;
    }

public:
    LoaderHeap(DWORD dwReserveBlockSize,
               DWORD dwCommitBlockSize,
               const BYTE* dwReservedRegionAddress,
               SIZE_T dwReservedRegionSize,
               RangeList *pRangeList = NULL,
               LoaderHeapImplementationKind kind = LoaderHeapImplementationKind::Data,
               BOOL fUnlocked = FALSE
               )
      : UnlockedLoaderHeap(dwReserveBlockSize,
                           dwCommitBlockSize,
                           dwReservedRegionAddress,
                           dwReservedRegionSize,
                           pRangeList,
                           kind),
        m_CriticalSection(fUnlocked ? NULL : CreateLoaderHeapLock())
    {
        WRAPPER_NO_CONTRACT;
    }

#endif // DACCESS_COMPILE

    virtual ~LoaderHeap()
    {
        WRAPPER_NO_CONTRACT;

#ifndef DACCESS_COMPILE
        if (m_CriticalSection != NULL)
        {
            ClrDeleteCriticalSection(m_CriticalSection);
        }
#endif // DACCESS_COMPILE
    }



#ifdef _DEBUG
#define AllocMem(dwSize)                  RealAllocMem( (dwSize), __FILE__, __LINE__ )
#define AllocMem_NoThrow(dwSize)          RealAllocMem_NoThrow( (dwSize), __FILE__, __LINE__ )
#else
#define AllocMem(dwSize)                  RealAllocMem( (dwSize) )
#define AllocMem_NoThrow(dwSize)          RealAllocMem_NoThrow( (dwSize) )
#endif

public:
    FORCEINLINE TaggedMemAllocPtr RealAllocMem(S_SIZE_T dwSize
#ifdef _DEBUG
                                  ,_In_ _In_z_ const char *szFile
                                  ,int  lineNum
#endif
                  )
    {
        WRAPPER_NO_CONTRACT;

        if(dwSize.IsOverflow()) ThrowOutOfMemory();

        return RealAllocMemUnsafe(dwSize.Value() COMMA_INDEBUG(szFile) COMMA_INDEBUG(lineNum));

    }

    FORCEINLINE TaggedMemAllocPtr RealAllocMem_NoThrow(S_SIZE_T  dwSize
#ifdef _DEBUG
                                           ,_In_ _In_z_ const char *szFile
                                           ,int  lineNum
#endif
                  )
    {
        WRAPPER_NO_CONTRACT;

        if(dwSize.IsOverflow()) {
            TaggedMemAllocPtr tmap;
            tmap.m_pMem             = NULL;
            tmap.m_dwRequestedSize  = dwSize.Value();
            tmap.m_pHeap            = this;
            tmap.m_dwExtra          = 0;
#ifdef _DEBUG
            tmap.m_szFile           = szFile;
            tmap.m_lineNum          = lineNum;
#endif

            return tmap;
        }

        return RealAllocMemUnsafe_NoThrow(dwSize.Value() COMMA_INDEBUG(szFile) COMMA_INDEBUG(lineNum));
    }
private:

    TaggedMemAllocPtr RealAllocMemUnsafe(size_t dwSize
#ifdef _DEBUG
                                  ,_In_ _In_z_ const char *szFile
                                  ,int  lineNum
#endif
                  )
    {
        WRAPPER_NO_CONTRACT;

        void *pResult;
        TaggedMemAllocPtr tmap;

        CRITSEC_Holder csh(m_CriticalSection);
        pResult = UnlockedAllocMem(dwSize
#ifdef _DEBUG
                                 , szFile
                                 , lineNum
#endif
                                 );
        tmap.m_pMem             = pResult;
        tmap.m_dwRequestedSize  = dwSize;
        tmap.m_pHeap            = this;
        tmap.m_dwExtra          = 0;
#ifdef _DEBUG
        tmap.m_szFile           = szFile;
        tmap.m_lineNum          = lineNum;
#endif

        return tmap;
    }

    TaggedMemAllocPtr RealAllocMemUnsafe_NoThrow(size_t  dwSize
#ifdef _DEBUG
                                           ,_In_ _In_z_ const char *szFile
                                           ,int  lineNum
#endif
                  )
    {
        WRAPPER_NO_CONTRACT;

        void *pResult;
        TaggedMemAllocPtr tmap;

        CRITSEC_Holder csh(m_CriticalSection);

        pResult = UnlockedAllocMem_NoThrow(dwSize
#ifdef _DEBUG
                                           , szFile
                                           , lineNum
#endif
                                           );

        tmap.m_pMem             = pResult;
        tmap.m_dwRequestedSize  = dwSize;
        tmap.m_pHeap            = this;
        tmap.m_dwExtra          = 0;
#ifdef _DEBUG
        tmap.m_szFile           = szFile;
        tmap.m_lineNum          = lineNum;
#endif

        return tmap;
    }



#ifdef _DEBUG
#define AllocAlignedMem(dwSize, dwAlign)                RealAllocAlignedMem( (dwSize), (dwAlign), __FILE__, __LINE__)
#define AllocAlignedMem_NoThrow(dwSize, dwAlign)        RealAllocAlignedMem_NoThrow( (dwSize), (dwAlign), __FILE__, __LINE__)
#else
#define AllocAlignedMem(dwSize, dwAlign)                RealAllocAlignedMem( (dwSize), (dwAlign) )
#define AllocAlignedMem_NoThrow(dwSize, dwAlign)        RealAllocAlignedMem_NoThrow( (dwSize), (dwAlign) )
#endif

public:
    TaggedMemAllocPtr RealAllocAlignedMem(size_t  dwRequestedSize
                                         ,size_t  dwAlignment
#ifdef _DEBUG
                                         ,_In_ _In_z_ const char *szFile
                                         ,int  lineNum
#endif
                                         )
    {
        WRAPPER_NO_CONTRACT;

        CRITSEC_Holder csh(m_CriticalSection);


        TaggedMemAllocPtr tmap;
        void *pResult;
        size_t dwExtra;

        pResult = UnlockedAllocAlignedMem(dwRequestedSize
                                         ,dwAlignment
                                         ,&dwExtra
#ifdef _DEBUG
                                         ,szFile
                                         ,lineNum
#endif
                                     );

        tmap.m_pMem             = (void*)(((BYTE*)pResult) - dwExtra);
        tmap.m_dwRequestedSize  = dwRequestedSize + dwExtra;
        tmap.m_pHeap            = this;
        tmap.m_dwExtra          = dwExtra;
#ifdef _DEBUG
        tmap.m_szFile           = szFile;
        tmap.m_lineNum          = lineNum;
#endif

        return tmap;
    }


    TaggedMemAllocPtr RealAllocAlignedMem_NoThrow(size_t  dwRequestedSize
                                                 ,size_t  dwAlignment
#ifdef _DEBUG
                                                 ,_In_ _In_z_ const char *szFile
                                                 ,int  lineNum
#endif
                                                 )
    {
        WRAPPER_NO_CONTRACT;

        CRITSEC_Holder csh(m_CriticalSection);


        TaggedMemAllocPtr tmap;
        void *pResult;
        size_t dwExtra;

        pResult = UnlockedAllocAlignedMem_NoThrow(dwRequestedSize
                                                 ,dwAlignment
                                                 ,&dwExtra
#ifdef _DEBUG
                                                 ,szFile
                                                 ,lineNum
#endif
                                            );

        _ASSERTE(!(pResult == NULL && dwExtra != 0));

        tmap.m_pMem             = (void*)(((BYTE*)pResult) - dwExtra);
        tmap.m_dwRequestedSize  = dwRequestedSize + dwExtra;
        tmap.m_pHeap            = this;
        tmap.m_dwExtra          = dwExtra;
#ifdef _DEBUG
        tmap.m_szFile           = szFile;
        tmap.m_lineNum          = lineNum;
#endif

        return tmap;
    }


public:
    // This frees memory allocated by RealAllocMem. It's given this horrible name to emphasize
    // that it's purpose is for error path leak prevention purposes. You shouldn't
    // use LoaderHeap's as general-purpose alloc-free heaps.
    void RealBackoutMem(void *pMem
                        , size_t dwSize
#ifdef _DEBUG
                        , _In_ _In_z_ const char *szFile
                        , int lineNum
                        , _In_ _In_z_ const char *szAllocFile
                        , int allocLineNum
#endif
                        )
    {
        WRAPPER_NO_CONTRACT;
        CRITSEC_Holder csh(m_CriticalSection);
        UnlockedBackoutMem(pMem
                           , dwSize
#ifdef _DEBUG
                           , szFile
                           , lineNum
                           , szAllocFile
                           , allocLineNum
#endif
                           );
    }

public:
// Extra CallTracing support
#ifdef _DEBUG
    void ClearEvents()
    {
        WRAPPER_NO_CONTRACT;
        CRITSEC_Holder csh(m_CriticalSection);
        UnlockedClearEvents();
    }

    void CompactEvents()
    {
        WRAPPER_NO_CONTRACT;
        CRITSEC_Holder csh(m_CriticalSection);
        UnlockedCompactEvents();
    }

    void PrintEvents()
    {
        WRAPPER_NO_CONTRACT;
        CRITSEC_Holder csh(m_CriticalSection);
        UnlockedPrintEvents();
    }
#endif

};


#ifdef _DEBUG
#define AllocStub()                RealAllocStub(__FILE__, __LINE__)
#else
#define AllocStub()                RealAllocStub()
#endif

//===============================================================================
// Thread-safe variant of UnlockedInterleavedLoaderHeap.
//===============================================================================
typedef DPTR(class InterleavedLoaderHeap) PTR_InterleavedLoaderHeap;
class InterleavedLoaderHeap : public UnlockedInterleavedLoaderHeap
{
private:
    CRITSEC_COOKIE    m_CriticalSection;

#ifndef DACCESS_COMPILE
public:
    InterleavedLoaderHeap(RangeList *pRangeList,
               BOOL fUnlocked,
               const InterleavedLoaderHeapConfig *pConfig
               )
      : UnlockedInterleavedLoaderHeap(
                           pRangeList,
                           pConfig),
        m_CriticalSection(fUnlocked ? NULL : CreateLoaderHeapLock())
    {
        WRAPPER_NO_CONTRACT;
    }

#endif // DACCESS_COMPILE

    virtual ~InterleavedLoaderHeap()
    {
        WRAPPER_NO_CONTRACT;

#ifndef DACCESS_COMPILE
        if (m_CriticalSection != NULL)
        {
            ClrDeleteCriticalSection(m_CriticalSection);
        }
#endif // DACCESS_COMPILE
    }

public:
    TaggedMemAllocPtr RealAllocStub(
#ifdef _DEBUG
                                         _In_ _In_z_ const char *szFile
                                         ,int  lineNum
#endif
                                         )
    {
        WRAPPER_NO_CONTRACT;

        CRITSEC_Holder csh(m_CriticalSection);


        TaggedMemAllocPtr tmap;
        void *pResult;

        pResult = UnlockedAllocStub(
#ifdef _DEBUG
                                         szFile
                                         ,lineNum
#endif
                                     );

        tmap.m_pMem             = pResult;
        tmap.m_dwRequestedSize  = 1;
        tmap.m_pHeap            = this;
        tmap.m_dwExtra          = 0;
#ifdef _DEBUG
        tmap.m_szFile           = szFile;
        tmap.m_lineNum          = lineNum;
#endif

        return tmap;
    }


public:
    // This frees memory allocated by RealAllocStub. It's given this horrible name to emphasize
    // that it's purpose is for error path leak prevention purposes. You shouldn't
    // use LoaderHeap's as general-purpose alloc-free heaps.
    void RealBackoutMem(void *pMem
                        , size_t dwSize
#ifdef _DEBUG
                        , _In_ _In_z_ const char *szFile
                        , int lineNum
                        , _In_ _In_z_ const char *szAllocFile
                        , int allocLineNum
#endif
                        )
    {
        WRAPPER_NO_CONTRACT;
        CRITSEC_Holder csh(m_CriticalSection);
        UnlockedBackoutStub(pMem
#ifdef _DEBUG
                           , szFile
                           , lineNum
                           , szAllocFile
                           , allocLineNum
#endif
                           );
    }

public:
// Extra CallTracing support
#ifdef _DEBUG
    void ClearEvents()
    {
        WRAPPER_NO_CONTRACT;
        CRITSEC_Holder csh(m_CriticalSection);
        UnlockedClearEvents();
    }

    void CompactEvents()
    {
        WRAPPER_NO_CONTRACT;
        CRITSEC_Holder csh(m_CriticalSection);
        UnlockedCompactEvents();
    }

    void PrintEvents()
    {
        WRAPPER_NO_CONTRACT;
        CRITSEC_Holder csh(m_CriticalSection);
        UnlockedPrintEvents();
    }
#endif
};

//==============================================================================
// AllocMemHolder : Allocated memory from LoaderHeap
//
// Old:
//
//   Foo* pFoo = (Foo*)pLoaderHeap->AllocMem(size);
//   pFoo->BackoutMem(pFoo, size)
//
//
// New:
//
//  {
//      AllocMemHolder<Foo> pfoo = pLoaderHeap->AllocMem();
//  } // BackoutMem on out of scope
//
//==============================================================================
template <typename TYPE>
class AllocMemHolder
{
    private:
        TaggedMemAllocPtr m_value;
        BOOL              m_fAcquired;


    //--------------------------------------------------------------------
    // All allowed (and disallowed) ctors here.
    //--------------------------------------------------------------------
    public:
        // Allow the construction "Holder h;"
        AllocMemHolder()
        {
            LIMITED_METHOD_CONTRACT;

            m_value.m_pMem = NULL;
            m_value.m_dwRequestedSize = 0;
            m_value.m_pHeap = 0;
            m_value.m_dwExtra = 0;
#ifdef _DEBUG
            m_value.m_szFile = NULL;
            m_value.m_lineNum = 0;
#endif
            m_fAcquired    = FALSE;
        }

    public:
        // Allow the construction "Holder h = pHeap->AllocMem()"
        AllocMemHolder(const TaggedMemAllocPtr value)
        {
            LIMITED_METHOD_CONTRACT;
            m_value     = value;
            m_fAcquired = TRUE;
        }

    private:
        // Disallow "Holder holder1 = holder2"
        AllocMemHolder(const AllocMemHolder<TYPE> &);


    private:
        // Disallow "Holder holder1 = void*"
        AllocMemHolder(const LPVOID &);

    //--------------------------------------------------------------------
    // Destructor (and the whole point of AllocMemHolder)
    //--------------------------------------------------------------------
    public:
        ~AllocMemHolder()
        {
            WRAPPER_NO_CONTRACT;
            if (m_fAcquired && m_value.m_pMem)
            {
                m_value.m_pHeap->RealBackoutMem(m_value.m_pMem,
                                                m_value.m_dwRequestedSize
#ifdef _DEBUG
                                                ,__FILE__
                                                ,__LINE__
                                                ,m_value.m_szFile
                                                ,m_value.m_lineNum
#endif
                                                );
            }
        }


    //--------------------------------------------------------------------
    // All allowed (and disallowed) assignment operators here.
    //--------------------------------------------------------------------
    public:
        // Reluctantly allow "AllocMemHolder h; ... h = pheap->AllocMem()"
        void operator=(const TaggedMemAllocPtr & value)
        {
            WRAPPER_NO_CONTRACT;
            // However, prevent repeated assignments as that would leak.
            _ASSERTE(m_value.m_pMem == NULL && !m_fAcquired);
            m_value = value;
            m_fAcquired = TRUE;
        }

    private:
        // Disallow "holder == holder2"
        const AllocMemHolder<TYPE> & operator=(const AllocMemHolder<TYPE> &);

    private:
        // Disallow "holder = void*"
        const AllocMemHolder<TYPE> & operator=(const LPVOID &);


    //--------------------------------------------------------------------
    // Operations on the holder itself
    //--------------------------------------------------------------------
    public:
        // Call this when you're ready to take ownership away from the holder.
        void SuppressRelease()
        {
            LIMITED_METHOD_CONTRACT;
            m_fAcquired = FALSE;
        }



    //--------------------------------------------------------------------
    // ... And the smart-pointer stuff so we can drop holders on top
    // of former pointer variables (mostly)
    //--------------------------------------------------------------------
    public:
        // Allow holder to be treated as the underlying pointer type
        operator TYPE* ()
        {
            LIMITED_METHOD_CONTRACT;
            return (TYPE*)(void*)m_value;
        }

    public:
        // Allow holder to be treated as the underlying pointer type
        TYPE* operator->()
        {
            LIMITED_METHOD_CONTRACT;
            return (TYPE*)(void*)m_value;
        }
    public:
        int operator==(TYPE* value)
        {
            LIMITED_METHOD_CONTRACT;
            return ((void*)m_value) == ((void*)value);
        }

    public:
        int operator!=(TYPE* value)
        {
            LIMITED_METHOD_CONTRACT;
            return ((void*)m_value) != ((void*)value);
        }

    public:
        int operator!() const
        {
            LIMITED_METHOD_CONTRACT;
            return m_value.m_pMem == NULL;
        }


};



// This utility helps track loaderheap allocations. Its main purpose
// is to backout allocations in case of an exception.
class AllocMemTracker
{
    public:
        AllocMemTracker();
        ~AllocMemTracker();

        // Tells tracker to store an allocated loaderheap block.
        //
        // Returns the pointer address of block for convenience.
        //
        // Ok to call on failed loaderheap allocation (will just do nothing and propagate the OOM as apropos).
        //
        // If Track fails due to an OOM allocating node space, it will backout the loaderheap block before returning.
        void *Track(TaggedMemAllocPtr tmap);
        void *Track_NoThrow(TaggedMemAllocPtr tmap);

        void SuppressRelease();

    private:
        struct AllocMemTrackerNode
        {
            ILoaderHeapBackout *m_pHeap;
            void            *m_pMem;
            size_t           m_dwRequestedSize;
#ifdef _DEBUG
            const char      *m_szAllocFile;
            int              m_allocLineNum;
#endif
        };

        enum
        {
            kAllocMemTrackerBlockSize =
#ifdef _DEBUG
                                        3
#else
                                       20
#endif
        };

        struct AllocMemTrackerBlock
        {
            AllocMemTrackerBlock    *m_pNext;
            int                      m_nextFree;
            AllocMemTrackerNode      m_Node[kAllocMemTrackerBlockSize];
        };

        AllocMemTrackerBlock        *m_pFirstBlock;
        AllocMemTrackerBlock        m_FirstBlock; // Stack-allocate the first block - "new" the rest.

    protected:
        BOOL                        m_fReleased;
};

#endif // __LoaderHeap_h__
