// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*++



Module Name:

    listedobjectmanager.hpp

Abstract:
    Shared memory based object manager



--*/

#ifndef _PAL_SHMOBJECTMANAGER_HPP_
#define _PAL_SHMOBJECTMANAGER_HPP_

#include "pal/corunix.hpp"
#include "pal/handlemgr.hpp"
#include "pal/list.h"
#include "listedobject.hpp"

namespace CorUnix
{
    class CListedObjectManager : public IPalObjectManager
    {
    protected:

        minipal_mutex m_csListLock;
        bool m_fListLockInitialized;
        LIST_ENTRY m_leNamedObjects;
        LIST_ENTRY m_leAnonymousObjects;

        CSimpleHandleManager m_HandleManager;

    public:

        CListedObjectManager()
            :
            m_fListLockInitialized(FALSE)
        {
        };

        virtual ~CListedObjectManager()
        {
        };

        PAL_ERROR
        Initialize(
            void
            );

        PAL_ERROR
        Shutdown(
            CPalThread *pthr
            );

        //
        // IPalObjectManager routines
        //

        virtual
        PAL_ERROR
        AllocateObject(
            CPalThread *pthr,
            CObjectType *pot,
            CObjectAttributes *poa,
            IPalObject **ppobjNew
            );

        virtual
        PAL_ERROR
        RegisterObject(
            CPalThread *pthr,
            IPalObject *pobjToRegister,
            CAllowedObjectTypes *paot,
            HANDLE *pHandle,
            IPalObject **ppobjRegistered
            );

        virtual
        PAL_ERROR
        LocateObject(
            CPalThread *pthr,
            CPalString *psObjectToLocate,
            CAllowedObjectTypes *paot,
            IPalObject **ppobj
            );

        virtual
        PAL_ERROR
        ObtainHandleForObject(
            CPalThread *pthr,
            IPalObject *pobj,
            HANDLE *pNewHandle
            );

        virtual
        PAL_ERROR
        RevokeHandle(
            CPalThread *pthr,
            HANDLE hHandleToRevoke
            );

        virtual
        PAL_ERROR
        ReferenceObjectByHandle(
            CPalThread *pthr,
            HANDLE hHandleToReference,
            CAllowedObjectTypes *paot,
            IPalObject **ppobj
            );

        virtual
        PAL_ERROR
        ReferenceMultipleObjectsByHandleArray(
            CPalThread *pthr,
            HANDLE rghHandlesToReference[],
            DWORD dwHandleCount,
            CAllowedObjectTypes *paot,
            IPalObject *rgpobjs[]
            );
    };
}

#endif // _PAL_SHMOBJECTMANAGER_HPP_

