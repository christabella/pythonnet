using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Python.Runtime
{
    internal static class OperatorMethod
    {
        /// <summary>
        /// Maps the compiled method name in .NET CIL (e.g. op_Addition) to
        /// the equivalent Python operator (e.g. __add__) as well as the offset
        /// that identifies that operator's slot (e.g. nb_add) in heap space.
        /// </summary>
        public static Dictionary<string, SlotDefinition> OpMethodMap { get; private set; }
        public readonly struct SlotDefinition
        {
            public SlotDefinition(string methodName, int typeOffset)
            {
                MethodName = methodName;
                TypeOffset = typeOffset;
            }
            public string MethodName { get; }
            public int TypeOffset { get; }
        }
        private static PyObject _opType;

        static OperatorMethod()
        {
            // .NET operator method names are documented at:
            // https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/operator-overloads
            // Python operator methods and slots are documented at:
            // https://docs.python.org/3/c-api/typeobj.html
            // TODO: Rich compare, inplace operator support
            OpMethodMap = new Dictionary<string, SlotDefinition>
            {
                ["op_Addition"] = new SlotDefinition("__add__", TypeOffset.nb_add),
                ["op_Subtraction"] = new SlotDefinition("__sub__", TypeOffset.nb_subtract),
                ["op_Multiply"] = new SlotDefinition("__mul__", TypeOffset.nb_multiply),
                ["op_Division"] = new SlotDefinition("__truediv__", TypeOffset.nb_true_divide),
                ["op_BitwiseAnd"] = new SlotDefinition("__and__", TypeOffset.nb_and),
                ["op_BitwiseOr"] = new SlotDefinition("__or__", TypeOffset.nb_or),
                ["op_ExclusiveOr"] = new SlotDefinition("__xor__", TypeOffset.nb_xor),
                ["op_LeftShift"] = new SlotDefinition("__lshift__", TypeOffset.nb_lshift),
                ["op_RightShift"] = new SlotDefinition("__rshift__", TypeOffset.nb_rshift),
                ["op_Modulus"] = new SlotDefinition("__mod__", TypeOffset.nb_remainder),
                ["op_OneComplement"] = new SlotDefinition("__invert__", TypeOffset.nb_invert)
            };
        }

        public static void Initialize()
        {
            _opType = GetOperatorType();
        }

        public static void Shutdown()
        {
            if (_opType != null)
            {
                _opType.Dispose();
                _opType = null;
            }
        }

        public static bool IsOperatorMethod(MethodBase method)
        {
            if (!method.IsSpecialName)
            {
                return false;
            }
            return OpMethodMap.ContainsKey(method.Name);
        }
        /// <summary>
        /// For the operator methods of a CLR type, set the special slots of the
        /// corresponding Python type's operator methods.
        /// </summary>
        /// <param name="pyType"></param>
        /// <param name="clrType"></param>
        public static void FixupSlots(IntPtr pyType, Type clrType)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            Debug.Assert(_opType != null);
            foreach (var method in clrType.GetMethods(flags))
            {
                if (!IsOperatorMethod(method))
                {
                    continue;
                }
                int offset = OpMethodMap[method.Name].TypeOffset;
                // Copy the default implementation of e.g. the nb_add slot,
                // which simply calls __add__ on the type.
                IntPtr func = Marshal.ReadIntPtr(_opType.Handle, offset);
                // Write the slot definition of the target Python type, so
                // that we can later modify __add___ and it will be called
                // when used with a Python operator.
                // https://tenthousandmeters.com/blog/python-behind-the-scenes-6-how-python-object-system-works/
                Marshal.WriteIntPtr(pyType, offset, func);

            }
        }

        public static string GetPyMethodName(string clrName)
        {
            return OpMethodMap[clrName].MethodName;
        }

        private static string GenerateDummyCode()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("class OperatorMethod(object):");
            foreach (var item in OpMethodMap.Values)
            {
                string def = string.Format("    def {0}(self, other): pass", item.MethodName);
                sb.AppendLine(def);
            }
            return sb.ToString();
        }

        private static PyObject GetOperatorType()
        {
            using (PyDict locals = new PyDict())
            {
                // A hack way for getting typeobject.c::slotdefs
                string code = GenerateDummyCode();
                // The resulting OperatorMethod class is stored in a PyDict.
                PythonEngine.Exec(code, null, locals.Handle);
                // Return the class itself, which is a type.
                return locals.GetItem("OperatorMethod");
            }
        }

        public static string ReversePyMethodName(string pyName)
        {
            return pyName.Insert(2, "r");
        }

        /// <summary>
        /// Check if the method is performing a forward or reverse operation.
        /// </summary>
        /// <param name="method">The operator method.</param>
        /// <returns></returns>
        public static bool IsForward(MethodInfo method)
        {
            Type declaringType = method.DeclaringType;
            Type leftOperandType = method.GetParameters()[0].ParameterType;
            return leftOperandType == declaringType;
        }

        public static void FilterMethods(MethodInfo[] methods, out MethodInfo[] forwardMethods, out MethodInfo[] reverseMethods)
        {
            List<MethodInfo> forwardMethodsList = new List<MethodInfo>();
            List<MethodInfo> reverseMethodsList = new List<MethodInfo>();
            foreach (var method in methods)
            {
                if (IsForward(method))
                {
                    forwardMethodsList.Add(method);
                } else
                {
                    reverseMethodsList.Add(method);
                }

            }
            forwardMethods = forwardMethodsList.ToArray();
            reverseMethods = reverseMethodsList.ToArray();
        }
    }
}
