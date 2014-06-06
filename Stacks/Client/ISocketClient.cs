﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;

namespace Stacks
{
    public interface ISocketClient
    {
        IExecutor Executor { get; }
        bool IsConnected { get; }

        IObservable<Unit> Connected { get; }
        IObservable<Exception> Disconnected { get; }

        IObservable<Unit> Connect(IPEndPoint remoteEndPoint);
        void Close();
    }
}
