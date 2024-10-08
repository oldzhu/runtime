// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Win32.SafeHandles
{
    internal sealed class SafeAsn1ObjectHandle : SafeHandle
    {
        public SafeAsn1ObjectHandle() :
            base(IntPtr.Zero, ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            Interop.Crypto.Asn1ObjectFree(handle);
            SetHandle(IntPtr.Zero);
            return true;
        }

        public override bool IsInvalid
        {
            get { return handle == IntPtr.Zero; }
        }
    }

    internal sealed class SafeAsn1OctetStringHandle : SafeHandle
    {
        public SafeAsn1OctetStringHandle() :
            base(IntPtr.Zero, ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            Interop.Crypto.Asn1OctetStringFree(handle);
            SetHandle(IntPtr.Zero);
            return true;
        }

        public override bool IsInvalid
        {
            get { return handle == IntPtr.Zero; }
        }
    }

    internal sealed class SafeSharedAsn1IntegerHandle : SafeInteriorHandle
    {
        public SafeSharedAsn1IntegerHandle() :
            base(IntPtr.Zero, ownsHandle: true)
        {
        }
    }

    internal sealed class SafeSharedAsn1OctetStringHandle : SafeInteriorHandle
    {
        public SafeSharedAsn1OctetStringHandle() :
            base(IntPtr.Zero, ownsHandle: true)
        {
        }
    }
}
