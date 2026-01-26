using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Nexum.SourceGen
{
    [Generator]
    public class NetSerializableGenerator : IIncrementalGenerator
    {
        private const string NetSerializableAttributeFullName = "Nexum.Core.Attributes.NetSerializableAttribute";
        private const string NetCoreMessageAttributeFullName = "Nexum.Core.Attributes.NetCoreMessageAttribute";
        private const string NetRmiAttributeFullName = "Nexum.Core.Attributes.NetRmiAttribute";
        private const string NetPropertyAttributeFullName = "Nexum.Core.Attributes.NetPropertyAttribute";

        private static readonly HashSet<string> SupportedPrimitiveTypes = new HashSet<string>
        {
            "System.Boolean",
            "System.Byte",
            "System.SByte",
            "System.Int16",
            "System.UInt16",
            "System.Int32",
            "System.UInt32",
            "System.Int64",
            "System.UInt64",
            "System.Single",
            "System.Double",
            "System.String",
            "System.Guid",
            "System.Version",
            "System.Net.IPEndPoint"
        };

        private static readonly HashSet<string> SupportedNexumTypes = new HashSet<string>
        {
            "Nexum.Core.ByteArray",
            "Nexum.Core.Serialization.ByteArray"
        };

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var netSerializableDeclarations = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    NetSerializableAttributeFullName,
                    static (node, _) => node is ClassDeclarationSyntax,
                    static (ctx, _) => GetPacketClassInfo(ctx))
                .Where(static info => info is not null)
                .Select(static (info, _) => info!);

            var netCoreMessageDeclarations = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    NetCoreMessageAttributeFullName,
                    static (node, _) => node is ClassDeclarationSyntax,
                    static (ctx, _) => GetPacketClassInfo(ctx))
                .Where(static info => info is not null)
                .Select(static (info, _) => info!);

            var netRmiDeclarations = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    NetRmiAttributeFullName,
                    static (node, _) => node is ClassDeclarationSyntax,
                    static (ctx, _) => GetPacketClassInfo(ctx))
                .Where(static info => info is not null)
                .Select(static (info, _) => info!);

            context.RegisterSourceOutput(netSerializableDeclarations,
                static (spc, classInfo) => Execute(spc, classInfo));
            context.RegisterSourceOutput(netCoreMessageDeclarations,
                static (spc, classInfo) => Execute(spc, classInfo));
            context.RegisterSourceOutput(netRmiDeclarations,
                static (spc, classInfo) => Execute(spc, classInfo));
        }

        private static PacketClassInfo? GetPacketClassInfo(GeneratorAttributeSyntaxContext context)
        {
            if (context.TargetSymbol is not INamedTypeSymbol classSymbol)
                return null;

            var classDeclaration = context.TargetNode as ClassDeclarationSyntax;
            if (classDeclaration is null)
                return null;

            bool isPartial = classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword);

            string? messageTypeName = null;
            var coreMessageAttr = context.Attributes
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == NetCoreMessageAttributeFullName);
            if (coreMessageAttr is not null && coreMessageAttr.ConstructorArguments.Length > 0)
            {
                var messageTypeArg = coreMessageAttr.ConstructorArguments[0];
                if (messageTypeArg.Type is INamedTypeSymbol enumType && messageTypeArg.Value is not null)
                {
                    int enumValue = Convert.ToInt32(messageTypeArg.Value);
                    var enumMember = enumType.GetMembers()
                        .OfType<IFieldSymbol>()
                        .FirstOrDefault(f => f.HasConstantValue && Convert.ToInt32(f.ConstantValue) == enumValue);
                    if (enumMember is not null)
                        messageTypeName = $"{enumType.ToDisplayString()}.{enumMember.Name}";
                }
            }

            ushort? rmiId = null;
            string? rmiIdEnumTypeName = null;
            string? rmiIdEnumUnderlyingTypeName = null;
            var rmiAttr = context.Attributes
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == NetRmiAttributeFullName);
            if (rmiAttr is not null && rmiAttr.ConstructorArguments.Length > 0)
            {
                var rmiArg = rmiAttr.ConstructorArguments[0];
                if (rmiArg.Type is INamedTypeSymbol rmiArgType)
                {
                    if (rmiArgType.TypeKind == TypeKind.Enum)
                    {
                        rmiIdEnumTypeName = rmiArgType.ToDisplayString();
                        var underlyingType = rmiArgType.EnumUnderlyingType;
                        rmiIdEnumUnderlyingTypeName = underlyingType?.ToDisplayString();
                        if (underlyingType?.SpecialType == SpecialType.System_UInt16 && rmiArg.Value is not null)
                            rmiId = Convert.ToUInt16(rmiArg.Value);
                    }
                    else if (rmiArg.Value is ushort ushortValue)
                    {
                        rmiId = ushortValue;
                    }
                }
            }

            var properties = ImmutableArray.CreateBuilder<PacketPropertyInfo>();

            foreach (var member in classSymbol.GetMembers())
            {
                if (member is not IPropertySymbol property)
                    continue;

                var netPropertyAttr = property.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == NetPropertyAttributeFullName);

                bool isArray = property.Type is IArrayTypeSymbol;
                string? elementTypeName = null;
                var elementSpecialType = SpecialType.None;
                var elementTypeKind = TypeKind.Unknown;
                bool isElementNetPacket = false;
                bool isNetPacketType = false;

                if (isArray && property.Type is IArrayTypeSymbol arrayType)
                {
                    var elementType = arrayType.ElementType;
                    elementTypeName = elementType.ToDisplayString();
                    elementSpecialType = elementType.SpecialType;
                    elementTypeKind = elementType.TypeKind;
                    isElementNetPacket = HasNetSerializableAttribute(elementType);
                }
                else if (property.Type is INamedTypeSymbol namedType)
                {
                    isNetPacketType = HasNetSerializableAttribute(namedType);
                }

                if (netPropertyAttr is null)
                {
                    properties.Add(new PacketPropertyInfo(
                        property.Name,
                        property.Type.ToDisplayString(),
                        property.Type.TypeKind,
                        property.Type.SpecialType,
                        -1,
                        null,
                        false,
                        isArray,
                        elementTypeName,
                        elementSpecialType,
                        elementTypeKind,
                        isElementNetPacket,
                        isNetPacketType));
                    continue;
                }

                int order = netPropertyAttr.ConstructorArguments.Length > 0
                    ? (int)netPropertyAttr.ConstructorArguments[0].Value!
                    : -1;

                string? serializerTypeName = null;

                if (netPropertyAttr.ConstructorArguments.Length > 1
                    && netPropertyAttr.ConstructorArguments[1].Value is INamedTypeSymbol ctorSerializerType)
                    serializerTypeName = ctorSerializerType.ToDisplayString();

                foreach (var namedArg in netPropertyAttr.NamedArguments)
                    if (namedArg.Key == "Serializer" && namedArg.Value.Value is INamedTypeSymbol serializerType)
                        serializerTypeName = serializerType.ToDisplayString();

                properties.Add(new PacketPropertyInfo(
                    property.Name,
                    property.Type.ToDisplayString(),
                    property.Type.TypeKind,
                    property.Type.SpecialType,
                    order,
                    serializerTypeName,
                    true,
                    isArray,
                    elementTypeName,
                    elementSpecialType,
                    elementTypeKind,
                    isElementNetPacket,
                    isNetPacketType));
            }

            return new PacketClassInfo(
                classSymbol.Name,
                classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                properties.ToImmutable(),
                isPartial,
                classDeclaration.GetLocation(),
                messageTypeName,
                rmiId,
                rmiIdEnumTypeName,
                rmiIdEnumUnderlyingTypeName);
        }

        private static bool HasNetSerializableAttribute(ITypeSymbol typeSymbol)
        {
            return typeSymbol.GetAttributes()
                .Any(a =>
                {
                    string? attrName = a.AttributeClass?.ToDisplayString();
                    return attrName == NetSerializableAttributeFullName || attrName == NetCoreMessageAttributeFullName;
                });
        }

        private static void Execute(SourceProductionContext context, PacketClassInfo classInfo)
        {
            var diagnostics = new List<Diagnostic>();

            if (!classInfo.IsPartial)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ClassNotPartial,
                    classInfo.Location,
                    classInfo.ClassName));
                return;
            }

            if (classInfo.RmiIdEnumTypeName is not null && classInfo.RmiId is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidRmiEnumUnderlyingType,
                    classInfo.Location,
                    classInfo.ClassName,
                    classInfo.RmiIdEnumTypeName,
                    classInfo.RmiIdEnumUnderlyingTypeName ?? "unknown"));
                return;
            }

            var validProperties = new List<PacketPropertyInfo>();
            var orderToProperty = new Dictionary<int, string>();

            foreach (var prop in classInfo.Properties)
            {
                if (!prop.HasNetPropertyAttribute)
                {
                    diagnostics.Add(Diagnostic.Create(
                        MissingNetPropertyAttribute,
                        classInfo.Location,
                        prop.Name,
                        classInfo.ClassName));
                    continue;
                }

                if (orderToProperty.TryGetValue(prop.Order, out string? existingProp))
                {
                    diagnostics.Add(Diagnostic.Create(
                        DuplicateOrder,
                        classInfo.Location,
                        prop.Name,
                        prop.Order,
                        existingProp));
                    continue;
                }

                bool hasCustomSerializer = prop.SerializerTypeName is not null;

                if (!IsSupportedType(prop) && !hasCustomSerializer)
                {
                    diagnostics.Add(Diagnostic.Create(
                        UnsupportedPropertyType,
                        classInfo.Location,
                        prop.Name,
                        prop.TypeName));
                    continue;
                }

                orderToProperty[prop.Order] = prop.Name;
                validProperties.Add(prop);
            }

            foreach (var diagnostic in diagnostics)
                context.ReportDiagnostic(diagnostic);

            validProperties.Sort((a, b) => a.Order.CompareTo(b.Order));

            string source = GenerateSource(classInfo, validProperties);
            context.AddSource($"{classInfo.ClassName}.g.cs", SourceText.From(source, Encoding.UTF8));
        }

        private static bool IsSupportedType(PacketPropertyInfo prop)
        {
            if (prop.IsArray)
            {
                if (prop.IsElementNetPacket)
                    return true;

                return IsSupportedElementType(prop.ElementTypeName, prop.ElementSpecialType, prop.ElementTypeKind);
            }

            if (prop.IsNetPacketType)
                return true;

            switch (prop.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_String:
                    return true;
            }

            if (SupportedPrimitiveTypes.Contains(prop.TypeName))
                return true;

            if (SupportedNexumTypes.Contains(prop.TypeName))
                return true;

            if (prop.TypeKind == TypeKind.Enum)
                return true;

            return false;
        }

        private static bool IsSupportedElementType(string? elementTypeName, SpecialType elementSpecialType,
            TypeKind elementTypeKind)
        {
            if (elementTypeName is null)
                return false;

            switch (elementSpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_String:
                    return true;
            }

            if (SupportedPrimitiveTypes.Contains(elementTypeName))
                return true;

            if (SupportedNexumTypes.Contains(elementTypeName))
                return true;

            if (elementTypeKind == TypeKind.Enum)
                return true;

            return false;
        }

        private static string GenerateSource(PacketClassInfo classInfo, List<PacketPropertyInfo> properties)
        {
            var sb = new StringBuilder(1024);

            sb.Append(
                "// <auto-generated/>\n#nullable enable\n\nusing System;\nusing System.CodeDom.Compiler;\nusing System.Diagnostics.CodeAnalysis;\nusing Nexum.Core;\nusing Nexum.Core.Configuration;\nusing Nexum.Core.Serialization;\nusing Nexum.Core.Attributes;\n\n");

            if (classInfo.Namespace.Length > 0)
                sb.Append("namespace ").Append(classInfo.Namespace).Append(";\n\n");

            sb.Append("[GeneratedCode(\"Nexum.SourceGen\", \"1.0.0\")]\npartial class ").Append(classInfo.ClassName);

            if (classInfo.RmiId is not null)
                sb.Append(" : INetRmi");
            else if (classInfo.MessageTypeName is not null)
                sb.Append(" : INetCoreMessage");

            sb.Append('\n').Append('{').Append('\n');

            GenerateSerializeMethod(sb, properties, classInfo.MessageTypeName, classInfo.RmiId);
            GenerateDeserializeMethod(sb, classInfo, properties);

            sb.Append('}').Append('\n');

            return sb.ToString();
        }

        private static void GenerateSerializeMethod(StringBuilder sb, List<PacketPropertyInfo> properties,
            string? messageTypeName, ushort? rmiId)
        {
            if (rmiId is not null)
            {
                sb.Append("    public NetMessage Serialize()\n    {\n        var inner = new NetMessage();\n");

                foreach (var prop in properties)
                    if (prop.SerializerTypeName is not null)
                        sb.Append("        ").Append(prop.SerializerTypeName).Append(".Serialize(inner, ")
                            .Append(prop.Name)
                            .Append(");\n");
                    else if (prop.IsArray)
                        GenerateArraySerialize(sb, prop, "inner");
                    else if (prop.IsNetPacketType)
                        sb.Append("        inner.Write(").Append(prop.Name).Append(".Serialize());\n");
                    else
                        sb.Append("        inner.Write(").Append(prop.Name).Append(");\n");

                sb.Append("        var msg = new Nexum.Core.Message.X2X.RmiMessage { RmiId = ").Append(rmiId.Value)
                    .Append(", Data = inner }.Serialize();\n");
                sb.Append("        msg.Compress = inner.Compress;\n");
                sb.Append("        msg.EncryptMode = inner.EncryptMode;\n");
                sb.Append("        msg.RelayFrom = inner.RelayFrom;\n");
                sb.Append("        msg.Reliable = inner.Reliable;\n");
                sb.Append("        return msg;\n    }\n\n");
            }
            else
            {
                sb.Append("    public NetMessage Serialize()\n    {\n        var msg = new NetMessage();\n");

                if (messageTypeName is not null)
                    sb.Append("        msg.Write(").Append(messageTypeName).Append(");\n");

                foreach (var prop in properties)
                    if (prop.SerializerTypeName is not null)
                        sb.Append("        ").Append(prop.SerializerTypeName).Append(".Serialize(msg, ")
                            .Append(prop.Name)
                            .Append(");\n");
                    else if (prop.IsArray)
                        GenerateArraySerialize(sb, prop);
                    else if (prop.IsNetPacketType)
                        sb.Append("        msg.Write(").Append(prop.Name).Append(".Serialize());\n");
                    else
                        sb.Append("        msg.Write(").Append(prop.Name).Append(");\n");

                sb.Append("        return msg;\n    }\n\n");
            }
        }

        private static void GenerateArraySerialize(StringBuilder sb, PacketPropertyInfo prop, string varName = "msg")
        {
            sb.Append("        ").Append(varName).Append(".WriteScalar(").Append(prop.Name).Append("?.Length ?? 0);\n");
            sb.Append("        if (").Append(prop.Name).Append(" is not null)\n        {\n");
            sb.Append("            foreach (var item in ").Append(prop.Name).Append(')').Append('\n');

            if (prop.IsElementNetPacket)
                sb.Append("            {\n                ").Append(varName)
                    .Append(".Write(item.Serialize());\n            }\n");
            else
                sb.Append("                ").Append(varName).Append(".Write(item);\n");

            sb.Append("        }\n");
        }

        private static void GenerateDeserializeMethod(StringBuilder sb, PacketClassInfo classInfo,
            List<PacketPropertyInfo> properties)
        {
            sb.Append("    public static bool Deserialize(NetMessage msg, [NotNullWhen(true)] out ")
                .Append(classInfo.ClassName).Append("? packet)\n    {\n        packet = null;\n\n");

            if (properties.Count == 0)
            {
                sb.Append("        packet = new ").Append(classInfo.ClassName)
                    .Append("();\n        return true;\n    }\n");
                return;
            }

            foreach (var prop in properties)
                sb.Append("        ").Append(prop.TypeName).Append(" _").Append(prop.Name).Append(" = ")
                    .Append(GetDefaultValue(prop)).Append(";\n");

            sb.Append('\n');

            bool hasComplexProperties = HasComplexProperties(properties);

            if (hasComplexProperties)
            {
                foreach (var prop in properties)
                {
                    if (prop.SerializerTypeName is not null)
                    {
                        sb.Append("        if (!").Append(prop.SerializerTypeName).Append(".Deserialize(msg, out _")
                            .Append(prop.Name).Append("))\n            return false;\n");
                    }
                    else if (prop.IsArray)
                    {
                        GenerateArrayDeserialize(sb, prop);
                    }
                    else if (prop.IsNetPacketType)
                    {
                        sb.Append("        if (!").Append(prop.TypeName).Append(".Deserialize(msg, out var _temp")
                            .Append(prop.Name).Append("))\n            return false;\n");
                        sb.Append("        _").Append(prop.Name).Append(" = _temp").Append(prop.Name).Append(";\n");
                    }
                    else
                    {
                        sb.Append("        if (!msg.Read(").Append(NeedsRefParameter(prop) ? "ref" : "out").Append(" _")
                            .Append(prop.Name).Append("))\n            return false;\n");
                    }

                    sb.Append('\n');
                }

                sb.Append("        packet = new ").Append(classInfo.ClassName).Append("\n        {\n");

                int lastIndex = properties.Count - 1;
                for (int i = 0; i < properties.Count; i++)
                {
                    var prop = properties[i];
                    sb.Append("            ").Append(prop.Name).Append(" = _").Append(prop.Name);
                    if (i < lastIndex)
                        sb.Append(',');
                    sb.Append('\n');
                }

                sb.Append("        };\n        return true;\n");
            }
            else
            {
                sb.Append("        if (");

                for (int i = 0; i < properties.Count; i++)
                {
                    var prop = properties[i];

                    if (i > 0)
                        sb.Append("\n            && ");

                    if (prop.SerializerTypeName is not null)
                        sb.Append(prop.SerializerTypeName).Append(".Deserialize(msg, out _").Append(prop.Name)
                            .Append(')');
                    else
                        sb.Append("msg.Read(").Append(NeedsRefParameter(prop) ? "ref" : "out").Append(" _")
                            .Append(prop.Name).Append(')');
                }

                sb.Append(")\n        {\n            packet = new ").Append(classInfo.ClassName)
                    .Append("\n            {\n");

                int lastIndex = properties.Count - 1;
                for (int i = 0; i < properties.Count; i++)
                {
                    var prop = properties[i];
                    sb.Append("                ").Append(prop.Name).Append(" = _").Append(prop.Name);
                    if (i < lastIndex)
                        sb.Append(',');
                    sb.Append('\n');
                }

                sb.Append("            };\n            return true;\n        }\n\n        return false;\n");
            }

            sb.Append("    }\n");
        }

        private static void GenerateArrayDeserialize(StringBuilder sb, PacketPropertyInfo prop)
        {
            sb.Append("        long _").Append(prop.Name).Append("Length = 0;\n");
            sb.Append("        if (!msg.ReadScalar(ref _").Append(prop.Name)
                .Append("Length))\n            return false;\n");
            sb.Append("        _").Append(prop.Name).Append(" = new ").Append(prop.ElementTypeName).Append("[(int)_")
                .Append(prop.Name).Append("Length];\n");
            sb.Append("        for (int i = 0; i < (int)_").Append(prop.Name).Append("Length; i++)\n        {\n");

            if (prop.IsElementNetPacket)
            {
                sb.Append("            if (!").Append(prop.ElementTypeName)
                    .Append(".Deserialize(msg, out var _element))\n                return false;\n");
                sb.Append("            _").Append(prop.Name).Append("[i] = _element;\n");
            }
            else
            {
                sb.Append("            if (!msg.Read(").Append(NeedsElementRefParameter(prop) ? "ref" : "out")
                    .Append(" _").Append(prop.Name).Append("[i]))\n                return false;\n");
            }

            sb.Append("        }\n");
        }

        private static bool HasComplexProperties(List<PacketPropertyInfo> properties)
        {
            for (int i = 0; i < properties.Count; i++)
            {
                var p = properties[i];
                if (p.IsArray || p.IsNetPacketType)
                    return true;
            }

            return false;
        }

        private static bool NeedsElementRefParameter(PacketPropertyInfo prop)
        {
            switch (prop.ElementSpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                    return true;
            }

            if (prop.ElementTypeName is "Nexum.Core.ByteArray" or "Nexum.Core.Serialization.ByteArray")
                return true;

            return false;
        }

        private static bool NeedsRefParameter(PacketPropertyInfo prop)
        {
            switch (prop.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                    return true;
            }

            if (prop.TypeName is "Nexum.Core.ByteArray" or "Nexum.Core.Serialization.ByteArray")
                return true;

            return false;
        }

        private static string GetDefaultValue(PacketPropertyInfo prop)
        {
            if (prop.IsArray)
                return $"Array.Empty<{prop.ElementTypeName}>()";

            if (prop.IsNetPacketType)
                return "null!";

            switch (prop.SpecialType)
            {
                case SpecialType.System_Boolean:
                    return "false";
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                    return "0";
                case SpecialType.System_Single:
                    return "0f";
                case SpecialType.System_Double:
                    return "0d";
                case SpecialType.System_String:
                    return "string.Empty";
            }

            return prop.TypeName switch
            {
                "System.Guid" => "Guid.Empty",
                "System.Net.IPEndPoint" => "null!",
                "System.Version" => "null!",
                "Nexum.Core.ByteArray" or "Nexum.Core.Serialization.ByteArray" => "new ByteArray()",
                _ when prop.TypeKind == TypeKind.Enum => $"default({prop.TypeName})",
                _ => $"default({prop.TypeName})!"
            };
        }

        #region Diagnostics

#pragma warning disable RS2008 // Enable analyzer release tracking
#pragma warning disable RS1032 // Define diagnostic message correctly

        private static readonly DiagnosticDescriptor MissingNetPropertyAttribute = new DiagnosticDescriptor("NXPKT001",
            "Missing NetProperty attribute",
            "Property '{0}' in NetSerializable class '{1}' is missing [NetProperty] attribute", "Nexum.SourceGen",
            DiagnosticSeverity.Warning, true);

        private static readonly DiagnosticDescriptor DuplicateOrder = new DiagnosticDescriptor("NXPKT002",
            "Duplicate property order", "Property '{0}' has duplicate Order value {1} (already used by '{2}')",
            "Nexum.SourceGen", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor UnsupportedPropertyType = new DiagnosticDescriptor("NXPKT003",
            "Unsupported property type", "Property '{0}' has unsupported type '{1}' - specify a custom Serializer",
            "Nexum.SourceGen", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor ClassNotPartial = new DiagnosticDescriptor("NXPKT005",
            "NetSerializable class must be partial",
            "Class '{0}' with [NetSerializable] attribute must be declared as partial",
            "Nexum.SourceGen", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor InvalidRmiEnumUnderlyingType = new DiagnosticDescriptor("NXPKT006",
            "Invalid RMI enum underlying type",
            "NetRmi attribute on class '{0}' uses enum '{1}' with underlying type '{2}', but ushort is required",
            "Nexum.SourceGen", DiagnosticSeverity.Error, true);

#pragma warning restore RS1032
#pragma warning restore RS2008

        #endregion
    }

    internal sealed class PacketClassInfo : IEquatable<PacketClassInfo>
    {
        public PacketClassInfo(
            string className,
            string @namespace,
            ImmutableArray<PacketPropertyInfo> properties,
            bool isPartial,
            Location? location,
            string? messageTypeName = null,
            ushort? rmiId = null,
            string? rmiIdEnumTypeName = null,
            string? rmiIdEnumUnderlyingTypeName = null)
        {
            ClassName = className;
            Namespace = @namespace;
            Properties = properties;
            IsPartial = isPartial;
            Location = location;
            MessageTypeName = messageTypeName;
            RmiId = rmiId;
            RmiIdEnumTypeName = rmiIdEnumTypeName;
            RmiIdEnumUnderlyingTypeName = rmiIdEnumUnderlyingTypeName;
        }

        public string ClassName { get; }
        public string Namespace { get; }
        public ImmutableArray<PacketPropertyInfo> Properties { get; }
        public bool IsPartial { get; }
        public Location? Location { get; }
        public string? MessageTypeName { get; }
        public ushort? RmiId { get; }
        public string? RmiIdEnumTypeName { get; }
        public string? RmiIdEnumUnderlyingTypeName { get; }

        public bool Equals(PacketClassInfo? other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            return ClassName == other.ClassName
                   && Namespace == other.Namespace
                   && IsPartial == other.IsPartial
                   && MessageTypeName == other.MessageTypeName
                   && RmiId == other.RmiId
                   && RmiIdEnumTypeName == other.RmiIdEnumTypeName
                   && RmiIdEnumUnderlyingTypeName == other.RmiIdEnumUnderlyingTypeName
                   && Properties.SequenceEqual(other.Properties);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as PacketClassInfo);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + ClassName.GetHashCode();
                hash = hash * 31 + Namespace.GetHashCode();
                hash = hash * 31 + IsPartial.GetHashCode();
                hash = hash * 31 + (MessageTypeName?.GetHashCode() ?? 0);
                hash = hash * 31 + (RmiId?.GetHashCode() ?? 0);
                hash = hash * 31 + (RmiIdEnumTypeName?.GetHashCode() ?? 0);
                hash = hash * 31 + (RmiIdEnumUnderlyingTypeName?.GetHashCode() ?? 0);
                foreach (var prop in Properties)
                    hash = hash * 31 + prop.GetHashCode();
                return hash;
            }
        }
    }

    internal sealed class PacketPropertyInfo : IEquatable<PacketPropertyInfo>
    {
        public PacketPropertyInfo(
            string name,
            string typeName,
            TypeKind typeKind,
            SpecialType specialType,
            int order,
            string? serializerTypeName,
            bool hasNetPropertyAttribute,
            bool isArray = false,
            string? elementTypeName = null,
            SpecialType elementSpecialType = SpecialType.None,
            TypeKind elementTypeKind = TypeKind.Unknown,
            bool isElementNetPacket = false,
            bool isNetPacketType = false)
        {
            Name = name;
            TypeName = typeName;
            TypeKind = typeKind;
            SpecialType = specialType;
            Order = order;
            SerializerTypeName = serializerTypeName;
            HasNetPropertyAttribute = hasNetPropertyAttribute;
            IsArray = isArray;
            ElementTypeName = elementTypeName;
            ElementSpecialType = elementSpecialType;
            ElementTypeKind = elementTypeKind;
            IsElementNetPacket = isElementNetPacket;
            IsNetPacketType = isNetPacketType;
        }

        public string Name { get; }
        public string TypeName { get; }
        public TypeKind TypeKind { get; }
        public SpecialType SpecialType { get; }
        public int Order { get; }
        public string? SerializerTypeName { get; }
        public bool HasNetPropertyAttribute { get; }
        public bool IsArray { get; }
        public string? ElementTypeName { get; }
        public SpecialType ElementSpecialType { get; }
        public TypeKind ElementTypeKind { get; }
        public bool IsElementNetPacket { get; }
        public bool IsNetPacketType { get; }

        public bool Equals(PacketPropertyInfo? other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            return Name == other.Name
                   && TypeName == other.TypeName
                   && TypeKind == other.TypeKind
                   && SpecialType == other.SpecialType
                   && Order == other.Order
                   && SerializerTypeName == other.SerializerTypeName
                   && HasNetPropertyAttribute == other.HasNetPropertyAttribute
                   && IsArray == other.IsArray
                   && ElementTypeName == other.ElementTypeName
                   && ElementSpecialType == other.ElementSpecialType
                   && ElementTypeKind == other.ElementTypeKind
                   && IsElementNetPacket == other.IsElementNetPacket
                   && IsNetPacketType == other.IsNetPacketType;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as PacketPropertyInfo);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Name.GetHashCode();
                hash = hash * 31 + TypeName.GetHashCode();
                hash = hash * 31 + TypeKind.GetHashCode();
                hash = hash * 31 + SpecialType.GetHashCode();
                hash = hash * 31 + Order.GetHashCode();
                hash = hash * 31 + (SerializerTypeName?.GetHashCode() ?? 0);
                hash = hash * 31 + HasNetPropertyAttribute.GetHashCode();
                hash = hash * 31 + IsArray.GetHashCode();
                hash = hash * 31 + (ElementTypeName?.GetHashCode() ?? 0);
                hash = hash * 31 + ElementSpecialType.GetHashCode();
                hash = hash * 31 + ElementTypeKind.GetHashCode();
                hash = hash * 31 + IsElementNetPacket.GetHashCode();
                hash = hash * 31 + IsNetPacketType.GetHashCode();
                return hash;
            }
        }
    }
}
