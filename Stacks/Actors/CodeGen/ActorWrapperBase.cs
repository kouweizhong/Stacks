﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stacks.Actors.CodeGen
{
    public class ActorWrapperBase : IActor
    {
        protected readonly Actor actorImplementation;
        public string Name => actorImplementation.Name;
        public IActor Parent => actorImplementation.Parent;
        public IEnumerable<IActor> Children => actorImplementation.Children;
        public Task Stop(bool stopImmediately = false) => actorImplementation.Stop(stopImmediately);

        public ActorWrapperBase(Actor actorImplementation)
        {
            this.actorImplementation = actorImplementation;
        }

        protected Task<T> HandleException<T>(string methodName, Task<T> task)
        {
            return task.ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    actorImplementation.ErrorOccuredInMethod(methodName, t.Exception);
                    throw t.Exception.InnerException;
                }
                return t.Result;
            });
        }

        protected Task HandleException(string methodName, Task task)
        {
            return task.ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    actorImplementation.ErrorOccuredInMethod(methodName, t.Exception);
                    throw t.Exception.InnerException;
                }
            });
        }

        internal Actor ActorImplementation => actorImplementation;
    }
}
