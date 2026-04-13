using AgentFlow.Abstractions;
using AgentFlow.Api.Controllers;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace AgentFlow.Tests.Integration.Threads;

public class ConversationThreadsControllerAuthTests
{
    private const string TenantId = "tenant-1";
    private const string OwnerUserId = "owner-user";
    private const string PeerUserId = "peer-user";

    [Fact]
    public async Task GetThread_Allows_Only_The_Owner()
    {
        var ownerThread = BuildThread(OwnerUserId);
        var controller = BuildController(new InMemoryThreadRepository(ownerThread), BuildPrincipal(OwnerUserId));

        var result = await controller.GetThread(TenantId, ownerThread.Id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ListThreads_WithAgentId_Filters_Out_Other_Users_In_Same_Tenant()
    {
        var ownerThread = BuildThread(OwnerUserId, "agent-1");
        var peerThread = BuildThread(PeerUserId, "agent-1");
        var controller = BuildController(new InMemoryThreadRepository(ownerThread, peerThread), BuildPrincipal(OwnerUserId));

        var result = await controller.ListThreads(TenantId, "agent-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsAssignableFrom<IEnumerable<ThreadResponse>>(ok.Value);
        var single = Assert.Single(response);
        Assert.Equal(ownerThread.Id, single.ThreadId);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("history")]
    [InlineData("messages")]
    [InlineData("archive")]
    [InlineData("delete")]
    public async Task Thread_Endpoints_Reject_Peer_User_In_Same_Tenant(string endpoint)
    {
        var thread = BuildThread(OwnerUserId);
        var controller = BuildController(new InMemoryThreadRepository(thread), BuildPrincipal(PeerUserId));

        IActionResult result = endpoint switch
        {
            "get" => await controller.GetThread(TenantId, thread.Id, CancellationToken.None),
            "history" => await controller.GetHistory(TenantId, thread.Id, 50, CancellationToken.None),
            "messages" => await controller.SendMessage(
                TenantId,
                thread.Id,
                new SendMessageRequest { Message = "hello" },
                CancellationToken.None),
            "archive" => await controller.ArchiveThread(TenantId, thread.Id, CancellationToken.None),
            "delete" => await controller.DeleteThread(TenantId, thread.Id, CancellationToken.None),
            _ => throw new NotSupportedException(endpoint)
        };

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Thread_Endpoints_Require_Authenticated_Identity_And_Required_Claim()
    {
        var thread = BuildThread(OwnerUserId);

        var unauthenticatedController = BuildController(new InMemoryThreadRepository(thread), new ClaimsPrincipal(new ClaimsIdentity()));
        var unauthenticatedResult = await unauthenticatedController.GetThread(TenantId, thread.Id, CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(unauthenticatedResult);

        var authenticatedWithoutClaims = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "TestAuth"));
        var noClaimController = BuildController(new InMemoryThreadRepository(thread), authenticatedWithoutClaims);
        var noClaimResult = await noClaimController.GetThread(TenantId, thread.Id, CancellationToken.None);
        Assert.IsType<ForbidResult>(noClaimResult);
    }

    private static ConversationThread BuildThread(string userId, string agentId = "agent-1")
        => ConversationThread.Create(
            tenantId: TenantId,
            threadKey: $"thread-{Guid.NewGuid():N}",
            agentDefinitionId: agentId,
            userId: userId,
            expiresIn: TimeSpan.FromHours(1));

    private static ClaimsPrincipal BuildPrincipal(string userId)
        => new(new ClaimsIdentity([new Claim("sub", userId)], authenticationType: "TestAuth"));

    private static ConversationThreadsController BuildController(IConversationThreadRepository threadRepo, ClaimsPrincipal user)
    {
        var controller = new ConversationThreadsController(
            threadRepo,
            new StubAgentDefinitionRepository(),
            new StubAgentExecutor(),
            NullLogger<ConversationThreadsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = user
                }
            }
        };

        return controller;
    }

    private sealed class InMemoryThreadRepository : IConversationThreadRepository
    {
        private readonly List<ConversationThread> _threads;

        public InMemoryThreadRepository(params ConversationThread[] threads)
        {
            _threads = threads.ToList();
        }

        public Task<ConversationThread?> GetByIdAsync(string threadId, string tenantId, CancellationToken ct = default)
            => Task.FromResult(_threads.SingleOrDefault(t => t.Id == threadId && t.TenantId == tenantId));

        public Task<ConversationThread?> GetByKeyAsync(string threadKey, string tenantId, CancellationToken ct = default)
            => Task.FromResult(_threads.SingleOrDefault(t => t.ThreadKey == threadKey && t.TenantId == tenantId));

        public Task<IReadOnlyList<ConversationThread>> GetActiveByUserAsync(string userId, string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConversationThread>>(
                _threads.Where(t => t.UserId == userId && t.TenantId == tenantId).ToList());

        public Task<IReadOnlyList<ConversationThread>> GetByAgentAsync(string agentDefinitionId, string tenantId, int skip = 0, int take = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConversationThread>>(
                _threads.Where(t => t.AgentDefinitionId == agentDefinitionId && t.TenantId == tenantId).ToList());

        public Task<Result> InsertAsync(ConversationThread thread, CancellationToken ct = default)
        {
            _threads.Add(thread);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> UpdateAsync(ConversationThread thread, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task<Result> DeleteAsync(string threadId, string tenantId, CancellationToken ct = default)
        {
            _threads.RemoveAll(t => t.Id == threadId && t.TenantId == tenantId);
            return Task.FromResult(Result.Success());
        }

        public Task<int> GetActiveCountAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_threads.Count(t => t.TenantId == tenantId));
    }

    private sealed class StubAgentExecutor : IAgentExecutor
    {
        public Task<AgentExecutionResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct = default)
            => Task.FromResult(new AgentExecutionResult
            {
                ExecutionId = "exec-1",
                AgentKey = request.AgentKey,
                AgentVersion = "v1",
                Status = ExecutionStatus.Completed
            });

        public Task<AgentExecutionResult> ResumeAsync(string executionId, string tenantId, CheckpointDecision decision, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Result> CancelAsync(string executionId, string tenantId, string cancelledBy, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class StubAgentDefinitionRepository : IAgentDefinitionRepository
    {
        public Task<Domain.Aggregates.AgentDefinition?> GetByIdAsync(string id, string tenantId, CancellationToken ct = default)
            => Task.FromResult<Domain.Aggregates.AgentDefinition?>(null);

        public Task<Domain.Aggregates.AgentDefinition?> GetByNameAsync(string name, string tenantId, CancellationToken ct = default)
            => Task.FromResult<Domain.Aggregates.AgentDefinition?>(null);

        public Task<IReadOnlyList<Domain.Aggregates.AgentDefinition>> GetAllAsync(string tenantId, int skip = 0, int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Domain.Aggregates.AgentDefinition>>(Array.Empty<Domain.Aggregates.AgentDefinition>());

        public Task<IReadOnlyList<Domain.Aggregates.AgentDefinition>> GetPublishedAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Domain.Aggregates.AgentDefinition>>(Array.Empty<Domain.Aggregates.AgentDefinition>());

        public Task<int> CountAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(0);

        public Task<Result> InsertAsync(Domain.Aggregates.AgentDefinition aggregate, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task<Result> UpdateAsync(Domain.Aggregates.AgentDefinition aggregate, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task<Result> DeleteAsync(string id, string tenantId, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task<bool> ExistsAsync(string id, string tenantId, CancellationToken ct = default)
            => Task.FromResult(false);
    }
}
