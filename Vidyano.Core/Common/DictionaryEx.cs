﻿using System;
using System.Collections.Generic;

namespace Vidyano.Common
{
    public static class DictionaryEx
    {
        public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> source, TKey key, Func<TKey, TValue> valueSelector)
        {
            lock (source)
            {
                if (!source.TryGetValue(key, out var value))
                    source.Add(key, value = valueSelector(key));

                return value;
            }
        }
    }
}