using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Zurari.App;

/// <summary>
/// Keeps the job strip's rows in step with the projected job list, without ever replacing the
/// collection the strip is bound to.
/// </summary>
/// <remarks>
/// Existing rows are updated in place via <see cref="JobRowVm.UpdateFrom"/> (raising only
/// property-level <c>PropertyChanged</c>, never touching the collection), a job no longer tracked
/// (dismissed, or auto-removed on a clean completion - see <c>Transition.JobFinished</c>) has its row
/// removed, and a brand-new job gets a brand-new row inserted/moved into position. This is what
/// keeps a running job's row container - including its "キャンセル" button - alive across the
/// ~10/second <c>Msg.JobProgress</c>-driven renders instead of the whole list being discarded and
/// rebuilt every tick (Phase 5 bug B1 - see <see cref="JobRowVm"/>'s remarks).
/// </remarks>
internal sealed class JobStripRows
{
    private readonly ObservableCollection<JobRowVm> rows = [];
    private readonly ItemsControl strip;

    /// <summary>Binds <paramref name="strip"/> to the rows, once, for good.</summary>
    public JobStripRows(ItemsControl strip)
    {
        ArgumentNullException.ThrowIfNull(strip);
        this.strip = strip;
        strip.ItemsSource = rows;
    }

    /// <summary>Reconciles the rows against <paramref name="jobs"/> and shows the strip only while it has any.</summary>
    public void Reconcile(IReadOnlyList<StateProjection.JobVm> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        for (var i = rows.Count - 1; i >= 0; i--)
        {
            var jobId = rows[i].JobId;
            var stillTracked = false;
            for (var j = 0; j < jobs.Count; j++)
            {
                if (jobs[j].JobId == jobId)
                {
                    stillTracked = true;
                    break;
                }
            }

            if (!stillTracked)
            {
                rows.RemoveAt(i);
            }
        }

        for (var i = 0; i < jobs.Count; i++)
        {
            var vm = jobs[i];
            var existingIndex = IndexOf(vm.JobId);
            if (existingIndex < 0)
            {
                rows.Insert(i, new JobRowVm(vm));
                continue;
            }

            if (existingIndex != i)
            {
                rows.Move(existingIndex, i);
            }

            rows[i].UpdateFrom(vm);
        }

        strip.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private int IndexOf(int jobId)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].JobId == jobId)
            {
                return i;
            }
        }

        return -1;
    }
}
