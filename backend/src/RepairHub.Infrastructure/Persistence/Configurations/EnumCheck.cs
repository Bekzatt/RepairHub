namespace RepairHub.Infrastructure.Persistence.Configurations;

internal static class EnumCheck
{
    /// <summary>SQL for a CHECK constraint allowing exactly the names of <typeparamref name="T"/>.</summary>
    public static string Sql<T>(string column) where T : struct, Enum =>
        $"{column} IN ({string.Join(", ", Enum.GetNames<T>().Select(n => $"'{n}'"))})";
}
