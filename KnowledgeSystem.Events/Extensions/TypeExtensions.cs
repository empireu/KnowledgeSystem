using System.Reflection;

namespace KnowledgeSystem.Events.Extensions;

public static class TypeExtensions
{
    public static string PrettyPrint(this Type? type)
    {
        if (type == null)
            return "null";
        if (type == typeof(int))
            return "int";
        if (type == typeof(short))
            return "short";
        if (type == typeof(byte))
            return "byte";
        if (type == typeof(bool))
            return "bool";
        if (type == typeof(long))
            return "long";
        if (type == typeof(float))
            return "float";
        if (type == typeof(double))
            return "double";
        if (type == typeof(decimal))
            return "decimal";
        if (type == typeof(string))
            return "string";
        if (type.IsGenericType)
            return type.Name.Split('`')[0] + "<" + string.Join(", ", type.GetGenericArguments().Select(PrettyPrint).ToArray()) + ">";
        return type.Name;
    }

    public static string PrettyPrint(this MethodBase method, bool showParameters = true)
    {
        var str = method.Name;

        if (method.DeclaringType != null)
        {
            str = method.DeclaringType.PrettyPrint() + '.' + str;
        }

        if (!showParameters) return str;
        var parameters = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.PrettyPrint()));
        str += $"({parameters})";
        return str;
    }
}