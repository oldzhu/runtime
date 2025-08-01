// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using Internal.Cryptography;

namespace System.Security.Cryptography
{
    /// <summary>
    ///   Provides a Cryptography Next Generation (CNG) implementation of the Module-Lattice-Based Digital Signature Algorithm (ML-DSA).
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     This algorithm is specified by FIPS-204.
    ///   </para>
    ///   <para>
    ///     Developers are encouraged to program against the <see cref="MLDsa" /> base class,
    ///     rather than any specific derived class.
    ///     The derived classes are intended for interop with the underlying system
    ///     cryptographic libraries.
    ///   </para>
    /// </remarks>
    [Experimental(Experimentals.PostQuantumCryptographyDiagId, UrlFormat = Experimentals.SharedUrlFormat)]
    public sealed partial class MLDsaCng : MLDsa
    {
        private CngKey _key;

        /// <summary>
        ///   Initializes a new instance of the <see cref="MLDsaCng"/> class by using the specified <see cref="CngKey"/>.
        /// </summary>
        /// <param name="key">
        ///   The key that will be used as input to the cryptographic operations performed by the current object.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///   <paramref name="key"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///   <paramref name="key"/> does not specify a Module-Lattice-Based Digital Signature Algorithm (ML-DSA) group.
        /// </exception>
        /// <exception cref="PlatformNotSupportedException">
        ///   Cryptography Next Generation (CNG) classes are not supported on this system.
        /// </exception>
        [SupportedOSPlatform("windows")]
        public MLDsaCng(CngKey key)
            : base(AlgorithmFromHandleWithPlatformCheck(key, out CngKey duplicateKey))
        {
            _key = duplicateKey;
        }

        /// <inheritdoc />
        protected override void SignMuCore(ReadOnlySpan<byte> mu, Span<byte> destination) =>
            throw new PlatformNotSupportedException();

        /// <inheritdoc />
        protected override bool VerifyMuCore(ReadOnlySpan<byte> mu, ReadOnlySpan<byte> signature) =>
            throw new PlatformNotSupportedException();

        private static MLDsaAlgorithm AlgorithmFromHandleWithPlatformCheck(CngKey key, out CngKey duplicateKey)
        {
            if (!Helpers.IsOSPlatformWindows)
            {
                throw new PlatformNotSupportedException();
            }

            return AlgorithmFromHandle(key, out duplicateKey);
        }

        private static partial MLDsaAlgorithm AlgorithmFromHandle(CngKey key, out CngKey duplicateKey);

        /// <summary>
        ///   Gets a new <see cref="CngKey" /> representing the key used by the current instance.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        ///   This instance has been disposed.
        /// </exception>
        /// <remarks>
        ///   This <see cref="CngKey"/> object is not the same as the one passed to <see cref="MLDsaCng(CngKey)"/>,
        ///   if that constructor was used. However, it will point to the same CNG key.
        /// </remarks>
        public partial CngKey GetKey();
    }
}
