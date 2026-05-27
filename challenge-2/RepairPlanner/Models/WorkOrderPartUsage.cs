using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

public sealed class WorkOrderPartUsage
{
    [JsonPropertyName("partId")]
    [JsonProperty("partId")]
    public string PartId { get; set; } = string.Empty;

    [JsonPropertyName("partNumber")]
    [JsonProperty("partNumber")]
    public string PartNumber { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    [JsonProperty("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("unitCost")]
    [JsonProperty("unitCost")]
    public decimal UnitCost { get; set; }

    [JsonPropertyName("totalCost")]
    [JsonProperty("totalCost")]
    public decimal TotalCost { get; set; }

    [JsonPropertyName("usedInTaskSequence")]
    [JsonProperty("usedInTaskSequence")]
    public int? UsedInTaskSequence { get; set; }

    [JsonPropertyName("status")]
    [JsonProperty("status")]
    public string Status { get; set; } = "allocated"; // allocated, used, returned, damaged
}
