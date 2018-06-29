

using System;
using System.Runtime.InteropServices;

namespace XPool.core.crypto.native
{



    public static unsafe class LibMultihash
    {


        const string path = "libmultihash";

        [DllImport(path, EntryPoint = "scrypt_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int scrypt(byte* input, byte* output, uint n, uint r, uint inputLength);

        [DllImport(path, EntryPoint = "quark_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int quark(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "x11_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int x11(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "x15_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int x15(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "x17_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int x17(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "neoscrypt_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int neoscrypt(byte* input, byte* output, uint inputLength, uint profile);

        [DllImport(path, EntryPoint = "scryptn_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int scryptn(byte* input, byte* output, uint nFactor, uint inputLength);

        [DllImport(path, EntryPoint = "kezzak_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int kezzak(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "bcrypt_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int bcrypt(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "skein_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int skein(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "groestl_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int groestl(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "groestl_myriad_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int groestl_myriad(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "blake_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int blake(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "blake2s_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int blake2s(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "dcrypt_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int dcrypt(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "fugue_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int fugue(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "qubit_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int qubit(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "s3_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int s3(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "hefty1_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int hefty1(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "shavite3_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int shavite3(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "nist5_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nist5(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "fresh_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int fresh(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "jh_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int jh(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "c11_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int c11(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "x16r_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int x16r(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "x16s_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int x16s(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "lyra2re_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int lyra2re(byte* input, byte* output);

        [DllImport(path, EntryPoint = "lyra2rev2_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int lyra2rev2(byte* input, byte* output);

        [DllImport(path, EntryPoint = "equihash_verify_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool equihash_verify(byte* header, int headerLength, byte* solution, int solutionLength);

        [DllImport(path, EntryPoint = "sha3_256_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sha3_256(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "sha3_512_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sha3_512(byte* input, byte* output, uint inputLength);

        #region Ethash

        [StructLayout(LayoutKind.Sequential)]
        public struct ethash_h256_t
        {
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U8, SizeConst = 32)] public byte[] value;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ethash_return_value
        {
            public ethash_h256_t result;
            public ethash_h256_t mix_hash;

            [MarshalAs(UnmanagedType.U1)] public bool success;
        }

        public delegate int ethash_callback_t(uint progress);

        [DllImport(path, EntryPoint = "ethash_light_new_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ethash_light_new(ulong block_number);

        [DllImport(path, EntryPoint = "ethash_light_delete_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ethash_light_delete(IntPtr handle);

        [DllImport(path, EntryPoint = "ethash_light_compute_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ethash_light_compute(IntPtr handle, byte* header_hash, ulong nonce, ref ethash_return_value result);

        [DllImport(path, EntryPoint = "ethash_full_new_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ethash_full_new(string dagDir, IntPtr light, ethash_callback_t callback);

        [DllImport(path, EntryPoint = "ethash_full_delete_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ethash_full_delete(IntPtr handle);

        [DllImport(path, EntryPoint = "ethash_full_compute_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ethash_full_compute(IntPtr handle, byte* header_hash, ulong nonce, ref ethash_return_value result);

        [DllImport(path, EntryPoint = "ethash_full_dag_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ethash_full_dag(IntPtr handle);

        [DllImport(path, EntryPoint = "ethash_full_dag_size_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong ethash_full_dag_size(IntPtr handle);

        [DllImport(path, EntryPoint = "ethash_get_seedhash_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern ethash_h256_t ethash_get_seedhash(ulong block_number);

        [DllImport(path, EntryPoint = "ethash_get_default_dirname_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ethash_get_default_dirname(byte* data, int length);

        #endregion     }
    }
}
