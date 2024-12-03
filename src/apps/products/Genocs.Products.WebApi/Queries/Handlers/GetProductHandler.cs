using Genocs.Core.CQRS.Queries;
using Genocs.Persistence.MongoDb.Domain.Repositories;
using Genocs.Products.WebApi.DTO;

namespace Genocs.Products.WebApi.Queries.Handlers;

public class GetProductHandler : IQueryHandler<GetProduct, ProductDto>
{
    private readonly IMongoDbBaseRepository<Domain.Product, Guid> _repository;

    private static readonly Random _random = new();

    public GetProductHandler(IMongoDbBaseRepository<Domain.Product, Guid> repository)
    {
        _repository = repository;
    }

    public async Task<ProductDto?> HandleAsync(GetProduct query, CancellationToken cancellationToken = default)
    {
        var product = await _repository.GetAsync(query.ProductId);

        int currentValue = _random.Next(1, 10);

        if (currentValue > 6)
        {
            throw new Exception("Random exception");
        }

        return product is null
            ? null
            : new ProductDto { Id = product.Id, SKU = product.SKU, UnitPrice = product.UnitPrice };
    }
}