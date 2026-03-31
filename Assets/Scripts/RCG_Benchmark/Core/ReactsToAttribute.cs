using System;

namespace RCG.Core
{
    /// <summary>
    /// Declares that the decorated method should be called by the RCGResolver
    /// whenever the specified Observable field on a referenced component changes value.
    ///
    /// Usage:
    ///   [ReactsTo("_health", "Current")]
    ///   void OnHealthChanged(float value) { ... }
    ///
    /// Parameters:
    ///   sourceFieldName      — name of a private/serialized field on THIS component
    ///                          that holds a reference to the upstream component.
    ///   observableFieldName  — name of the Observable[T] field on that upstream component.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class ReactsToAttribute : Attribute
    {
        public readonly string SourceFieldName;
        public readonly string ObservableFieldName;

        public ReactsToAttribute(string sourceFieldName, string observableFieldName)
        {
            SourceFieldName      = sourceFieldName;
            ObservableFieldName  = observableFieldName;
        }
    }
}
