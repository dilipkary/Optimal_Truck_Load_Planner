using System.Text.Json.Serialization;

namespace SmartLoad.Api;

public sealed record Truck(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("max_weight_lbs")] int MaxWeightLbs,
    [property: JsonPropertyName("max_volume_cuft")] int MaxVolumeCuft
);

public sealed record Order(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("payout_cents")] long PayoutCents,
    [property: JsonPropertyName("weight_lbs")] int WeightLbs,
    [property: JsonPropertyName("volume_cuft")] int VolumeCuft,
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("destination")] string Destination,
    [property: JsonPropertyName("pickup_date")] DateOnly PickupDate,
    [property: JsonPropertyName("delivery_date")] DateOnly DeliveryDate,
    [property: JsonPropertyName("is_hazmat")] bool IsHazmat
);

public sealed record OptimizeRequest(
    [property: JsonPropertyName("truck")] Truck Truck,
    [property: JsonPropertyName("orders")] IReadOnlyList<Order> Orders
);

public sealed record OptimizeResponse(
    [property: JsonPropertyName("truck_id")] string TruckId,
    [property: JsonPropertyName("selected_order_ids")] IReadOnlyList<string> SelectedOrderIds,
    [property: JsonPropertyName("total_payout_cents")] long TotalPayoutCents,
    [property: JsonPropertyName("total_weight_lbs")] int TotalWeightLbs,
    [property: JsonPropertyName("total_volume_cuft")] int TotalVolumeCuft,
    [property: JsonPropertyName("utilization_weight_percent")] double UtilizationWeightPercent,
    [property: JsonPropertyName("utilization_volume_percent")] double UtilizationVolumePercent
);

public sealed record ValidationError(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("message")] string Message
);

public sealed record ParetoSolution(
    [property: JsonPropertyName("selected_order_ids")] IReadOnlyList<string> SelectedOrderIds,
    [property: JsonPropertyName("total_payout_cents")] long TotalPayoutCents,
    [property: JsonPropertyName("total_weight_lbs")] int TotalWeightLbs,
    [property: JsonPropertyName("total_volume_cuft")] int TotalVolumeCuft,
    [property: JsonPropertyName("utilization_weight_percent")] double UtilizationWeightPercent,
    [property: JsonPropertyName("utilization_volume_percent")] double UtilizationVolumePercent
);

public sealed record ParetoResponse(
    [property: JsonPropertyName("truck_id")] string TruckId,
    [property: JsonPropertyName("solutions")] IReadOnlyList<ParetoSolution> Solutions
);
