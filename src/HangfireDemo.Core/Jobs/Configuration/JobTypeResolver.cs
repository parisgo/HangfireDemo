using Hangfire.Common;

namespace HangfireDemo.Core.Jobs.Configuration;

public static class JobTypeResolver
{
    public static Type Resolve(string typeName)
    {
        // Existing persisted jobs still refer to the assembly used before the merge.
        var parts = typeName.Split(',');
        if (parts.Length >= 2 &&
            parts[0].Trim() == "HangfireDemo.Jobs.ImportCommandeJob" &&
            parts[1].Trim() == "HangfireDemo.Jobs")
        {
            return typeof(ImportCommandeJob);
        }

        return TypeHelper.DefaultTypeResolver(typeName);
    }
}
