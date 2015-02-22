﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
// ReSharper disable InconsistentNaming

namespace Stacks.Actors
{
    public class ActorSystem
    {
        public static ActorSystem Default { get; private set; }

        static ActorSystem()
        {
            Default = new ActorSystem("Default");
        }

        public string Name { get; private set; }

        private readonly ConcurrentDictionary<string, IActor> registeredActors;

        internal ActorSystem(string name)
        {
            Name = name;
            registeredActors = new ConcurrentDictionary<string, IActor>();
        }

        private Type GuessActorInterfaceType<T>()
        {
            var t = typeof(T);
            var implementedInterfaces = typeof(T).GetInterfaces();

            var matchedInterface = implementedInterfaces.FirstOrDefault(
                ii => ii.Name.Equals(t.Name, StringComparison.InvariantCultureIgnoreCase) ||
                      ("I" + ii.Name).Equals(t.Name, StringComparison.InvariantCultureIgnoreCase));

            if (matchedInterface != null)
                return matchedInterface;

            throw new Exception(string.Format("When creating an actor and providing only implementation type, " +
                "this type must implement a contract interface. Interface name must follow one of conventions: \r\n" +
                " - It must have the same name as implementation\r\n" +
                " - It must begin with \"I\" followed by implementation name"));
        }

        public IActor CreateActor<T>(Func<T> implementationProvider, string name = null)
            where T : class
        {
            var interfaceType = GuessActorInterfaceType<T>();
            return (IActor)CreateActor(interfaceType, implementationProvider, name);
        }

        public IActor CreateActor<T>(string name = null)
            where T : class, new()
        {
            var interfaceType = GuessActorInterfaceType<T>();
            return (IActor)CreateActor(interfaceType, () => new T(), name);
        }


        /// <summary>
        /// Creates a new actor, using I as an interface and TImpl as an implementation.
        /// I must be an interface. TImpl must implement this interface. 
        /// TImpl is an actual implementation that will be created, therefore it must support empty constructor.
        /// Name is optional, however, empty or null name will result in not registereing created actor in the actor system.
        /// </summary>
        /// <typeparam name="TImpl">Actor implementation type.</typeparam>
        /// <typeparam name="I">Actor interface.</typeparam>
        /// <param name="name">Optional name. Only named actors are registered to the system.</param>
        /// <returns></returns>
        public I CreateActor<TImpl, I>(string name = null)
            where TImpl : class, I, new()
        {
            return (I)CreateActor(typeof(I), () => new TImpl(), name);
        }

        /// <summary>
        /// Creates a new actor, using I as an interface. implementation passed as a parameter is used as an implementation.
        /// I must be an interface. TImpl must implement this interface. 
        /// TImpl is an actual implementation that will be created, therefore it must support empty constructor.
        /// Name is optional, however, empty or null name will result in not registereing created actor in the actor system.
        /// </summary>
        /// <typeparam name="TImpl">Actor implementation type.</typeparam>
        /// <typeparam name="I">Actor interface</typeparam>
        /// <param name="implementationProvider">Actual implementation of an actor. Must inherit from Actor class.</param>
        /// <param name="name">Optional name. Only named actors are registered to the system.</param>
        /// <returns></returns>
        public I CreateActor<TImpl, I>(Func<TImpl> implementationProvider, string name = null)
            where TImpl: class, I
        {
            return (I)CreateActor(typeof (I), implementationProvider, name);
        }

        private object CreateActor<T>(Type interfaceType, Func<T> implementationProvider, string name = null)
        {
            Ensure.IsNotNull(implementationProvider, "implementationProvider");
            EnsureInheritsActor<T>();

            var actorImplementation = ResolveImplementationProvider(implementationProvider);

            var implementationAsActor = CastImplementationToActor(actorImplementation);
            RegisterActorToSystem(implementationAsActor, name);

            return actorImplementation;
        }

        private TImpl ResolveImplementationProvider<TImpl>(Func<TImpl> implementationProvider)
        {
            try
            {
                ActorCtorGuardian.SetGuard();
                return implementationProvider();
            }
            finally
            {
                ActorCtorGuardian.ClearGuard();
            }
        }

        private Actor CastImplementationToActor<TImpl>(TImpl actorImplementation)
        {
            var implementationAsActor = actorImplementation as Actor;

            if (implementationAsActor == null)
                throw new Exception("Created implementation could not be casted to Stacks.Actors.Actor type.");

            return implementationAsActor;
        }

        private void RegisterActorToSystem(Actor actorImplementation, string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            if (!registeredActors.TryAdd(name, actorImplementation))
            {
                throw new Exception(string.Format("Tried to create actor named {0} inside system {1}. Actor with such name is already added",
                    name, this.Name));
            }
        }

        private static void EnsureInheritsActor<TImpl>()
        {
            if (!typeof(Actor).IsAssignableFrom(typeof(TImpl)))
                throw new ArgumentException(
                    string.Format(
                        "Implementation type (TImpl) is of type {0} which does not inherits from Stacks.Actors.Actor.",
                        typeof(TImpl).FullName));
        }
    }
}
