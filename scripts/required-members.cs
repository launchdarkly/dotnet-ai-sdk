// Lists the `required` members of an assembly's public types, for scripts/api-compat.sh.
// ApiCompat does not flag a member that becomes required, and the PublicAPI report does
// not record `required`, so this reads the RequiredMemberAttribute from the metadata.
//
// Usage: dotnet run scripts/required-members.cs -- <assembly.dll>
// Output, one per line, sorted:
//   type <Type>             a public type
//   constructible <Type>    a public type that callers can construct: a public or
//                           protected constructor without [SetsRequiredMembers]
//   required <Type>.<Name>  a public required property or field
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();
var lines = new SortedSet<string>(StringComparer.Ordinal);

foreach (var handle in md.TypeDefinitions)
{
    var type = md.GetTypeDefinition(handle);
    if (!IsVisible(type))
    {
        continue;
    }

    var name = TypeName(handle);
    lines.Add($"type {name}");
    var isStatic = type.Attributes.HasFlag(TypeAttributes.Abstract) && type.Attributes.HasFlag(TypeAttributes.Sealed);
    foreach (var m in type.GetMethods().Select(md.GetMethodDefinition))
    {
        var access = m.Attributes & MethodAttributes.MemberAccessMask;
        if (!isStatic && md.GetString(m.Name) == ".ctor" && IsCallerAccess(access)
            && !HasAttribute(m.GetCustomAttributes(), "SetsRequiredMembersAttribute"))
        {
            lines.Add($"constructible {name}");
        }
    }

    foreach (var p in type.GetProperties().Select(md.GetPropertyDefinition))
    {
        var accessors = p.GetAccessors();
        var setter = accessors.Setter.IsNil ? accessors.Getter : accessors.Setter;
        if (!setter.IsNil && IsCallerAccess(md.GetMethodDefinition(setter).Attributes & MethodAttributes.MemberAccessMask)
            && HasAttribute(p.GetCustomAttributes(), "RequiredMemberAttribute"))
        {
            lines.Add($"required {name}.{md.GetString(p.Name)}");
        }
    }

    foreach (var f in type.GetFields().Select(md.GetFieldDefinition))
    {
        if (IsCallerAccess((MethodAttributes)(f.Attributes & FieldAttributes.FieldAccessMask))
            && HasAttribute(f.GetCustomAttributes(), "RequiredMemberAttribute"))
        {
            lines.Add($"required {name}.{md.GetString(f.Name)}");
        }
    }
}

foreach (var line in lines)
{
    Console.WriteLine(line);
}

// Public, protected or protected internal: what a caller outside the assembly can use.
static bool IsCallerAccess(MethodAttributes access) =>
    access is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

bool IsVisible(TypeDefinition type)
{
    var visibility = type.Attributes & TypeAttributes.VisibilityMask;
    if (visibility == TypeAttributes.Public)
    {
        return true;
    }

    return visibility is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem
        && IsVisible(md.GetTypeDefinition(type.GetDeclaringType()));
}

string TypeName(TypeDefinitionHandle handle)
{
    var type = md.GetTypeDefinition(handle);
    var declaring = type.GetDeclaringType();
    return declaring.IsNil
        ? $"{md.GetString(type.Namespace)}.{md.GetString(type.Name)}".TrimStart('.')
        : $"{TypeName(declaring)}+{md.GetString(type.Name)}";
}

// Matches the attribute by its simple name, whether it comes from the framework or is
// defined in the assembly itself (as the compiler requires on netstandard2.0).
bool HasAttribute(CustomAttributeHandleCollection attributes, string simpleName)
{
    foreach (var a in attributes.Select(md.GetCustomAttribute))
    {
        var parent = a.Constructor.Kind switch
        {
            HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent,
            HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)a.Constructor).GetDeclaringType(),
            _ => default,
        };
        var typeName = parent.Kind switch
        {
            HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name),
            HandleKind.TypeDefinition => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)parent).Name),
            _ => null,
        };
        if (typeName == simpleName)
        {
            return true;
        }
    }

    return false;
}
