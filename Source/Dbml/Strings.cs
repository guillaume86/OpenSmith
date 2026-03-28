namespace LinqToSqlShared.DbmlObjectModel;

internal static class Strings
{
    internal static string Bug(object p0) =>
        $"BUG {p0}";

    internal static string DatabaseNodeNotFound(object p0) =>
        $"Database node not found.  Is the DBML namespace ({p0}) correctly specified?";

    internal static string ElementMoreThanOnceViolation(object p0) =>
        $"Element {p0} must only appear at most once.";

    internal static string InvalidBooleanAttributeValueViolation(object p0) =>
        $"The boolean attribute value '{p0}' is invalid.";

    internal static string InvalidEnumAttributeValueViolation(object p0) =>
        $"The value '{p0}' is invalid.";

    internal static string RequiredAttributeMissingViolation(object p0) =>
        $"Required attribute {p0} is missing.";

    internal static string RequiredElementMissingViolation(object p0) =>
        $"Element {p0} is required.";

    internal static string SchemaDuplicateIdViolation(object p0, object p1) =>
        $"Duplicate {p0} seen with value '{p1}'.";

    internal static string SchemaExpectedEmptyElement(object p0, object p1, object p2) =>
        $"Element '{p0}' must be empty, but contains a node of type {p1} named '{p2}'.";

    internal static string SchemaInvalidIdRefToNonexistentId(object p0, object p1, object p2) =>
        $"Unresolved reference {p0} {p1}: '{p2}'.";

    internal static string SchemaOrRequirementViolation(object p0, object p1, object p2) =>
        $"{p0} requires {p1} or {p2}.";

    internal static string SchemaRecursiveTypeReference(object p0, object p1) =>
        $"The type IdRef '{p0}' cannot point to the type '{p1}' because it is in the same inheritance hierarchy.";

    internal static string SchemaRequirementViolation(object p0, object p1) =>
        $"{p0} requires {p1}.";

    internal static string SchemaUnexpectedAdditionalAttributeViolation(object p0, object p1) =>
        $"Attribute {p0} on element {p1} must appear alone.";

    internal static string SchemaUnexpectedElementViolation(object p0, object p1) =>
        $"Element {p0} is unexpected under {p1} element.";

    internal static string SchemaUnrecognizedAttribute(object p0) =>
        $"Unrecognized attribute '{p0}' in file.";

    internal static string TypeNameNotUnique(object p0) =>
        $"The Name attribute '{p0}' of the Type element is already used by another type.";
}
