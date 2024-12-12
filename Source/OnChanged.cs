using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Celeste.Mod.ILHookDebugger
{
    static class _Wrapper
    {
        public static Func<T, T> Tap<T>(Action<T> func) => o =>
            {
                func(o);
                return o;
            };
        public static Func<T> With<T>(Action func, T val) => () =>
            {
                func();
                return val;
            };
    }
    public struct OnChanged<T>(Func<T?, T?> OnChange, T? init = default) where T : IEquatable<T>
    {

        private T? val = init;

        public OnChanged(Action<T?> OnChange, T? init = default) : this(_Wrapper.Tap(OnChange), init)
        {
        }

        public T? Value
        {
            readonly get => val;
            set
            {
                if ((val is null && value is not null) || (val is not null && !val.Equals(value)))
                {
                    val = OnChange(value);
                }
                else
                {
                    val = value;
                }
            }
        }
        public static implicit operator T?(OnChanged<T> d) => d.Value;

    }
    public struct Swapping
    {
        OnChanged<bool> Base;
        public static implicit operator bool(Swapping d) => d.Value;
        public bool Value { readonly get => Base; set => Base.Value = value; }

        public Swapping(Func<bool, bool> OnChange, bool init = false)
        {
            Base = new(OnChange, init);
        }

        public Swapping(Action<bool> OnChange, bool init = false)
        {
            Base = new(OnChange, init);
        }

        public Swapping(Func<bool> ToTrue, Func<bool> ToFalse, bool init = false)
        {
            Base = new(o => o switch
            {
                true => ToTrue(),
                false => ToFalse(),
            }, init);
        }

        public Swapping(Action ToTrue, Action ToFalse, bool init = false)
            : this(_Wrapper.With(ToTrue, true), _Wrapper.With(ToFalse, false), init)
        {
        }
    }
}
