using Faborite.Core.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Faborite.Core.Tests.Resilience;

public class ResiliencePoliciesTests
{
    [Fact]
    public void GetDefaultRetryPolicy_ReturnsPolicy()
    {
        // Act
        var policy = ResiliencePolicies.GetDefaultRetryPolicy();

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetDefaultRetryPolicy_WithLogger_ReturnsPolicy()
    {
        // Arrange
        var logger = Mock.Of<ILogger>();

        // Act
        var policy = ResiliencePolicies.GetDefaultRetryPolicy(logger);

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetRetryPolicy_WithDefaultParameters_ReturnsPolicy()
    {
        // Act
        var policy = ResiliencePolicies.GetRetryPolicy();

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetRetryPolicy_WithCustomParameters_ReturnsPolicy()
    {
        // Act
        var policy = ResiliencePolicies.GetRetryPolicy(
            retryCount: 5,
            baseDelaySeconds: 1);

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetRetryPolicy_WithLogger_ReturnsPolicy()
    {
        // Arrange
        var logger = Mock.Of<ILogger>();

        // Act
        var policy = ResiliencePolicies.GetRetryPolicy(
            retryCount: 3,
            baseDelaySeconds: 2,
            logger: logger);

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetRetryPolicyGeneric_ReturnsPolicy()
    {
        // Act
        var policy = ResiliencePolicies.GetRetryPolicy<string>();

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetRetryPolicyGeneric_WithCustomParameters_ReturnsPolicy()
    {
        // Act
        var policy = ResiliencePolicies.GetRetryPolicy<int>(
            retryCount: 5,
            baseDelaySeconds: 1);

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetRetryPolicyGeneric_WithLogger_ReturnsPolicy()
    {
        // Arrange
        var logger = Mock.Of<ILogger>();

        // Act
        var policy = ResiliencePolicies.GetRetryPolicy<bool>(
            retryCount: 3,
            baseDelaySeconds: 2,
            logger: logger);

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public async Task DefaultRetryPolicy_RetriesOnHttpRequestException()
    {
        // Arrange
        var policy = ResiliencePolicies.GetDefaultRetryPolicy();
        var callCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await policy.ExecuteAsync(async () =>
            {
                callCount++;
                throw new HttpRequestException("Test error");
            });
        });

        // Should retry 3 times + 1 initial = 4 calls
        callCount.Should().Be(4);
    }

    [Fact]
    public async Task DefaultRetryPolicy_RetriesOnTimeoutException()
    {
        // Arrange
        var policy = ResiliencePolicies.GetDefaultRetryPolicy();
        var callCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await policy.ExecuteAsync(async () =>
            {
                callCount++;
                throw new TimeoutException("Test timeout");
            });
        });

        callCount.Should().Be(4);
    }

    [Fact]
    public async Task DefaultRetryPolicy_DoesNotRetryOnOperationCanceled()
    {
        // Arrange
        var policy = ResiliencePolicies.GetDefaultRetryPolicy();
        var callCount = 0;
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await policy.ExecuteAsync(async () =>
            {
                callCount++;
                throw new TaskCanceledException("Canceled", null, cts.Token);
            });
        });

        // Should not retry on cancellation
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task DefaultRetryPolicy_SucceedsEventually()
    {
        // Arrange
        var policy = ResiliencePolicies.GetDefaultRetryPolicy();
        var callCount = 0;

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            callCount++;
            if (callCount < 3)
                throw new HttpRequestException("Test error");
            return "success";
        });

        // Assert
        result.Should().Be("success");
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task CustomRetryPolicy_UsesCustomRetryCount()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(retryCount: 2);
        var callCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await policy.ExecuteAsync(async () =>
            {
                callCount++;
                throw new HttpRequestException("Test error");
            });
        });

        // 2 retries + 1 initial = 3 calls
        callCount.Should().Be(3);
    }
}
