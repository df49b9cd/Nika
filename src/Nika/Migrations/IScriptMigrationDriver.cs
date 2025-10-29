using System.Threading;
using System.Threading.Tasks;

namespace Nika.Migrations;

/// <summary>
/// Optional interface for drivers that can execute textual migration scripts.
/// </summary>
public interface IScriptMigrationDriver : IMigrationDriver
{
    Task ExecuteScriptAsync(MigrationScript script, CancellationToken cancellationToken);
}
