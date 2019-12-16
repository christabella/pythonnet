using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Python.Runtime.Native
{
    /// <summary>
    /// Abstract class defining boiler plate methods that
    /// Custom Marshalers will use.
    /// </summary>
    internal abstract class MarshalerBase : ICustomMarshaler
    {
        #if UCS2 && PYTHON2
        internal static Encoding PyEncoding = Encoding.Unicode;
        internal static int UCS = 2;
        #else
        internal static Encoding PyEncoding = Encoding.UTF32;
        internal static int UCS = 4;
        #endif

        public object MarshalNativeToManaged(IntPtr pNativeData)
        {
            throw new NotImplementedException();
        }

        public abstract IntPtr MarshalManagedToNative(object managedObj);

        public void CleanUpNativeData(IntPtr pNativeData)
        {
            Marshal.FreeHGlobal(pNativeData);
        }

        public void CleanUpManagedData(object managedObj)
        {
            // Let GC deal with it
        }

        public int GetNativeDataSize()
        {
            return IntPtr.Size;
        }
    }


    /// <summary>
    /// Custom Marshaler to deal with Managed String to Native
    /// conversion differences on UCS2/UCS4.
    /// </summary>
    internal class UcsMarshaler : MarshalerBase
    {
        private static readonly MarshalerBase Instance = new UcsMarshaler();

        public override IntPtr MarshalManagedToNative(object managedObj)
        {
            var s = managedObj as string;

            if (s == null)
            {
                return IntPtr.Zero;
            }

            byte[] bStr = PyEncoding.GetBytes(s + "\0");
            IntPtr mem = Marshal.AllocHGlobal(bStr.Length);
            try
            {
                Marshal.Copy(bStr, 0, mem, bStr.Length);
            }
            catch (Exception)
            {
                Marshal.FreeHGlobal(mem);
                throw;
            }

            return mem;
        }

        public static ICustomMarshaler GetInstance(string cookie)
        {
            return Instance;
        }

        public static string PtrToStringUni(IntPtr p)
        {
            if (p == IntPtr.Zero)
            {
                return null;
            }

            int size = GetUnicodeByteLength(p);
            var buffer = new byte[size];
            Marshal.Copy(p, buffer, 0, size);
            return PyEncoding.GetString(buffer, 0, size);
        }

        public static int GetUnicodeByteLength(IntPtr p)
        {
            var len = 0;
            while (true)
            {
#if UCS2 && PYTHON2
                int c = Marshal.ReadInt16(p, len * 2);
#else
                int c = Marshal.ReadInt32(p, len * 4);
#endif

                if (c == 0)
                {
                    return len * UCS;
                }
                checked
                {
                    ++len;
                }
            }
        }

        /// <summary>
        /// Utility function for Marshaling Unicode on PY3 and AnsiStr on PY2.
        /// Use on functions whose Input signatures changed between PY2/PY3.
        /// Ex. Py_SetPythonHome
        /// </summary>
        /// <param name="s">Managed String</param>
        /// <returns>
        /// Ptr to Native String ANSI(PY2)/Unicode(PY3/UCS2)/UTF32(PY3/UCS4.
        /// </returns>
        /// <remarks>
        /// You MUST deallocate the IntPtr of the Return when done with it.
        /// </remarks>
        public static IntPtr Py3UnicodePy2StringtoPtr(string s)
        {
#if PYTHON2
            return Marshal.StringToHGlobalAnsi(s);
#else
            return Instance.MarshalManagedToNative(s);
#endif
        }

        /// <summary>
        /// Utility function for Marshaling Unicode IntPtr on PY3 and
        /// AnsiStr IntPtr on PY2 to Managed Strings. Use on Python functions
        /// whose return type changed between PY2/PY3.
        /// Ex. Py_GetPythonHome
        /// </summary>
        /// <param name="p">Native Ansi/Unicode/UTF32 String</param>
        /// <returns>
        /// Managed String
        /// </returns>
        public static string PtrToPy3UnicodePy2String(IntPtr p)
        {
#if PYTHON2
            return Marshal.PtrToStringAnsi(p);
#else
            return PtrToStringUni(p);
#endif
        }
    }


    /// <summary>
    /// Custom Marshaler to deal with Managed String Arrays to Native
    /// conversion differences on UCS2/UCS4.
    /// </summary>
    internal class StrArrayMarshaler : MarshalerBase
    {
        private static readonly MarshalerBase Instance = new StrArrayMarshaler();

        public override IntPtr MarshalManagedToNative(object managedObj)
        {
            var argv = managedObj as string[];

            if (argv == null)
            {
                return IntPtr.Zero;
            }

            int totalStrLength = argv.Sum(arg => arg.Length + 1);
            int memSize = argv.Length * IntPtr.Size + totalStrLength * UCS;

            IntPtr mem = Marshal.AllocHGlobal(memSize);
            try
            {
                // Preparing array of pointers to strings
                IntPtr curStrPtr = mem + argv.Length * IntPtr.Size;
                for (var i = 0; i < argv.Length; i++)
                {
                    byte[] bStr = PyEncoding.GetBytes(argv[i] + "\0");
                    Marshal.Copy(bStr, 0, curStrPtr, bStr.Length);
                    Marshal.WriteIntPtr(mem + i * IntPtr.Size, curStrPtr);
                    curStrPtr += bStr.Length;
                }
            }
            catch (Exception)
            {
                Marshal.FreeHGlobal(mem);
                throw;
            }

            return mem;
        }

        public static ICustomMarshaler GetInstance(string cookie)
        {
            return Instance;
        }
    }


    /// <summary>
    /// Custom Marshaler to deal with Managed String to Native
    /// conversion on UTF-8. Use on functions that expect UTF-8 encoded
    /// strings like `PyUnicode_FromStringAndSize`
    /// </summary>
    /// <remarks>
    /// If instead we used `MarshalAs(UnmanagedType.LPWStr)` the output to
    /// `foo` would be `f\x00o\x00o\x00`.
    /// </remarks>
    internal class Utf8Marshaler : MarshalerBase
    {
        private static readonly MarshalerBase Instance = new Utf8Marshaler();
        private static readonly Encoding PyEncoding = Encoding.UTF8;

        public override IntPtr MarshalManagedToNative(object managedObj)
        {
            var s = managedObj as string;

            if (s == null)
            {
                return IntPtr.Zero;
            }

            byte[] bStr = PyEncoding.GetBytes(s + "\0");
            IntPtr mem = Marshal.AllocHGlobal(bStr.Length);
            try
            {
                Marshal.Copy(bStr, 0, mem, bStr.Length);
            }
            catch (Exception)
            {
                Marshal.FreeHGlobal(mem);
                throw;
            }

            return mem;
        }

        public static ICustomMarshaler GetInstance(string cookie)
        {
            return Instance;
        }
    }
}