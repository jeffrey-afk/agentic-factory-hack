namespace RepairPlanner.Services;

public interface IFaultMappingService
{
    /// <summary>
    /// Gets the required skills for a given fault type.
    /// </summary>
    /// <param name="faultType">The fault type identifier</param>
    /// <returns>List of required skill identifiers</returns>
    IReadOnlyList<string> GetRequiredSkills(string faultType);

    /// <summary>
    /// Gets the required parts for a given fault type.
    /// </summary>
    /// <param name="faultType">The fault type identifier</param>
    /// <returns>List of required part numbers</returns>
    IReadOnlyList<string> GetRequiredParts(string faultType);
}
