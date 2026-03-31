using System;
using System.Reflection;
using UnityEngine;

namespace RCG.Core
{
    /// <summary>
    /// Base class for all System D components.
    /// In Start(), scans all methods decorated with [ReactsTo], resolves the referenced
    /// Observable[T] field via reflection, and registers a pre-compiled delegate on it.
    ///
    /// After Start() completes, zero reflection occurs per frame.
    /// All [ReactsTo] methods are invoked via pre-compiled Action[T] delegates stored
    /// inside Observable[T]._dependents.
    /// </summary>
    public abstract class RCGBehaviour : MonoBehaviour
    {
        protected virtual void Start()
        {
            WireDependencies();
        }

        private void WireDependencies()
        {
            Type selfType = GetType();
            MethodInfo[] methods = selfType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (MethodInfo method in methods)
            {
                var attrs = method.GetCustomAttributes(typeof(ReactsToAttribute), false);
                foreach (ReactsToAttribute attr in attrs)
                {
                    TryWire(selfType, method, attr);
                }
            }
        }

        private void TryWire(Type selfType, MethodInfo method, ReactsToAttribute attr)
        {
            // 1. Resolve the source component field on this MonoBehaviour.
            FieldInfo sourceField = selfType.GetField(
                attr.SourceFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (sourceField == null)
            {
                Debug.LogError($"[RCG] [{selfType.Name}] Source field '{attr.SourceFieldName}' not found.");
                return;
            }

            object sourceComponent = sourceField.GetValue(this);
            if (sourceComponent == null)
            {
                Debug.LogError($"[RCG] [{selfType.Name}] Source field '{attr.SourceFieldName}' is null. " +
                               "Call Initialize() before Start().");
                return;
            }

            // 2. Resolve the Observable<T> field on the source component.
            Type sourceType = sourceComponent.GetType();
            FieldInfo observableField = sourceType.GetField(
                attr.ObservableFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (observableField == null)
            {
                Debug.LogError($"[RCG] [{selfType.Name}] Observable field '{attr.ObservableFieldName}' " +
                               $"not found on {sourceType.Name}.");
                return;
            }

            object observableObj = observableField.GetValue(sourceComponent);
            if (observableObj == null)
            {
                Debug.LogError($"[RCG] [{selfType.Name}] Observable '{attr.ObservableFieldName}' is null.");
                return;
            }

            // 3. Validate the method signature: exactly one parameter whose type matches Observable<T>.
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                Debug.LogError($"[RCG] [{selfType.Name}] Method '{method.Name}' must have exactly 1 parameter.");
                return;
            }
            Type paramType = parameters[0].ParameterType;

            // 4. Create a pre-compiled Action<T> delegate and call RegisterDependent on the Observable.
            try
            {
                Type actionType          = typeof(Action<>).MakeGenericType(paramType);
                Delegate action          = Delegate.CreateDelegate(actionType, this, method);
                MethodInfo registerMethod = observableObj.GetType().GetMethod(
                    "RegisterDependent",
                    BindingFlags.Instance | BindingFlags.Public);

                if (registerMethod == null)
                {
                    Debug.LogError($"[RCG] RegisterDependent not found on {observableObj.GetType().Name}.");
                    return;
                }

                registerMethod.Invoke(observableObj, new object[] { action });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RCG] Failed to wire '{method.Name}' on {selfType.Name}: {ex.Message}");
            }
        }
    }
}
