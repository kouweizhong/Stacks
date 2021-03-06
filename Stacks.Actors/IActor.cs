﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stacks.Actors
{
    public interface IActor
    {
        bool Stopped { get; }
        string Name { get; }
        string Path { get; }
        IActor Parent { get; }
        IEnumerable<IActor> Children { get; }

        Task Stop();

        IObservable<Exception> ExceptionThrown { get; } 
    }
}
