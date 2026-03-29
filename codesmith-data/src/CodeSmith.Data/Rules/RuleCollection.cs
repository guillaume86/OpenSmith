using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace CodeSmith.Data.Rules
{
    /// <summary>
    /// A collection of rules.
    /// </summary>
    public class RuleCollection : ConcurrentDictionary<Type, RuleList>
    { }
}