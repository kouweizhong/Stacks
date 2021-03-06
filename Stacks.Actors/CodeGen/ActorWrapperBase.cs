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
        public string Path => actorImplementation.Path;
        public IActor Parent => actorImplementation.Parent;
        public IEnumerable<IActor> Children => actorImplementation.Children;
        public IObservable<Exception> ExceptionThrown => actorImplementation.ExceptionThrown; 
        public Task Stop() => actorImplementation.Stop();
        public bool Stopped => actorImplementation.Stopped;


        public ActorWrapperBase(Actor actorImplementation)
        {
            this.actorImplementation = actorImplementation;
        }

        protected void StopActorAndNotifySystem(string methodName, Exception exception)
        {
            // actorImplementation.StopBecauseOfError(methodName, exception);
            actorImplementation.OnExceptionThrown(exception);
        }

        protected Task<T> HandleException<T>(string methodName, Task<T> task)
        {
            return task.ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    StopActorAndNotifySystem(methodName, t.Exception.InnerException);
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
                    StopActorAndNotifySystem(methodName, t.Exception.InnerException);
                    throw t.Exception.InnerException;
                }
            });
        }

        internal Actor ActorImplementation => actorImplementation;
    }
}
