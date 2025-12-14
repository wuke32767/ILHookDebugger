using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger
{
    internal unsafe struct NotTooLazy<U, T>(Func<U, T> create, U obj)
    {
        T value = default!;
        bool created;
        internal T Value
        {
            get
            {
                if (!created)
                {
                    value = create(obj);
                    created = true;
                }
                return value;
            }
        }
    }
}
