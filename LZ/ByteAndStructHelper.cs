using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LZ
{
    public static class BytesAndStructHelper
    {
        //struct转换为byte[]
        public static byte[] StructToBytes(object structObj)
        {
            int size = Marshal.SizeOf(structObj);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            byte[] bytes = new byte[size];
            try
            {
                Marshal.StructureToPtr(structObj, buffer, false);
                Marshal.Copy(buffer, bytes, 0, size);
                ConvertL2B(ref bytes, structObj.GetType());
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return bytes;
        }

        public static byte[] StructToBytes<T>(T structObj, bool convertL2B) where T : struct
        {
            int size = Marshal.SizeOf(structObj);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            byte[] bytes = new byte[size];
            try
            {
                Marshal.StructureToPtr(structObj, buffer, false);
                Marshal.Copy(buffer, bytes, 0, size);

                if (convertL2B)
                {
                    ConvertL2B(ref bytes, structObj.GetType());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return bytes;
        }


        /// <summary>
        /// byte数组转结构体
        /// </summary>
        /// <param name="bytes">byte数组</param>
        /// <param name="type">结构体类型</param>
        /// <param name="offset">字节数组前面的头长</param>
        /// <param name="convertL2B">大小端转换</param>
        /// <returns>转换后的结构体</returns>
        public static object BytesToStruct(byte[] bytes, Type type, int offset = 0, bool convertL2B = true)
        {
            //得到结构体的大小
            int size = Marshal.SizeOf(type);
            //byte数组长度小于结构体的大小
            if (size > (bytes.Length - offset))
            {
                //返回空
                return null;
            }

            if (convertL2B)
            {
                ConvertL2B(ref bytes, type, offset);
            }

            //分配结构体大小的内存空间
            IntPtr structPtr = Marshal.AllocHGlobal(size);
            //将byte数组拷到分配好的内存空间
            Marshal.Copy(bytes, offset, structPtr, size);
            //将内存空间转换为目标结构体
            object obj = Marshal.PtrToStructure(structPtr, type);
            //释放内存空间
            Marshal.FreeHGlobal(structPtr);
            //返回结构体
            return obj;
        }

        /// <summary>
        /// byte数组转结构体
        /// </summary>
        /// <param name="bytes">byte数组</param>
        /// <param name="offset">字节数组前面的头长</param>
        /// <param name="convertL2B">大小端转换</param>
        /// <returns>转换后的结构体</returns>
        public static T BytesToStruct<T>(byte[] bytes, int offset = 0, bool convertL2B = true) where T : struct
        {
            // 获取结构体大小
            int size = Marshal.SizeOf<T>();

            // 因为 Marshal 与 Unsafe 的 SizeOf 方法，在结构体上有差异，所以接入原来的方法
            // 没有差异的，使用新的优化方法
            if (size != Unsafe.SizeOf<T>())
            {
                return (T)BytesToStruct(bytes, typeof(T), offset, convertL2B);
            }

            // 验证字节数组的大小是否满足结构体的要求
            if (bytes.Length < size + offset)
            {
                return default(T);
            }

            if (offset < 0)
            {
                return default(T);
            }

            // 如果需要大小端转换，进行转换
            if (convertL2B)
            {
                ConvertL2B_2(ref bytes, typeof(T), offset);
            }

            // 使用 MemoryMarshal 来避免内存分配
            Span<byte> span = bytes.AsSpan().Slice(offset, size);

            return MemoryMarshal.Read<T>(span); // 直接从字节流读取结构体
        }

        /// <summary>
        /// 执行大小端转换，C# 默认小端，需要将大端数据转为小端
        /// </summary>
        /// <param name="bytes">字节数组</param>
        /// <param name="type">结构体类型</param>
        /// <param name="offset">偏移量</param>
        public static void ConvertL2B_2(ref byte[] bytes, Type type, int offset = 0)
        {
            FieldInfo[] fields = type.GetFields().Where(f => !f.IsStatic).ToArray();

            // 遍历每个字段
            foreach (var field in fields)
            {
                int fieldSize = Marshal.SizeOf(field.FieldType);
                Span<byte> fieldSpan = bytes.AsSpan().Slice(offset, fieldSize);
                fieldSpan.Reverse();
                offset += fieldSize; // 移动偏移量到下一个字段
            }
        }

        //大小端转换.大端接收，C#默认是小端，需要转
        //根据结构体类型，将每个字段对应的byte[]进行大小端转换
        public static void ConvertL2B(ref byte[] bytes, Type type, int offset = 0)
        {
            FieldInfo[] fields = type.GetFields().Where(item => !item.Attributes.HasFlag(FieldAttributes.Static)).ToArray();

            int index = offset;
            int fieldsLen = fields.Length == 0 ? 1 : fields.Length;
            for (int i = 0; i < fieldsLen; i++)
            {
                int len = fields.Length != 0 ? Marshal.SizeOf(fields[i].FieldType) : Marshal.SizeOf(type);
                int j = len + index - 1;
                int t = index;
                for (; j >= (index + len / 2); j--)
                {
                    byte temp = bytes[t];
                    bytes[t] = bytes[j];
                    bytes[j] = temp;
                    t++;
                }

                index += len;
            }

            //foreach (var field in fields)
            //{
            //    int len = Marshal.SizeOf(field.FieldType);

            //    Array.Reverse(bytes, index, len); // 直接使用内置反转
            //    index += len;
            //}

        }

        /// <summary>
        /// 通过结构体字段名设置字段值
        /// </summary>
        /// <param name="structObj">结构体</param>
        /// <param name="name">字段名称</param>
        /// <param name="v">值</param>
        /// <returns></returns>
        public static T SetStrcutValueByName<T>(T structObj, string name, double v) where T : struct
        {
            Dictionary<string, double[]> t = GetStructValue(structObj);

            int _index = (int)(t[name][0] * 8);

            byte[] tempt = StructToBytes(structObj, false);
            byte[] valueByte = BitConverter.GetBytes(v);
            tempt[_index] = valueByte[0];
            tempt[_index + 1] = valueByte[1];
            tempt[_index + 2] = valueByte[2];
            tempt[_index + 3] = valueByte[3];
            tempt[_index + 4] = valueByte[4];
            tempt[_index + 5] = valueByte[5];
            tempt[_index + 6] = valueByte[6];
            tempt[_index + 7] = valueByte[7];

            return BytesToStruct<T>(tempt, 0, false);
        }

        /// <summary>
        /// 反射获取结构体的值
        /// </summary>
        /// <typeparam name="T">结构体，使用了<see cref="StructLayout"/>属性</typeparam>
        /// <param name="stcuctObj"></param>
        /// <returns>字典<see cref="Dictionary{TKey, TValue}"/>键：结构体值全名；值：数组[排列索引值，值]</returns>
        public static Dictionary<string, double[]> GetStructValue<T>(T stcuctObj) where T : struct
        {
            Dictionary<string, double[]> result = new Dictionary<string, double[]>();
            int index = 0;
            StringBuilder sb = new StringBuilder();
            sb.Append(stcuctObj.GetType().Name + ".");
            foreach (var item in stcuctObj.GetType().GetFields())
            {
                object obj = item.GetValue(stcuctObj);
                string itemName = item.Name + ".";
                sb.Append(itemName);
                foreach (var d in obj.GetType().GetFields())
                {
                    object objs = d.GetValue(obj);
                    string dName = d.Name + ".";
                    sb.Append(dName);
                    foreach (var t in objs.GetType().GetFields())
                    {
                        sb.Append(t.Name);
                        double[] value = new double[2] { index, (double)t.GetValue(objs) };

                        result[sb.ToString()] = value;
                        index = index + 1;

                        sb.Remove(sb.Length - t.Name.Length, t.Name.Length);
                    }
                    sb.Remove(sb.Length - dName.Length, dName.Length);
                }
                sb.Remove(sb.Length - itemName.Length, itemName.Length);
            }
            return result;
        }

    }
    public static class StructStreamReader
    {
        // 从流中异步读取数据到结构体
        public static async Task<T> ReadStructAsync<T>(Stream stream) where T : struct
        {
            // 获取结构体大小
            int structSize = Marshal.SizeOf(typeof(T));
            byte[] buffer = new byte[structSize];

            // 读取完整的结构体字节
            int bytesRead = 0;
            while (bytesRead < structSize)
            {
                int read = await stream.ReadAsync(buffer, bytesRead, structSize - bytesRead);
                if (read == 0)
                {
                    throw new EndOfStreamException("无法读取足够的数据来填充结构体");
                }
                bytesRead += read;
            }

            // 将字节数组转换为结构体
            return ByteArrayToStruct<T>(buffer);
        }

        // 字节数组转结构体
        private static T ByteArrayToStruct<T>(byte[] bytes) where T : struct
        {
            // 在非托管内存中分配结构体大小的空间
            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);

            try
            {
                // 将字节数组复制到非托管内存
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                // 将非托管内存转换为结构体
                return (T)Marshal.PtrToStructure(ptr, typeof(T));
            }
            finally
            {
                // 释放非托管内存
                Marshal.FreeHGlobal(ptr);
            }
        }
        // 结构体转字节数组
        public static byte[] StructToByteArray<T>(T structure) where T : struct
        {
            int size = Marshal.SizeOf(structure);
            byte[] byteArray = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(structure, ptr, true);
                Marshal.Copy(ptr, byteArray, 0, size);
                return byteArray;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

}
