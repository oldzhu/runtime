// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using System;
using System.Collections.Generic;
using System.Reflection.Runtime.General;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using Internal.Metadata.NativeFormat;
using Internal.NativeFormat;
using Internal.Runtime.Augments;
using Internal.Runtime.CompilerServices;
using Internal.TypeSystem;

using Debug = System.Diagnostics.Debug;

namespace Internal.Runtime.TypeLoader
{
    internal class Callbacks : TypeLoaderCallbacks
    {
        public override bool TryGetOwningTypeForMethodDictionary(IntPtr dictionary, out RuntimeTypeHandle owningType)
        {
            // PERF: computing NameAndSignature and the instantiation (that we discard) was useless
            return TypeLoaderEnvironment.Instance.TryGetGenericMethodComponents(dictionary, out owningType, out _, out _);
        }

        public override TypeManagerHandle GetModuleForMetadataReader(MetadataReader reader)
        {
            return ModuleList.Instance.GetModuleForMetadataReader(reader);
        }

        public override bool TryGetConstructedGenericTypeForComponents(RuntimeTypeHandle genericTypeDefinitionHandle, RuntimeTypeHandle[] genericTypeArgumentHandles, out RuntimeTypeHandle runtimeTypeHandle)
        {
            return TypeLoaderEnvironment.Instance.TryGetConstructedGenericTypeForComponents(genericTypeDefinitionHandle, genericTypeArgumentHandles, out runtimeTypeHandle);
        }

        public override IntPtr GetThreadStaticGCDescForDynamicType(TypeManagerHandle typeManagerHandle, int index)
        {
            // We can use InstanceOrNull because we can't have a reference to a dynamic type without creating type loader first
            return TypeLoaderEnvironment.InstanceOrNull.GetThreadStaticGCDescForDynamicType(typeManagerHandle, (uint)index);
        }

        public override IntPtr GenericLookupFromContextAndSignature(IntPtr context, IntPtr signature, out IntPtr auxResult)
        {
            return TypeLoaderEnvironment.Instance.GenericLookupFromContextAndSignature(context, signature, out auxResult);
        }

        public override RuntimeMethodHandle GetRuntimeMethodHandleForComponents(RuntimeTypeHandle declaringTypeHandle, MethodHandle handle, RuntimeTypeHandle[] genericMethodArgs)
        {
            return TypeLoaderEnvironment.Instance.GetRuntimeMethodHandleForComponents(declaringTypeHandle, handle, genericMethodArgs);
        }

        public override IntPtr TryGetDefaultConstructorForType(RuntimeTypeHandle runtimeTypeHandle)
        {
            return TypeLoaderEnvironment.Instance.TryGetDefaultConstructorForType(runtimeTypeHandle);
        }

        public override IntPtr ResolveGenericVirtualMethodTarget(RuntimeTypeHandle targetTypeHandle, RuntimeMethodHandle declMethod)
        {
            return TypeLoaderEnvironment.Instance.ResolveGenericVirtualMethodTarget(targetTypeHandle, declMethod);
        }

        public override RuntimeFieldHandle GetRuntimeFieldHandleForComponents(RuntimeTypeHandle declaringTypeHandle, FieldHandle handle)
        {
            return TypeLoaderEnvironment.Instance.GetRuntimeFieldHandleForComponents(declaringTypeHandle, handle);
        }

        public override IntPtr ConvertUnboxingFunctionPointerToUnderlyingNonUnboxingPointer(IntPtr unboxingFunctionPointer, RuntimeTypeHandle declaringType)
        {
            return TypeLoaderEnvironment.ConvertUnboxingFunctionPointerToUnderlyingNonUnboxingPointer(unboxingFunctionPointer, declaringType);
        }

        public override bool TryGetPointerTypeForTargetType(RuntimeTypeHandle pointeeTypeHandle, out RuntimeTypeHandle pointerTypeHandle)
        {
            return TypeLoaderEnvironment.Instance.TryGetPointerTypeForTargetType(pointeeTypeHandle, out pointerTypeHandle);
        }

        public override bool TryGetArrayTypeForElementType(RuntimeTypeHandle elementTypeHandle, bool isMdArray, int rank, out RuntimeTypeHandle arrayTypeHandle)
        {
            return TypeLoaderEnvironment.Instance.TryGetArrayTypeForElementType(elementTypeHandle, isMdArray, rank, out arrayTypeHandle);
        }
    }

    public sealed partial class TypeLoaderEnvironment
    {
        [ThreadStatic]
        private static bool t_isReentrant;

        private static TypeLoaderEnvironment s_instance;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static TypeLoaderEnvironment InitializeInstance()
        {
            TypeLoaderEnvironment instance = new TypeLoaderEnvironment();
            if (Interlocked.CompareExchange(ref s_instance, instance, null) == null)
                return instance;
            return s_instance;
        }

        public static TypeLoaderEnvironment Instance => s_instance ?? InitializeInstance();

        public static TypeLoaderEnvironment InstanceOrNull => s_instance;

        // Cache the NativeReader in each module to avoid looking up the NativeLayoutInfo blob each
        // time we call GetNativeLayoutInfoReader(). The dictionary is a thread static variable to ensure
        // thread safety. Using ThreadStatic instead of a lock is ok as long as the NativeReader class is
        // small enough in size (which is the case today).
        [ThreadStatic]
        private static LowLevelDictionary<TypeManagerHandle, NativeReader> t_moduleNativeReaders;

        // Eager initialization called from LibraryInitializer for the assembly.
        internal static void Initialize()
        {
            RuntimeAugments.InitializeLookups(new Callbacks());
        }

        // To keep the synchronization simple, we execute all type loading under a global lock
        private Lock _typeLoaderLock = new Lock(useTrivialWaits: true);

        public void VerifyTypeLoaderLockHeld()
        {
            if (!_typeLoaderLock.IsHeldByCurrentThread)
                Environment.FailFast("TypeLoaderLock not held");
        }

        public IntPtr GenericLookupFromContextAndSignature(IntPtr context, IntPtr signature, out IntPtr auxResult)
        {
            IntPtr result;

            using (_typeLoaderLock.EnterScope())
            {
                try
                {
                    if (t_isReentrant)
                        Environment.FailFast("Reentrant lazy generic lookup");
                    t_isReentrant = true;

                    result = TypeBuilder.BuildGenericLookupTarget(context, signature, out auxResult);

                    t_isReentrant = false;
                }
                catch
                {
                    // Catch and rethrow any exceptions instead of using finally block. Otherwise, filters that are run during
                    // the first pass of exception unwind may hit the re-entrancy fail fast above.

                    // TODO: Convert this to filter for better diagnostics once we switch to Roslyn

                    t_isReentrant = false;
                    throw;
                }
            }

            return result;
        }

        private bool EnsureTypeHandleForType(TypeDesc type)
        {
            if (type.RuntimeTypeHandle.IsNull())
            {
                using (_typeLoaderLock.EnterScope())
                {
                    // Now that we hold the lock, we may find that existing types can now find
                    // their associated RuntimeTypeHandle. Flush the type builder states as a way
                    // to force the reresolution of RuntimeTypeHandles which couldn't be found before.
                    type.Context.FlushTypeBuilderStates();
                    try
                    {
                        new TypeBuilder().BuildType(type);
                    }
                    catch (TypeBuilder.MissingTemplateException)
                    {
                        return false;
                    }
                }
            }

            // Returned type has to have a valid type handle value
            Debug.Assert(!type.RuntimeTypeHandle.IsNull());
            return !type.RuntimeTypeHandle.IsNull();
        }

        //
        // Returns the native layout info reader
        //
        internal static unsafe NativeReader GetNativeLayoutInfoReader(TypeManagerHandle moduleHandle)
        {
            Debug.Assert(!moduleHandle.IsNull);

            t_moduleNativeReaders ??= new LowLevelDictionary<TypeManagerHandle, NativeReader>();

            NativeReader result;
            if (t_moduleNativeReaders.TryGetValue(moduleHandle, out result))
                return result;

            byte* pBlob;
            uint cbBlob;
            if (RuntimeAugments.FindBlob(moduleHandle, (int)ReflectionMapBlob.NativeLayoutInfo, new IntPtr(&pBlob), new IntPtr(&cbBlob)))
                result = new NativeReader(pBlob, cbBlob);

            t_moduleNativeReaders.Add(moduleHandle, result);
            return result;
        }

        private static RuntimeTypeHandle[] GetTypeSequence(ref ExternalReferencesTable extRefs, ref NativeParser parser)
        {
            uint count = parser.GetUnsigned();
            RuntimeTypeHandle[] result = new RuntimeTypeHandle[count];
            for (uint i = 0; i < count; i++)
                result[i] = extRefs.GetRuntimeTypeHandleFromIndex(parser.GetUnsigned());

            return result;
        }

        public bool TryGetConstructedGenericTypeForComponents(RuntimeTypeHandle genericTypeDefinitionHandle, RuntimeTypeHandle[] genericTypeArgumentHandles, out RuntimeTypeHandle runtimeTypeHandle)
        {
            if (TryLookupConstructedGenericTypeForComponents(genericTypeDefinitionHandle, genericTypeArgumentHandles, out runtimeTypeHandle))
                return true;

            using (_typeLoaderLock.EnterScope())
            {
                return TypeBuilder.TryBuildGenericType(genericTypeDefinitionHandle, genericTypeArgumentHandles, out runtimeTypeHandle);
            }
        }

        public bool TryGetFunctionPointerTypeForComponents(RuntimeTypeHandle returnTypeHandle, RuntimeTypeHandle[] parameterHandles, bool isUnmanaged, out RuntimeTypeHandle runtimeTypeHandle)
        {
            if (TryLookupFunctionPointerTypeForComponents(returnTypeHandle, parameterHandles, isUnmanaged, out runtimeTypeHandle))
                return true;

            using (_typeLoaderLock.EnterScope())
            {
                return TypeBuilder.TryBuildFunctionPointerType(returnTypeHandle, parameterHandles, isUnmanaged, out runtimeTypeHandle);
            }
        }

        public bool TryLookupFunctionPointerTypeForComponents(RuntimeTypeHandle returnTypeHandle, RuntimeTypeHandle[] parameterHandles, bool isUnmanaged, out RuntimeTypeHandle runtimeTypeHandle)
        {
            var key = new TypeSystemContext.FunctionPointerTypeKey(returnTypeHandle, parameterHandles, isUnmanaged);
            if (TypeSystemContext.FunctionPointerTypesCache.TryGetValue(key, out runtimeTypeHandle))
                return true;

            if (!RuntimeAugments.IsDynamicType(returnTypeHandle)
                && AllNonDynamicTypes(parameterHandles)
                && TryGetStaticFunctionPointerTypeForComponents(returnTypeHandle, parameterHandles, isUnmanaged, out runtimeTypeHandle))
            {
                TypeSystemContext.FunctionPointerTypesCache.AddOrGetExisting(runtimeTypeHandle);
                return true;
            }

            return false;

            static bool AllNonDynamicTypes(RuntimeTypeHandle[] handles)
            {
                foreach (RuntimeTypeHandle h in handles)
                    if (RuntimeAugments.IsDynamicType(h))
                        return false;
                return true;
            }
        }

        // Get an array RuntimeTypeHandle given an element's RuntimeTypeHandle and rank. Pass false for isMdArray, and rank == -1 for SzArrays
        public bool TryGetArrayTypeForElementType(RuntimeTypeHandle elementTypeHandle, bool isMdArray, int rank, out RuntimeTypeHandle arrayTypeHandle)
        {
            if (TryGetArrayTypeForElementType_LookupOnly(elementTypeHandle, isMdArray, rank, out arrayTypeHandle))
            {
                return true;
            }

            using (_typeLoaderLock.EnterScope())
            {
                if (isMdArray && (rank < MDArray.MinRank) && (rank > MDArray.MaxRank))
                {
                    arrayTypeHandle = default(RuntimeTypeHandle);
                    return false;
                }

                if (TypeSystemContext.GetArrayTypesCache(isMdArray, rank).TryGetValue(elementTypeHandle, out arrayTypeHandle))
                    return true;

                return TypeBuilder.TryBuildArrayType(elementTypeHandle, isMdArray, rank, out arrayTypeHandle);
            }
        }

        // Looks up an array RuntimeTypeHandle given an element's RuntimeTypeHandle and rank. A rank of -1 indicates SzArray
        internal static bool TryGetArrayTypeForElementType_LookupOnly(RuntimeTypeHandle elementTypeHandle, bool isMdArray, int rank, out RuntimeTypeHandle arrayTypeHandle)
        {
            if (isMdArray && (rank < MDArray.MinRank) && (rank > MDArray.MaxRank))
            {
                arrayTypeHandle = default(RuntimeTypeHandle);
                return false;
            }

            if (TypeSystemContext.GetArrayTypesCache(isMdArray, rank).TryGetValue(elementTypeHandle, out arrayTypeHandle))
                return true;

            if (!RuntimeAugments.IsDynamicType(elementTypeHandle) &&
                TryGetArrayTypeForNonDynamicElementType(elementTypeHandle, isMdArray, rank, out arrayTypeHandle))
            {
                TypeSystemContext.GetArrayTypesCache(isMdArray, rank).AddOrGetExisting(arrayTypeHandle);
                return true;
            }

            return false;
        }

        public bool TryGetPointerTypeForTargetType(RuntimeTypeHandle pointeeTypeHandle, out RuntimeTypeHandle pointerTypeHandle)
        {
            if (TryGetPointerTypeForTargetType_LookupOnly(pointeeTypeHandle, out pointerTypeHandle))
                return true;

            using (_typeLoaderLock.EnterScope())
            {
                if (TypeSystemContext.PointerTypesCache.TryGetValue(pointeeTypeHandle, out pointerTypeHandle))
                    return true;

                return TypeBuilder.TryBuildPointerType(pointeeTypeHandle, out pointerTypeHandle);
            }
        }

        public static bool TryGetPointerTypeForTargetType_LookupOnly(RuntimeTypeHandle pointeeTypeHandle, out RuntimeTypeHandle pointerTypeHandle)
        {
            if (TypeSystemContext.PointerTypesCache.TryGetValue(pointeeTypeHandle, out pointerTypeHandle))
                return true;

            if (!RuntimeAugments.IsDynamicType(pointeeTypeHandle) &&
                TryGetPointerTypeForNonDynamicElementType(pointeeTypeHandle, out pointerTypeHandle))
            {
                TypeSystemContext.PointerTypesCache.AddOrGetExisting(pointerTypeHandle);
                return true;
            }

            return false;
        }

        public bool TryGetByRefTypeForTargetType(RuntimeTypeHandle pointeeTypeHandle, out RuntimeTypeHandle byRefTypeHandle)
        {
            if (TryGetByRefTypeForTargetType_LookupOnly(pointeeTypeHandle, out byRefTypeHandle))
                return true;

            using (_typeLoaderLock.EnterScope())
            {
                if (TypeSystemContext.ByRefTypesCache.TryGetValue(pointeeTypeHandle, out byRefTypeHandle))
                    return true;

                return TypeBuilder.TryBuildByRefType(pointeeTypeHandle, out byRefTypeHandle);
            }
        }

        public static bool TryGetByRefTypeForTargetType_LookupOnly(RuntimeTypeHandle pointeeTypeHandle, out RuntimeTypeHandle pointerTypeHandle)
        {
            if (TypeSystemContext.ByRefTypesCache.TryGetValue(pointeeTypeHandle, out pointerTypeHandle))
                return true;

            if (!RuntimeAugments.IsDynamicType(pointeeTypeHandle) &&
                TryGetByRefTypeForNonDynamicElementType(pointeeTypeHandle, out pointerTypeHandle))
            {
                TypeSystemContext.ByRefTypesCache.AddOrGetExisting(pointerTypeHandle);
                return true;
            }

            return false;
        }

        public int GetCanonicalHashCode(RuntimeTypeHandle typeHandle, CanonicalFormKind kind)
        {
            TypeSystemContext context = TypeSystemContextFactory.Create();
            TypeDesc type = context.ResolveRuntimeTypeHandle(typeHandle);
            int hashCode = type.ConvertToCanonForm(kind).GetHashCode();
            TypeSystemContextFactory.Recycle(context);

            return hashCode;
        }

        public bool TryGetGenericMethodDictionaryForComponents(RuntimeTypeHandle declaringTypeHandle, RuntimeTypeHandle[] genericMethodArgHandles, MethodNameAndSignature nameAndSignature, out IntPtr methodDictionary)
        {
            TypeSystemContext context = TypeSystemContextFactory.Create();

            DefType declaringType = (DefType)context.ResolveRuntimeTypeHandle(declaringTypeHandle);
            InstantiatedMethod methodBeingLoaded = (InstantiatedMethod)context.ResolveGenericMethodInstantiation(false, declaringType, nameAndSignature, context.ResolveRuntimeTypeHandles(genericMethodArgHandles));

            if (TryLookupGenericMethodDictionary(new MethodDescBasedGenericMethodLookup(methodBeingLoaded), out methodDictionary))
            {
                TypeSystemContextFactory.Recycle(context);
                return true;
            }

            using (_typeLoaderLock.EnterScope())
            {
                bool success = TypeBuilder.TryBuildGenericMethod(methodBeingLoaded, out methodDictionary);

                // Recycle the context only if we successfully built the method. The state may be partially initialized otherwise.
                if (success)
                    TypeSystemContextFactory.Recycle(context);

                return success;
            }
        }

        public bool CanInstantiationsShareCode(RuntimeTypeHandle[] genericArgHandles1, RuntimeTypeHandle[] genericArgHandles2, CanonicalFormKind kind)
        {
            if (genericArgHandles1.Length != genericArgHandles2.Length)
                return false;

            bool match = true;

            TypeSystemContext context = TypeSystemContextFactory.Create();

            for (int i = 0; i < genericArgHandles1.Length; i++)
            {
                TypeDesc genericArg1 = context.ResolveRuntimeTypeHandle(genericArgHandles1[i]);
                TypeDesc genericArg2 = context.ResolveRuntimeTypeHandle(genericArgHandles2[i]);

                if (context.ConvertToCanon(genericArg1, kind) != context.ConvertToCanon(genericArg2, kind))
                {
                    match = false;
                    break;
                }
            }

            TypeSystemContextFactory.Recycle(context);

            return match;
        }

        // get the generics hash table and external references table for a module
        // TODO multi-file: consider whether we want to cache this info
        private static unsafe bool GetHashtableFromBlob(NativeFormatModuleInfo module, ReflectionMapBlob blobId, out NativeHashtable hashtable, out ExternalReferencesTable externalReferencesLookup)
        {
            byte* pBlob;
            uint cbBlob;

            hashtable = default(NativeHashtable);
            externalReferencesLookup = default(ExternalReferencesTable);

            if (!module.TryFindBlob(blobId, out pBlob, out cbBlob))
                return false;

            NativeReader reader = new NativeReader(pBlob, cbBlob);
            NativeParser parser = new NativeParser(reader, 0);

            hashtable = new NativeHashtable(parser);

            return externalReferencesLookup.InitializeNativeReferences(module);
        }

        public static unsafe bool TryGetTargetOfUnboxingAndInstantiatingStub(IntPtr maybeInstantiatingAndUnboxingStub, out IntPtr targetMethod)
        {
            targetMethod = RuntimeAugments.GetTargetOfUnboxingAndInstantiatingStub(maybeInstantiatingAndUnboxingStub);
            return (targetMethod != IntPtr.Zero);
        }

        public static IntPtr ConvertUnboxingFunctionPointerToUnderlyingNonUnboxingPointer(IntPtr unboxingFunctionPointer, RuntimeTypeHandle declaringType)
        {
            if (FunctionPointerOps.IsGenericMethodPointer(unboxingFunctionPointer))
            {
                // Handle shared generic methods
                unsafe
                {
                    GenericMethodDescriptor* functionPointerDescriptor = FunctionPointerOps.ConvertToGenericDescriptor(unboxingFunctionPointer);
                    IntPtr nonUnboxingTarget = RuntimeAugments.GetCodeTarget(functionPointerDescriptor->MethodFunctionPointer);
                    Debug.Assert(nonUnboxingTarget != functionPointerDescriptor->MethodFunctionPointer);
                    Debug.Assert(nonUnboxingTarget == RuntimeAugments.GetCodeTarget(nonUnboxingTarget));
                    return FunctionPointerOps.GetGenericMethodFunctionPointer(nonUnboxingTarget, functionPointerDescriptor->InstantiationArgument);
                }
            }

            // GetCodeTarget will look through simple unboxing stubs (ones that consist of adjusting the this pointer and then
            // jumping to the target.
            IntPtr exactTarget = RuntimeAugments.GetCodeTarget(unboxingFunctionPointer);
            if (RuntimeAugments.IsGenericType(declaringType))
            {
                IntPtr fatFunctionPointerTarget;

                // This check looks for unboxing and instantiating stubs generated via the compiler backend
                if (TypeLoaderEnvironment.TryGetTargetOfUnboxingAndInstantiatingStub(exactTarget, out fatFunctionPointerTarget))
                {
                    // If this is an unboxing and instantiating stub, use separate table, find target, and create fat function pointer
                    exactTarget = FunctionPointerOps.GetGenericMethodFunctionPointer(fatFunctionPointerTarget,
                                                                                        declaringType.ToIntPtr());
                }
            }

            return exactTarget;
        }
    }
}
