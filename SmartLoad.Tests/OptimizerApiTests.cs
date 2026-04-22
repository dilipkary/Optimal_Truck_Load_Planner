using System.Net;
using System.Text;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartLoad.Tests;

public sealed class OptimizerApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public OptimizerApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Healthz_ReturnsOk()
    {
        var response = await _client.GetAsync("/healthz");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Optimize_HappyPath_ReturnsExpectedCombination()
    {
        var payload = new
        {
            truck = new
            {
                id = "truck-123",
                max_weight_lbs = 44000,
                max_volume_cuft = 3000
            },
            orders = new object[]
            {
                new
                {
                    id = "ord-001",
                    payout_cents = 250000,
                    weight_lbs = 18000,
                    volume_cuft = 1200,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-05",
                    delivery_date = "2025-12-09",
                    is_hazmat = false
                },
                new
                {
                    id = "ord-002",
                    payout_cents = 180000,
                    weight_lbs = 12000,
                    volume_cuft = 900,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-04",
                    delivery_date = "2025-12-10",
                    is_hazmat = false
                },
                new
                {
                    id = "ord-003",
                    payout_cents = 320000,
                    weight_lbs = 30000,
                    volume_cuft = 1800,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-06",
                    delivery_date = "2025-12-08",
                    is_hazmat = true
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/v1/load-optimizer/optimize", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OptimizeResult>();
        Assert.NotNull(body);
        Assert.Equal(430000L, body!.total_payout_cents);
        Assert.Equal(30000, body.total_weight_lbs);
        Assert.Equal(2100, body.total_volume_cuft);
        Assert.Equal(2, body.selected_order_ids.Count);
        Assert.Contains("ord-001", body.selected_order_ids);
        Assert.Contains("ord-002", body.selected_order_ids);
    }

    [Fact]
    public async Task Optimize_InvalidDateWindow_ReturnsBadRequest()
    {
        var payload = new
        {
            truck = new
            {
                id = "truck-123",
                max_weight_lbs = 44000,
                max_volume_cuft = 3000
            },
            orders = new object[]
            {
                new
                {
                    id = "ord-001",
                    payout_cents = 250000,
                    weight_lbs = 18000,
                    volume_cuft = 1200,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-10",
                    delivery_date = "2025-12-09",
                    is_hazmat = false
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/v1/load-optimizer/optimize", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Optimize_NoFeasibleOrders_ReturnsEmptySelection()
    {
        var payload = new
        {
            truck = new
            {
                id = "truck-123",
                max_weight_lbs = 1000,
                max_volume_cuft = 100
            },
            orders = new object[]
            {
                new
                {
                    id = "ord-001",
                    payout_cents = 250000,
                    weight_lbs = 18000,
                    volume_cuft = 1200,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-05",
                    delivery_date = "2025-12-09",
                    is_hazmat = false
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/v1/load-optimizer/optimize", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OptimizeResult>();
        Assert.NotNull(body);
        Assert.Equal(0L, body!.total_payout_cents);
        Assert.Empty(body.selected_order_ids);
    }

    [Fact]
    public async Task Optimize_EnforcesTimeWindowCompatibility()
    {
        var payload = new
        {
            truck = new
            {
                id = "truck-123",
                max_weight_lbs = 44000,
                max_volume_cuft = 3000
            },
            orders = new object[]
            {
                new
                {
                    id = "a",
                    payout_cents = 200000,
                    weight_lbs = 10000,
                    volume_cuft = 800,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-10",
                    delivery_date = "2025-12-11",
                    is_hazmat = false
                },
                new
                {
                    id = "b",
                    payout_cents = 220000,
                    weight_lbs = 10000,
                    volume_cuft = 800,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-01",
                    delivery_date = "2025-12-02",
                    is_hazmat = false
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/v1/load-optimizer/optimize", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OptimizeResult>();
        Assert.NotNull(body);
        Assert.Equal(220000L, body!.total_payout_cents);
        Assert.Single(body.selected_order_ids);
        Assert.Equal("b", body.selected_order_ids[0]);
    }

    [Fact]
    public async Task Pareto_ReturnsMultipleSolutions()
    {
        var payload = new
        {
            truck = new
            {
                id = "truck-123",
                max_weight_lbs = 44000,
                max_volume_cuft = 3000
            },
            orders = new object[]
            {
                new
                {
                    id = "ord-001",
                    payout_cents = 250000,
                    weight_lbs = 18000,
                    volume_cuft = 1200,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-05",
                    delivery_date = "2025-12-09",
                    is_hazmat = false
                },
                new
                {
                    id = "ord-002",
                    payout_cents = 180000,
                    weight_lbs = 12000,
                    volume_cuft = 900,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-04",
                    delivery_date = "2025-12-10",
                    is_hazmat = false
                },
                new
                {
                    id = "ord-003",
                    payout_cents = 320000,
                    weight_lbs = 30000,
                    volume_cuft = 1800,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-06",
                    delivery_date = "2025-12-08",
                    is_hazmat = true
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/v1/load-optimizer/pareto", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ParetoResult>();
        Assert.NotNull(body);
        Assert.NotEmpty(body!.solutions);
        // Solutions should be ordered by payout descending
        Assert.True(body.solutions[0].total_payout_cents >= body.solutions[body.solutions.Count - 1].total_payout_cents);
    }

    [Fact]
    public async Task Optimize_DoesNotMixDifferentLanes()
    {
        var payload = new
        {
            truck = new
            {
                id = "truck-123",
                max_weight_lbs = 44000,
                max_volume_cuft = 3000
            },
            orders = new object[]
            {
                new
                {
                    id = "la-dal-1",
                    payout_cents = 180000,
                    weight_lbs = 8000,
                    volume_cuft = 600,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-05",
                    delivery_date = "2025-12-08",
                    is_hazmat = false
                },
                new
                {
                    id = "la-dal-2",
                    payout_cents = 170000,
                    weight_lbs = 7000,
                    volume_cuft = 550,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-06",
                    delivery_date = "2025-12-09",
                    is_hazmat = false
                },
                new
                {
                    id = "la-phx",
                    payout_cents = 250000,
                    weight_lbs = 9000,
                    volume_cuft = 650,
                    origin = "Los Angeles, CA",
                    destination = "Phoenix, AZ",
                    pickup_date = "2025-12-05",
                    delivery_date = "2025-12-07",
                    is_hazmat = false
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/v1/load-optimizer/optimize", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OptimizeResult>();
        Assert.NotNull(body);
        Assert.Equal(350000L, body!.total_payout_cents);
        Assert.Equal(2, body.selected_order_ids.Count);
        Assert.Contains("la-dal-1", body.selected_order_ids);
        Assert.Contains("la-dal-2", body.selected_order_ids);
        Assert.DoesNotContain("la-phx", body.selected_order_ids);
    }

    [Fact]
    public async Task Optimize_DoesNotMixHazmatAndNonHazmat()
    {
        var payload = new
        {
            truck = new
            {
                id = "truck-123",
                max_weight_lbs = 44000,
                max_volume_cuft = 3000
            },
            orders = new object[]
            {
                new
                {
                    id = "haz-1",
                    payout_cents = 200000,
                    weight_lbs = 10000,
                    volume_cuft = 700,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-05",
                    delivery_date = "2025-12-08",
                    is_hazmat = true
                },
                new
                {
                    id = "nonhaz-1",
                    payout_cents = 190000,
                    weight_lbs = 10000,
                    volume_cuft = 700,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-05",
                    delivery_date = "2025-12-08",
                    is_hazmat = false
                },
                new
                {
                    id = "nonhaz-2",
                    payout_cents = 180000,
                    weight_lbs = 9000,
                    volume_cuft = 650,
                    origin = "Los Angeles, CA",
                    destination = "Dallas, TX",
                    pickup_date = "2025-12-06",
                    delivery_date = "2025-12-09",
                    is_hazmat = false
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/v1/load-optimizer/optimize", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OptimizeResult>();
        Assert.NotNull(body);
        Assert.Equal(370000L, body!.total_payout_cents);
        Assert.Equal(2, body.selected_order_ids.Count);
        Assert.Contains("nonhaz-1", body.selected_order_ids);
        Assert.Contains("nonhaz-2", body.selected_order_ids);
        Assert.DoesNotContain("haz-1", body.selected_order_ids);
    }

    [Fact]
    public async Task Optimize_OversizedPayload_Returns413()
    {
        var oversized = new string('x', 1_000_100);
        using var content = new StringContent(oversized, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/v1/load-optimizer/optimize", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private sealed class OptimizeResult
    {
        public string truck_id { get; init; } = string.Empty;
        public List<string> selected_order_ids { get; init; } = [];
        public long total_payout_cents { get; init; }
        public int total_weight_lbs { get; init; }
        public int total_volume_cuft { get; init; }
        public double utilization_weight_percent { get; init; }
        public double utilization_volume_percent { get; init; }
    }

    private sealed class ParetoSolution
    {
        public List<string> selected_order_ids { get; init; } = [];
        public long total_payout_cents { get; init; }
        public int total_weight_lbs { get; init; }
        public int total_volume_cuft { get; init; }
        public double utilization_weight_percent { get; init; }
        public double utilization_volume_percent { get; init; }
    }

    private sealed class ParetoResult
    {
        public string truck_id { get; init; } = string.Empty;
        public List<ParetoSolution> solutions { get; init; } = [];
    }
}
