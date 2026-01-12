using Microsoft.Extensions.Logging;

namespace Faborite.Core.Scheduling;

/// <summary>
/// Scheduler for automated sync operations with cron expressions.
/// Issue #33
/// </summary>
public class SyncScheduler
{
    private readonly ILogger<SyncScheduler> _logger;
    private readonly Dictionary<string, ScheduledJob> _jobs = new();
    private readonly Timer _timer;

    public SyncScheduler(ILogger<SyncScheduler> logger)
    {
        _logger = logger;
        _timer = new Timer(CheckSchedules, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Schedules a sync job with cron expression.
    /// </summary>
    public void ScheduleJob(string jobId, string cronExpression, Func<CancellationToken, Task> syncAction)
    {
        if (!CronExpression.TryParse(cronExpression, out var cron))
        {
            throw new ArgumentException($"Invalid cron expression: {cronExpression}");
        }

        var job = new ScheduledJob(jobId, cron, syncAction);
        _jobs[jobId] = job;

        _logger.LogInformation("Scheduled job {JobId} with cron: {Cron}", jobId, cronExpression);
    }

    /// <summary>
    /// Removes a scheduled job.
    /// </summary>
    public bool UnscheduleJob(string jobId)
    {
        if (_jobs.Remove(jobId))
        {
            _logger.LogInformation("Unscheduled job {JobId}", jobId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets next run time for a job.
    /// </summary>
    public DateTime? GetNextRunTime(string jobId)
    {
        return _jobs.TryGetValue(jobId, out var job) 
            ? job.CronExpression.GetNextOccurrence(DateTime.UtcNow)
            : null;
    }

    private async void CheckSchedules(object? state)
    {
        var now = DateTime.UtcNow;

        foreach (var (jobId, job) in _jobs)
        {
            if (job.IsRunning)
            {
                _logger.LogDebug("Job {JobId} is still running, skipping", jobId);
                continue;
            }

            var nextRun = job.CronExpression.GetNextOccurrence(job.LastRun ?? now.AddMinutes(-1));
            if (nextRun.HasValue && nextRun.Value <= now)
            {
                _ = ExecuteJobAsync(job);
            }
        }
    }

    private async Task ExecuteJobAsync(ScheduledJob job)
    {
        job.IsRunning = true;
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("Executing scheduled job {JobId}", job.JobId);
            
            using var cts = new CancellationTokenSource(TimeSpan.FromHours(2));
            await job.SyncAction(cts.Token);

            job.LastRun = startTime;
            job.LastStatus = JobStatus.Success;

            _logger.LogInformation("Job {JobId} completed successfully", job.JobId);
        }
        catch (Exception ex)
        {
            job.LastStatus = JobStatus.Failed;
            job.LastError = ex.Message;
            _logger.LogError(ex, "Job {JobId} failed", job.JobId);
        }
        finally
        {
            job.IsRunning = false;
            job.ExecutionCount++;
        }
    }

    public List<JobInfo> GetAllJobs()
    {
        return _jobs.Values.Select(j => new JobInfo(
            j.JobId,
            j.CronExpression.ToString(),
            j.LastRun,
            j.LastStatus,
            j.IsRunning,
            j.ExecutionCount,
            GetNextRunTime(j.JobId)
        )).ToList();
    }
}

public class ScheduledJob
{
    public string JobId { get; }
    public CronExpression CronExpression { get; }
    public Func<CancellationToken, Task> SyncAction { get; }
    public DateTime? LastRun { get; set; }
    public JobStatus LastStatus { get; set; } = JobStatus.Pending;
    public string? LastError { get; set; }
    public bool IsRunning { get; set; }
    public int ExecutionCount { get; set; }

    public ScheduledJob(string jobId, CronExpression cronExpression, Func<CancellationToken, Task> syncAction)
    {
        JobId = jobId;
        CronExpression = cronExpression;
        SyncAction = syncAction;
    }
}

public enum JobStatus
{
    Pending,
    Success,
    Failed
}

public record JobInfo(
    string JobId,
    string CronExpression,
    DateTime? LastRun,
    JobStatus LastStatus,
    bool IsRunning,
    int ExecutionCount,
    DateTime? NextRun);

/// <summary>
/// Simple cron expression parser supporting standard cron format.
/// Format: minute hour day month dayOfWeek
/// </summary>
public class CronExpression
{
    private readonly string _expression;
    private readonly int[] _minutes;
    private readonly int[] _hours;
    private readonly int[] _days;
    private readonly int[] _months;
    private readonly int[] _daysOfWeek;

    private CronExpression(string expression, int[] minutes, int[] hours, int[] days, int[] months, int[] daysOfWeek)
    {
        _expression = expression;
        _minutes = minutes;
        _hours = hours;
        _days = days;
        _months = months;
        _daysOfWeek = daysOfWeek;
    }

    public static bool TryParse(string expression, out CronExpression? cron)
    {
        try
        {
            var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5)
            {
                cron = null;
                return false;
            }

            var minutes = ParseField(parts[0], 0, 59);
            var hours = ParseField(parts[1], 0, 23);
            var days = ParseField(parts[2], 1, 31);
            var months = ParseField(parts[3], 1, 12);
            var daysOfWeek = ParseField(parts[4], 0, 6);

            cron = new CronExpression(expression, minutes, hours, days, months, daysOfWeek);
            return true;
        }
        catch
        {
            cron = null;
            return false;
        }
    }

    private static int[] ParseField(string field, int min, int max)
    {
        if (field == "*")
            return Enumerable.Range(min, max - min + 1).ToArray();

        if (field.Contains('/'))
        {
            var parts = field.Split('/');
            var step = int.Parse(parts[1]);
            return Enumerable.Range(min, max - min + 1).Where(x => x % step == 0).ToArray();
        }

        if (field.Contains('-'))
        {
            var parts = field.Split('-');
            var start = int.Parse(parts[0]);
            var end = int.Parse(parts[1]);
            return Enumerable.Range(start, end - start + 1).ToArray();
        }

        return new[] { int.Parse(field) };
    }

    public DateTime? GetNextOccurrence(DateTime after)
    {
        var current = after.AddMinutes(1);
        
        for (int i = 0; i < 366; i++)
        {
            if (_months.Contains(current.Month) &&
                _days.Contains(current.Day) &&
                _daysOfWeek.Contains((int)current.DayOfWeek))
            {
                foreach (var hour in _hours)
                {
                    foreach (var minute in _minutes)
                    {
                        var candidate = new DateTime(current.Year, current.Month, current.Day, hour, minute, 0);
                        if (candidate > after)
                            return candidate;
                    }
                }
            }
            current = current.AddDays(1);
        }

        return null;
    }

    public override string ToString() => _expression;
}
