using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using SmartLoad.Api;
using SmartLoad.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ILoadOptimizer, LoadOptimizer>();

var app = builder.Build();

const long maxContentLengthBytes = 1_000_000;

app.Use(async (context, next) =>
{
	if (context.Request.ContentLength is long contentLength && contentLength > maxContentLengthBytes)
	{
		context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
		await context.Response.WriteAsJsonAsync(new { detail = "Payload too large" });
		return;
	}

	if (context.Request.ContentLength is null &&
		(HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method)))
	{
		context.Request.EnableBuffering();

		long totalRead = 0;
		var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
		try
		{
			int read;
			while ((read = await context.Request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), context.RequestAborted)) > 0)
			{
				totalRead += read;
				if (totalRead > maxContentLengthBytes)
				{
					context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
					await context.Response.WriteAsJsonAsync(new { detail = "Payload too large" });
					return;
				}
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
			context.Request.Body.Position = 0;
		}
	}

	await next();
});

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/v1/load-optimizer/optimize", (
	OptimizeRequest request,
	ILoadOptimizer optimizer,
	IMemoryCache cache) =>
{
	var validationErrors = ValidateRequest(request);
	if (validationErrors.Count > 0)
	{
		return Results.BadRequest(new { detail = validationErrors });
	}

	var cacheKey = BuildCacheKey(request);
	if (cache.TryGetValue(cacheKey, out OptimizeResponse? cached) && cached is not null)
	{
		return Results.Ok(cached);
	}

	var result = optimizer.Optimize(request);
	cache.Set(cacheKey, result, new MemoryCacheEntryOptions
	{
		AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
		Size = 1
	});

	return Results.Ok(result);
});

app.MapPost("/api/v1/load-optimizer/pareto", (
    OptimizeRequest request,
    ILoadOptimizer optimizer,
    IMemoryCache cache) =>
{
    var validationErrors = ValidateRequest(request);
    if (validationErrors.Count > 0)
    {
        return Results.BadRequest(new { detail = validationErrors });
    }

    var cacheKey = BuildCacheKey(request, "pareto");
    if (cache.TryGetValue(cacheKey, out ParetoResponse? cached) && cached is not null)
    {
        return Results.Ok(cached);
    }

    var result = optimizer.OptimizePareto(request);
    cache.Set(cacheKey, result, new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
        Size = 1
    });

    return Results.Ok(result);
});

app.Run();

static List<ValidationError> ValidateRequest(OptimizeRequest request)
{
	var errors = new List<ValidationError>();

	if (request.Truck is null)
	{
		errors.Add(new ValidationError("truck", "truck is required"));
		return errors;
	}

	if (request.Truck.Id is null || string.IsNullOrWhiteSpace(request.Truck.Id))
	{
		errors.Add(new ValidationError("truck.id", "truck id is required"));
	}

	if (request.Truck.MaxWeightLbs <= 0)
	{
		errors.Add(new ValidationError("truck.max_weight_lbs", "must be greater than zero"));
	}

	if (request.Truck.MaxVolumeCuft <= 0)
	{
		errors.Add(new ValidationError("truck.max_volume_cuft", "must be greater than zero"));
	}

	if (request.Orders is null)
	{
		errors.Add(new ValidationError("orders", "orders is required"));
		return errors;
	}

	if (request.Orders.Count > 22)
	{
		errors.Add(new ValidationError("orders", "orders must contain at most 22 items"));
	}

	var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	for (var i = 0; i < request.Orders.Count; i++)
	{
		var order = request.Orders[i];
		var prefix = $"orders[{i}]";

		if (string.IsNullOrWhiteSpace(order.Id))
		{
			errors.Add(new ValidationError($"{prefix}.id", "id is required"));
		}
		else if (!seenIds.Add(order.Id))
		{
			errors.Add(new ValidationError($"{prefix}.id", "duplicate order id"));
		}

		if (order.PayoutCents < 0)
		{
			errors.Add(new ValidationError($"{prefix}.payout_cents", "must be non-negative"));
		}

		if (order.WeightLbs <= 0)
		{
			errors.Add(new ValidationError($"{prefix}.weight_lbs", "must be greater than zero"));
		}

		if (order.VolumeCuft <= 0)
		{
			errors.Add(new ValidationError($"{prefix}.volume_cuft", "must be greater than zero"));
		}

		if (string.IsNullOrWhiteSpace(order.Origin))
		{
			errors.Add(new ValidationError($"{prefix}.origin", "origin is required"));
		}

		if (string.IsNullOrWhiteSpace(order.Destination))
		{
			errors.Add(new ValidationError($"{prefix}.destination", "destination is required"));
		}

		if (order.PickupDate > order.DeliveryDate)
		{
			errors.Add(new ValidationError($"{prefix}", "pickup_date must be on or before delivery_date"));
		}
	}

	return errors;
}

static string BuildCacheKey(OptimizeRequest request, string suffix = "")
{
    var payload = JsonSerializer.Serialize(request);
    var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    var key = Convert.ToHexString(hashBytes);
    return string.IsNullOrEmpty(suffix) ? key : $"{key}:{suffix}";
}

public partial class Program
{
}
