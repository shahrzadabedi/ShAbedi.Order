using Hangfire;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderJobs.Application;
using OrderJobs.Application.DTOs;
using ShAbedi.Order.Domain;
using ShAbedi.OrderJobs.Persistence;
using System.Data;
using System.Text.Json;

namespace OrderJobs.Jobs
{
    [DisableConcurrentExecution(3600)]
    public class OutboxProcessorJob
    {
        private AppDbContext dbContext;
        private readonly IServiceScopeFactory _scopeFactory;
        private IUnitOfWork _unitOfWork;
        private IPublishEndpoint _publishEndpoint;

        public OutboxProcessorJob(AppDbContext db,
            IServiceScopeFactory scopeFactory,
            IPublishEndpoint publishEndpoint)
        {
            this.dbContext = db;
            this._scopeFactory = scopeFactory;
            this._publishEndpoint = publishEndpoint;
        }

        public async Task ProcessOrderCreatedOutbox(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            _unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            _publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

            await _unitOfWork.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            int batchSize = 10;

            var messages = dbContext.Outbox
                .FromSql($@"SELECT TOP ({batchSize}) Id, Type, Payload,IsProcessed, ProcessedOn, OccurredOn, RetryCount 
                    FROM Outbox WITH (ROWLOCK, UPDLOCK, READPAST)
                    WHERE IsProcessed = 0 
                    ORDER BY OccurredOn")
                            .ToList();

            foreach (var message in messages)
            {
                var eventDto = JsonSerializer.Deserialize<OrderCreated>(message.Payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                message.MarkAsProcessed();
               
                await _publishEndpoint.Publish(eventDto,
                    x=> x.SetRoutingKey("order.pharmacy.created")
                    , cancellationToken);
            }

            if (messages.Count > 0)
            {
                //await Task.WhenAll(messages.Select(m =>
                //    _publishEndpoint.Publish(JsonSerializer.Deserialize<OrderCreated>(m.Payload)!)
                //));

                await _unitOfWork.CommitAsync(cancellationToken);
            }
        }
    }
}
