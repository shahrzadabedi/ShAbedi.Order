using MassTransit;
using OrderJobs.Application.DTOs;
using PharmacyOrder.Application.Consumers;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
builder.Host.UseSerilog();
Serilog.Debugging.SelfLog.Enable(Console.Out);
// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Message<OrderCreated>(x => x.SetEntityName("order-created-exchange"));

        cfg.ReceiveEndpoint("order-created-pharmacy-queue", e =>
        {
            e.UseMessageRetry(r => r.Interval(1, TimeSpan.FromMilliseconds(1)));
            e.UseInMemoryOutbox();

            e.ConfigureConsumeTopology = false;

            e.Durable = true;
            e.AutoDelete = false;

            e.Bind("order-created-exchange", c =>
            {
                c.RoutingKey = "order.pharmacy.#";
                c.ExchangeType = "topic";
            });

            e.ConfigureConsumer<OrderCreatedConsumer>(ctx);
        });

        cfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
    });
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
