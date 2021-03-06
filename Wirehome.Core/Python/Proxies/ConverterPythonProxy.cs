﻿#pragma warning disable IDE1006 // Naming Styles
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Global

using IronPython.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Wirehome.Core.Python.Proxies
{
    public class ConverterPythonProxy : IInjectedPythonProxy
    {
        public string ModuleName { get; } = "convert";

        public static object to_bool(object value)
        {
            if (value is bool b)
            {
                return b;
            }

            var text = to_string(value);
            if (!bool.TryParse(text, out var result))
            {
                return null;
            }

            return result;
        }

        public static object to_double(object value)
        {
            if (value is double d)
            {
                return d;
            }

            var text = to_string(value);
            if (!double.TryParse(text, out var result))
            {
                return null;
            }

            return result;
        }

        public static object to_int(object value)
        {
            if (value is int i)
            {
                return i;
            }

            var text = to_string(value);
            if (!int.TryParse(text, out var result))
            {
                return null;
            }

            return result;
        }

        public static string to_string(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is string s)
            {
                return s;
            }

            if (value is byte[] b)
            {
                return Encoding.UTF8.GetString(b);
            }

            if (value is IEnumerable<object> o)
            {
                return Encoding.UTF8.GetString(o.Select(i => Convert.ToByte(i, CultureInfo.InvariantCulture)).ToArray());
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static List to_list(object value)
        {
            if (value is IEnumerable enumerable)
            {
                PythonConvert.ToPythonList(enumerable);
            }

            return new List { PythonConvert.ToPython(value) };
        }

        public static List ulong_to_list(ulong buffer, int length)
        {
            return PythonConvert.ToPythonList(ULongToArray(buffer, length));
        }

        public static ulong list_to_ulong(List list)
        {
            if (list == null)
            {
                return 0;
            }

            var buffer = list.Select(Convert.ToByte).ToList();
            return ArrayToULong(buffer);
        }

        public static string list_to_hex_string(List list)
        {
            if (list == null)
            {
                return null;
            }

            if (list.Count == 0)
            {
                return string.Empty;
            }

            var buffer = list.Select(Convert.ToByte).ToArray();
            return BitConverter.ToString(buffer).Replace("-", string.Empty, StringComparison.Ordinal);
        }

        public static List hex_string_to_list(string hexString)
        {
            if (hexString == null)
            {
                return null;
            }

            if (hexString.Length == 0)
            {
                return new List();
            }

            if (hexString.Length % 2 != 0)
            {
                throw new ArgumentException("The hex string cannot have an odd number of digits.");
            }

            var buffer = new byte[hexString.Length / 2];
            for (var i = 0; i < buffer.Length; i++)
            {
                var byteValue = hexString.Substring(i * 2, 2);
                buffer[i] = byte.Parse(byteValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return PythonConvert.ToPythonList(buffer);
        }

        public static string list_to_bit_string(List list)
        {
            if (list == null)
            {
                return null;
            }

            var buffer = new char[list.Count * 8];
            var bufferOffset = buffer.Length - 1;

            foreach (var item in list)
            {
                var @byte = Convert.ToByte(item, CultureInfo.InvariantCulture);

                for (var i = 0; i < 8; i++)
                {
                    if ((@byte & (1 << i)) > 0)
                    {
                        buffer[bufferOffset] = '1';
                    }
                    else
                    {
                        buffer[bufferOffset] = '0';
                    }

                    bufferOffset--;
                }
            }

            return new string(buffer);
        }

        internal static byte[] ULongToArray(ulong buffer, int length)
        {
            var result = new byte[length];
            for (var i = 0; i < length; i++)
            {
                result[i] = (byte)(buffer >> (8 * i));
            }

            return result;
        }

        internal static ulong ArrayToULong(IList<byte> array)
        {
            ulong result = 0;
            for (var i = 0; i < array.Count; i++)
            {
                result |= (ulong)array[i] << (8 * i);
            }

            return result;
        }

        internal static byte[] ListToByteArray(List list)
        {
            var buffer = new byte[list.Count];
            for (var i = 0; i < list.Count; i++)
            {
                buffer[i] = Convert.ToByte(list[i]);
            }

            return buffer;
        }
    }
}