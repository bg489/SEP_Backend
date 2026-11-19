using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.DTOs.Workers;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

public sealed class WorkerManagementServiceTests
{
    private readonly IWorkerRepository _repository = Substitute.For<IWorkerRepository>();

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task Invalid_capacity_never_persists(int capacity)
    {
        var service = new WorkerManagementService(_repository, FakeUnitOfWork.User());
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegisterAsync(new("worker", "localhost", Capacity: capacity)));
        await _repository.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_map_is_rejected_instead_of_accepting_wrong_world()
    {
        var service = new WorkerManagementService(_repository, FakeUnitOfWork.User());
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegisterAsync(new("worker", "localhost", Maps: ["Town99"])));
    }

    [Fact]
    public async Task Heartbeat_cannot_impersonate_another_worker_account()
    {
        var worker = new SimulationWorker { Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid() };
        _repository.GetAsync(worker.Id, Arg.Any<CancellationToken>()).Returns(worker);
        var service = new WorkerManagementService(_repository, FakeUnitOfWork.User());
        await Assert.ThrowsAsync<ForbiddenException>(() => service.HeartbeatAsync(worker.Id, new()));
        await _repository.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Live_heartbeat_computes_remaining_capacity_and_pins_low_quality()
    {
        var user = FakeUnitOfWork.User();
        var worker = new SimulationWorker { Id = Guid.NewGuid(), OwnerUserId = user.UserId, Capacity = 2 };
        _repository.GetAsync(worker.Id, Arg.Any<CancellationToken>()).Returns(worker);
        _repository.ActiveRunsAsync(worker.Id, Arg.Any<CancellationToken>()).Returns(1);
        var result = await new WorkerManagementService(_repository, user).HeartbeatAsync(worker.Id, new(["Town01", "Town01_Opt"]));
        Assert.Equal("Healthy", result.Health);
        Assert.Equal("Low", result.Quality);
        Assert.Equal(1, result.AvailableCapacity);
        Assert.Contains("Town01_Opt", result.Maps);
        await _repository.Received(1).SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Restarted_worker_recovers_expired_leases_before_becoming_healthy()
    {
        var user = FakeUnitOfWork.User();
        var worker = new SimulationWorker { Id = Guid.NewGuid(), OwnerUserId = user.UserId,
            LastHeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-2) };
        _repository.GetAsync(worker.Id, Arg.Any<CancellationToken>()).Returns(worker);
        await new WorkerManagementService(_repository, user).HeartbeatAsync(worker.Id, new());
        Received.InOrder(() => {
            _repository.RecoverLostAsync(Arg.Any<CancellationToken>());
            _repository.SaveAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Stale_or_disabled_worker_has_zero_available_capacity()
    {
        var stale = new SimulationWorker { Id = Guid.NewGuid(), LastHeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-2) };
        var disabled = new SimulationWorker { Id = Guid.NewGuid(), Enabled = false, LastHeartbeatAt = DateTimeOffset.UtcNow };
        _repository.ListAsync(Arg.Any<CancellationToken>()).Returns(new[] { stale, disabled });
        var result = await new WorkerManagementService(_repository, FakeUnitOfWork.User()).ListAsync();
        Assert.All(result, item => Assert.Equal(0, item.AvailableCapacity));
        Assert.Equal("Offline", result[0].Health);
        Assert.Equal("Disabled", result[1].Health);
    }
}
