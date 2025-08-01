// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Internal.TypeSystem;
using Internal.ReadyToRunConstants;

using ILCompiler;
using ILCompiler.DependencyAnalysis;

using Debug = System.Diagnostics.Debug;
using DependencyList = ILCompiler.DependencyAnalysisFramework.DependencyNodeCore<ILCompiler.DependencyAnalysis.NodeFactory>.DependencyList;
using CombinedDependencyList = System.Collections.Generic.List<ILCompiler.DependencyAnalysisFramework.DependencyNodeCore<ILCompiler.DependencyAnalysis.NodeFactory>.CombinedDependencyListEntry>;
using DependencyListEntry = ILCompiler.DependencyAnalysisFramework.DependencyNodeCore<ILCompiler.DependencyAnalysis.NodeFactory>.DependencyListEntry;

#pragma warning disable IDE0060

namespace Internal.IL
{
    // Implements an IL scanner that scans method bodies to be compiled by the code generation
    // backend before the actual compilation happens to gain insights into the code.
    internal partial class ILImporter
    {
        private readonly MethodIL _methodIL;
        private readonly MethodIL _canonMethodIL;
        private readonly ILScanner _compilation;
        private readonly ILScanNodeFactory _factory;

        // True if we're scanning a throwing method body because scanning the real body failed.
        private readonly bool _isFallbackBodyCompilation;

        private readonly MethodDesc _canonMethod;

        private DependencyList _unconditionalDependencies = new DependencyList();

        private readonly byte[] _ilBytes;

        private TypeEqualityPatternAnalyzer _typeEqualityPatternAnalyzer;
        private IsInstCheckPatternAnalyzer _isInstCheckPatternAnalyzer;

        private sealed class BasicBlock
        {
            // Common fields
            public enum ImportState : byte
            {
                Unmarked,
                IsPending
            }

            public BasicBlock Next;

            public int StartOffset;
            public ImportState State = ImportState.Unmarked;

            public bool TryStart;
            public bool FilterStart;
            public bool HandlerStart;

            public object Condition;
            public DependencyList Dependencies;
        }

        private bool _isReadOnly;
        private TypeDesc _constrained;

        private DependencyList _dependencies;
        private BasicBlock _lateBasicBlocks;

        private sealed class ExceptionRegion
        {
            public ILExceptionRegion ILRegion;
        }
        private ExceptionRegion[] _exceptionRegions;

        public ILImporter(ILScanner compilation, MethodDesc method, MethodIL methodIL = null)
        {
            if (methodIL == null)
            {
                methodIL = compilation.GetMethodIL(method);
            }
            else
            {
                _isFallbackBodyCompilation = true;
            }

            // This is e.g. an "extern" method in C# without a DllImport or InternalCall.
            if (methodIL == null)
            {
                ThrowHelper.ThrowInvalidProgramException(ExceptionStringID.InvalidProgramSpecific, method);
            }

            _compilation = compilation;
            _factory = (ILScanNodeFactory)compilation.NodeFactory;

            _ilBytes = methodIL.GetILBytes();

            _canonMethodIL = methodIL;

            // Get the runtime determined method IL so that this works right in shared code
            // and tokens in shared code resolve to runtime determined types.
            MethodIL uninstantiatiedMethodIL = methodIL.GetMethodILDefinition();
            if (methodIL != uninstantiatiedMethodIL)
            {
                MethodDesc sharedMethod = method.GetSharedRuntimeFormMethodTarget();
                _methodIL = new InstantiatedMethodIL(sharedMethod, uninstantiatiedMethodIL);
            }
            else
            {
                _methodIL = methodIL;
            }

            _canonMethod = method;

            var ilExceptionRegions = methodIL.GetExceptionRegions();
            _exceptionRegions = new ExceptionRegion[ilExceptionRegions.Length];
            for (int i = 0; i < ilExceptionRegions.Length; i++)
            {
                _exceptionRegions[i] = new ExceptionRegion() { ILRegion = ilExceptionRegions[i] };
            }

            _dependencies = _unconditionalDependencies;
        }

        public (DependencyList, CombinedDependencyList) Import()
        {
            TypeDesc owningType = _canonMethod.OwningType;
            if (_compilation.HasLazyStaticConstructor(owningType))
            {
                // Don't trigger cctor if this is a fallback compilation (bad cctor could have been the reason for fallback).
                // Otherwise follow the rules from ECMA-335 I.8.9.5.
                if (!_isFallbackBodyCompilation &&
                    (_canonMethod.Signature.IsStatic || _canonMethod.IsConstructor || owningType.IsValueType || owningType.IsInterface))
                {
                    // For beforefieldinit, we can wait for field access.
                    if (!((MetadataType)owningType).IsBeforeFieldInit)
                    {
                        MethodDesc method = _methodIL.OwningMethod;
                        if (method.OwningType.IsRuntimeDeterminedSubtype)
                        {
                            _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.GetNonGCStaticBase, method.OwningType), "Owning type cctor");
                        }
                        else
                        {
                            _dependencies.Add(_factory.ReadyToRunHelper(ReadyToRunHelperId.GetNonGCStaticBase, method.OwningType), "Owning type cctor");
                        }
                    }
                }
            }

            if (_canonMethod.IsSynchronized)
            {
                const string reason = "Synchronized method";
                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.MonitorEnter), reason);
                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.MonitorExit), reason);
                if (_canonMethod.Signature.IsStatic)
                {
                    _dependencies.Add(_compilation.NodeFactory.MethodEntrypoint(_compilation.NodeFactory.TypeSystemContext.GetHelperEntryPoint("SynchronizedMethodHelpers", "GetSyncFromClassHandle")), reason);

                    MethodDesc method = _methodIL.OwningMethod;
                    if (method.OwningType.IsRuntimeDeterminedSubtype)
                    {
                        _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.NecessaryTypeHandle, method.OwningType), reason);
                    }
                    else
                    {
                        _dependencies.Add(_factory.NecessaryTypeSymbol(method.OwningType), reason);
                    }

                    if (_canonMethod.IsCanonicalMethod(CanonicalFormKind.Any))
                    {
                        if (_canonMethod.RequiresInstMethodDescArg())
                            _dependencies.Add(_compilation.NodeFactory.MethodEntrypoint(_compilation.NodeFactory.TypeSystemContext.GetHelperEntryPoint("SynchronizedMethodHelpers", "GetClassFromMethodParam")), reason);
                    }
                }
            }

            FindBasicBlocks();
            ImportBasicBlocks();

            CombinedDependencyList conditionalDependencies = null;
            foreach (BasicBlock bb in _basicBlocks)
            {
                if (bb?.Condition == null)
                    continue;

                conditionalDependencies ??= new CombinedDependencyList();
                foreach (DependencyListEntry dep in bb.Dependencies)
                    conditionalDependencies.Add(new(dep.Node, bb.Condition, dep.Reason));
            }

            CodeBasedDependencyAlgorithm.AddDependenciesDueToMethodCodePresence(ref _unconditionalDependencies, _factory, _canonMethod, _canonMethodIL);
            CodeBasedDependencyAlgorithm.AddConditionalDependenciesDueToMethodCodePresence(ref conditionalDependencies, _factory, _canonMethod);

            return (_unconditionalDependencies, conditionalDependencies);
        }

        private ISymbolNode GetGenericLookupHelper(ReadyToRunHelperId helperId, object helperArgument)
        {
            GenericDictionaryLookup lookup = _compilation.ComputeGenericLookup(_canonMethod, helperId, helperArgument);
            Debug.Assert(lookup.UseHelper);

            if (_canonMethod.RequiresInstMethodDescArg())
            {
                return _compilation.NodeFactory.ReadyToRunHelperFromDictionaryLookup(lookup.HelperId, lookup.HelperObject, _canonMethod);
            }
            else
            {
                Debug.Assert(_canonMethod.RequiresInstArg() || _canonMethod.AcquiresInstMethodTableFromThis());
                return _compilation.NodeFactory.ReadyToRunHelperFromTypeLookup(lookup.HelperId, lookup.HelperObject, _canonMethod.OwningType);
            }
        }

        private ISymbolNode GetHelperEntrypoint(ReadyToRunHelper helper)
        {
            return _compilation.GetHelperEntrypoint(helper);
        }

        private static void MarkInstructionBoundary() { }

        private void EndImportingBasicBlock(BasicBlock basicBlock)
        {
            if (_pendingBasicBlocks == null)
            {
                _pendingBasicBlocks = _lateBasicBlocks;
                _lateBasicBlocks = null;
            }
        }

        private void StartImportingBasicBlock(BasicBlock basicBlock)
        {
            _dependencies = basicBlock.Condition != null ? basicBlock.Dependencies : _unconditionalDependencies;

            // Import all associated EH regions
            foreach (ExceptionRegion ehRegion in _exceptionRegions)
            {
                ILExceptionRegion region = ehRegion.ILRegion;
                if (region.TryOffset == basicBlock.StartOffset)
                {
                    ImportBasicBlockEdge(basicBlock, _basicBlocks[region.HandlerOffset]);
                    if (region.Kind == ILExceptionRegionKind.Filter)
                        ImportBasicBlockEdge(basicBlock, _basicBlocks[region.FilterOffset]);

                    if (region.Kind == ILExceptionRegionKind.Catch)
                    {
                        TypeDesc catchType = (TypeDesc)_methodIL.GetObject(region.ClassToken);

                        // EH tables refer to this type
                        if (catchType.IsRuntimeDeterminedSubtype)
                        {
                            _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.TypeHandleForCasting, catchType), "EH");
                        }
                        else
                        {
                            _dependencies.Add(_compilation.ComputeConstantLookup(ReadyToRunHelperId.TypeHandleForCasting, catchType), "EH");
                        }
                    }
                }
            }

            _typeEqualityPatternAnalyzer = default;
            _isInstCheckPatternAnalyzer = default;
        }

        partial void StartImportingInstruction(ILOpcode opcode)
        {
            _typeEqualityPatternAnalyzer.Advance(opcode, new ILReader(_ilBytes, _currentOffset), _methodIL);
            _isInstCheckPatternAnalyzer.Advance(opcode, new ILReader(_ilBytes, _currentOffset), _methodIL);
        }

        private void EndImportingInstruction()
        {
            // The instruction should have consumed any prefixes.
            _constrained = null;
            _isReadOnly = false;
        }

        private void ImportCasting(ILOpcode opcode, int token)
        {
            TypeDesc type = (TypeDesc)_methodIL.GetObject(token);

            if (type.IsRuntimeDeterminedSubtype)
            {
                _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.TypeHandleForCasting, type), "IsInst/CastClass");
            }
            else
            {
                _dependencies.Add(_compilation.ComputeConstantLookup(ReadyToRunHelperId.TypeHandleForCasting, type), "IsInst/CastClass");
            }
        }

        private IMethodNode GetMethodEntrypoint(MethodDesc method)
        {
            if (method.HasInstantiation || method.OwningType.HasInstantiation)
            {
                _compilation.DetectGenericCycles(_canonMethod, method);
            }

            return _factory.MethodEntrypointOrTentativeMethod(method);
        }

        private void ImportCall(ILOpcode opcode, int token)
        {
            // We get both the canonical and runtime determined form - JitInterface mostly operates
            // on the canonical form.
            var runtimeDeterminedMethod = (MethodDesc)_methodIL.GetObject(token);
            var method = (MethodDesc)_canonMethodIL.GetObject(token);

            _compilation.TypeSystemContext.EnsureLoadableMethod(method);
            if ((method.Signature.Flags & MethodSignatureFlags.UnmanagedCallingConventionMask) == MethodSignatureFlags.CallingConventionVarargs)
                ThrowHelper.ThrowBadImageFormatException();

            _compilation.NodeFactory.MetadataManager.GetDependenciesDueToAccess(ref _dependencies, _compilation.NodeFactory, _canonMethodIL, method);

            if (method.IsRawPInvoke())
            {
                // Raw P/invokes don't have any dependencies.
                return;
            }

            string reason = null;
            switch (opcode)
            {
                case ILOpcode.newobj:
                    reason = "newobj"; break;
                case ILOpcode.call:
                    reason = "call"; break;
                case ILOpcode.callvirt:
                    reason = "callvirt"; break;
                case ILOpcode.ldftn:
                    reason = "ldftn"; break;
                case ILOpcode.ldvirtftn:
                    reason = "ldvirtftn"; break;
                default:
                    Debug.Assert(false); break;
            }

            if (opcode == ILOpcode.newobj)
            {
                TypeDesc owningType = runtimeDeterminedMethod.OwningType;
                if (owningType.IsString)
                {
                    // String .ctor handled specially below
                }
                else if (owningType.IsGCPointer)
                {
                    if (owningType.IsRuntimeDeterminedSubtype)
                    {
                        _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.TypeHandle, owningType), reason);
                    }
                    else
                    {
                        _dependencies.Add(_factory.ConstructedTypeSymbol(owningType), reason);
                    }

                    if (owningType.IsArray)
                    {
                        // RyuJIT is going to call the "MdArray" creation helper even if this is an SzArray,
                        // hence the IsArray check above. Note that the MdArray helper can handle SzArrays.
                        if (((ArrayType)owningType).Rank == 1)
                            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.NewMultiDimArrRare), reason);
                        else
                            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.NewMultiDimArr), reason);
                        return;
                    }
                    else
                    {
                        _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.NewObject), reason);
                    }
                }
            }

            if (method.OwningType.IsDelegate && method.Name == "Invoke" &&
                opcode != ILOpcode.ldftn && opcode != ILOpcode.ldvirtftn)
            {
                // This call is expanded as an intrinsic; it's not an actual function call.
                // Before codegen realizes this is an intrinsic, it might still ask questions about
                // the vtable of this virtual method, so let's make sure it's marked in the scanner's
                // dependency graph.
                _dependencies.Add(_factory.VTable(method.OwningType), reason);
                return;
            }

            if (method.IsIntrinsic)
            {
                if (IsActivatorDefaultConstructorOf(method))
                {
                    if (runtimeDeterminedMethod.IsRuntimeDeterminedExactMethod)
                    {
                        _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.DefaultConstructor, runtimeDeterminedMethod.Instantiation[0]), reason);
                    }
                    else
                    {
                        TypeDesc type = method.Instantiation[0];
                        MethodDesc ctor = Compilation.GetConstructorForCreateInstanceIntrinsic(type);
                        _dependencies.Add(type.IsValueType ? _factory.ExactCallableAddress(ctor) : _factory.CanonicalEntrypoint(ctor), reason);
                    }

                    return;
                }

                if (IsActivatorAllocatorOf(method))
                {
                    if (runtimeDeterminedMethod.IsRuntimeDeterminedExactMethod)
                    {
                        _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.ObjectAllocator, runtimeDeterminedMethod.Instantiation[0]), reason);
                    }
                    else
                    {
                        _dependencies.Add(_compilation.ComputeConstantLookup(ReadyToRunHelperId.ObjectAllocator, method.Instantiation[0]), reason);
                    }

                    return;
                }

                if (IsEETypePtrOf(method))
                {
                    if (runtimeDeterminedMethod.IsRuntimeDeterminedExactMethod)
                    {
                        _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.TypeHandle, runtimeDeterminedMethod.Instantiation[0]), reason);
                    }
                    else
                    {
                        _dependencies.Add(_factory.ConstructedTypeSymbol(method.Instantiation[0]), reason);
                    }
                    return;
                }

                if (opcode != ILOpcode.ldftn)
                {
                    if (IsRuntimeHelpersIsReferenceOrContainsReferences(method))
                    {
                        return;
                    }

                    if (IsMemoryMarshalGetArrayDataReference(method))
                    {
                        return;
                    }
                }
            }

            TypeDesc exactType = method.OwningType;

            bool resolvedConstraint = false;
            bool forceUseRuntimeLookup = false;
            DefaultInterfaceMethodResolution staticResolution = default;

            MethodDesc methodAfterConstraintResolution = method;
            if (_constrained != null)
            {
                // We have a "constrained." call.  Try a partial resolve of the constraint call.  Note that this
                // will not necessarily resolve the call exactly, since we might be compiling
                // shared generic code - it may just resolve it to a candidate suitable for
                // JIT compilation, and require a runtime lookup for the actual code pointer
                // to call.

                TypeDesc constrained = _constrained;
                if (constrained.IsRuntimeDeterminedSubtype)
                    constrained = constrained.ConvertToCanonForm(CanonicalFormKind.Specific);

                MethodDesc directMethod = constrained.GetClosestDefType().TryResolveConstraintMethodApprox(method.OwningType, method, out forceUseRuntimeLookup, ref staticResolution);
                if (directMethod == null && constrained.IsEnum)
                {
                    // Constrained calls to methods on enum methods resolve to System.Enum's methods. System.Enum is a reference
                    // type though, so we would fail to resolve and box. We have a special path for those to avoid boxing.
                    directMethod = _compilation.TypeSystemContext.TryResolveConstrainedEnumMethod(constrained, method);
                }

                if (directMethod != null)
                {
                    // Either
                    //    1. no constraint resolution at compile time (!directMethod)
                    // OR 2. no code sharing lookup in call
                    // OR 3. we have resolved to an instantiating stub

                    methodAfterConstraintResolution = directMethod;

                    Debug.Assert(!methodAfterConstraintResolution.OwningType.IsInterface
                        || methodAfterConstraintResolution.Signature.IsStatic);
                    resolvedConstraint = true;

                    exactType = directMethod.OwningType;

                    _factory.MetadataManager.NoteOverridingMethod(method, directMethod);
                }
                else if (method.Signature.IsStatic)
                {
                    Debug.Assert(method.OwningType.IsInterface);
                    exactType = constrained;
                }
                else if (constrained.IsValueType)
                {
                    // We'll need to box `this`. Note we use _constrained here, because the other one is canonical.
                    AddBoxingDependencies(_constrained, reason);
                }
            }

            MethodDesc targetMethod = methodAfterConstraintResolution;

            bool exactContextNeedsRuntimeLookup;
            if (targetMethod.HasInstantiation)
            {
                exactContextNeedsRuntimeLookup = targetMethod.IsSharedByGenericInstantiations;
            }
            else
            {
                exactContextNeedsRuntimeLookup = exactType.IsCanonicalSubtype(CanonicalFormKind.Any);
            }

            //
            // Determine whether to perform direct call
            //

            bool directCall = false;

            if (targetMethod.Signature.IsStatic)
            {
                if (_constrained != null && (!resolvedConstraint || forceUseRuntimeLookup))
                {
                    // Constrained call to static virtual interface method we didn't resolve statically
                    Debug.Assert(targetMethod.IsVirtual && targetMethod.OwningType.IsInterface);
                }
                else
                {
                    // Static methods are always direct calls
                    directCall = true;
                }
            }
            else if ((opcode != ILOpcode.callvirt && opcode != ILOpcode.ldvirtftn) || resolvedConstraint)
            {
                directCall = true;
            }
            else
            {
                if (!targetMethod.IsVirtual ||
                    // Final/sealed has no meaning for interfaces, but lets us devirtualize otherwise
                    (!targetMethod.OwningType.IsInterface && (targetMethod.IsFinal || targetMethod.OwningType.IsSealed())))
                {
                    directCall = true;
                }
            }

            if (directCall && targetMethod.IsAbstract)
            {
                ThrowHelper.ThrowBadImageFormatException();
            }

            MethodDesc targetForDelegate = !resolvedConstraint || forceUseRuntimeLookup ? runtimeDeterminedMethod : targetMethod;
            TypeDesc constraintForDelegate = !resolvedConstraint || forceUseRuntimeLookup ? _constrained : null;
            int numDependenciesBeforeTargetDetermination = _dependencies.Count;

            bool allowInstParam = opcode != ILOpcode.ldvirtftn && opcode != ILOpcode.ldftn;

            if (directCall && resolvedConstraint && exactContextNeedsRuntimeLookup)
            {
                // We want to do a direct call to a shared method on a valuetype. We need to provide
                // a generic context, but the JitInterface doesn't provide a way for us to do it from here.
                // So we do the next best thing and ask RyuJIT to look up a fat pointer.
                //
                // We have the canonical version of the method - find the runtime determined version.
                // This is simplified because we know the method is on a valuetype.
                Debug.Assert(targetMethod.OwningType.IsValueType);

                if (forceUseRuntimeLookup)
                {
                    // The below logic would incorrectly resolve the lookup into the first match we found,
                    // but there was a compile-time ambiguity due to shared code. The correct fix should
                    // use the ConstrainedMethodUseLookupResult dictionary entry so that the exact
                    // dispatch can be computed with the help of the generic dictionary.
                    // We fail the compilation here to avoid bad codegen. This is not actually an invalid program.
                    // https://github.com/dotnet/runtimelab/issues/1431
                    ThrowHelper.ThrowInvalidProgramException();
                }

                MethodDesc targetOfLookup;
                if (_constrained.IsRuntimeDeterminedType)
                    targetOfLookup = _compilation.TypeSystemContext.GetMethodForRuntimeDeterminedType(targetMethod.GetTypicalMethodDefinition(), (RuntimeDeterminedType)_constrained);
                else if (_constrained.HasInstantiation)
                    targetOfLookup = _compilation.TypeSystemContext.GetMethodForInstantiatedType(targetMethod.GetTypicalMethodDefinition(), (InstantiatedType)_constrained);
                else
                    targetOfLookup = targetMethod.GetMethodDefinition();
                if (targetOfLookup.HasInstantiation)
                {
                    targetOfLookup = targetOfLookup.MakeInstantiatedMethod(runtimeDeterminedMethod.Instantiation);
                }
                Debug.Assert(targetOfLookup.GetCanonMethodTarget(CanonicalFormKind.Specific) == targetMethod.GetCanonMethodTarget(CanonicalFormKind.Specific));
                _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.MethodEntry, targetOfLookup), reason);

                targetForDelegate = targetOfLookup;
            }
            else if (directCall && !allowInstParam && targetMethod.GetCanonMethodTarget(CanonicalFormKind.Specific).RequiresInstArg())
            {
                // Needs a single address to call this method but the method needs a hidden argument.
                // We need a fat function pointer for this that captures both things.

                if (exactContextNeedsRuntimeLookup)
                {
                    _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.MethodEntry, runtimeDeterminedMethod), reason);
                }
                else
                {
                    _dependencies.Add(_factory.FatFunctionPointer(targetMethod), reason);
                }
            }
            else if (directCall)
            {
                bool referencingArrayAddressMethod = false;

                if (targetMethod.IsIntrinsic)
                {
                    // If this is an intrinsic method with a callsite-specific expansion, this will replace
                    // the method with a method the intrinsic expands into. If it's not the special intrinsic,
                    // method stays unchanged.
                    targetMethod = _compilation.ExpandIntrinsicForCallsite(targetMethod, _canonMethod);

                    // Array address method requires special dependency tracking.
                    referencingArrayAddressMethod = targetMethod.IsArrayAddressMethod();
                }

                MethodDesc concreteMethod = targetMethod;
                targetMethod = targetMethod.GetCanonMethodTarget(CanonicalFormKind.Specific);

                if (targetMethod.IsConstructor && targetMethod.OwningType.IsString)
                {
                    _dependencies.Add(_factory.StringAllocator(targetMethod), reason);
                }
                else if (exactContextNeedsRuntimeLookup)
                {
                    if (targetMethod.IsSharedByGenericInstantiations && !resolvedConstraint && !referencingArrayAddressMethod)
                    {
                        ISymbolNode instParam = null;

                        if (targetMethod.RequiresInstMethodDescArg())
                        {
                            instParam = GetGenericLookupHelper(ReadyToRunHelperId.MethodDictionary, runtimeDeterminedMethod);
                        }
                        else if (targetMethod.RequiresInstMethodTableArg())
                        {
                            bool hasHiddenParameter = true;

                            if (targetMethod.IsIntrinsic)
                            {
                                if (_factory.TypeSystemContext.IsSpecialUnboxingThunkTargetMethod(targetMethod))
                                    hasHiddenParameter = false;
                            }

                            if (hasHiddenParameter)
                                instParam = GetGenericLookupHelper(ReadyToRunHelperId.TypeHandle, runtimeDeterminedMethod.OwningType);
                        }

                        if (instParam != null)
                        {
                            _dependencies.Add(instParam, reason);
                        }

                        _dependencies.Add(_factory.CanonicalEntrypoint(targetMethod), reason);
                    }
                    else
                    {
                        Debug.Assert(!forceUseRuntimeLookup);
                        _dependencies.Add(GetMethodEntrypoint(targetMethod), reason);

                        if (targetMethod.RequiresInstMethodTableArg() && resolvedConstraint)
                        {
                            if (_constrained.IsRuntimeDeterminedSubtype)
                                _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.TypeHandle, _constrained), reason);
                            else
                                _dependencies.Add(_factory.ConstructedTypeSymbol(_constrained), reason);
                        }

                        if (referencingArrayAddressMethod && !_isReadOnly)
                        {
                            // Address method is special - it expects an instantiation argument, unless a readonly prefix was applied.
                            _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.TypeHandle, runtimeDeterminedMethod.OwningType), reason);
                        }
                    }
                }
                else
                {
                    ISymbolNode instParam = null;

                    if (targetMethod.RequiresInstMethodDescArg())
                    {
                        instParam = _compilation.NodeFactory.MethodGenericDictionary(concreteMethod);
                    }
                    else if (targetMethod.RequiresInstMethodTableArg() || (referencingArrayAddressMethod && !_isReadOnly))
                    {
                        // Ask for a constructed type symbol because we need the vtable to get to the dictionary
                        instParam = _compilation.NodeFactory.ConstructedTypeSymbol(concreteMethod.OwningType);
                    }

                    if (instParam != null)
                    {
                        _dependencies.Add(instParam, reason);
                    }

                    _dependencies.Add(GetMethodEntrypoint(targetMethod), reason);
                }
            }
            else if (staticResolution is DefaultInterfaceMethodResolution.Diamond or DefaultInterfaceMethodResolution.Reabstraction)
            {
                Debug.Assert(targetMethod.OwningType.IsInterface && targetMethod.IsVirtual && _constrained != null);

                ThrowHelper.ThrowBadImageFormatException();
            }
            else if (method.Signature.IsStatic)
            {
                // This should be an unresolved static virtual interface method call. Other static methods should
                // have been handled as a directCall above.
                Debug.Assert(targetMethod.OwningType.IsInterface && targetMethod.IsVirtual && _constrained != null);

                var constrainedCallInfo = new ConstrainedCallInfo(_constrained, runtimeDeterminedMethod);
                var constrainedHelperId = ReadyToRunHelperId.ConstrainedDirectCall;

                // Constant lookup doesn't make sense and we don't implement it. If we need constant lookup,
                // the method wasn't implemented. Don't crash on it.
                if (!_compilation.NeedsRuntimeLookup(constrainedHelperId, constrainedCallInfo))
                    ThrowHelper.ThrowTypeLoadException(_constrained);

                _dependencies.Add(GetGenericLookupHelper(constrainedHelperId, constrainedCallInfo), reason);
            }
            else if (method.HasInstantiation)
            {
                // Generic virtual method call

                MethodDesc methodToLookup = _compilation.GetTargetOfGenericVirtualMethodCall(runtimeDeterminedMethod);

                _compilation.DetectGenericCycles(
                        _canonMethod,
                        methodToLookup.GetCanonMethodTarget(CanonicalFormKind.Specific));

                if (exactContextNeedsRuntimeLookup)
                {
                    _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.MethodHandle, methodToLookup), reason);
                }
                else
                {
                    _dependencies.Add(_factory.RuntimeMethodHandle(methodToLookup), reason);
                }

                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.GVMLookupForSlot), reason);
            }
            else if (method.OwningType.IsInterface)
            {
                if (exactContextNeedsRuntimeLookup)
                {
                    _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.VirtualDispatchCell, runtimeDeterminedMethod), reason);
                }
                else
                {
                    _dependencies.Add(_factory.InterfaceDispatchCell(method), reason);
                }
            }
            else if (_compilation.NeedsSlotUseTracking(method.OwningType))
            {
                MethodDesc slotDefiningMethod = targetMethod.IsNewSlot ?
                        targetMethod : MetadataVirtualMethodAlgorithm.FindSlotDefiningMethodForVirtualMethod(targetMethod);
                _dependencies.Add(_factory.VirtualMethodUse(slotDefiningMethod), reason);
            }

            // Is this a verifiable delegate creation sequence (load function pointer followed by newobj)?
            if ((opcode == ILOpcode.ldftn || opcode == ILOpcode.ldvirtftn)
                && _currentOffset + 5 < _ilBytes.Length
                && _basicBlocks[_currentOffset] == null
                && _ilBytes[_currentOffset] == (byte)ILOpcode.newobj)
            {
                // TODO: for ldvirtftn we need to also check for the `dup` instruction
                int ctorToken = ReadILTokenAt(_currentOffset + 1);
                var ctorMethod = (MethodDesc)_methodIL.GetObject(ctorToken);
                if (ctorMethod.OwningType.IsDelegate)
                {
                    // Yep, verifiable delegate creation

                    // Drop any dependencies we inserted so far - the delegate construction helper is the only dependency
                    while (_dependencies.Count > numDependenciesBeforeTargetDetermination)
                        _dependencies.RemoveAt(_dependencies.Count - 1);

                    TypeDesc canonDelegateType = ctorMethod.OwningType.ConvertToCanonForm(CanonicalFormKind.Specific);
                    DelegateCreationInfo info = _compilation.GetDelegateCtor(canonDelegateType, targetForDelegate, constraintForDelegate, opcode == ILOpcode.ldvirtftn);

                    if (info.NeedsRuntimeLookup)
                        _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.DelegateCtor, info), reason);
                    else
                        _dependencies.Add(_factory.ReadyToRunHelper(ReadyToRunHelperId.DelegateCtor, info), reason);
                }
            }
        }

        private void ImportLdFtn(int token, ILOpcode opCode)
        {
            ImportCall(opCode, token);
        }

        private void ImportJmp(int token)
        {
            // JMP is kind of like a tail call (with no arguments pushed on the stack).
            ImportCall(ILOpcode.call, token);
        }

        private void ImportCalli(int token)
        {
            MethodSignature signature = (MethodSignature)_methodIL.GetObject(token);

            // Managed calli
            if ((signature.Flags & MethodSignatureFlags.UnmanagedCallingConventionMask) == 0)
                return;

            // Calli in marshaling stubs
            if (_methodIL is Internal.IL.Stubs.PInvokeILStubMethodIL)
                return;

            MethodDesc stub = _compilation.PInvokeILProvider.GetCalliStub(
                signature,
                ((MetadataType)_methodIL.OwningMethod.OwningType).Module);

            _dependencies.Add(_factory.CanonicalEntrypoint(stub), "calli");
        }

        private void ImportBranch(ILOpcode opcode, BasicBlock target, BasicBlock fallthrough)
        {
            object condition = null;

            if (opcode == ILOpcode.brfalse
                && _typeEqualityPatternAnalyzer.IsTypeEqualityBranch
                && !_typeEqualityPatternAnalyzer.IsTwoTokens
                && !_typeEqualityPatternAnalyzer.IsInequality)
            {
                TypeDesc typeEqualityCheckType = (TypeDesc)_canonMethodIL.GetObject(_typeEqualityPatternAnalyzer.Token1);
                if (!typeEqualityCheckType.IsGenericDefinition
                    && ConstructedEETypeNode.CreationAllowed(typeEqualityCheckType)
                    && !typeEqualityCheckType.ConvertToCanonForm(CanonicalFormKind.Specific).IsCanonicalSubtype(CanonicalFormKind.Any))
                {
                    // If the type could generate metadata, we set the condition to the presence of the metadata.
                    // This covers situations where the typeof is compared against metadata-only types.
                    // Note this assumes a constructed MethodTable always implies having metadata.
                    // This will likely remain true because anyone can call Object.GetType on a constructed type.
                    // If the type cannot generate metadata, we only condition on the MethodTable itself.
                    if (!_factory.MetadataManager.IsReflectionBlocked(typeEqualityCheckType)
                        && typeEqualityCheckType.GetTypeDefinition() is MetadataType typeEqualityCheckMetadataType)
                        condition = _factory.TypeMetadata(typeEqualityCheckMetadataType);
                    else
                        condition = _factory.MaximallyConstructableType(typeEqualityCheckType);
                }
            }

            if (opcode == ILOpcode.brfalse && _isInstCheckPatternAnalyzer.IsIsInstBranch)
            {
                TypeDesc isinstCheckType = (TypeDesc)_canonMethodIL.GetObject(_isInstCheckPatternAnalyzer.Token);
                if (ConstructedEETypeNode.CreationAllowed(isinstCheckType)
                    // Below makes sure we don't need to worry about variance
                    && !isinstCheckType.ConvertToCanonForm(CanonicalFormKind.Specific).IsCanonicalSubtype(CanonicalFormKind.Any)
                    // However, we still need to worry about variant-by-size casting with arrays
                    && !_factory.TypeSystemContext.IsArrayVariantCastable(isinstCheckType))
                {
                    condition = _factory.MaximallyConstructableType(isinstCheckType);
                }
            }

            ImportFallthrough(target);

            if (fallthrough != null)
                ImportFallthrough(fallthrough, condition);
        }

        private void ImportSwitchJump(int jmpBase, int[] jmpDelta, BasicBlock fallthrough)
        {
            for (int i = 0; i < jmpDelta.Length; i++)
            {
                BasicBlock target = _basicBlocks[jmpBase + jmpDelta[i]];
                ImportFallthrough(target);
            }

            if (fallthrough != null)
                ImportFallthrough(fallthrough);
        }

        private void ImportUnbox(int token, ILOpcode opCode)
        {
            TypeDesc type = (TypeDesc)_methodIL.GetObject(token);

            if (!type.IsValueType)
            {
                if (opCode == ILOpcode.unbox_any)
                {
                    // When applied to a reference type, unbox_any has the same effect as castclass.
                    ImportCasting(ILOpcode.castclass, token);
                }
                return;
            }

            if (type.IsRuntimeDeterminedSubtype)
            {
                _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.NecessaryTypeHandle, type), "Unbox");
            }
            else
            {
                _dependencies.Add(_factory.NecessaryTypeSymbol(type), "Unbox");
            }

            ReadyToRunHelper helper;
            if (opCode == ILOpcode.unbox)
            {
                helper = ReadyToRunHelper.Unbox;
                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Unbox_TypeTest), "Unbox");
            }
            else
            {
                Debug.Assert(opCode == ILOpcode.unbox_any);
                helper = ReadyToRunHelper.Unbox_Nullable;
            }

            _dependencies.Add(GetHelperEntrypoint(helper), "Unbox");
        }

        private void ImportRefAnyVal(int token)
        {
            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.GetRefAny), "refanyval");
            ImportTypedRefOperationDependencies(token, "refanyval");
        }

        private void ImportMkRefAny(int token)
        {
            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.TypeHandleToRuntimeType), "mkrefany");
            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.TypeHandleToRuntimeTypeHandle), "mkrefany");
            _factory.MetadataManager.GetDependenciesDueToAccess(ref _dependencies, _factory, _methodIL, (TypeDesc)_canonMethodIL.GetObject(token));
            ImportTypedRefOperationDependencies(token, "mkrefany");
        }

        private void ImportTypedRefOperationDependencies(int token, string reason)
        {
            var type = (TypeDesc)_methodIL.GetObject(token);
            if (type.IsRuntimeDeterminedSubtype)
            {
                _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.TypeHandle, type), reason);
            }
            else
            {
                _dependencies.Add(_factory.MaximallyConstructableType(type), reason);
            }
        }

        private void ImportLdToken(int token)
        {
            object obj = _methodIL.GetObject(token);

            if (obj is TypeDesc type)
            {
                // We might also be able to optimize this a little if this is a ldtoken/GetTypeFromHandle/Equals sequence.
                bool isTypeEquals = false;
                TypeEqualityPatternAnalyzer analyzer = _typeEqualityPatternAnalyzer;
                ILReader reader = new ILReader(_ilBytes, _currentOffset);
                while (!analyzer.IsDefault)
                {
                    ILOpcode opcode = reader.ReadILOpcode();
                    analyzer.Advance(opcode, reader, _methodIL);
                    reader.Skip(opcode);

                    if (analyzer.IsTypeEqualityCheck)
                    {
                        isTypeEquals = true;
                        break;
                    }
                }

                _factory.MetadataManager.GetDependenciesDueToAccess(ref _dependencies, _factory, _methodIL, (TypeDesc)_canonMethodIL.GetObject(token));

                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.GetRuntimeTypeHandle), "ldtoken");
                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.GetRuntimeType), "ldtoken");

                ISymbolNode reference;
                if (type.IsRuntimeDeterminedSubtype)
                {
                    reference = GetGenericLookupHelper(
                        isTypeEquals ? ReadyToRunHelperId.NecessaryTypeHandle : ReadyToRunHelperId.TypeHandle, type);
                }
                else
                {
                    reference = _compilation.ComputeConstantLookup(
                        isTypeEquals ? ReadyToRunHelperId.NecessaryTypeHandle : _compilation.GetLdTokenHelperForType(type), type);
                }
                _dependencies.Add(reference, "ldtoken");
            }
            else if (obj is MethodDesc method)
            {
                _factory.MetadataManager.GetDependenciesDueToAccess(ref _dependencies, _factory, _methodIL, (MethodDesc)_canonMethodIL.GetObject(token));

                if (method.IsRuntimeDeterminedExactMethod)
                {
                    _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.MethodHandle, method), "ldtoken");
                }
                else
                {
                    _dependencies.Add(_factory.RuntimeMethodHandle(method), "ldtoken");
                }

                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.GetRuntimeMethodHandle), "ldtoken");
            }
            else
            {
                var field = (FieldDesc)obj;

                _factory.MetadataManager.GetDependenciesDueToAccess(ref _dependencies, _factory, _methodIL, (FieldDesc)_canonMethodIL.GetObject(token));

                if (field.OwningType.IsRuntimeDeterminedSubtype)
                {
                    _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.FieldHandle, field), "ldtoken");
                }
                else
                {
                    _dependencies.Add(_factory.RuntimeFieldHandle(field), "ldtoken");
                }

                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.GetRuntimeFieldHandle), "ldtoken");
            }
        }

        private static void ImportRefAnyType()
        {
            // TODO
        }

        private static void ImportArgList()
        {
        }

        private void ImportConstrainedPrefix(int token)
        {
            _constrained = (TypeDesc)_methodIL.GetObject(token);
        }

        private void ImportReadOnlyPrefix()
        {
            _isReadOnly = true;
        }

        private void ImportFieldAccess(int token, bool isStatic, bool? write, string reason)
        {
            var field = (FieldDesc)_methodIL.GetObject(token);
            var canonField = (FieldDesc)_canonMethodIL.GetObject(token);

            if (field.IsLiteral)
                ThrowHelper.ThrowMissingFieldException(field.OwningType, field.Name);

            _compilation.NodeFactory.MetadataManager.GetDependenciesDueToAccess(ref _dependencies, _compilation.NodeFactory, _canonMethodIL, canonField);

            // `write` will be null for ld(s)flda. Consider address loads write unless they were
            // for initonly static fields. We'll trust the initonly that this is not a write.
            write ??= !field.IsInitOnly || !field.IsStatic;

            if (write.Value)
            {
                bool isInitOnlyWrite = field.OwningType == _methodIL.OwningMethod.OwningType
                    && ((field.IsStatic && _methodIL.OwningMethod.IsStaticConstructor)
                        || (!field.IsStatic && _methodIL.OwningMethod.IsConstructor));

                if (!isInitOnlyWrite)
                {
                    FieldDesc fieldToReport = canonField;
                    DefType fieldOwningType = canonField.OwningType;
                    TypeDesc canonFieldOwningType = fieldOwningType.ConvertToCanonForm(CanonicalFormKind.Specific);
                    if (fieldOwningType != canonFieldOwningType)
                        fieldToReport = _factory.TypeSystemContext.GetFieldForInstantiatedType(fieldToReport.GetTypicalFieldDefinition(), (InstantiatedType)canonFieldOwningType);

                    _dependencies.Add(_factory.NotReadOnlyField(fieldToReport), "Field written outside initializer");
                }
            }

            // Covers both ldsfld/ldsflda and ldfld/ldflda with a static field
            if (isStatic || field.IsStatic)
            {
                // ldsfld/ldsflda with an instance field is invalid IL
                if (isStatic && !field.IsStatic)
                    ThrowHelper.ThrowInvalidProgramException();

                // References to literal fields from IL body should never resolve.
                // The CLR would throw a MissingFieldException while jitting and so should we.
                if (field.IsLiteral)
                    ThrowHelper.ThrowMissingFieldException(field.OwningType, field.Name);

                ReadyToRunHelperId helperId;
                if (field.HasRva)
                {
                    // We don't care about field RVA data for the usual cases, but if this is one of the
                    // magic fields the compiler synthetized, the data blob might bring more dependencies
                    // and we need to scan those.
                    _dependencies.Add(_compilation.GetFieldRvaData(field), reason);
                    if (_compilation.HasLazyStaticConstructor(canonField.OwningType))
                    {
                        helperId = ReadyToRunHelperId.GetNonGCStaticBase;
                        goto addBase;
                    }
                    return;
                }

                if (field.IsThreadStatic)
                {
                    helperId = ReadyToRunHelperId.GetThreadStaticBase;
                }
                else if (field.HasGCStaticBase)
                {
                    helperId = ReadyToRunHelperId.GetGCStaticBase;
                }
                else
                {
                    helperId = ReadyToRunHelperId.GetNonGCStaticBase;
                }

            addBase:
                TypeDesc owningType = field.OwningType;
                if (owningType.IsRuntimeDeterminedSubtype)
                {
                    _dependencies.Add(GetGenericLookupHelper(helperId, owningType), reason);
                }
                else
                {
                    _dependencies.Add(_factory.ReadyToRunHelper(helperId, owningType), reason);
                }
            }
        }

        private void ImportLoadField(int token, bool isStatic)
        {
            ImportFieldAccess(token, isStatic, write: false, isStatic ? "ldsfld" : "ldfld");
        }

        private void ImportAddressOfField(int token, bool isStatic)
        {
            ImportFieldAccess(token, isStatic, write: null, isStatic ? "ldsflda" : "ldflda");
        }

        private void ImportStoreField(int token, bool isStatic)
        {
            ImportFieldAccess(token, isStatic, write: true, isStatic ? "stsfld" : "stfld");
        }

        private void ImportLoadString(int token)
        {
            _dependencies.Add(_factory.SerializedStringObject((string)_methodIL.GetObject(token)), "ldstr");
        }

        private void ImportBox(int token)
        {
            var type = (TypeDesc)_methodIL.GetObject(token);

            // There are some sequences of box with ByRefLike types that are allowed
            // per the extension to the ECMA-335 specification.
            // Everything else is invalid.
            if (!type.IsRuntimeDeterminedType && type.IsByRefLike)
            {
                ILReader reader = new ILReader(_ilBytes, _currentOffset);
                ILOpcode nextOpcode = reader.ReadILOpcode();

                // box ; br_true/false
                if (nextOpcode is ILOpcode.brtrue or ILOpcode.brtrue_s or ILOpcode.brfalse or ILOpcode.brfalse_s)
                    return;

                if (nextOpcode is ILOpcode.unbox_any or ILOpcode.isinst)
                {
                    var type2 = (TypeDesc)_methodIL.GetObject(reader.ReadILToken());
                    if (type == type2)
                    {
                        // box ; unbox_any with same token
                        if (nextOpcode == ILOpcode.unbox_any)
                            return;

                        nextOpcode = reader.ReadILOpcode();

                        // box ; isinst ; br_true/false
                        if (nextOpcode is ILOpcode.brtrue or ILOpcode.brtrue_s or ILOpcode.brfalse or ILOpcode.brfalse_s)
                            return;

                        // box ; isinst ; unbox_any
                        if (nextOpcode == ILOpcode.unbox_any)
                        {
                            type2 = (TypeDesc)_methodIL.GetObject(reader.ReadILToken());
                            if (type2 == type)
                                return;
                        }
                    }
                }

                ThrowHelper.ThrowInvalidProgramException();
            }

            AddBoxingDependencies(type, "Box");
        }

        private void AddBoxingDependencies(TypeDesc type, string reason)
        {
            Debug.Assert(!type.IsCanonicalSubtype(CanonicalFormKind.Any));

            // Generic code will have BOX instructions when referring to T - the instruction is a no-op
            // if the substitution wasn't a value type.
            if (!type.IsValueType)
                return;

            TypeDesc typeForAccessCheck = type.IsRuntimeDeterminedSubtype ? type.ConvertToCanonForm(CanonicalFormKind.Specific) : type;
            _factory.MetadataManager.GetDependenciesDueToAccess(ref _dependencies, _factory, _methodIL, typeForAccessCheck);

            if (type.IsRuntimeDeterminedSubtype)
            {
                _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.TypeHandle, type), reason);
            }
            else
            {
                _dependencies.Add(_factory.ConstructedTypeSymbol(type), reason);
            }

            if (type.IsNullable)
            {
                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Box_Nullable), reason);
            }
            else
            {
                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Box), reason);
            }
        }

        private void ImportLeave(BasicBlock target)
        {
            ImportFallthrough(target);
        }

        private void ImportNewArray(int token)
        {
            var elementType = (TypeDesc)_methodIL.GetObject(token);
            _factory.MetadataManager.GetDependenciesDueToAccess(ref _dependencies, _factory, _methodIL, (TypeDesc)_canonMethodIL.GetObject(token));
            if (elementType.IsRuntimeDeterminedSubtype)
            {
                _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.TypeHandle, elementType.MakeArrayType()), "newarr");
                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.NewArray), "newarr");
            }
            else
            {
                if (elementType.IsVoid)
                    ThrowHelper.ThrowInvalidProgramException();
                _dependencies.Add(_factory.ConstructedTypeSymbol(elementType.MakeArrayType()), "newarr");
            }
        }

        private void ImportLoadElement(int token)
        {
            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.RngChkFail), "ldelem");
        }

        private void ImportLoadElement(TypeDesc elementType)
        {
            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.RngChkFail), "ldelem");
        }

        private void ImportStoreElement(int token)
        {
            ImportStoreElement((TypeDesc)_methodIL.GetObject(token));
        }

        private void ImportStoreElement(TypeDesc elementType)
        {
            if (elementType == null || elementType.IsGCPointer)
            {
                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Stelem_Ref), "stelem");
            }

            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.RngChkFail), "stelem");
        }

        private void ImportAddressOfElement(int token)
        {
            TypeDesc elementType = (TypeDesc)_methodIL.GetObject(token);
            if (elementType.IsGCPointer && !_isReadOnly)
            {
                if (elementType.IsRuntimeDeterminedSubtype)
                    _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.NecessaryTypeHandle, elementType), "ldelema");
                else
                    _dependencies.Add(_factory.NecessaryTypeSymbol(elementType), "ldelema");

                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Ldelema_Ref), "ldelema");
            }

            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.RngChkFail), "ldelema");
        }

        private void ImportBinaryOperation(ILOpcode opcode)
        {
            switch (opcode)
            {
                case ILOpcode.add_ovf:
                case ILOpcode.add_ovf_un:
                case ILOpcode.sub_ovf:
                case ILOpcode.sub_ovf_un:
                    _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Overflow), "_ovf");
                    break;
                case ILOpcode.mul_ovf:
                case ILOpcode.mul_ovf_un:
                    if (_compilation.TypeSystemContext.Target.PointerSize == 4)
                    {
                        _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.LMulOfv), "_lmulovf");
                        _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.ULMulOvf), "_ulmulovf");
                    }

                    _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Overflow), "_ovf");
                    break;
                case ILOpcode.div:
                case ILOpcode.div_un:
                    if (_compilation.TypeSystemContext.Target.Architecture is TargetArchitecture.ARM or TargetArchitecture.X86)
                    {
                        _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.ULDiv), "_uldiv");
                        _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.LDiv), "_ldiv");

                        if (_compilation.TypeSystemContext.Target.Architecture is TargetArchitecture.ARM)
                        {
                            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.UDiv), "_udiv");
                            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Div), "_div");
                        }
                    }
                    else if (_compilation.TypeSystemContext.Target.Architecture == TargetArchitecture.ARM64)
                    {
                        _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.ThrowDivZero), "_divbyzero");
                        if (opcode == ILOpcode.div)
                        {
                            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Overflow), "_ovf");
                        }
                    }
                    break;
                case ILOpcode.rem:
                case ILOpcode.rem_un:
                    if (_compilation.TypeSystemContext.Target.Architecture is TargetArchitecture.ARM or TargetArchitecture.X86)
                    {
                        _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.ULMod), "_ulmod");
                        _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.LMod), "_lmod");
                        if (_compilation.TypeSystemContext.Target.Architecture is TargetArchitecture.ARM)
                        {
                            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.UMod), "_umod");
                            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Mod), "_mod");
                        }
                    }
                    else if (_compilation.TypeSystemContext.Target.Architecture == TargetArchitecture.ARM64)
                    {
                        _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.ThrowDivZero), "_divbyzero");
                        if (opcode == ILOpcode.rem)
                        {
                            _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Overflow), "_ovf");
                        }
                    }

                    _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.DblRem), "rem");
                    _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.FltRem), "rem");
                    break;
            }
        }

        private void ImportConvert(WellKnownType wellKnownType, bool checkOverflow, bool unsigned)
        {
            if (checkOverflow)
            {
                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Dbl2IntOvf), "_dbl2intovf");
                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Dbl2UIntOvf), "_dbl2uintovf");
                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Dbl2LngOvf), "_dbl2lngovf");
                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Dbl2ULngOvf), "_dbl2ulngovf");

                _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Overflow), "_ovf");
            }
        }

        private void ImportBasicBlockEdge(BasicBlock source, BasicBlock next, object condition = null)
        {
            // We don't track multiple conditions; if the source basic block is only reachable under a condition,
            // this condition will be used for the next basic block, irrespective if we could make it more narrow.
            object effectiveCondition = source.Condition ?? condition;

            // Did we already look at this basic block?
            if (next.State != BasicBlock.ImportState.Unmarked)
            {
                // If next is not conditioned, it stays not conditioned.
                // If it was conditioned on something else, it needs to become unconditional.
                // If the conditions match, it stays conditioned on the same thing.
                if (next.Condition != null && next.Condition != effectiveCondition)
                {
                    // Now we need to make `next` not conditioned. We move all of its dependencies to
                    // unconditional dependencies, and do this for all basic blocks that are reachable
                    // from it.
                    // TODO-SIZE: below doesn't do it for all basic blocks reachable from `next`, but
                    // for all basic blocks with the same conditon. This is a shortcut. It likely
                    // doesn't matter in practice.
                    object conditionToRemove = next.Condition;
                    foreach (BasicBlock bb in _basicBlocks)
                    {
                        if (bb?.Condition == conditionToRemove)
                        {
                            _unconditionalDependencies.AddRange(bb.Dependencies);
                            bb.Dependencies = null;
                            bb.Condition = null;
                        }
                    }
                }
            }
            else
            {
                if (effectiveCondition != null)
                {
                    next.Condition = effectiveCondition;
                    next.Dependencies = new DependencyList();
                    MarkBasicBlock(next, ref _lateBasicBlocks);
                }
                else
                {
                    MarkBasicBlock(next);
                }
            }
        }

        private void ImportFallthrough(BasicBlock next, object condition = null)
        {
            ImportBasicBlockEdge(_currentBasicBlock, next, condition);
        }

        private int ReadILTokenAt(int ilOffset)
        {
            return (int)(_ilBytes[ilOffset]
                + (_ilBytes[ilOffset + 1] << 8)
                + (_ilBytes[ilOffset + 2] << 16)
                + (_ilBytes[ilOffset + 3] << 24));
        }

        private static void ReportInvalidBranchTarget(int targetOffset)
        {
            ThrowHelper.ThrowInvalidProgramException();
        }

        private static void ReportFallthroughAtEndOfMethod()
        {
            ThrowHelper.ThrowInvalidProgramException();
        }

        private static void ReportMethodEndInsideInstruction()
        {
            ThrowHelper.ThrowInvalidProgramException();
        }

        private static void ReportInvalidInstruction(ILOpcode opcode)
        {
            ThrowHelper.ThrowInvalidProgramException();
        }

        private static bool IsTypeGetTypeFromHandle(MethodDesc method)
        {
            if (method.IsIntrinsic && method.Name == "GetTypeFromHandle")
            {
                MetadataType owningType = method.OwningType as MetadataType;
                if (owningType != null)
                {
                    return owningType.Name == "Type" && owningType.Namespace == "System";
                }
            }

            return false;
        }

        private static bool IsActivatorDefaultConstructorOf(MethodDesc method)
        {
            if (method.IsIntrinsic && method.Name == "DefaultConstructorOf" && method.Instantiation.Length == 1)
            {
                MetadataType owningType = method.OwningType as MetadataType;
                if (owningType != null)
                {
                    return owningType.Name == "Activator" && owningType.Namespace == "System";
                }
            }

            return false;
        }

        private static bool IsActivatorAllocatorOf(MethodDesc method)
        {
            if (method.IsIntrinsic && method.Name == "AllocatorOf" && method.Instantiation.Length == 1)
            {
                MetadataType owningType = method.OwningType as MetadataType;
                if (owningType != null)
                {
                    return owningType.Name == "Activator" && owningType.Namespace == "System";
                }
            }

            return false;
        }

        private static bool IsEETypePtrOf(MethodDesc method)
        {
            if (method.IsIntrinsic && method.Name == "Of" && method.Instantiation.Length == 1)
            {
                MetadataType owningType = method.OwningType as MetadataType;
                if (owningType != null)
                {
                    return owningType.Name == "MethodTable" && owningType.Namespace == "Internal.Runtime";
                }
            }

            return false;
        }

        private static bool IsRuntimeHelpersIsReferenceOrContainsReferences(MethodDesc method)
        {
            if (method.IsIntrinsic && method.Name == "IsReferenceOrContainsReferences" && method.Instantiation.Length == 1)
            {
                MetadataType owningType = method.OwningType as MetadataType;
                if (owningType != null)
                {
                    return owningType.Name == "RuntimeHelpers" && owningType.Namespace == "System.Runtime.CompilerServices";
                }
            }

            return false;
        }

        private static bool IsMemoryMarshalGetArrayDataReference(MethodDesc method)
        {
            if (method.IsIntrinsic && method.Name == "GetArrayDataReference" && method.Instantiation.Length == 1)
            {
                MetadataType owningType = method.OwningType as MetadataType;
                if (owningType != null)
                {
                    return owningType.Name == "MemoryMarshal" && owningType.Namespace == "System.Runtime.InteropServices";
                }
            }

            return false;
        }

        private DefType GetWellKnownType(WellKnownType wellKnownType)
        {
            return _compilation.TypeSystemContext.GetWellKnownType(wellKnownType);
        }

        private static void StartImportingInstruction() { }
        private static void ImportNop() { }
        private static void ImportBreak() { }
        private static void ImportLoadVar(int index, bool argument) { }
        private static void ImportStoreVar(int index, bool argument) { }
        private static void ImportAddressOfVar(int index, bool argument) { }
        private static void ImportDup() { }
        private static void ImportPop() { }
        private static void ImportLoadNull() { }
        private static void ImportReturn() { }
        private static void ImportLoadInt(long value, StackValueKind kind) { }
        private static void ImportLoadFloat(double value) { }
        private static void ImportLoadIndirect(int token) { }
        private static void ImportLoadIndirect(TypeDesc type) { }
        private static void ImportStoreIndirect(int token) { }
        private static void ImportStoreIndirect(TypeDesc type) { }
        private static void ImportShiftOperation(ILOpcode opcode) { }
        private static void ImportCompareOperation(ILOpcode opcode) { }
        private static void ImportUnaryOperation(ILOpcode opCode) { }
        private static void ImportCpOpj(int token) { }
        private static void ImportCkFinite() { }
        private static void ImportLocalAlloc() { }
        private static void ImportEndFilter() { }
        private static void ImportCpBlk() { }
        private static void ImportInitBlk() { }
        private static void ImportRethrow() { }
        private static void ImportSizeOf(int token) { }
        private static void ImportUnalignedPrefix(byte alignment) { }
        private static void ImportVolatilePrefix() { }
        private static void ImportTailPrefix() { }
        private static void ImportNoPrefix(byte mask) { }
        private static void ImportThrow() { }
        private static void ImportInitObj(int token) { }
        private static void ImportLoadLength() { }
        private static void ImportEndFinally() { }
    }
}
