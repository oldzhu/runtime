// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using Internal.Cryptography;

namespace System.Security.Cryptography.Pkcs
{
    internal partial class CmsSignature
    {
        static partial void PrepareRegistrationDsa(Dictionary<string, CmsSignature> lookup)
        {
            if (Helpers.IsDSASupported)
            {
                lookup.Add(Oids.DsaWithSha1, new DSACmsSignature(Oids.DsaWithSha1, HashAlgorithmName.SHA1));
                lookup.Add(Oids.DsaWithSha256, new DSACmsSignature(Oids.DsaWithSha256, HashAlgorithmName.SHA256));
                lookup.Add(Oids.DsaWithSha384, new DSACmsSignature(Oids.DsaWithSha384, HashAlgorithmName.SHA384));
                lookup.Add(Oids.DsaWithSha512, new DSACmsSignature(Oids.DsaWithSha512, HashAlgorithmName.SHA512));
                lookup.Add(Oids.Dsa, new DSACmsSignature(null, default));
            }
        }

        private sealed class DSACmsSignature : CmsSignature
        {
            private readonly HashAlgorithmName _expectedDigest;
            private readonly string? _signatureAlgorithm;

            internal override RSASignaturePadding? SignaturePadding => null;

            internal DSACmsSignature(string? signatureAlgorithm, HashAlgorithmName expectedDigest)
            {
                _signatureAlgorithm = signatureAlgorithm;
                _expectedDigest = expectedDigest;
            }

            protected override bool VerifyKeyType(object key) => key is DSA;
            internal override bool NeedsHashedMessage => true;

            internal override bool VerifySignature(
#if NET || NETSTANDARD2_1
                ReadOnlySpan<byte> valueHash,
                ReadOnlyMemory<byte> signature,
#else
                byte[] valueHash,
                byte[] signature,
#endif
                string? digestAlgorithmOid,
                ReadOnlyMemory<byte>? signatureParameters,
                X509Certificate2 certificate)
            {
                HashAlgorithmName digestAlgorithmName = PkcsHelpers.GetDigestAlgorithm(digestAlgorithmOid, forVerification: true);

                if (_expectedDigest != digestAlgorithmName)
                {
                    throw new CryptographicException(
                        SR.Format(
                            SR.Cryptography_Cms_InvalidSignerHashForSignatureAlg,
                            digestAlgorithmOid,
                            _signatureAlgorithm));
                }

                Debug.Assert(Helpers.IsDSASupported);

                DSA? dsa = certificate.GetDSAPublicKey();

                if (dsa == null)
                {
                    return false;
                }

                using (dsa)
                {
                    DSAParameters dsaParameters = dsa.ExportParameters(false);
                    int bufSize = 2 * dsaParameters.Q!.Length;

#if NET || NETSTANDARD2_1
                    byte[] rented = CryptoPool.Rent(bufSize);
                    Span<byte> ieee = new Span<byte>(rented, 0, bufSize);

                    try
                    {
#else
                    byte[] ieee = new byte[bufSize];
#endif
                        if (!DsaDerToIeee(signature, ieee))
                        {
                            return false;
                        }

                        return dsa.VerifySignature(valueHash, ieee);
#if NET || NETSTANDARD2_1
                    }
                    finally
                    {
                        CryptoPool.Return(rented, bufSize);
                    }
#endif
                }
            }

            protected override bool Sign(
#if NET || NETSTANDARD2_1
                ReadOnlySpan<byte> dataHash,
#else
                ReadOnlyMemory<byte> dataHash,
#endif
                string? hashAlgorithmOid,
                X509Certificate2 certificate,
                object? key,
                bool silent,
                [NotNullWhen(true)] out string? signatureAlgorithm,
                [NotNullWhen(true)] out byte[]? signatureValue,
                out byte[]? signatureParameters)
            {
                Debug.Assert(Helpers.IsDSASupported);
                signatureParameters = null;

                using (GetSigningKey(key, certificate, silent, DSACertificateExtensions.GetDSAPublicKey, out DSA? dsa))
                {
                    if (dsa == null)
                    {
                        signatureAlgorithm = null;
                        signatureValue = null;
                        return false;
                    }

                    string? oidValue =
                        hashAlgorithmOid switch
                        {
                            Oids.Sha1 => Oids.DsaWithSha1,
                            Oids.Sha256 => Oids.DsaWithSha256,
                            Oids.Sha384 => Oids.DsaWithSha384,
                            Oids.Sha512 => Oids.DsaWithSha512,
                            _ => null
                        };

                    if (oidValue == null)
                    {
                        signatureAlgorithm = null;
                        signatureValue = null;
                        return false;
                    }

                    signatureAlgorithm = oidValue;

#if NET || NETSTANDARD2_1
                    // The Q size cannot be bigger than the KeySize.
                    byte[] rented = CryptoPool.Rent(dsa.KeySize / 8);
                    int bytesWritten = 0;

                    try
                    {
                        if (dsa.TryCreateSignature(dataHash, rented, out bytesWritten))
                        {
                            var signature = new ReadOnlySpan<byte>(rented, 0, bytesWritten);

                            if (key != null)
                            {
                                using (DSA certKey = certificate.GetDSAPublicKey()!)
                                {
                                    if (!certKey.VerifySignature(dataHash, signature))
                                    {
                                        // key did not match certificate
                                        signatureValue = null;
                                        return false;
                                    }
                                }
                            }

                            signatureValue = DsaIeeeToDer(signature);
                            return true;
                        }
                    }
                    finally
                    {
                        CryptoPool.Return(rented, bytesWritten);
                    }

                    signatureValue = null;
                    return false;
#else
                    byte[] signature = dsa.CreateSignature(dataHash);
                    signatureValue = DsaIeeeToDer(new ReadOnlySpan<byte>(signature));
                    return true;
#endif
                }
            }
        }
    }
}
