﻿namespace GWallet.Backend

open System
open System.Text
open System.Security.Cryptography

// .NET implementation for WarpWallet: https://keybase.io/warp/
module WarpKey =

    let XOR (a: array<byte>) (b: array<byte>): array<byte> =
        if (a.Length <> b.Length) then
            raise (ArgumentException())
        else
            let result = Array.create<byte> a.Length (byte 0)
            for i = 0 to (a.Length - 1) do
                result.[i] <- ((a.[i]) ^^^ (b.[i]))
            result

    let Scrypt (passphrase: string) (salt: string): array<byte> =
        // FIXME: stop using mutable collections
        let passphraseByteList = System.Collections.Generic.List<byte>()
        passphraseByteList.AddRange (Encoding.UTF8.GetBytes(passphrase))
        passphraseByteList.Add (byte 1)

        let saltByteList = System.Collections.Generic.List<byte>()
        saltByteList.AddRange (Encoding.UTF8.GetBytes(salt))
        saltByteList.Add (byte 1)

        NBitcoin.Crypto.SCrypt.ComputeDerivedKey(passphraseByteList.ToArray(),
                                                 saltByteList.ToArray(),
                                                 262144, 8, 1, Nullable<int>(), 32)

    let PBKDF2 (passphrase: string) (salt: string): array<byte> =
        // FIXME: stop using mutable collections
        let passphraseByteList = System.Collections.Generic.List<byte>()
        passphraseByteList.AddRange (Encoding.UTF8.GetBytes(passphrase))
        passphraseByteList.Add (byte 2)

        let saltByteList = System.Collections.Generic.List<byte>()
        saltByteList.AddRange (Encoding.UTF8.GetBytes(salt))
        saltByteList.Add (byte 2)

        let hashAlgo = new HMACSHA256(passphraseByteList.ToArray())

        // FIXME: use NBitcoin (instead of CryptSharp) when this has been merged: https://github.com/MetacoSA/NBitcoin/pull/444
        CryptSharp.Utility.Pbkdf2.ComputeDerivedKey(hashAlgo, saltByteList.ToArray(), 65536, 32)

    let private LENGTH_OF_PRIVATE_KEYS = 32
    let CreatePrivateKey (passphrase: string) (salt: string) =
        let scrypt = Scrypt passphrase salt
        let pbkdf2 = PBKDF2 passphrase salt
        let privKeyBytes = XOR scrypt pbkdf2
        if (privKeyBytes.Length <> LENGTH_OF_PRIVATE_KEYS) then
            failwithf "Something went horribly wrong because length of privKey was not %d but %d"
                      LENGTH_OF_PRIVATE_KEYS privKeyBytes.Length
        privKeyBytes
        //NBitcoin.Key(privKeyBytes, LENGTH_OF_PRIVATE_KEYS, false)