namespace SmartLoad.Api.Services;

public interface ILoadOptimizer
{
    OptimizeResponse Optimize(OptimizeRequest request);
    ParetoResponse OptimizePareto(OptimizeRequest request);
}

public sealed class LoadOptimizer : ILoadOptimizer
{
    private sealed record CandidateOrder(
        string Id,
        long PayoutCents,
        int WeightLbs,
        int VolumeCuft,
        DateOnly PickupDate,
        DateOnly DeliveryDate
    );

    private sealed record GroupKey(string Origin, string Destination, bool IsHazmat);

    private sealed class BestSolution
    {
        public long PayoutCents { get; set; }
        public int WeightLbs { get; set; }
        public int VolumeCuft { get; set; }
        public List<int> SelectedIndices { get; set; } = [];
    }

    public OptimizeResponse Optimize(OptimizeRequest request)
    {
        var truck = request.Truck;
        var groupedOrders = new Dictionary<GroupKey, List<CandidateOrder>>();

        foreach (var order in request.Orders)
        {
            if (order.WeightLbs > truck.MaxWeightLbs || order.VolumeCuft > truck.MaxVolumeCuft)
            {
                continue;
            }

            var key = new GroupKey(
                order.Origin.Trim().ToLowerInvariant(),
                order.Destination.Trim().ToLowerInvariant(),
                order.IsHazmat
            );

            if (!groupedOrders.TryGetValue(key, out var list))
            {
                list = [];
                groupedOrders[key] = list;
            }

            list.Add(new CandidateOrder(
                order.Id,
                order.PayoutCents,
                order.WeightLbs,
                order.VolumeCuft,
                order.PickupDate,
                order.DeliveryDate
            ));
        }

        BestSolution globalBest = new();
        IReadOnlyList<CandidateOrder> winningGroup = [];

        foreach (var group in groupedOrders.Values)
        {
            var localBest = OptimizeGroup(truck, group);
            if (localBest.PayoutCents > globalBest.PayoutCents)
            {
                globalBest = localBest;
                winningGroup = group;
            }
        }

        var selectedIds = globalBest.SelectedIndices
            .Select(index => winningGroup[index].Id)
            .ToList();

        return new OptimizeResponse(
            truck.Id,
            selectedIds,
            globalBest.PayoutCents,
            globalBest.WeightLbs,
            globalBest.VolumeCuft,
            UtilizationPercent(globalBest.WeightLbs, truck.MaxWeightLbs),
            UtilizationPercent(globalBest.VolumeCuft, truck.MaxVolumeCuft)
        );
    }

    public ParetoResponse OptimizePareto(OptimizeRequest request)
    {
        var truck = request.Truck;
        var groupedOrders = new Dictionary<GroupKey, List<CandidateOrder>>();

        foreach (var order in request.Orders)
        {
            if (order.WeightLbs > truck.MaxWeightLbs || order.VolumeCuft > truck.MaxVolumeCuft)
            {
                continue;
            }

            var key = new GroupKey(
                order.Origin.Trim().ToLowerInvariant(),
                order.Destination.Trim().ToLowerInvariant(),
                order.IsHazmat
            );

            if (!groupedOrders.TryGetValue(key, out var list))
            {
                list = [];
                groupedOrders[key] = list;
            }

            list.Add(new CandidateOrder(
                order.Id,
                order.PayoutCents,
                order.WeightLbs,
                order.VolumeCuft,
                order.PickupDate,
                order.DeliveryDate
            ));
        }

        var allParetoSolutions = new List<(long PayoutCents, int WeightLbs, int VolumeCuft, List<string> SelectedIds)>();

        foreach (var group in groupedOrders.Values)
        {
            var groupPareto = OptimizeGroupPareto(truck, group);
            allParetoSolutions.AddRange(groupPareto);
        }

        var paretoFront = FilterParetoFront(allParetoSolutions, truck);

        var solutions = paretoFront
            .Select(sol => new ParetoSolution(
                sol.SelectedIds,
                sol.PayoutCents,
                sol.WeightLbs,
                sol.VolumeCuft,
                UtilizationPercent(sol.WeightLbs, truck.MaxWeightLbs),
                UtilizationPercent(sol.VolumeCuft, truck.MaxVolumeCuft)
            ))
            .ToList();

        return new ParetoResponse(truck.Id, solutions);
    }

    private static BestSolution OptimizeGroup(Truck truck, IReadOnlyList<CandidateOrder> orders)
    {
        if (orders.Count == 0)
        {
            return new BestSolution();
        }

        var sortedPairs = orders
            .Select((order, index) => new { OriginalIndex = index, Order = order })
            .OrderByDescending(x => x.Order.PayoutCents)
            .ToArray();

        var sortedOrders = sortedPairs.Select(x => x.Order).ToArray();
        var originalIndices = sortedPairs.Select(x => x.OriginalIndex).ToArray();

        var suffixPayouts = new long[sortedOrders.Length + 1];
        for (var i = sortedOrders.Length - 1; i >= 0; i--)
        {
            suffixPayouts[i] = suffixPayouts[i + 1] + sortedOrders[i].PayoutCents;
        }

        var best = new BestSolution();
        var selected = new List<int>(sortedOrders.Length);

        void Dfs(
            int index,
            long currentPayout,
            int currentWeight,
            int currentVolume,
            DateOnly? currentMaxPickup,
            DateOnly? currentMinDelivery
        )
        {
            if (currentWeight > truck.MaxWeightLbs || currentVolume > truck.MaxVolumeCuft)
            {
                return;
            }

            if (currentMaxPickup.HasValue && currentMinDelivery.HasValue && currentMaxPickup.Value > currentMinDelivery.Value)
            {
                return;
            }

            if (currentPayout + suffixPayouts[index] < best.PayoutCents)
            {
                return;
            }

            if (currentPayout > best.PayoutCents)
            {
                best.PayoutCents = currentPayout;
                best.WeightLbs = currentWeight;
                best.VolumeCuft = currentVolume;
                best.SelectedIndices = [.. selected];
            }

            if (index == sortedOrders.Length)
            {
                return;
            }

            Dfs(index + 1, currentPayout, currentWeight, currentVolume, currentMaxPickup, currentMinDelivery);

            var order = sortedOrders[index];
            var nextMaxPickup = currentMaxPickup.HasValue
                ? (currentMaxPickup.Value > order.PickupDate ? currentMaxPickup.Value : order.PickupDate)
                : order.PickupDate;
            var nextMinDelivery = currentMinDelivery.HasValue
                ? (currentMinDelivery.Value < order.DeliveryDate ? currentMinDelivery.Value : order.DeliveryDate)
                : order.DeliveryDate;

            selected.Add(originalIndices[index]);
            Dfs(
                index + 1,
                currentPayout + order.PayoutCents,
                currentWeight + order.WeightLbs,
                currentVolume + order.VolumeCuft,
                nextMaxPickup,
                nextMinDelivery
            );
            selected.RemoveAt(selected.Count - 1);
        }

        Dfs(0, 0L, 0, 0, null, null);
        return best;
    }

    private static List<(long PayoutCents, int WeightLbs, int VolumeCuft, List<string> SelectedIds)> OptimizeGroupPareto(
        Truck truck,
        IReadOnlyList<CandidateOrder> orders
    )
    {
        if (orders.Count == 0)
        {
            return [(0L, 0, 0, [])];
        }

        var sortedPairs = orders
            .Select((order, index) => new { OriginalIndex = index, Order = order })
            .OrderByDescending(x => x.Order.PayoutCents)
            .ToArray();

        var sortedOrders = sortedPairs.Select(x => x.Order).ToArray();
        var originalIndices = sortedPairs.Select(x => x.OriginalIndex).ToArray();

        var suffixPayouts = new long[sortedOrders.Length + 1];
        for (var i = sortedOrders.Length - 1; i >= 0; i--)
        {
            suffixPayouts[i] = suffixPayouts[i + 1] + sortedOrders[i].PayoutCents;
        }

        var allSolutions = new List<(long PayoutCents, int WeightLbs, int VolumeCuft, List<int> Indices)>();
        var selected = new List<int>(sortedOrders.Length);

        void Dfs(
            int index,
            long currentPayout,
            int currentWeight,
            int currentVolume,
            DateOnly? currentMaxPickup,
            DateOnly? currentMinDelivery
        )
        {
            if (currentWeight > truck.MaxWeightLbs || currentVolume > truck.MaxVolumeCuft)
            {
                return;
            }

            if (currentMaxPickup.HasValue && currentMinDelivery.HasValue && currentMaxPickup.Value > currentMinDelivery.Value)
            {
                return;
            }

            allSolutions.Add((currentPayout, currentWeight, currentVolume, [..selected]));

            if (index == sortedOrders.Length)
            {
                return;
            }

            Dfs(index + 1, currentPayout, currentWeight, currentVolume, currentMaxPickup, currentMinDelivery);

            var order = sortedOrders[index];
            var nextMaxPickup = currentMaxPickup.HasValue
                ? (currentMaxPickup.Value > order.PickupDate ? currentMaxPickup.Value : order.PickupDate)
                : order.PickupDate;
            var nextMinDelivery = currentMinDelivery.HasValue
                ? (currentMinDelivery.Value < order.DeliveryDate ? currentMinDelivery.Value : order.DeliveryDate)
                : order.DeliveryDate;

            selected.Add(originalIndices[index]);
            Dfs(
                index + 1,
                currentPayout + order.PayoutCents,
                currentWeight + order.WeightLbs,
                currentVolume + order.VolumeCuft,
                nextMaxPickup,
                nextMinDelivery
            );
            selected.RemoveAt(selected.Count - 1);
        }

        Dfs(0, 0L, 0, 0, null, null);

        var paretoSolutions = FilterPareto(allSolutions, truck);
        return paretoSolutions
            .Select(sol => (sol.PayoutCents, sol.WeightLbs, sol.VolumeCuft, sol.Indices.Select(i => orders[i].Id).ToList()))
            .ToList();
    }

    private static List<(long PayoutCents, int WeightLbs, int VolumeCuft, List<int> Indices)> FilterPareto(
        List<(long PayoutCents, int WeightLbs, int VolumeCuft, List<int> Indices)> solutions,
        Truck truck
    )
    {
        if (!solutions.Any())
        {
            return [];
        }

        var pareto = new List<(long PayoutCents, int WeightLbs, int VolumeCuft, List<int> Indices)>();

        foreach (var (payout, weight, volume, indices) in solutions)
        {
            var currUtil = AverageUtilization(weight, volume, truck);
            var dominated = pareto.Any(p =>
            {
                var pUtil = AverageUtilization(p.WeightLbs, p.VolumeCuft, truck);
                return p.PayoutCents >= payout && pUtil >= currUtil
                    && (p.PayoutCents > payout || pUtil > currUtil);
            });

            if (!dominated)
            {
                pareto = pareto
                    .Where(p =>
                    {
                        var pUtil = AverageUtilization(p.WeightLbs, p.VolumeCuft, truck);
                        return !(payout >= p.PayoutCents && currUtil >= pUtil
                            && (payout > p.PayoutCents || currUtil > pUtil));
                    })
                    .ToList();
                pareto.Add((payout, weight, volume, indices));
            }
        }

        return pareto;
    }

    private static List<(long PayoutCents, int WeightLbs, int VolumeCuft, List<string> SelectedIds)> FilterParetoFront(
        List<(long PayoutCents, int WeightLbs, int VolumeCuft, List<string> SelectedIds)> solutions,
        Truck truck
    )
    {
        if (!solutions.Any())
        {
            return [];
        }

        var pareto = new List<(long PayoutCents, int WeightLbs, int VolumeCuft, List<string> SelectedIds)>();

        foreach (var (payout, weight, volume, selectedIds) in solutions)
        {
            var avgUtil = AverageUtilization(weight, volume, truck);

            var dominated = pareto.Any(p =>
            {
                var pAvgUtil = AverageUtilization(p.WeightLbs, p.VolumeCuft, truck);
                return p.PayoutCents >= payout && pAvgUtil >= avgUtil
                    && (p.PayoutCents > payout || pAvgUtil > avgUtil);
            });

            if (!dominated)
            {
                pareto = pareto
                    .Where(p =>
                    {
                        var pAvgUtil = AverageUtilization(p.WeightLbs, p.VolumeCuft, truck);
                        return !(payout >= p.PayoutCents && avgUtil >= pAvgUtil
                            && (payout > p.PayoutCents || avgUtil > pAvgUtil));
                    })
                    .ToList();
                pareto.Add((payout, weight, volume, selectedIds));
            }
        }

        return pareto.OrderByDescending(p => p.PayoutCents).ToList();
    }

    private static double UtilizationPercent(int used, int capacity)
    {
        if (capacity <= 0)
        {
            return 0;
        }

        return Math.Round((double)used * 100 / capacity, 2, MidpointRounding.AwayFromZero);
    }

    private static double AverageUtilization(int weight, int volume, Truck truck)
    {
        var utilW = truck.MaxWeightLbs > 0 ? (double)weight * 100 / truck.MaxWeightLbs : 0;
        var utilV = truck.MaxVolumeCuft > 0 ? (double)volume * 100 / truck.MaxVolumeCuft : 0;
        return (utilW + utilV) / 2;
    }
}
