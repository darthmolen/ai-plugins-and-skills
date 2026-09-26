// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RemindersSetOutlook.Todo;

namespace RemindersSetOutlook.Tests;

/// <summary>
/// An in-memory To Do that records every write.
/// </summary>
internal sealed class FakeTodoGateway : ITodoGateway
{
    private int nextId;

    public Dictionary<string, TodoTaskDraft> Tasks { get; } = new();

    public List<string> Calls { get; } = new();

    public bool FailWrites { get; set; }

    public Task<string> GetOrCreateListAsync(string displayName, CancellationToken cancellationToken)
    {
        this.Calls.Add($"list {displayName}");
        return Task.FromResult("list-1");
    }

    public Task<string> CreateTaskAsync(string listId, TodoTaskDraft draft, CancellationToken cancellationToken)
    {
        this.ThrowIfFailing();
        var id = $"task-{++this.nextId}";
        this.Tasks[id] = draft;
        this.Calls.Add($"create {id}");
        return Task.FromResult(id);
    }

    public Task<bool> UpdateTaskAsync(string listId, string taskId, TodoTaskDraft draft, CancellationToken cancellationToken)
    {
        this.ThrowIfFailing();
        this.Calls.Add($"update {taskId}");
        if (!this.Tasks.ContainsKey(taskId))
        {
            return Task.FromResult(false);
        }

        this.Tasks[taskId] = draft;
        return Task.FromResult(true);
    }

    private void ThrowIfFailing()
    {
        if (this.FailWrites)
        {
            throw new HttpRequestException("Microsoft Graph returned 500 Internal Server Error: boom");
        }
    }
}
