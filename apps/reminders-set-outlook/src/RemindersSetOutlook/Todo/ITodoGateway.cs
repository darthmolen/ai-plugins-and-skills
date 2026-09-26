// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System.Threading;
using System.Threading.Tasks;

namespace RemindersSetOutlook.Todo;

/// <summary>
/// The Microsoft To Do operations the sync needs.
/// </summary>
internal interface ITodoGateway
{
    /// <summary>
    /// Finds a task list by display name, creating it when it does not exist.
    /// </summary>
    /// <param name="displayName">The list display name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list id.</returns>
    public Task<string> GetOrCreateListAsync(string displayName, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a task.
    /// </summary>
    /// <param name="listId">The list id.</param>
    /// <param name="draft">The task to create.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new task id.</returns>
    public Task<string> CreateTaskAsync(string listId, TodoTaskDraft draft, CancellationToken cancellationToken);

    /// <summary>
    /// Overwrites a task with the draft.
    /// </summary>
    /// <param name="listId">The list id.</param>
    /// <param name="taskId">The task id.</param>
    /// <param name="draft">The desired task state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>false</c> when the task (or its list) no longer exists; otherwise <c>true</c>.</returns>
    public Task<bool> UpdateTaskAsync(string listId, string taskId, TodoTaskDraft draft, CancellationToken cancellationToken);
}
