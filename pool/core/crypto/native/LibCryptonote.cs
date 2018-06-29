

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using XPool.utils;

namespace XPool.core.crypto.native
{
    public static unsafe class LibCryptonote
    {
  
        const string path = "libmultihash";

        [DllImport(path, EntryPoint = "convert_blob_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool convert_blob(byte* input, int inputSize, byte* output, ref int outputSize);

        [DllImport(path, EntryPoint = "decode_address_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern UInt64 decode_address(byte* input, int inputSize);

        [DllImport(path, EntryPoint = "decode_integrated_address_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern UInt64 decode_integrated_address(byte* input, int inputSize);

        [DllImport(path, EntryPoint = "cn_slow_hash_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern int cn_slow_hash(byte* input, byte* output, uint inputLength, int variant);

        [DllImport(path, EntryPoint = "cn_slow_hash_lite_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern int cn_slow_hash_lite(byte* input, byte* output, uint inputLength);

        [DllImport(path, EntryPoint = "cn_fast_hash_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern int cn_fast_hash(byte* input, byte* output, uint inputLength);

        public static byte[] ConvertBlob(byte[] data, int size)
        {
            Assertion.RequiresNonNull(data, nameof(data));
            Assertion.Requires<ArgumentException>(data.Length > 0, $"{nameof(data)} must not be empty");

            fixed(byte* input = data)
            {
                                var outputBuffer = ArrayPool<byte>.Shared.Rent(0x100);

                try
                {
                    var outputBufferLength = outputBuffer.Length;

                    var success = false;
                    fixed (byte* output = outputBuffer)
                    {
                        success = convert_blob(input, size, output, ref outputBufferLength);
                    }

                    if (!success)
                    {
                                                if (outputBufferLength == 0)
                            return null; 
                                                ArrayPool<byte>.Shared.Return(outputBuffer);
                        outputBuffer = ArrayPool<byte>.Shared.Rent(outputBufferLength);

                        fixed (byte* output = outputBuffer)
                        {
                            success = convert_blob(input, size, output, ref outputBufferLength);
                        }

                        if (!success)
                            return null;                     }

                                        var result = new byte[outputBufferLength];
                    Buffer.BlockCopy(outputBuffer, 0, result, 0, outputBufferLength);

                    return result;
                }

                finally
                {
                    ArrayPool<byte>.Shared.Return(outputBuffer);
                }
            }
        }

        public static UInt64 DecodeAddress(string address)
        {
            Assertion.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

            var data = Encoding.UTF8.GetBytes(address);

            fixed(byte* input = data)
            {
                return decode_address(input, data.Length);
            }
        }

        public static UInt64 DecodeIntegratedAddress(string address)
        {
            Assertion.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

            var data = Encoding.UTF8.GetBytes(address);

            fixed (byte* input = data)
            {
                return decode_integrated_address(input, data.Length);
            }
        }

        public static PooledArraySegment<byte> CryptonightHashSlow(byte[] data, int variant)
        {
            Assertion.RequiresNonNull(data, nameof(data));

            var result = new PooledArraySegment<byte>(32);

            fixed (byte* input = data)
            {
                fixed(byte* output = result.Array)
                {
                    cn_slow_hash(input, output, (uint) data.Length, variant);
                }
            }

            return result;
        }

        public static PooledArraySegment<byte> CryptonightHashSlowLite(byte[] data)
        {
            Assertion.RequiresNonNull(data, nameof(data));

            var result = new PooledArraySegment<byte>(32);

            fixed (byte* input = data)
            {
                fixed (byte* output = result.Array)
                {
                    cn_slow_hash_lite(input, output, (uint)data.Length);
                }
            }

            return result;
        }

        public static PooledArraySegment<byte> CryptonightHashFast(byte[] data)
        {
            Assertion.RequiresNonNull(data, nameof(data));

            var result = new PooledArraySegment<byte>(32);

            fixed (byte* input = data)
            {
                fixed(byte* output = result.Array)
                {
                    cn_fast_hash(input, output, (uint) data.Length);
                }
            }

            return result;
        }
    }
}
