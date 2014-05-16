﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stacks
{
    public class MessageTypeCodeCache : IMessageTypeCodeCache
    {
        private Dictionary<Type, int> typeCodeByType;

        ReaderWriterLockSlim rwLock;

        public MessageTypeCodeCache()
        {
            typeCodeByType = new Dictionary<Type, int>();

            rwLock = new ReaderWriterLockSlim();

        }

        public void PreLoadTypesFromAssemblyOfType<T>()
        {
            var codeByTypeLocal = new Dictionary<Type, int>();

            foreach (var t in typeof(T).Assembly.GetTypes()
                                       .Where(t => !t.IsInterface)
                                       .Where(t => !t.IsAbstract))
            {
                var attr = t.GetCustomAttribute<StacksMessageAttribute>();

                if (attr != null)
                {
                    codeByTypeLocal[t] = attr.TypeCode;
                }
            }
                              
            try
            {
                rwLock.EnterWriteLock();

                foreach (var kv in codeByTypeLocal)
                {
                    typeCodeByType[kv.Key] = kv.Value;
                }
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        public int GetTypeCode<T>()
        {
            return GetTypeCode(typeof(T));
        }

        public int GetTypeCode(Type t)
        {
            try
            {
                rwLock.EnterUpgradeableReadLock();

                int typeCode;
                if (typeCodeByType.TryGetValue(t, out typeCode))
                {
                    return typeCode;
                }
                else
                {
                    var attribute = t.GetCustomAttribute<StacksMessageAttribute>();

                    if (attribute == null)
                    {
                        throw new InvalidDataException(string.Format("Cannot resolve type id for type {0}. " +
                        "It has no {1} attribute and it wasn't declared imperatively",
                            t.Name, typeof(StacksMessageAttribute).Name));
                    }

                    try
                    {
                        rwLock.EnterWriteLock();

                        typeCodeByType[t] = attribute.TypeCode;

                        return attribute.TypeCode;
                    }
                    finally { rwLock.ExitWriteLock(); }
                }
            }
            finally
            {
                rwLock.ExitUpgradeableReadLock();
            }
        }

        public void PreLoadType<T>()
        {
            PreLoadType(typeof(T));
        }

        public void PreLoadType(Type t)
        {
            var attr = t.GetCustomAttribute<StacksMessageAttribute>();

            if (attr != null)
            {
                try
                {
                    rwLock.EnterWriteLock();

                    typeCodeByType[t] = attr.TypeCode;
                }
                finally
                {
                    rwLock.ExitWriteLock();
                }
            }
        }
    }
}
