// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*++
Module Name:

    include/pal/synchcache.hpp

Abstract:
    Simple look-aside cache for unused objects with default
    constructor or no constructor
--*/

#ifndef _SYNCH_CACHE_H_
#define _SYNCH_CACHE_H_

#include "pal/thread.hpp"
#include <new>

namespace CorUnix
{
    template <typename T> class CSynchCache
    {
        typedef union _USynchCacheStackNode
        {
            union _USynchCacheStackNode * next;
            BYTE objraw[sizeof(T)];
        } USynchCacheStackNode;

        static const int MaxDepth = 256;

        Volatile<USynchCacheStackNode*> m_pHead;
        minipal_mutex m_cs;
        Volatile<int> m_iDepth;
        int m_iMaxDepth;
#ifdef _DEBUG
        int m_iMaxTrackedDepth;
#endif

        void Lock(CPalThread * pthrCurrent)
            { minipal_mutex_enter(&m_cs); }
        void Unlock(CPalThread * pthrCurrent)
            { minipal_mutex_leave(&m_cs); }

     public:
        CSynchCache(int iMaxDepth = MaxDepth) :
            m_pHead(NULL),
            m_iDepth(0),
            m_iMaxDepth(iMaxDepth)
#ifdef _DEBUG
            ,m_iMaxTrackedDepth(0)
#endif
        {
            minipal_mutex_init(&m_cs);
            if (m_iMaxDepth < 0)
            {
                m_iMaxDepth = 0;
            }
        }

        ~CSynchCache()
        {
            Flush(NULL, true);
            minipal_mutex_destroy(&m_cs);
        }

#ifdef _DEBUG
        int GetMaxTrackedDepth() { return m_iMaxTrackedDepth; }
#endif

        T * Get(CPalThread * pthrCurrent)
        {
            T * pObj = NULL;

            Get(pthrCurrent, 1, &pObj);
            return pObj;
        }

        int Get(CPalThread * pthrCurrent, int n, T ** ppObjs)
        {
            void * pvObjRaw;
            USynchCacheStackNode * pNode;
            int i = 0,j;

            Lock(pthrCurrent);
            pNode = m_pHead;
            while (pNode && i < n)
            {
                ppObjs[i] = (T *)pNode;
                pNode = pNode->next;
                i++;
            }
            m_pHead = pNode;
            m_iDepth -= i;

#ifdef _DEBUG
            if (NULL == m_pHead && m_iDepth != 0)
            {
                // Can't use ASSERT here, since this is header
                // is included by other headers with inline methods
                // which causes template instatiation in the header
                // where the DEBUG CHANNEL is not defined and cannot
                // be defined
                fprintf(stderr,"SYNCCACHE: Invalid cache depth value");
                DebugBreak();
            }
#endif // _DEBUG

            Unlock(pthrCurrent);

            for (j=i;j<n;j++)
            {
                pvObjRaw = (void *) new(std::nothrow) USynchCacheStackNode();
                if (NULL == pvObjRaw)
                    break;
#ifdef _DEBUG
                memset(pvObjRaw, 0, sizeof(USynchCacheStackNode));
#endif
                ppObjs[j] = reinterpret_cast<T*>(pvObjRaw);
            }

            for (i=0;i<j;i++)
            {
                new ((void *)ppObjs[i]) T;
            }

            return j;
        }

        void Add(CPalThread * pthrCurrent, T * pobj)
        {
            USynchCacheStackNode * pNode = reinterpret_cast<USynchCacheStackNode *>(pobj);

            if (NULL == pobj)
            {
                return;
            }

            pobj->~T();

            Lock(pthrCurrent);
            if (m_iDepth < m_iMaxDepth)
            {
#ifdef _DEBUG
                if (m_iDepth > m_iMaxTrackedDepth)
                {
                    m_iMaxTrackedDepth = m_iDepth;
                }
#endif
                pNode->next = m_pHead;
                m_pHead = pNode;
                m_iDepth++;
            }
            else
            {
                delete (char *)pNode;
            }
            Unlock(pthrCurrent);
        }

        void Flush(CPalThread * pthrCurrent, bool fDontLock = false)
        {
            USynchCacheStackNode * pNode, * pTemp;

            if (!fDontLock)
            {
                Lock(pthrCurrent);
            }
            pNode = m_pHead;
            m_pHead = NULL;
            m_iDepth = 0;
            if (!fDontLock)
            {
                Unlock(pthrCurrent);
            }

            while (pNode)
            {
                pTemp = pNode;
                pNode = pNode->next;
                delete (char *)pTemp;
            }
        }
    };

    template <typename T> class CSHRSynchCache
    {
        union _USHRSynchCacheStackNode; // fwd declaration
        typedef struct _SHRCachePTRs
        {
            union _USHRSynchCacheStackNode * pNext;
            SharedID shrid;
        } SHRCachePTRs;
        typedef union _USHRSynchCacheStackNode
        {
            SHRCachePTRs  pointers;
            BYTE objraw[sizeof(T)];
        } USHRSynchCacheStackNode;

        static const int MaxDepth       = 256;
        static const int PreAllocFactor = 10; // Everytime a Get finds no available
                                              // cached raw instances, it preallocates
                                              // MaxDepth/PreAllocFactor new raw
                                              // instances and store them into the
                                              // cache before continuing

        Volatile<USHRSynchCacheStackNode*> m_pHead;
        minipal_mutex m_cs;
        Volatile<int> m_iDepth;
        int m_iMaxDepth;
#ifdef _DEBUG
        int m_iMaxTrackedDepth;
#endif

        void Lock(CPalThread * pthrCurrent)
            { minipal_mutex_enter(&m_cs); }
        void Unlock(CPalThread * pthrCurrent)
            { minipal_mutex_leave(&m_cs); }

     public:
        CSHRSynchCache(int iMaxDepth = MaxDepth) :
            m_pHead(NULL),
            m_iDepth(0),
            m_iMaxDepth(iMaxDepth)
#ifdef _DEBUG
            ,m_iMaxTrackedDepth(0)
#endif
        {
            minipal_mutex_init(&m_cs);
            if (m_iMaxDepth < 0)
            {
                m_iMaxDepth = 0;
            }
        }

        ~CSHRSynchCache()
        {
            Flush(NULL, true);
            minipal_mutex_destroy(&m_cs);
        }

#ifdef _DEBUG
        int GetMaxTrackedDepth() { return m_iMaxTrackedDepth; }
#endif

        SharedID Get(CPalThread * pthrCurrent)
        {
            SharedID shridObj = NULL;

            Get(pthrCurrent, 1, &shridObj);
            return shridObj;
        }

        int Get(CPalThread * pthrCurrent, int n, SharedID * shridpObjs)
        {
            SharedID shridObj;
            void * pvObjRaw = NULL;
            USHRSynchCacheStackNode * pNode;
            int i = 0, j, k;

            Lock(pthrCurrent);
            pNode = m_pHead;
            while (pNode && i < n)
            {
                shridpObjs[i] = pNode->pointers.shrid;
                pvObjRaw = (void *)pNode;
                pNode = pNode->pointers.pNext;
                i++;
            }
            m_pHead = pNode;
            m_iDepth -= i;

#ifdef _DEBUG
            if (NULL == m_pHead && m_iDepth != 0)
            {
                    // Can't use ASSERT here, since this is header
                    // (see comment above)
                    fprintf(stderr,"SYNCCACHE: Invalid cache depth value");
                    DebugBreak();
            }
#endif // _DEBUG

            if (0 == m_iDepth)
            {
                for (k=0; k<m_iMaxDepth/PreAllocFactor-n+i; k++)
                {
                    shridObj = malloc(sizeof(USHRSynchCacheStackNode));
                    if (NULL == shridObj)
                    {
                        Flush(pthrCurrent, true);
                        break;
                    }
                    pNode = SharedIDToTypePointer(USHRSynchCacheStackNode, shridObj);
#ifdef _DEBUG
                    memset(reinterpret_cast<void*>(pNode), 0, sizeof(USHRSynchCacheStackNode));
#endif
                    pNode->pointers.shrid = shridObj;
                    pNode->pointers.pNext = m_pHead;
                    m_pHead = pNode;
                    m_iDepth++;
                }
            }

            Unlock(pthrCurrent);

            for (j=i;j<n;j++)
            {
                shridObj = malloc(sizeof(USHRSynchCacheStackNode));
                if (NULL == shridObj)
                    break;
#ifdef _DEBUG
                pvObjRaw = SharedIDToPointer(shridObj);
                memset(pvObjRaw, 0, sizeof(USHRSynchCacheStackNode));
#endif
                shridpObjs[j] = shridObj;
            }

            for (i=0;i<j;i++)
            {
                pvObjRaw = SharedIDToPointer(shridpObjs[i]);
                new (pvObjRaw) T;
            }

            return j;
        }

        void Add(CPalThread * pthrCurrent, SharedID shridObj)
        {
            if (NULL == shridObj)
            {
                return;
            }

            USHRSynchCacheStackNode * pNode = SharedIDToTypePointer(USHRSynchCacheStackNode, shridObj);
            T * pObj = reinterpret_cast<T *>(pNode);

            pObj->~T();

            pNode->pointers.shrid = shridObj;

            Lock(pthrCurrent);
            if (m_iDepth < m_iMaxDepth)
            {
                m_iDepth++;
#ifdef _DEBUG
                if (m_iDepth > m_iMaxTrackedDepth)
                {
                    m_iMaxTrackedDepth = m_iDepth;
                }
#endif
                pNode->pointers.pNext = m_pHead;
                m_pHead = pNode;
            }
            else
            {
                free(shridObj);
            }
            Unlock(pthrCurrent);
        }

        void Flush(CPalThread * pthrCurrent, bool fDontLock = false)
        {
            USHRSynchCacheStackNode * pNode, * pTemp;
            SharedID shridTemp;

            if (!fDontLock)
            {
                Lock(pthrCurrent);
            }
            pNode = m_pHead;
            m_pHead = NULL;
            m_iDepth = 0;
            if (!fDontLock)
            {
                Unlock(pthrCurrent);
            }

            while (pNode)
            {
                pTemp = pNode;
                pNode = pNode->pointers.pNext;
                shridTemp = pTemp->pointers.shrid;
                free(shridTemp);
            }
        }
    };
}

#endif // _SYNCH_CACHE_H_

