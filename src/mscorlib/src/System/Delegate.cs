// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System {

    using System;
    using System.Reflection;
    using System.Runtime;
    using System.Threading;
    using System.Runtime.Serialization;
    using System.Runtime.InteropServices;
    using System.Runtime.CompilerServices;
    using System.Runtime.Versioning;
    using System.Diagnostics.Contracts;

    [Serializable]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [System.Runtime.InteropServices.ComVisible(true)]
    public abstract class Delegate : ICloneable, ISerializable 
    {
        // _target is the object we will invoke on
        [System.Security.SecurityCritical]
        internal Object _target;

        // MethodBase, either cached after first request or assigned from a DynamicMethod
        // For open delegates to collectible types, this may be a LoaderAllocator object
        [System.Security.SecurityCritical]
        internal Object _methodBase;

        // _methodPtr is a pointer to the method we will invoke
        // It could be a small thunk if this is a static or UM call
        [System.Security.SecurityCritical]
        internal IntPtr _methodPtr;
        
        // In the case of a static method passed to a delegate, this field stores
        // whatever _methodPtr would have stored: and _methodPtr points to a
        // small thunk which removes the "this" pointer before going on
        // to _methodPtrAux.
        [System.Security.SecurityCritical]
        internal IntPtr _methodPtrAux;

        // This constructor is called from the class generated by the
        //  compiler generated code
        [System.Security.SecuritySafeCritical]  // auto-generated
        protected Delegate(Object target,String method)
        {
            if (target == null)
                throw new ArgumentNullException("target");
            
            if (method == null)
                throw new ArgumentNullException("method");
            Contract.EndContractBlock();
            
            // This API existed in v1/v1.1 and only expected to create closed
            // instance delegates. Constrain the call to BindToMethodName to
            // such and don't allow relaxed signature matching (which could make
            // the choice of target method ambiguous) for backwards
            // compatibility. The name matching was case sensitive and we
            // preserve that as well.
            if (!BindToMethodName(target, (RuntimeType)target.GetType(), method,
                                  DelegateBindingFlags.InstanceMethodOnly |
                                  DelegateBindingFlags.ClosedDelegateOnly))
                throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));
        }
            
        // This constructor is called from a class to generate a 
        // delegate based upon a static method name and the Type object
        // for the class defining the method.
        [System.Security.SecuritySafeCritical]  // auto-generated
        protected unsafe Delegate(Type target,String method)
        {
            if (target == null)
                throw new ArgumentNullException("target");

            if (target.IsGenericType && target.ContainsGenericParameters)
                throw new ArgumentException(Environment.GetResourceString("Arg_UnboundGenParam"), "target");

            if (method == null)
                throw new ArgumentNullException("method");
            Contract.EndContractBlock();

            RuntimeType rtTarget = target as RuntimeType;
            if (rtTarget == null)
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "target");

            // This API existed in v1/v1.1 and only expected to create open
            // static delegates. Constrain the call to BindToMethodName to such
            // and don't allow relaxed signature matching (which could make the
            // choice of target method ambiguous) for backwards compatibility.
            // The name matching was case insensitive (no idea why this is
            // different from the constructor above) and we preserve that as
            // well.
            BindToMethodName(null, rtTarget, method,
                             DelegateBindingFlags.StaticMethodOnly |
                             DelegateBindingFlags.OpenDelegateOnly |
                             DelegateBindingFlags.CaselessMatching);
        }
            
        // Protect the default constructor so you can't build a delegate
        private Delegate()
        {
        }

        public Object DynamicInvoke(params Object[] args)
        {
            // Theoretically we should set up a LookForMyCaller stack mark here and pass that along.
            // But to maintain backward compatibility we can't switch to calling an 
            // internal overload of DynamicInvokeImpl that takes a stack mark.
            // Fortunately the stack walker skips all the reflection invocation frames including this one.
            // So this method will never be returned by the stack walker as the caller.
            // See SystemDomain::CallersMethodCallbackWithStackMark in AppDomain.cpp.
            return DynamicInvokeImpl(args);
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        protected virtual object DynamicInvokeImpl(object[] args) 
        {
            RuntimeMethodHandleInternal method = new RuntimeMethodHandleInternal(GetInvokeMethod());
            RuntimeMethodInfo invoke = (RuntimeMethodInfo)RuntimeType.GetMethodBase((RuntimeType)this.GetType(), method);

            return invoke.UnsafeInvoke(this, BindingFlags.Default, null, args, null);
        }

            
        [System.Security.SecuritySafeCritical]  // auto-generated
        public override bool Equals(Object obj)
        {                
            if (obj == null || !InternalEqualTypes(this, obj))
                return false;      

            Delegate d = (Delegate) obj;

            // do an optimistic check first. This is hopefully cheap enough to be worth
            if (_target == d._target && _methodPtr == d._methodPtr && _methodPtrAux == d._methodPtrAux) 
                return true;

            // even though the fields were not all equals the delegates may still match
            // When target carries the delegate itself the 2 targets (delegates) may be different instances
            // but the delegates are logically the same
            // It may also happen that the method pointer was not jitted when creating one delegate and jitted in the other 
            // if that's the case the delegates may still be equals but we need to make a more complicated check

            if (_methodPtrAux.IsNull())
            {
                if (!d._methodPtrAux.IsNull())
                    return false; // different delegate kind
                // they are both closed over the first arg
                if (_target != d._target)
                    return false;
                // fall through method handle check
            }
            else
            {
                if (d._methodPtrAux.IsNull())
                    return false; // different delegate kind

                // Ignore the target as it will be the delegate instance, though it may be a different one
                /*
                if (_methodPtr != d._methodPtr)
                    return false;
                    */

                if (_methodPtrAux == d._methodPtrAux) 
                    return true;
                // fall through method handle check
            }

            // method ptrs don't match, go down long path
            // 
            if (_methodBase == null || d._methodBase == null || !(_methodBase is MethodInfo) || !(d._methodBase is MethodInfo))
                return Delegate.InternalEqualMethodHandles(this, d);
            else 
                return _methodBase.Equals(d._methodBase);

        }

        public override int GetHashCode()
        {
            //
            // this is not right in the face of a method being jitted in one delegate and not in another
            // in that case the delegate is the same and Equals will return true but GetHashCode returns a
            // different hashcode which is not true.
            /*
            if (_methodPtrAux.IsNull())
                return unchecked((int)((long)this._methodPtr));
            else
                return unchecked((int)((long)this._methodPtrAux));
            */
            return GetType().GetHashCode();
        }

        public static Delegate Combine(Delegate a, Delegate b)
        {
            if ((Object)a == null) // cast to object for a more efficient test
                return b;

            return  a.CombineImpl(b);
        }
            
        [System.Runtime.InteropServices.ComVisible(true)]
        public static Delegate Combine(params Delegate[] delegates)
        {
            if (delegates == null || delegates.Length == 0)
                return null;
                    
            Delegate d = delegates[0];
            for (int i = 1; i < delegates.Length; i++)
                d = Combine(d,delegates[i]);
            
            return d;
        }
                    
        public virtual Delegate[] GetInvocationList()
        {
            Delegate[] d = new Delegate[1];
            d[0] = this;
            return d;
        }
            
        // This routine will return the method
        public MethodInfo Method
        {                
            get 
            { 
                return GetMethodImpl();
            }
        }
            
        [System.Security.SecuritySafeCritical]  // auto-generated
        protected virtual MethodInfo GetMethodImpl()
        {
            if ((_methodBase == null) || !(_methodBase is MethodInfo))
            {
                IRuntimeMethodInfo method = FindMethodHandle();
                RuntimeType declaringType = RuntimeMethodHandle.GetDeclaringType(method);
                // need a proper declaring type instance method on a generic type
                if (RuntimeTypeHandle.IsGenericTypeDefinition(declaringType) || RuntimeTypeHandle.HasInstantiation(declaringType)) 
                {
                    bool isStatic = (RuntimeMethodHandle.GetAttributes(method) & MethodAttributes.Static) != (MethodAttributes)0;
                    if (!isStatic) 
                    {
                        if (_methodPtrAux == (IntPtr)0) 
                        {
                            // The target may be of a derived type that doesn't have visibility onto the
                            // target method. We don't want to call RuntimeType.GetMethodBase below with that
                            // or reflection can end up generating a MethodInfo where the ReflectedType cannot
                            // see the MethodInfo itself and that breaks an important invariant. But the
                            // target type could include important generic type information we need in order
                            // to work out what the exact instantiation of the method's declaring type is. So
                            // we'll walk up the inheritance chain (which will yield exactly instantiated
                            // types at each step) until we find the declaring type. Since the declaring type
                            // we get from the method is probably shared and those in the hierarchy we're
                            // walking won't be we compare using the generic type definition forms instead.
                            Type currentType = _target.GetType();
                            Type targetType = declaringType.GetGenericTypeDefinition();
                            while (currentType != null)
                            {
                                if (currentType.IsGenericType &&
                                    currentType.GetGenericTypeDefinition() == targetType)
                                {
                                    declaringType = currentType as RuntimeType;
                                    break;
                                }
                                currentType = currentType.BaseType;
                            }

                            // RCWs don't need to be "strongly-typed" in which case we don't find a base type
                            // that matches the declaring type of the method. This is fine because interop needs
                            // to work with exact methods anyway so declaringType is never shared at this point.
                            BCLDebug.Assert(currentType != null || _target.GetType().IsCOMObject, "The class hierarchy should declare the method");
                        }
                        else
                        {
                            // it's an open one, need to fetch the first arg of the instantiation
                            MethodInfo invoke = this.GetType().GetMethod("Invoke");
                            declaringType = (RuntimeType)invoke.GetParameters()[0].ParameterType;
                        }
                    }
                }
                _methodBase = (MethodInfo)RuntimeType.GetMethodBase(declaringType, method);
            }
            return (MethodInfo)_methodBase; 
        }
            
        public Object Target
        {
            get
            {
                return GetTarget();
            }
        }
    
    
        [System.Security.SecuritySafeCritical]  // auto-generated
        public static Delegate Remove(Delegate source, Delegate value)
        {
            if (source == null)
                return null;
            
            if (value == null)
                return source;
            
            if (!InternalEqualTypes(source, value))
                throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTypeMis"));
            
            return source.RemoveImpl(value);
        }

        public static Delegate RemoveAll(Delegate source, Delegate value)
        {
            Delegate newDelegate = null;

            do
            { 
                newDelegate = source;
                source = Remove(source, value);
            }
            while (newDelegate != source);

            return newDelegate;
        }
            
        protected virtual Delegate CombineImpl(Delegate d)                                                           
        {
            throw new MulticastNotSupportedException(Environment.GetResourceString("Multicast_Combine"));
        }
            
        protected virtual Delegate RemoveImpl(Delegate d)
        {
            return (d.Equals(this)) ? null : this;
        }
    
    
        public virtual Object Clone()
        {
            return MemberwiseClone();
        }
            
        // V1 API.
        public static Delegate CreateDelegate(Type type, Object target, String method)
        {
            return CreateDelegate(type, target, method, false, true);
        }
        
        // V1 API.
        public static Delegate CreateDelegate(Type type, Object target, String method, bool ignoreCase)
        {
            return CreateDelegate(type, target, method, ignoreCase, true);
        }

#if FEATURE_LEGACYNETCF
        private static void CheckGetMethodInfo_Quirk(Type type, Type target, String method, BindingFlags bindingFlags)
        {
            ParameterInfo[] parameters = type.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance).GetParameters();

            Type[] types = new Type[parameters.Length];
            for (Int32 i = 0; i < parameters.Length; i++)
                types[i] = parameters[i].ParameterType;
    
            MethodInfo mInfo = target.GetMethod(method, bindingFlags, null, types, null);
            if (mInfo == null)
                throw new MissingMethodException(target.FullName, method);
        }    
#endif
 

            
        // V1 API.
        [System.Security.SecuritySafeCritical]  // auto-generated
        public static Delegate CreateDelegate(Type type, Object target, String method, bool ignoreCase, bool throwOnBindFailure)
        {
            if (type == null)
                throw new ArgumentNullException("type");
            if (target == null)
                throw new ArgumentNullException("target");
            if (method == null)
                throw new ArgumentNullException("method");
            Contract.EndContractBlock();

            RuntimeType rtType = type as RuntimeType;
            if (rtType == null)
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "type");
            if (!rtType.IsDelegate())
                throw new ArgumentException(Environment.GetResourceString("Arg_MustBeDelegate"),"type");

#if FEATURE_LEGACYNETCF
            if (CompatibilitySwitches.IsAppEarlierThanWindowsPhone8)
                CheckGetMethodInfo_Quirk(type, target.GetType(), method,
                                         BindingFlags.NonPublic | 
                                         BindingFlags.Public | 
                                         BindingFlags.Instance |
                                         (ignoreCase?BindingFlags.IgnoreCase:0));
#endif

            Delegate d = InternalAlloc(rtType);
            // This API existed in v1/v1.1 and only expected to create closed
            // instance delegates. Constrain the call to BindToMethodName to such
            // and don't allow relaxed signature matching (which could make the
            // choice of target method ambiguous) for backwards compatibility.
            // We never generate a closed over null delegate and this is
            // actually enforced via the check on target above, but we pass
            // NeverCloseOverNull anyway just for clarity.
            if (!d.BindToMethodName(target, (RuntimeType)target.GetType(), method,
                                    DelegateBindingFlags.InstanceMethodOnly |
                                    DelegateBindingFlags.ClosedDelegateOnly |
                                    DelegateBindingFlags.NeverCloseOverNull |
                                    (ignoreCase ? DelegateBindingFlags.CaselessMatching : 0)))
            {
                if (throwOnBindFailure)
                    throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));
                d = null;
            }
            
            return d;
        }
        
        // V1 API.
        public static Delegate CreateDelegate(Type type, Type target, String method)
        {
            return CreateDelegate(type, target, method, false, true);
        }
            
        // V1 API.
        public static Delegate CreateDelegate(Type type, Type target, String method, bool ignoreCase)
        {
            return CreateDelegate(type, target, method, ignoreCase, true);
        }

        // V1 API.
        [System.Security.SecuritySafeCritical]  // auto-generated
        public static Delegate CreateDelegate(Type type, Type target, String method, bool ignoreCase, bool throwOnBindFailure)
        {
            if (type == null)
                    throw new ArgumentNullException("type");
            if (target == null)
                throw new ArgumentNullException("target");
            if (target.IsGenericType && target.ContainsGenericParameters)
                throw new ArgumentException(Environment.GetResourceString("Arg_UnboundGenParam"), "target");
            if (method == null)
                throw new ArgumentNullException("method");
            Contract.EndContractBlock();

            RuntimeType rtType = type as RuntimeType;
            RuntimeType rtTarget = target as RuntimeType;
            if (rtType == null)
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "type");
            if (rtTarget == null)
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "target");
            if (!rtType.IsDelegate())
                throw new ArgumentException(Environment.GetResourceString("Arg_MustBeDelegate"),"type");

#if FEATURE_LEGACYNETCF
            if (CompatibilitySwitches.IsAppEarlierThanWindowsPhone8)
                CheckGetMethodInfo_Quirk(type, target, method,
                                         BindingFlags.NonPublic | 
                                         BindingFlags.Public | 
                                         BindingFlags.Static | 
                                         (ignoreCase?BindingFlags.IgnoreCase:0));
#endif

            Delegate d = InternalAlloc(rtType);
            // This API existed in v1/v1.1 and only expected to create open
            // static delegates. Constrain the call to BindToMethodName to such
            // and don't allow relaxed signature matching (which could make the
            // choice of target method ambiguous) for backwards compatibility.
            if (!d.BindToMethodName(null, rtTarget, method,
                                    DelegateBindingFlags.StaticMethodOnly |
                                    DelegateBindingFlags.OpenDelegateOnly |
                                    (ignoreCase ? DelegateBindingFlags.CaselessMatching : 0)))
            {
                if (throwOnBindFailure)
                    throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));
                d = null;
            }
            
            return d;
        }
            
        // V1 API.
        [System.Security.SecuritySafeCritical]
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        public static Delegate CreateDelegate(Type type, MethodInfo method, bool throwOnBindFailure)
        {
            // Validate the parameters.
            if (type == null)
                throw new ArgumentNullException("type");
            if (method == null)
                throw new ArgumentNullException("method");
            Contract.EndContractBlock();

            RuntimeType rtType = type as RuntimeType;
            if (rtType == null)
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "type");

            RuntimeMethodInfo rmi = method as RuntimeMethodInfo;
            if (rmi == null)
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeMethodInfo"), "method");

            if (!rtType.IsDelegate())
                throw new ArgumentException(Environment.GetResourceString("Arg_MustBeDelegate"), "type");

            // This API existed in v1/v1.1 and only expected to create closed
            // instance delegates. Constrain the call to BindToMethodInfo to
            // open delegates only for backwards compatibility. But we'll allow
            // relaxed signature checking and open static delegates because
            // there's no ambiguity there (the caller would have to explicitly
            // pass us a static method or a method with a non-exact signature
            // and the only change in behavior from v1.1 there is that we won't
            // fail the call).
            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            Delegate d = CreateDelegateInternal(
                rtType,
                rmi,
                null,
                DelegateBindingFlags.OpenDelegateOnly | DelegateBindingFlags.RelaxedSignature,
                ref stackMark);

            if (d == null && throwOnBindFailure)
                throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));

            return d;
        }
        
        // V2 API.
        public static Delegate CreateDelegate(Type type, Object firstArgument, MethodInfo method)
        {
            return CreateDelegate(type, firstArgument, method, true);
        }

        // V2 API.
        [System.Security.SecuritySafeCritical]
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        public static Delegate CreateDelegate(Type type, Object firstArgument, MethodInfo method, bool throwOnBindFailure)
        {
            // Validate the parameters.
            if (type == null)
                throw new ArgumentNullException("type");
            if (method == null)
                throw new ArgumentNullException("method");
            Contract.EndContractBlock();

            RuntimeType rtType = type as RuntimeType;
            if (rtType == null)
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "type");

            RuntimeMethodInfo rmi = method as RuntimeMethodInfo;
            if (rmi == null)
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeMethodInfo"), "method");

            if (!rtType.IsDelegate())
                throw new ArgumentException(Environment.GetResourceString("Arg_MustBeDelegate"), "type");

            // This API is new in Whidbey and allows the full range of delegate
            // flexability (open or closed delegates binding to static or
            // instance methods with relaxed signature checking. The delegate
            // can also be closed over null. There's no ambiguity with all these
            // options since the caller is providing us a specific MethodInfo.
            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            Delegate d = CreateDelegateInternal(
                rtType,
                rmi,
                firstArgument,
                DelegateBindingFlags.RelaxedSignature,
               ref stackMark);

            if (d == null && throwOnBindFailure)
                throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));

            return d;
        }

        public static bool operator ==(Delegate d1, Delegate d2)
        {
            if ((Object)d1 == null)
                return (Object)d2 == null;
            
            return d1.Equals(d2);
        }
        
        public static bool operator != (Delegate d1, Delegate d2)
        {
            if ((Object)d1 == null)
                return (Object)d2 != null;
            
            return !d1.Equals(d2);
        }
    
        //
        // Implementation of ISerializable
        //

        [System.Security.SecurityCritical]
        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            throw new NotSupportedException();
        }
        //
        // internal implementation details (FCALLS and utilities)
        //

        // V2 internal API.
        // This is Critical because it skips the security check when creating the delegate.
        [System.Security.SecurityCritical]  // auto-generated
        internal unsafe static Delegate CreateDelegateNoSecurityCheck(Type type, Object target, RuntimeMethodHandle method)
        {
            // Validate the parameters.
            if (type == null)
                throw new ArgumentNullException("type");
            Contract.EndContractBlock();

            if (method.IsNullHandle())
                throw new ArgumentNullException("method");

            RuntimeType rtType = type as RuntimeType;
            if (rtType == null)
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "type");
            
            if (!rtType.IsDelegate())
                throw new ArgumentException(Environment.GetResourceString("Arg_MustBeDelegate"),"type");
            
            // Initialize the method...
            Delegate d = InternalAlloc(rtType);
            // This is a new internal API added in Whidbey. Currently it's only
            // used by the dynamic method code to generate a wrapper delegate.
            // Allow flexible binding options since the target method is
            // unambiguously provided to us.

            if (!d.BindToMethodInfo(target,
                                    method.GetMethodInfo(),
                                    RuntimeMethodHandle.GetDeclaringType(method.GetMethodInfo()),
                                    DelegateBindingFlags.RelaxedSignature | DelegateBindingFlags.SkipSecurityChecks))
                throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));
            return d;
        }

        // Caution: this method is intended for deserialization only, no security checks are performed.
        [System.Security.SecurityCritical]  // auto-generated
        internal static Delegate CreateDelegateNoSecurityCheck(RuntimeType type, Object firstArgument, MethodInfo method)
        {
            // Validate the parameters.
            if (type == null)
                throw new ArgumentNullException("type");
            if (method == null)
                throw new ArgumentNullException("method");

            Contract.EndContractBlock();

            RuntimeMethodInfo rtMethod = method as RuntimeMethodInfo;
            if (rtMethod == null)
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeMethodInfo"), "method");

            if (!type.IsDelegate())
                throw new ArgumentException(Environment.GetResourceString("Arg_MustBeDelegate"),"type");
            
            // This API is used by the formatters when deserializing a delegate.
            // They pass us the specific target method (that was already the
            // target in a valid delegate) so we should bind with the most
            // relaxed rules available (the result will never be ambiguous, it
            // just increases the chance of success with minor (compatible)
            // signature changes). We explicitly skip security checks here --
            // we're not really constructing a delegate, we're cloning an
            // existing instance which already passed its checks.
            Delegate d = UnsafeCreateDelegate(type, rtMethod, firstArgument,
                                              DelegateBindingFlags.SkipSecurityChecks |
                                              DelegateBindingFlags.RelaxedSignature);

            if (d == null)
                throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));

            return d;
        }

        // V1 API.
        public static Delegate CreateDelegate(Type type, MethodInfo method)
        {
            return CreateDelegate(type, method, true);
        }

        [System.Security.SecuritySafeCritical]
        internal static Delegate CreateDelegateInternal(RuntimeType rtType, RuntimeMethodInfo rtMethod, Object firstArgument, DelegateBindingFlags flags, ref StackCrawlMark stackMark)
        {
            Contract.Assert((flags & DelegateBindingFlags.SkipSecurityChecks) == 0);

#if FEATURE_APPX
            bool nonW8PMethod = (rtMethod.InvocationFlags & INVOCATION_FLAGS.INVOCATION_FLAGS_NON_W8P_FX_API) != 0;
            bool nonW8PType = (rtType.InvocationFlags & INVOCATION_FLAGS.INVOCATION_FLAGS_NON_W8P_FX_API) != 0;
            if (nonW8PMethod || nonW8PType)
            {
                RuntimeAssembly caller = RuntimeAssembly.GetExecutingAssembly(ref stackMark);
                if (caller != null && !caller.IsSafeForReflection())
                    throw new InvalidOperationException(
                        Environment.GetResourceString("InvalidOperation_APIInvalidForCurrentContext",
                                                      nonW8PMethod ? rtMethod.FullName : rtType.FullName));
            }
#endif

            return UnsafeCreateDelegate(rtType, rtMethod, firstArgument, flags);
        }

        [System.Security.SecurityCritical]
        internal static Delegate UnsafeCreateDelegate(RuntimeType rtType, RuntimeMethodInfo rtMethod, Object firstArgument, DelegateBindingFlags flags)
        {

#if FEATURE_LEGACYNETCF
            if (CompatibilitySwitches.IsAppEarlierThanWindowsPhone8)
                if(rtMethod.IsGenericMethodDefinition)
                    throw new MissingMethodException(rtMethod.DeclaringType.FullName, rtMethod.Name);
#endif

            Delegate d = InternalAlloc(rtType);

            if (d.BindToMethodInfo(firstArgument, rtMethod, rtMethod.GetDeclaringTypeInternal(), flags))
                return d;
            else
                return null;
        }

        //
        // internal implementation details (FCALLS and utilities)
        //

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern bool BindToMethodName(Object target, RuntimeType methodType, String method, DelegateBindingFlags flags);
            
        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern bool BindToMethodInfo(Object target, IRuntimeMethodInfo method, RuntimeType methodType, DelegateBindingFlags flags);
            
        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern static MulticastDelegate InternalAlloc(RuntimeType type);
            
        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern static MulticastDelegate InternalAllocLike(Delegate d);

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern static bool InternalEqualTypes(object a, object b);

        // Used by the ctor. Do not call directly.
        // The name of this function will appear in managed stacktraces as delegate constructor.
        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern void DelegateConstruct(Object target, IntPtr slot);

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern IntPtr GetMulticastInvoke();

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern IntPtr GetInvokeMethod();

        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern IRuntimeMethodInfo FindMethodHandle();

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern static bool InternalEqualMethodHandles(Delegate left, Delegate right);

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern IntPtr AdjustTarget(Object target, IntPtr methodPtr);

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern IntPtr GetCallStub(IntPtr methodPtr);

        [System.Security.SecuritySafeCritical]
        internal virtual Object GetTarget()
        {
            return (_methodPtrAux.IsNull()) ? _target : null;
        }

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern static bool CompareUnmanagedFunctionPtrs (Delegate d1, Delegate d2);
    }

    // These flags effect the way BindToMethodInfo and BindToMethodName are allowed to bind a delegate to a target method. Their
    // values must be kept in sync with the definition in vm\comdelegate.h.
    internal enum DelegateBindingFlags
    {
        StaticMethodOnly    =   0x00000001, // Can only bind to static target methods
        InstanceMethodOnly  =   0x00000002, // Can only bind to instance (including virtual) methods
        OpenDelegateOnly    =   0x00000004, // Only allow the creation of delegates open over the 1st argument
        ClosedDelegateOnly  =   0x00000008, // Only allow the creation of delegates closed over the 1st argument
        NeverCloseOverNull  =   0x00000010, // A null target will never been considered as a possible null 1st argument
        CaselessMatching    =   0x00000020, // Use case insensitive lookup for methods matched by name
        SkipSecurityChecks  =   0x00000040, // Skip security checks (visibility, link demand etc.)
        RelaxedSignature    =   0x00000080, // Allow relaxed signature matching (co/contra variance)
    }
}
