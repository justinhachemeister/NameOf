NameOf
======

NuGet package available at https://www.nuget.org/packages/NameOf.Fody/

Provides strongly typed access to a compile-time string representing the name of a variable, field, property, method, event, enum value, or type.

Other approaches require reflection or traversing the expression tree of a lamdba, each with hits at run-time and less-than-ideal syntax.

This project provides a series of `Name.Of...` methods to support the cleanest syntax C# currently allows, and with no overhead at run-time. Each instance of the `Name.Of...` methods you use in your code gets removed and replaced at build time with the intended string. This is done using a technique which is more widely referred to as IL weaving (for more info, check out [Fody](https://github.com/Fody/Fody) and [Cecil](http://www.mono-project.com/Cecil)).

Reference to the `Name.Of...` dummy methods and assembly is removed. Any anonymous methods and fields generated by lambda expressions in some calls to `Name.Of...` are removed from the assembly as well.

## Usage

#### General purpose

    String localVariableName = Name.Of(localVariable); // yields "localVariable"
    String propertyName = Name.Of(instanceClass.Property); // yields "Property"
    String methodName = Name.Of(StaticClass.SomeMethod); // yields "SomeMethod"
    String fieldNameWithoutInstance = Name.Of<InstanceClass>(x => x.Field); // yields "Field"
    String nonVoidMethodWithoutInstance = Name.Of<InstanceClass, ReturnType>(x => x.NonVoidMethod); // yields "NonVoidMethod"

#### Events

    String eventName = Name.Of(e => instanceClass.InstanceClassEvent += e); // yields "InstanceClassEvent"
    String eventNameWithoutInstance = Name.Of<InstanceClass>((x,e) => x.InstanceClassEvent += e); // yields "InstanceClassEvent"
We need to use this assign syntax because its the only way C# allows us to reference an event outside of its containing type

#### PropertyChanged events (check out [PropertyChanged.Fody](https://github.com/Fody/PropertyChanged) for an automatic solution)

    private String displayText;
	public String DisplayText {
	    get { return displayText; }
		set {
		    if (value == displayText)
			    return;
			displayText = value;
			RaisePropertyChanged(Name.Of(DisplayText));
		}
	}

#### ArgumentException throw with argument name

    public void Foo(String methodArgument) {
		if (methodArgument == null)
			throw new ArgumentNullException(Name.Of(methodArgument), "String must not be null");
		if (methodArgument.Length < 42)
			throw new ArgumentException("String not long enough", Name.Of(methodArgument)); // Yep, ArgumentException's constructor arguments are in the opposite order of ArgumentNullException's constructor arguments.
		DoSomething();
    }

#### Void methods

    String voidMethodName = Name.OfVoid(VoidReturnMethod); // yields "VoidReturnMethod"
We need to use a different method (`OfVoid`) for a reason best explained by [Eric Lippert](http://ericlippert.com/)
> The principle here is that determining method group convertibility requires selecting a method from a method group using overload resolution, and overload resolution does not consider return types.

There is a full explanation in [a StackOverflow post](http://stackoverflow.com/questions/2057146/compiler-ambiguous-invocation-error-anonymous-method-and-method-group-with-fun).

#### Generic methods

    String genericMethodName = Name.Of(instance.GenericMethod<V, R>) // yields "GenericMethod"
V (value) and R (reference) are dummy struct and class types, respectively, for supplying constrained generic arguments

Example signature

    public void GenericMethod<T, U>() where T : struct
                                      where U : class {}

#### Types

    String className = Name.Of<InstanceClass>(); // yields "InstanceClass"
	String staticClassName = Name.Of(typeof(StaticClass)); // yields "StaticClass"
we have to use the `typeof` operator because you can't pass a static class as an argument nor generic argument (don't worry; it doesn't actually use reflection - the `typeof` call is removed during build)

#### Enums

    String enumName = Name.Of<EnumValues>(); // yields "EnumValues"
	String enumValueName = Name.Of(EnumValues.FooValue); // yields "FooValue"
`EnumValues.FooValue.ToString()` uses reflection which is why I have included support for enum values
