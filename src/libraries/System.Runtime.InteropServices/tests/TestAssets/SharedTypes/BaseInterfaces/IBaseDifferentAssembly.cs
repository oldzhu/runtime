﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SharedTypes.ComInterfaces
{
    [GeneratedComInterface]
    [Guid(IID)]
    public partial interface IExternalBase
    {
        public int GetInt();
        public void SetInt(int x);
        public const string IID = "2c3f9903-b586-46b1-881b-adfce9af47b1";
    }
}
